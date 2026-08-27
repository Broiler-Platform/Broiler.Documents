using System;
using System.Collections.Generic;
using System.Text;
using Broiler.Documents.Pdf.Filters;
using Broiler.Documents.Pdf.Structure;
using Broiler.Documents.Pdf.Syntax;
using Broiler.Graphics;

namespace Broiler.Documents.Pdf.Text;

/// <summary>
/// Executes the content-stream operators that carry text, and records where each
/// run landed.
/// </summary>
/// <remarks>
/// <para>
/// This is an interpreter for extraction, not a renderer. It tracks exactly the
/// state that changes what characters mean or where they sit — the text and
/// current matrices, the font, the spacing parameters, the fill colour, and the
/// render mode — and it skips path, shading, and pattern operators after
/// reporting once that artwork was dropped.
/// </para>
/// <para>
/// Every recursion point is bounded: Form XObjects nest to a fixed depth with a
/// visited set, inline images are consumed by a length-bounded scan for
/// <c>EI</c>, and each operator is charged against the document's operator
/// budget.
/// </para>
/// </remarks>
internal sealed class PdfContentInterpreter
{
    private readonly PdfObjectStore _store;
    private readonly List<PdfTextFragment> _fragments = [];
    private readonly Dictionary<PdfDictionary, PdfFont> _fontCache = new();
    private readonly HashSet<PdfDictionary> _activeForms = [];

    private readonly Stack<GraphicsState> _stack = new();
    private GraphicsState _state = GraphicsState.Initial;
    private PdfMatrix _textMatrix = PdfMatrix.Identity;
    private PdfMatrix _lineMatrix = PdfMatrix.Identity;
    private string? _pendingActualText;

    // The run being accumulated; flushed when style, baseline, or spacing breaks.
    private readonly StringBuilder _runText = new();
    private double _runStartX;
    private double _runY;
    private double _runEndX;
    private GraphicsState _runState = GraphicsState.Initial;
    private double _runFontSize;
    private double _runSpaceWidth;
    private bool _runOpen;

    public PdfContentInterpreter(PdfObjectStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>Runs a page's content and returns the text runs it placed.</summary>
    public IReadOnlyList<PdfTextFragment> Run(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        _fragments.Clear();
        _state = GraphicsState.Initial;
        _stack.Clear();

        byte[]? content = ReadPageContent(page);
        if (content is null || content.Length == 0)
            return _fragments;

        Execute(content, page.Resources, depth: 0);
        FlushRun();
        return _fragments;
    }

    private byte[]? ReadPageContent(PdfPage page)
    {
        PdfObject? contents = _store.Resolve(page.Dictionary["Contents"]);

        if (contents is PdfStream single)
            return DecodeContent(single);

        if (contents is not PdfArray array)
            return null;

        // Multiple content streams are concatenated with a separator, because the
        // format allows an operator's operands to be split across the boundary
        // only if the streams are joined with whitespace between them.
        var joined = new List<byte>();
        foreach (PdfObject entry in array)
        {
            if (_store.Resolve(entry) is not PdfStream stream)
                continue;
            byte[]? part = DecodeContent(stream);
            if (part is null)
                continue;
            joined.AddRange(part);
            joined.Add((byte)'\n');
        }

        return joined.ToArray();
    }

    private byte[]? DecodeContent(PdfStream stream)
    {
        PdfStreamDecodeResult decoded = _store.Filters.Decode(stream, _store.Resolve, _store.Budget);
        if (decoded.Succeeded)
            return decoded.Data;

        _store.Diagnostics.Skipped(
            decoded.DiagnosticCode ?? PdfDiagnosticCodes.FilterMalformed,
            decoded.Message ?? "A content stream could not be decoded.");
        return null;
    }

    // ---- the operator loop ----------------------------------------------------

    private void Execute(byte[] content, PdfDictionary? resources, int depth)
    {
        var lexer = new PdfLexer(content, _store.Budget.Limits);
        var operands = new List<PdfObject>();
        var parser = new PdfObjectParser(lexer, _store.Budget);

        while (true)
        {
            PdfToken token = lexer.PeekToken();
            if (token.Type == PdfTokenType.EndOfData)
                break;

            if (token.Type != PdfTokenType.Keyword)
            {
                PdfObject value = parser.ParseObject();
                parser.Rewind();
                if (operands.Count < 64)
                    operands.Add(value);
                continue;
            }

            lexer.ReadToken();
            _store.Budget.ChargeOperator();

            switch (token.Text)
            {
                // Graphics state.
                case "q":
                    if (_stack.Count < _store.Budget.Limits.MaxNestingDepth)
                        _stack.Push(_state);
                    break;
                case "Q":
                    if (_stack.Count > 0)
                        _state = _stack.Pop();
                    break;
                case "cm":
                    if (TryMatrix(operands, out PdfMatrix cm))
                        _state = _state.WithMatrix(cm.Concat(_state.Matrix));
                    break;

                // Colour, in the device spaces the model can represent.
                case "g":
                    _state = _state.WithColor(Gray(operands, 0));
                    break;
                case "rg":
                    _state = _state.WithColor(Rgb(operands));
                    break;
                case "k":
                    _state = _state.WithColor(Cmyk(operands));
                    break;
                case "sc":
                case "scn":
                    _state = _state.WithColor(FromComponents(operands));
                    break;
                case "cs":
                    // Selecting a colour space resets the colour to its initial black.
                    _state = _state.WithColor(BColor.Black);
                    break;

                // Text objects.
                case "BT":
                    _textMatrix = PdfMatrix.Identity;
                    _lineMatrix = PdfMatrix.Identity;
                    break;
                case "ET":
                    FlushRun();
                    break;
                case "Tf":
                    SetFont(operands, resources);
                    break;
                case "Td":
                    TranslateLine(Number(operands, 0), Number(operands, 1));
                    break;
                case "TD":
                    _state = _state.WithLeading(-Number(operands, 1));
                    TranslateLine(Number(operands, 0), Number(operands, 1));
                    break;
                case "Tm":
                    if (TryMatrix(operands, out PdfMatrix tm))
                    {
                        FlushRun();
                        _lineMatrix = tm;
                        _textMatrix = tm;
                    }

                    break;
                case "T*":
                    NextLine();
                    break;
                case "TL":
                    _state = _state.WithLeading(Number(operands, 0));
                    break;
                case "Tc":
                    _state = _state.WithCharSpacing(Number(operands, 0));
                    break;
                case "Tw":
                    _state = _state.WithWordSpacing(Number(operands, 0));
                    break;
                case "Tz":
                    _state = _state.WithHorizontalScale(Number(operands, 0) / 100d);
                    break;
                case "Ts":
                    _state = _state.WithRise(Number(operands, 0));
                    break;
                case "Tr":
                    _state = _state.WithRenderMode((int)Number(operands, 0));
                    break;

                // Show text.
                case "Tj":
                    ShowString(operands, operands.Count - 1);
                    break;
                case "'":
                    NextLine();
                    ShowString(operands, operands.Count - 1);
                    break;
                case "\"":
                    _state = _state.WithWordSpacing(Number(operands, 0)).WithCharSpacing(Number(operands, 1));
                    NextLine();
                    ShowString(operands, operands.Count - 1);
                    break;
                case "TJ":
                    ShowArray(operands);
                    break;

                // Marked content: ActualText replaces whatever the glyphs say.
                case "BDC":
                    BeginMarkedContent(operands, resources);
                    break;
                case "EMC":
                    // Flush first: the run inside the marked-content sequence is
                    // what ActualText replaces, and clearing it before the flush
                    // would emit the glyphs the tag was there to override.
                    FlushRun();
                    _pendingActualText = null;
                    break;

                // External objects.
                case "Do":
                    InvokeXObject(operands, resources, depth);
                    break;
                case "BI":
                    SkipInlineImage(lexer);
                    break;

                // Path painting. Vector artwork has no logical representation, so
                // it is reported once and dropped rather than approximated.
                case "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*" or "sh":
                    NoteVectorArtwork();
                    break;
            }

            operands.Clear();
        }
    }

    // ---- text placement -------------------------------------------------------

    private void SetFont(List<PdfObject> operands, PdfDictionary? resources)
    {
        FlushRun();

        double size = Number(operands, operands.Count - 1);
        string? name = operands.Count >= 2 ? (operands[^2] as PdfName)?.Value : null;

        PdfFont font = PdfFont.Fallback;
        if (name is not null && resources is not null &&
            _store.Resolve(resources["Font"]) is PdfDictionary fonts &&
            _store.Resolve(fonts[name]) is PdfDictionary fontDictionary)
        {
            if (!_fontCache.TryGetValue(fontDictionary, out PdfFont? cached))
            {
                cached = PdfFont.Load(_store, fontDictionary);
                _fontCache[fontDictionary] = cached;
            }

            font = cached;
        }
        else if (name is not null)
        {
            _store.Diagnostics.Skipped(
                PdfDiagnosticCodes.TextMappingMissing,
                "A content stream selected a font that its resource dictionary does not define.");
        }

        _state = _state.WithFont(font, size);
    }

    private void TranslateLine(double tx, double ty)
    {
        FlushRun();
        _lineMatrix = PdfMatrix.Translation(tx, ty).Concat(_lineMatrix);
        _textMatrix = _lineMatrix;
    }

    private void NextLine() => TranslateLine(0, -_state.Leading);

    private void ShowArray(List<PdfObject> operands)
    {
        if (operands.Count == 0 || operands[^1] is not PdfArray array)
            return;

        foreach (PdfObject entry in array)
        {
            switch (entry)
            {
                case PdfString text:
                    ShowBytes(text.Bytes);
                    break;
                case PdfNumber adjustment:
                    // A positive adjustment moves the pen left by that many
                    // thousandths of an em; a large one is a word or column gap.
                    AdvanceText(-adjustment.Value / 1000d * _state.FontSize * _state.HorizontalScale);
                    break;
            }
        }
    }

    private void ShowString(List<PdfObject> operands, int index)
    {
        if (index < 0 || index >= operands.Count || operands[index] is not PdfString text)
            return;
        ShowBytes(text.Bytes);
    }

    private void ShowBytes(byte[] bytes)
    {
        PdfFont font = _state.Font;
        if (_state.FontSize == 0)
            return;

        foreach (PdfGlyph glyph in font.Decode(bytes))
        {
            _store.Budget.ThrowIfCancelled();

            double advance = ((glyph.Width * _state.FontSize) + _state.CharSpacing +
                              (glyph.IsSpace ? _state.WordSpacing : 0)) * _state.HorizontalScale;

            if (!glyph.IsMapped && glyph.Text.Length == 0)
            {
                // An unmappable code still occupies space. Advancing without
                // emitting text keeps the following runs correctly positioned.
                NoteUnmappedGlyph();
                AdvanceText(advance);
                continue;
            }

            AppendGlyph(glyph.Text, advance);
        }
    }

    private void AppendGlyph(string text, double advance)
    {
        (double x, double y) = CurrentPen();
        double effectiveSize = EffectiveFontSize();

        if (_runOpen && !ContinuesRun(x, y))
            FlushRun();

        if (!_runOpen)
        {
            _runOpen = true;
            _runText.Clear();
            _runStartX = x;
            _runY = y;
            _runState = _state;
            _runFontSize = effectiveSize;
            _runSpaceWidth = SpaceWidth(effectiveSize);
        }

        _store.Budget.ChargeCharacters(text.Length);
        _runText.Append(text);
        AdvanceText(advance);
        _runEndX = CurrentPen().X;
    }

    // A run continues while the pen stays on the same baseline and has not jumped
    // forward by more than a space: a wider gap is a word or column boundary that
    // the reading-order pass must see.
    private bool ContinuesRun(double x, double y)
    {
        if (Math.Abs(y - _runY) > 0.1)
            return false;
        double gap = x - _runEndX;
        return gap >= -_runSpaceWidth && gap <= _runSpaceWidth * 0.28;
    }

    private void AdvanceText(double amount)
    {
        if (!double.IsFinite(amount))
            return;
        _textMatrix = PdfMatrix.Translation(amount, 0).Concat(_textMatrix);
    }

    private (double X, double Y) CurrentPen()
    {
        PdfMatrix combined = _textMatrix.Concat(_state.Matrix);
        return combined.Transform(0, _state.Rise);
    }

    private double EffectiveFontSize()
    {
        PdfMatrix combined = _textMatrix.Concat(_state.Matrix);
        double scale = combined.VerticalScale;
        double size = Math.Abs(_state.FontSize * (scale == 0 ? 1 : scale));
        return double.IsFinite(size) && size > 0 ? size : Math.Abs(_state.FontSize);
    }

    private double SpaceWidth(double effectiveSize)
    {
        // A space is roughly a quarter of the em in the Latin faces this release
        // handles; the value only has to separate words, not measure them.
        double width = effectiveSize * 0.25 * Math.Max(0.1, _state.HorizontalScale);
        return width > 0 ? width : 1;
    }

    private void FlushRun()
    {
        if (!_runOpen)
            return;

        _runOpen = false;
        string text = _pendingActualText ?? _runText.ToString();
        _runText.Clear();

        if (text.Length == 0)
            return;

        _fragments.Add(new PdfTextFragment(
            text,
            _runStartX,
            _runY,
            _runEndX,
            _runFontSize,
            _runSpaceWidth,
            _runState.Font.Family,
            _runState.Font.IsBold,
            _runState.Font.IsItalic,
            _runState.Color,
            _runState.RenderMode));

        if (_runState.RenderMode is 3 or 7 && !_store.Diagnostics.Contains(PdfDiagnosticCodes.TextVisibilityUncertain))
        {
            _store.Diagnostics.Skipped(
                PdfDiagnosticCodes.TextVisibilityUncertain,
                "The page draws text in an invisible or clipping-only rendering mode. It was extracted, because this release makes no claim about what a reader displays.");
        }
    }

    // ---- marked content, XObjects and inline images ---------------------------

    private void BeginMarkedContent(List<PdfObject> operands, PdfDictionary? resources)
    {
        // BDC carries a tag and either an inline dictionary or a name into
        // /Properties. ActualText on it replaces the glyphs it encloses.
        PdfObject? properties = operands.Count >= 1 ? operands[^1] : null;
        PdfDictionary? dictionary = properties as PdfDictionary;

        if (dictionary is null && properties is PdfName name && resources is not null &&
            _store.Resolve(resources["Properties"]) is PdfDictionary table)
        {
            dictionary = _store.Resolve(table[name.Value]) as PdfDictionary;
        }

        if (dictionary is null)
            return;

        if (_store.Resolve(dictionary["ActualText"]) is PdfString actual)
        {
            FlushRun();
            _pendingActualText = DecodeTextString(actual.Bytes);
        }
    }

    private static string DecodeTextString(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            var builder = new StringBuilder();
            for (int i = 2; i + 1 < bytes.Length; i += 2)
                builder.Append((char)((bytes[i] << 8) | bytes[i + 1]));
            return builder.ToString();
        }

        var latin = new StringBuilder(bytes.Length);
        foreach (byte b in bytes)
            latin.Append(PdfDocEncoding.ToChar(b));
        return latin.ToString();
    }

    private void InvokeXObject(List<PdfObject> operands, PdfDictionary? resources, int depth)
    {
        if (operands.Count == 0 || operands[^1] is not PdfName name || resources is null)
            return;
        if (_store.Resolve(resources["XObject"]) is not PdfDictionary xobjects)
            return;
        if (_store.Resolve(xobjects[name.Value]) is not PdfStream stream)
            return;

        string subtype = (_store.Resolve(stream.Dictionary["Subtype"]) as PdfName)?.Value ?? string.Empty;

        if (subtype == "Image")
        {
            NoteImage(stream);
            return;
        }

        if (subtype != "Form")
            return;

        if (depth >= _store.Budget.Limits.MaxFormRecursionDepth)
        {
            _store.Diagnostics.Warning(
                PdfDiagnosticCodes.Limit,
                "A Form XObject nested past the recursion limit and was skipped.");
            return;
        }

        if (!_activeForms.Add(stream.Dictionary))
        {
            _store.Diagnostics.Warning(PdfDiagnosticCodes.ObjectCycle, "A Form XObject invoked itself; the repeat was skipped.");
            return;
        }

        try
        {
            byte[]? content = DecodeContent(stream);
            if (content is null)
                return;

            FlushRun();
            GraphicsState saved = _state;
            PdfMatrix savedText = _textMatrix;
            PdfMatrix savedLine = _lineMatrix;

            if (_store.Resolve(stream.Dictionary["Matrix"]) is PdfArray matrixArray && TryMatrix(matrixArray, out PdfMatrix formMatrix))
                _state = _state.WithMatrix(formMatrix.Concat(_state.Matrix));

            PdfDictionary? formResources = _store.Resolve(stream.Dictionary["Resources"]) as PdfDictionary ?? resources;
            Execute(content, formResources, depth + 1);
            FlushRun();

            _state = saved;
            _textMatrix = savedText;
            _lineMatrix = savedLine;
        }
        finally
        {
            _activeForms.Remove(stream.Dictionary);
        }
    }

    /// <summary>
    /// Consumes an inline image. The scan for <c>EI</c> is bounded by the content
    /// stream itself and requires the keyword to be delimited, so image data that
    /// happens to contain the bytes "EI" cannot end the image early — and a
    /// truncated image cannot loop.
    /// </summary>
    private void SkipInlineImage(PdfLexer lexer)
    {
        byte[] data = lexer.Data;
        int position = lexer.Position;

        // Skip the parameter dictionary up to ID.
        while (position < lexer.End)
        {
            if (data[position] == (byte)'I' && position + 1 < lexer.End && data[position + 1] == (byte)'D')
            {
                position += 2;
                break;
            }

            position++;
        }

        // Exactly one whitespace byte separates ID from the samples.
        if (position < lexer.End && PdfLexer.IsWhitespace(data[position]))
            position++;

        while (position + 1 < lexer.End)
        {
            if (data[position] == (byte)'E' && data[position + 1] == (byte)'I' &&
                (position == 0 || PdfLexer.IsWhitespace(data[position - 1])) &&
                (position + 2 >= lexer.End || !PdfLexer.IsRegular(data[position + 2])))
            {
                position += 2;
                break;
            }

            position++;
        }

        lexer.Position = Math.Min(position, lexer.End);
        NoteInlineImage();
    }

    // ---- diagnostics ----------------------------------------------------------

    private void NoteImage(PdfStream stream)
    {
        string filter = (_store.Resolve(stream.Dictionary["Filter"]) as PdfName)?.Value ?? string.Empty;
        string code = filter.Length > 0 && PdfFilterNames.IsImageFilter(filter)
            ? PdfFilterNames.UnsupportedDiagnosticFor(filter)
            : PdfDiagnosticCodes.ImageNotComposed;

        _store.Diagnostics.Skipped(
            code,
            "The page draws a raster image. This build composes no image decoder, so the image was detected and skipped.");
    }

    private void NoteInlineImage() =>
        _store.Diagnostics.Skipped(
            PdfDiagnosticCodes.ImageNotComposed,
            "The page draws an inline image. This build composes no image decoder, so the image was detected and skipped.");

    private void NoteVectorArtwork()
    {
        if (!_store.Diagnostics.Contains(PdfDiagnosticCodes.VectorArtworkDropped))
        {
            _store.Diagnostics.Skipped(
                PdfDiagnosticCodes.VectorArtworkDropped,
                "The page draws vector artwork, which a logical rich-text document cannot represent. It was dropped.");
        }
    }

    private void NoteUnmappedGlyph()
    {
        if (!_store.Diagnostics.Contains(PdfDiagnosticCodes.TextMappingMissing))
        {
            _store.Diagnostics.Skipped(
                PdfDiagnosticCodes.TextMappingMissing,
                "Some character codes had no reliable Unicode mapping and were omitted rather than guessed.");
        }
    }

    // ---- operand helpers ------------------------------------------------------

    private static double Number(List<PdfObject> operands, int index) =>
        index >= 0 && index < operands.Count && operands[index] is PdfNumber number && double.IsFinite(number.Value)
            ? number.Value
            : 0d;

    private static bool TryMatrix(List<PdfObject> operands, out PdfMatrix matrix)
    {
        matrix = PdfMatrix.Identity;
        if (operands.Count < 6)
            return false;

        int start = operands.Count - 6;
        Span<double> values = stackalloc double[6];
        for (int i = 0; i < 6; i++)
        {
            if (operands[start + i] is not PdfNumber number || !double.IsFinite(number.Value))
                return false;
            values[i] = number.Value;
        }

        matrix = new PdfMatrix(values[0], values[1], values[2], values[3], values[4], values[5]);
        return matrix.IsFinite;
    }

    private static bool TryMatrix(PdfArray array, out PdfMatrix matrix)
    {
        matrix = PdfMatrix.Identity;
        if (array.Count < 6)
            return false;

        Span<double> values = stackalloc double[6];
        for (int i = 0; i < 6; i++)
        {
            if (array[i] is not PdfNumber number || !double.IsFinite(number.Value))
                return false;
            values[i] = number.Value;
        }

        matrix = new PdfMatrix(values[0], values[1], values[2], values[3], values[4], values[5]);
        return matrix.IsFinite;
    }

    private static BColor Gray(List<PdfObject> operands, int _)
    {
        double value = Number(operands, operands.Count - 1);
        byte level = ToByte(value);
        return new BColor(level, level, level);
    }

    private static BColor Rgb(List<PdfObject> operands)
    {
        if (operands.Count < 3)
            return BColor.Black;
        int start = operands.Count - 3;
        return new BColor(
            ToByte(Number(operands, start)),
            ToByte(Number(operands, start + 1)),
            ToByte(Number(operands, start + 2)));
    }

    /// <summary>
    /// Converts DeviceCMYK to RGB with the format's own simple relationship. It
    /// is a device conversion, not a colour-managed one, and the codec never
    /// claims colorimetric fidelity for it.
    /// </summary>
    private static BColor Cmyk(List<PdfObject> operands)
    {
        if (operands.Count < 4)
            return BColor.Black;
        int start = operands.Count - 4;
        double c = Clamp(Number(operands, start));
        double m = Clamp(Number(operands, start + 1));
        double y = Clamp(Number(operands, start + 2));
        double k = Clamp(Number(operands, start + 3));
        return new BColor(
            ToByte((1 - c) * (1 - k)),
            ToByte((1 - m) * (1 - k)),
            ToByte((1 - y) * (1 - k)));
    }

    // sc/scn take one, three, or four components depending on the selected space.
    private static BColor FromComponents(List<PdfObject> operands)
    {
        int numeric = 0;
        foreach (PdfObject operand in operands)
        {
            if (operand is PdfNumber)
                numeric++;
        }

        return numeric switch
        {
            1 => Gray(operands, 0),
            3 => Rgb(operands),
            4 => Cmyk(operands),
            _ => BColor.Black,
        };
    }

    private static double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    private static byte ToByte(double value) => (byte)Math.Round(Clamp(value) * 255);

    /// <summary>The interpreter's state, kept immutable so <c>q</c>/<c>Q</c> is a push and a pop.</summary>
    private readonly struct GraphicsState
    {
        private GraphicsState(
            PdfMatrix matrix,
            PdfFont font,
            double fontSize,
            double charSpacing,
            double wordSpacing,
            double horizontalScale,
            double leading,
            double rise,
            int renderMode,
            BColor color)
        {
            Matrix = matrix;
            Font = font;
            FontSize = fontSize;
            CharSpacing = charSpacing;
            WordSpacing = wordSpacing;
            HorizontalScale = horizontalScale;
            Leading = leading;
            Rise = rise;
            RenderMode = renderMode;
            Color = color;
        }

        public static GraphicsState Initial { get; } = new(
            PdfMatrix.Identity, PdfFont.Fallback, 0, 0, 0, 1, 0, 0, 0, BColor.Black);

        public PdfMatrix Matrix { get; }

        public PdfFont Font { get; }

        public double FontSize { get; }

        public double CharSpacing { get; }

        public double WordSpacing { get; }

        public double HorizontalScale { get; }

        public double Leading { get; }

        public double Rise { get; }

        public int RenderMode { get; }

        public BColor Color { get; }

        public GraphicsState WithMatrix(PdfMatrix matrix) => matrix.IsFinite
            ? new GraphicsState(matrix, Font, FontSize, CharSpacing, WordSpacing, HorizontalScale, Leading, Rise, RenderMode, Color)
            : this;

        public GraphicsState WithFont(PdfFont font, double size) =>
            new(Matrix, font, double.IsFinite(size) ? size : 0, CharSpacing, WordSpacing, HorizontalScale, Leading, Rise, RenderMode, Color);

        public GraphicsState WithCharSpacing(double value) =>
            new(Matrix, Font, FontSize, Finite(value), WordSpacing, HorizontalScale, Leading, Rise, RenderMode, Color);

        public GraphicsState WithWordSpacing(double value) =>
            new(Matrix, Font, FontSize, CharSpacing, Finite(value), HorizontalScale, Leading, Rise, RenderMode, Color);

        public GraphicsState WithHorizontalScale(double value) =>
            new(Matrix, Font, FontSize, CharSpacing, WordSpacing, value is > 0 and < 100 ? value : 1, Leading, Rise, RenderMode, Color);

        public GraphicsState WithLeading(double value) =>
            new(Matrix, Font, FontSize, CharSpacing, WordSpacing, HorizontalScale, Finite(value), Rise, RenderMode, Color);

        public GraphicsState WithRise(double value) =>
            new(Matrix, Font, FontSize, CharSpacing, WordSpacing, HorizontalScale, Leading, Finite(value), RenderMode, Color);

        public GraphicsState WithRenderMode(int value) =>
            new(Matrix, Font, FontSize, CharSpacing, WordSpacing, HorizontalScale, Leading, Rise, value is >= 0 and <= 7 ? value : 0, Color);

        public GraphicsState WithColor(BColor value) =>
            new(Matrix, Font, FontSize, CharSpacing, WordSpacing, HorizontalScale, Leading, Rise, RenderMode, value);

        private static double Finite(double value) => double.IsFinite(value) ? value : 0;
    }
}
