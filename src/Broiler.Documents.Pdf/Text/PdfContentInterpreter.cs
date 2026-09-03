using System;
using System.Collections.Generic;
using System.Text;
using Broiler.Documents.Model;
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
    /// <summary>
    /// How thin an axis-aligned shape has to be, in points, before it reads as a
    /// rule rather than a filled area. Three points is about the heaviest
    /// underline or table border a text document draws; past that a shape is
    /// wide enough to be a panel, and calling it a rule would say the document
    /// had structure it does not.
    /// </summary>
    private const double RuleThickness = 3.0;

    /// <summary>
    /// How long a thin shape has to run before it is a rule at all, rather than
    /// a tick, a dot leader's dot, or a checkbox edge.
    /// </summary>
    private const double MinimumRuleLength = 6.0;

    /// <summary>
    /// How far off an axis a segment may drift and still count as along it. A
    /// hairline that misses by a hundredth of a point is a rule; anything looser
    /// would start calling shallow diagonals horizontal.
    /// </summary>
    private const double AxisTolerance = 0.01;

    /// <summary>
    /// How many parameters an inline image's abbreviated dictionary is read for.
    /// The dictionary is a description of a construct that is being skipped, so
    /// it is bounded well below anything a real one uses.
    /// </summary>
    private const int MaxInlineImageParameters = 32;

    private readonly PdfObjectStore _store;
    private readonly List<PdfTextFragment> _fragments = [];
    private readonly Dictionary<PdfDictionary, PdfFont> _fontCache = new();
    private readonly HashSet<PdfDictionary> _activeForms = [];

    private readonly Stack<GraphicsState> _stack = new();
    private GraphicsState _state = GraphicsState.Initial;
    private readonly DocumentConversionContextBuilder? _resources;
    private readonly List<PdfPlacedImage> _placedImages = [];
    private PdfMatrix _textMatrix = PdfMatrix.Identity;
    private PdfMatrix _lineMatrix = PdfMatrix.Identity;
    private string? _pendingActualText;

    /// <summary>The value of <see cref="_hiddenDepth"/> when nothing is hidden.</summary>
    private const int NotHidden = -1;

    private readonly PdfOptionalContent _optionalContent;

    /// <summary>
    /// How many marked-content sequences are open. Counted for both `BMC` and
    /// `BDC`, because the matching `EMC` does not say which it closes and a
    /// layer has to end at its own.
    /// </summary>
    private int _markedContentDepth;

    /// <summary>
    /// The marked-content level at which the current layer began hiding, or
    /// <see cref="NotHidden"/>. Nested layers inside a hidden one do not move it:
    /// the outermost decision stands until its own `EMC`.
    /// </summary>
    private int _hiddenDepth = NotHidden;

    /// <summary>
    /// Whether content is being drawn outside the default presentation. State
    /// operators still run while this holds — a layer's `cm`, `Tf`, and `q`/`Q`
    /// affect what follows it — and only content is withheld.
    /// </summary>
    private bool Hidden => _hiddenDepth != NotHidden;

    // Path construction state. The geometry is never rendered — it is tracked
    // only so that a paint operator can say what shape it dropped.
    private double _pathMinX;
    private double _pathMinY;
    private double _pathMaxX;
    private double _pathMaxY;
    private double _pathX;
    private double _pathY;
    private double _pathStartX;
    private double _pathStartY;
    private bool _pathOpen;
    private bool _pathIrregular;

    // The run being accumulated; flushed when style, baseline, or spacing breaks.
    private readonly StringBuilder _runText = new();
    private double _runStartX;
    private double _runY;
    private double _runEndX;
    private GraphicsState _runState = GraphicsState.Initial;
    private double _runFontSize;
    private double _runSpaceWidth;
    private bool _runOpen;

    public PdfContentInterpreter(
        PdfObjectStore store,
        DocumentConversionContextBuilder? resources = null,
        PdfOptionalContent? optionalContent = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _resources = resources;
        _optionalContent = optionalContent ?? PdfOptionalContent.None;
    }

    /// <summary>
    /// The images this page drew that the caller's policy allowed into the model,
    /// with the box each is drawn in. Empty when no policy permits extraction, or
    /// when nothing decoded to samples the model can take.
    /// </summary>
    public IReadOnlyList<PdfPlacedImage> PlacedImages => _placedImages;

    /// <summary>Runs a page's content and returns the text runs it placed.</summary>
    public IReadOnlyList<PdfTextFragment> Run(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        _fragments.Clear();
        _placedImages.Clear();
        _state = GraphicsState.Initial;
        _stack.Clear();
        ResetPath();

        // A page's marked content cannot span pages, so an unbalanced BDC on one
        // must not leave the next hidden.
        _markedContentDepth = 0;
        _hiddenDepth = NotHidden;

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

                // Marked content: ActualText replaces whatever the glyphs say,
                // and an /OC tag can put everything up to the matching EMC
                // outside the document's default presentation.
                case "BDC":
                    BeginMarkedContent(operands, resources);
                    break;
                case "BMC":
                    // Nothing is tagged here, but the level still counts: a plain
                    // sequence inside a hidden one would otherwise close it at its
                    // own EMC and let the rest of the layer back into the text.
                    _markedContentDepth++;
                    break;
                case "EMC":
                    // Flush first: the run inside the marked-content sequence is
                    // what ActualText replaces, and clearing it before the flush
                    // would emit the glyphs the tag was there to override.
                    FlushRun();
                    _pendingActualText = null;
                    EndMarkedContent();
                    break;

                // External objects.
                case "Do":
                    InvokeXObject(operands, resources, depth);
                    break;
                case "BI":
                    SkipInlineImage(lexer);
                    break;

                // Path construction. Nothing here is drawn or kept; the points
                // are followed only far enough to tell a rule from a picture
                // when the painting operator arrives.
                case "m":
                    MoveTo(Number(operands, 0), Number(operands, 1));
                    break;
                case "l":
                    LineTo(Number(operands, 0), Number(operands, 1));
                    break;
                case "c":
                    CurveTo(Number(operands, 4), Number(operands, 5));
                    break;
                case "v":
                case "y":
                    CurveTo(Number(operands, 2), Number(operands, 3));
                    break;
                case "re":
                    AddRectangle(Number(operands, 0), Number(operands, 1), Number(operands, 2), Number(operands, 3));
                    break;
                case "h":
                    ClosePath();
                    break;

                // Path painting. Vector artwork has no logical representation, so
                // it is classified, counted, and dropped rather than approximated.
                case "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*":
                    // Artwork in a layer outside the presentation is not artwork
                    // this document dropped: reporting it would inflate the count
                    // with shapes the default configuration never showed.
                    if (!Hidden)
                        NoteVectorArtwork(ClassifyPath());
                    ResetPath();
                    break;
                case "sh":
                    // A shading paints without a path of its own.
                    if (!Hidden)
                        NoteVectorArtwork(PdfArtworkKind.Shading);
                    break;
                case "n":
                    // A path used only to clip paints nothing, so it drops nothing.
                    ResetPath();
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

        // The single point where text becomes a fragment, and so the single place
        // a hidden layer has to be withheld. Positioning is tracked in the text
        // matrices rather than here, so dropping the run costs nothing that the
        // visible content after the layer depends on.
        if (Hidden)
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

        // Reported per run rather than once per document: the sink keeps a single
        // entry either way, and letting it count tells a reader whether one
        // watermark was invisible or the whole page was.
        if (_runState.RenderMode is 3 or 7)
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
        // /Properties. ActualText on it replaces the glyphs it encloses; an /OC
        // tag names the layer they belong to.
        _markedContentDepth++;

        PdfObject? properties = operands.Count >= 1 ? operands[^1] : null;
        string tag = operands.Count >= 2 && operands[^2] is PdfName tagName ? tagName.Value : string.Empty;

        // The property entry is resolved through /Properties as it stands rather
        // than as a resolved dictionary, because an optional-content group is
        // identified by the object it resolves to and the lookup must not lose
        // the reference on the way.
        PdfObject? entry = properties;
        if (properties is PdfName name && resources is not null &&
            _store.Resolve(resources["Properties"]) is PdfDictionary table)
        {
            entry = table[name.Value];
        }

        if (tag == "OC" && !Hidden)
            BeginOptionalContent(entry);

        if (_store.Resolve(entry) is not PdfDictionary dictionary)
            return;

        if (_store.Resolve(dictionary["ActualText"]) is PdfString actual)
        {
            FlushRun();
            _pendingActualText = DecodeTextString(actual.Bytes);
        }
    }

    /// <summary>
    /// Enters a layer, and starts hiding when the document's own default
    /// configuration puts it outside the presentation.
    /// </summary>
    private void BeginOptionalContent(PdfObject? entry)
    {
        if (_optionalContent.IsHidden(_store, entry, out bool undecidable))
        {
            if (!_optionalContent.Enforced)
            {
                _store.Features.NoteOptionalContentKept(_store.CurrentPage);
                return;
            }

            // Whatever run is open belongs to the visible content before this
            // point, so it is emitted rather than swallowed by the layer.
            FlushRun();
            _hiddenDepth = _markedContentDepth;
            _store.Features.NoteOptionalContentHidden(_store.CurrentPage);
            return;
        }

        if (undecidable)
            _store.Features.NoteOptionalContentUndecidable(_store.CurrentPage);
    }

    /// <summary>
    /// Leaves one marked-content level, and stops hiding on leaving the level
    /// that started it.
    /// </summary>
    private void EndMarkedContent()
    {
        if (_hiddenDepth == _markedContentDepth)
            _hiddenDepth = NotHidden;

        // An unbalanced EMC is a malformed stream, not a licence to go negative
        // and let a later EMC re-open a layer that was never entered.
        if (_markedContentDepth > 0)
            _markedContentDepth--;
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

        // An XObject carries its own layer membership rather than relying on a
        // marked-content sequence around it, and either can put it outside the
        // presentation. A form is skipped whole: its content is hidden with it,
        // and `Do` restores the graphics state around a form anyway, so running
        // it would change nothing that survives.
        if (Hidden)
            return;

        if (_optionalContent.IsHidden(_store, stream.Dictionary["OC"], out bool undecidable))
        {
            if (_optionalContent.Enforced)
            {
                _store.Features.NoteOptionalContentHidden(_store.CurrentPage);
                return;
            }

            _store.Features.NoteOptionalContentKept(_store.CurrentPage);
        }
        else if (undecidable)
        {
            _store.Features.NoteOptionalContentUndecidable(_store.CurrentPage);
        }

        if (subtype == "Image")
        {
            NoteImage(stream, resources);
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
        int parametersStart = lexer.Position;
        int position = parametersStart;
        int parametersEnd = lexer.End;

        // Skip the parameter dictionary up to ID, remembering where it ended so
        // the image can be described from its own declaration.
        while (position < lexer.End)
        {
            if (data[position] == (byte)'I' && position + 1 < lexer.End && data[position + 1] == (byte)'D')
            {
                parametersEnd = position;
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

        // The samples are consumed either way — the scan is what keeps the lexer
        // on an operator boundary — but an image in a layer outside the
        // presentation is not one this document asked to show.
        if (!Hidden)
            NoteInlineImage(data, parametersStart, parametersEnd);
    }

    // ---- diagnostics ----------------------------------------------------------

    /// <summary>
    /// Records a skipped image XObject and what its dictionary declared about it.
    /// </summary>
    /// <remarks>
    /// The dictionary is read; the samples never are. Reporting the size, depth,
    /// colour space, and filter chain costs nothing a skip did not already pay
    /// for, and it is the difference between "an image was skipped" and a
    /// statement of exactly which decoder tuples this document would need — the
    /// question IP-005 has to answer before <c>DCTDecode</c> can be composed.
    /// </remarks>
    private void NoteImage(PdfStream stream, PdfDictionary? resources)
    {
        PdfDictionary dictionary = stream.Dictionary;
        PdfObject? filter = dictionary["Filter"];

        var shape = new PdfImageShape(
            Integer(dictionary, "Width", "W"),
            Integer(dictionary, "Height", "H"),
            Integer(dictionary, "BitsPerComponent", "BPC"),
            DescribeColorSpace(dictionary["ColorSpace"], dictionary["ImageMask"], inline: false),
            DescribeFilters(filter),
            IsInline: false);

        // With every filter in the chain composed, the dictionary stops being the
        // last word. It is what the document claims; the decode is what is true,
        // and the two disagreeing is worth knowing. A chain of byte-stream
        // filters — or no filter at all — qualifies exactly as a composed image
        // codec does: Flate samples are as reachable as JPEG ones, and treating
        // them otherwise told the caller this build had no decoder for an image
        // whose decoder it had composed all along.
        if (CanDecode(filter) && DecodeToDescribe(stream) is PdfStreamDecodeResult decoded)
        {
            if (decoded.Succeeded)
            {
                bool codec = HasImageFilter(filter);
                _store.Features.NoteDecodedImage(shape, decoded.Data!.LongLength, _store.CurrentPage, codec);
                Project(stream, shape, decoded.Data, codec, resources);
                return;
            }

            _store.Features.NoteImage(
                decoded.DiagnosticCode ?? ImageDiagnosticFor(filter),
                shape,
                _store.CurrentPage,
                decoded.Message);
            return;
        }

        _store.Features.NoteImage(ImageDiagnosticFor(filter), shape, _store.CurrentPage);
    }

    /// <summary>
    /// True when this chain ends in an image codec rather than in byte-stream
    /// filters, which decides what a successful decode's samples look like.
    /// </summary>
    private bool HasImageFilter(PdfObject? filter)
    {
        foreach (string name in FilterNames(filter))
        {
            if (PdfFilterNames.IsImageFilter(name))
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when every filter in this chain has a composed implementation, so a
    /// decode would describe the image rather than fail for want of a decoder.
    /// An empty chain qualifies: raw samples need no decoder at all.
    /// </summary>
    private bool CanDecode(PdfObject? filter)
    {
        foreach (string name in FilterNames(filter))
        {
            if (!_store.Filters.IsComposed(name))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Decodes an image for the sole purpose of describing it, or returns null
    /// when the budget declined the attempt and the dictionary stays the only
    /// account of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing decoded here reaches the model, because the model carries no
    /// images (PDF roadmap §6.2). The decode buys a true sentence and an honest
    /// byte count, so it spends like the diagnostic work it is: one image may
    /// spend up to <see cref="PdfLimits.MaxDescribedImageBytes"/> of encoded
    /// input, and descriptions stop entirely once half the read's decoded-byte
    /// allowance is gone. Describing images can therefore never be the reason a
    /// document's own content runs out of budget.
    /// </para>
    /// <para>
    /// A declined attempt reports the image from its dictionary, under the same
    /// code and the same sentence an image this build cannot reach would use. It
    /// does not say a budget stopped it: how much allowance was left when a given
    /// image was reached is a fact about the read, not about the document, and a
    /// diagnostic names constructs.
    /// </para>
    /// </remarks>
    private PdfStreamDecodeResult? DecodeToDescribe(PdfStream stream)
    {
        PdfWorkBudget budget = _store.Budget;
        if (stream.RawData.LongLength > budget.Limits.MaxDescribedImageBytes ||
            budget.RemainingDecodedBytes <= budget.Limits.MaxDecodedStreamBytes / 2)
        {
            return null;
        }

        try
        {
            return _store.Filters.DecodeImage(stream, _store.Resolve, budget);
        }
        catch (PdfLimitExceededException)
        {
            // Describing a construct that is being skipped must not be able to
            // fail the document — the same rule the inline-image parameter read
            // follows. A charge that lands past a limit ends this image's
            // description and nothing else; the next charge for work the
            // document actually needs raises it again.
            return null;
        }
    }

    /// <summary>
    /// Records a skipped inline image, described from the abbreviated parameter
    /// dictionary the <c>ID</c> scan already delimited.
    /// </summary>
    private void NoteInlineImage(byte[] data, int start, int end)
    {
        // An inline image's samples sit in the content stream rather than in a
        // stream object, so there is nothing for the filter pipeline to decode
        // here. It is reported from its declaration whether a decoder is
        // composed or not.
        PdfDictionary parameters = ReadInlineImageParameters(data, start, end);
        PdfObject? filter = parameters["F"] ?? parameters["Filter"];

        _store.Features.NoteImage(
            ImageDiagnosticFor(filter),
            new PdfImageShape(
                Integer(parameters, "W", "Width"),
                Integer(parameters, "H", "Height"),
                Integer(parameters, "BPC", "BitsPerComponent"),
                DescribeColorSpace(parameters["CS"] ?? parameters["ColorSpace"], parameters["IM"] ?? parameters["ImageMask"], inline: true),
                DescribeFilters(filter),
                IsInline: true),
            _store.CurrentPage);
    }

    private void NoteVectorArtwork(PdfArtworkKind kind) =>
        _store.Features.NoteArtwork(kind, _store.CurrentPage);

    /// <summary>
    /// Reports one character code that could not be mapped. Every one is
    /// reported, not just the first: the sink collapses them into a single entry,
    /// and the count it keeps is the difference between a document that lost one
    /// glyph and one that lost a language.
    /// </summary>
    private void NoteUnmappedGlyph() =>
        _store.Diagnostics.Skipped(
            PdfDiagnosticCodes.TextMappingMissing,
            "Some character codes had no reliable Unicode mapping and were omitted rather than guessed.");

    // ---- describing what was skipped ------------------------------------------

    /// <summary>
    /// Parses the abbreviated key/value pairs between <c>BI</c> and <c>ID</c>.
    /// The range is the one the <c>ID</c> scan already bounded, so this reads
    /// parameters and can never wander into sample data.
    /// </summary>
    private PdfDictionary ReadInlineImageParameters(byte[] data, int start, int end)
    {
        var parameters = new PdfDictionary();
        if (end <= start)
            return parameters;

        try
        {
            var lexer = new PdfLexer(data, _store.Budget.Limits, start, end);
            var parser = new PdfObjectParser(lexer, _store.Budget);

            while (parameters.Count < MaxInlineImageParameters)
            {
                // End of the range, or anything that is not a key, ends the read.
                if (parser.ParseObject() is not PdfName key)
                    break;

                parameters[key.Value] = parser.ParseObject();
            }
        }
        catch (PdfLimitExceededException)
        {
            // Describing a construct that is being skipped must not be able to
            // fail the document. Whatever was parsed before the budget bound is
            // still worth reporting; the next real charge will raise this again.
        }

        return parameters;
    }

    /// <summary>
    /// The diagnostic an undecoded image reports: the code belonging to the first
    /// image filter in its chain, so a JPEG says JPEG and names its own register
    /// row, and the generic not-composed code when the chain holds none.
    /// </summary>
    private string ImageDiagnosticFor(PdfObject? filter)
    {
        foreach (string name in FilterNames(filter))
        {
            if (PdfFilterNames.IsImageFilter(name))
                return PdfFilterNames.UnsupportedDiagnosticFor(name);
        }

        return PdfDiagnosticCodes.ImageNotComposed;
    }

    /// <summary>The filter chain as canonical names, in the order it is applied.</summary>
    private List<string> FilterNames(PdfObject? filter)
    {
        var names = new List<string>(2);

        switch (_store.Resolve(filter))
        {
            case PdfName single:
                names.Add(PdfFilterNames.Canonicalize(single.Value));
                break;
            case PdfArray array:
                foreach (PdfObject entry in array)
                {
                    if (_store.Resolve(entry) is PdfName name)
                        names.Add(PdfFilterNames.Canonicalize(name.Value));
                }

                break;
        }

        return names;
    }

    private string DescribeFilters(PdfObject? filter) => string.Join("+", FilterNames(filter));

    /// <summary>
    /// The colour space as its family name only: a named space by name, an array
    /// by its family, and a stencil mask as a mask. A family name is a construct
    /// the format defines, not a value out of the document, so it stays reportable
    /// under the privacy rule even where the space is a named or separation one.
    /// </summary>
    private string DescribeColorSpace(PdfObject? colorSpace, PdfObject? imageMask, bool inline)
    {
        if (_store.Resolve(imageMask) is PdfBoolean mask && mask.Value)
            return "ImageMask";

        return _store.Resolve(colorSpace) switch
        {
            PdfName name => ExpandColorSpace(name.Value, inline),
            PdfArray array when array.Count > 0 && _store.Resolve(array[0]) is PdfName family => ExpandColorSpace(family.Value, inline),
            PdfArray => "colour-space array",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Expands the abbreviated colour-space names an inline image is allowed to
    /// use, so one document's images are inventoried under one set of names
    /// whether they were drawn inline or as XObjects.
    /// </summary>
    /// <remarks>
    /// The expansion applies only inline, where the format reserves these four
    /// spellings. A resource dictionary may name a colour space anything it
    /// likes, and rewriting a space genuinely called <c>/G</c> would report a
    /// construct the document does not contain.
    /// </remarks>
    private static string ExpandColorSpace(string name, bool inline) =>
        inline
            ? name switch
            {
                "G" => "DeviceGray",
                "RGB" => "DeviceRGB",
                "CMYK" => "DeviceCMYK",
                "I" => "Indexed",
                _ => name,
            }
            : name;

    /// <summary>
    /// A non-negative integer entry, under either its full or its abbreviated
    /// inline-image key, or zero when the entry is absent or unusable.
    /// </summary>
    private int Integer(PdfDictionary dictionary, string key, string alternate)
    {
        PdfObject? value = _store.Resolve(dictionary[key]) ?? _store.Resolve(dictionary[alternate]);
        return value is PdfNumber number && double.IsFinite(number.Value) && number.Value is >= 0 and <= int.MaxValue
            ? (int)number.Value
            : 0;
    }

    // ---- path tracking --------------------------------------------------------

    /// <summary>
    /// What the path just painted looked like. Only two questions decide it: was
    /// the path built from axis-aligned straight lines, and if so, is its box thin
    /// enough to be a rule. Everything else is a picture.
    /// </summary>
    private PdfArtworkKind ClassifyPath()
    {
        if (!_pathOpen || _pathIrregular)
            return PdfArtworkKind.Path;

        double across = Math.Min(_pathMaxX - _pathMinX, _pathMaxY - _pathMinY);
        double along = Math.Max(_pathMaxX - _pathMinX, _pathMaxY - _pathMinY);

        return across <= RuleThickness && along >= MinimumRuleLength
            ? PdfArtworkKind.Rule
            : PdfArtworkKind.Block;
    }

    private void ResetPath()
    {
        _pathOpen = false;
        _pathIrregular = false;
    }

    private void MoveTo(double x, double y)
    {
        (double deviceX, double deviceY) = _state.Matrix.Transform(x, y);
        _pathStartX = deviceX;
        _pathStartY = deviceY;
        _pathX = deviceX;
        _pathY = deviceY;
        Extend(deviceX, deviceY);
    }

    private void LineTo(double x, double y)
    {
        (double deviceX, double deviceY) = _state.Matrix.Transform(x, y);
        NoteSegment(deviceX, deviceY);
        _pathX = deviceX;
        _pathY = deviceY;
        Extend(deviceX, deviceY);
    }

    /// <summary>
    /// Follows a Bézier to its endpoint. Only the endpoint is tracked: the
    /// control points can push the true curve outside this box, which would
    /// matter to a renderer, and does not matter to a classifier that has already
    /// called the path a picture.
    /// </summary>
    private void CurveTo(double x, double y)
    {
        _pathIrregular = true;
        (double deviceX, double deviceY) = _state.Matrix.Transform(x, y);
        _pathX = deviceX;
        _pathY = deviceY;
        Extend(deviceX, deviceY);
    }

    private void ClosePath()
    {
        if (_pathOpen)
            NoteSegment(_pathStartX, _pathStartY);

        _pathX = _pathStartX;
        _pathY = _pathStartY;
    }

    private void AddRectangle(double x, double y, double width, double height)
    {
        (double x0, double y0) = _state.Matrix.Transform(x, y);
        (double x1, double y1) = _state.Matrix.Transform(x + width, y);
        (double x2, double y2) = _state.Matrix.Transform(x + width, y + height);
        (double x3, double y3) = _state.Matrix.Transform(x, y + height);

        // A rectangle in user space is only a rectangle in device space while the
        // transform keeps it one; under a rotation it is as diagonal as any other
        // path, and saying otherwise would report rules a reader never saw.
        if (!IsAxisAligned(x1 - x0, y1 - y0) || !IsAxisAligned(x3 - x0, y3 - y0))
            _pathIrregular = true;

        Extend(x0, y0);
        Extend(x1, y1);
        Extend(x2, y2);
        Extend(x3, y3);

        _pathStartX = x0;
        _pathStartY = y0;
        _pathX = x0;
        _pathY = y0;
    }

    private void NoteSegment(double x, double y)
    {
        if (!IsAxisAligned(x - _pathX, y - _pathY))
            _pathIrregular = true;
    }

    private static bool IsAxisAligned(double dx, double dy) =>
        Math.Abs(dx) <= AxisTolerance || Math.Abs(dy) <= AxisTolerance;

    /// <summary>Grows the path's bounding box, in device space, to hold a point.</summary>
    private void Extend(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            // A point that is not a number makes the box meaningless. The path
            // still painted something, so it is reported — just not as a rule.
            _pathIrregular = true;
            return;
        }

        if (!_pathOpen)
        {
            _pathOpen = true;
            _pathMinX = _pathMaxX = x;
            _pathMinY = _pathMaxY = y;
            return;
        }

        _pathMinX = Math.Min(_pathMinX, x);
        _pathMaxX = Math.Max(_pathMaxX, x);
        _pathMinY = Math.Min(_pathMinY, y);
        _pathMaxY = Math.Max(_pathMaxY, y);
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

    /// <summary>
    /// Turns decoded samples into an image in the model, when the samples are a
    /// picture, the dictionary does not reinterpret them, and the caller's policy
    /// permits extraction.
    /// </summary>
    /// <param name="codec">
    /// True when an image codec produced the samples. A codec has already
    /// resolved the frame's colour space and normalized the result to RGBA, so
    /// its output stands on its own; a byte-stream chain yields the image's own
    /// samples instead, and only the dictionary says what they mean.
    /// </param>
    /// <remarks>
    /// <para>
    /// What is refused here is refused rather than guessed at, because each case
    /// would otherwise produce a plausible wrong picture instead of an error. A
    /// <c>/ImageMask</c> stencil paints the current fill colour through a
    /// one-bit shape, so projecting it as black-and-white invents a colour the
    /// page never used. An <c>/SMask</c> or a colour-key <c>/Mask</c> carries the
    /// transparency the picture is drawn with, and this build composites
    /// neither, so carrying the image opaque puts a solid box where a logo's
    /// transparent ground belongs. A colour space outside the approved raw-sample
    /// subset needs a transform this project does not own.
    /// </para>
    /// <para>
    /// Every refusal names what it met, because the reasons are answered by
    /// different work: composing a decoder, widening the approved subset, and
    /// fixing a document that contradicts itself are three different things for a
    /// caller to be told.
    /// </para>
    /// </remarks>
    private void Project(PdfStream stream, in PdfImageShape shape, byte[] samples, bool codec, PdfDictionary? resources)
    {
        if (_resources is null)
            return;

        void NotProjected(string reason) => _store.Features.NoteImageNotProjected(_store.CurrentPage, reason);

        if (shape.Width <= 0 || shape.Height <= 0)
        {
            NotProjected("an unstated pixel size");
            return;
        }

        PdfDictionary dictionary = stream.Dictionary;

        if (_store.Resolve(dictionary["ImageMask"]) is PdfBoolean mask && mask.Value)
        {
            NotProjected("a stencil mask");
            return;
        }

        if (dictionary["SMask"] is not null || dictionary["Mask"] is not null)
        {
            NotProjected("transparency this build does not composite");
            return;
        }

        long pixels = (long)shape.Width * shape.Height;
        if (pixels > int.MaxValue / BPixelBuffer.BytesPerPixel)
        {
            NotProjected("a pixel count past what one buffer holds");
            return;
        }

        long rgbaBytes = pixels * BPixelBuffer.BytesPerPixel;

        // Projecting is charged like the decode that fed it, and for the same
        // reason: the pixels are four bytes each however few bytes the samples
        // packed them into, and a one-bit page-sized scan expands thirty-two
        // fold. A charge that lands past a limit drops this image and nothing
        // else — carrying a picture must never be the reason a document fails.
        try
        {
            _store.Budget.ChargeDecodedBytes(rgbaBytes);
        }
        catch (PdfLimitExceededException)
        {
            NotProjected("a pixel count past the read's remaining allowance");
            return;
        }

        byte[]? rgba;
        if (codec && samples.LongLength == rgbaBytes)
        {
            rgba = samples;
        }
        else if (TryResolveSamples(dictionary, shape, resources, out PdfSampleFormat format, out string refusal))
        {
            rgba = PdfImageSamples.ToRgba(format, samples);
            if (rgba is null)
            {
                NotProjected("a sample count its declaration does not account for");
                return;
            }
        }
        else
        {
            NotProjected(refusal);
            return;
        }

        (double left, double top, double width, double height) = PlacementOf(_state.Matrix);
        if (width <= 0 || height <= 0)
        {
            NotProjected("a placement matrix with no extent");
            return;
        }

        var resource = BImageResource.FromPixels(new BPixelBuffer(shape.Width, shape.Height, rgba));
        if (!_resources.TryAdmit(
                new DocumentResourceRequest(
                    resource,
                    DocumentResourceProvenance.ReadFromSource,
                    DocumentResourceDisposition.Embedded,
                    name: null,
                    sourceFormat: "PDF"),
                DocumentResourceOperations.ExtractToModel,
                out DocumentResourceId id,
                out string? denial))
        {
            _store.Features.NoteImageDenied(_store.CurrentPage, denial);
            return;
        }

        // The drawn box is the display size, in points, because user space is
        // points and the matrix is what decides how large the picture appears.
        var image = new InlineImage(resource, id, width, height);
        _placedImages.Add(new PdfPlacedImage(image, left, top, width, height));
    }

    /// <summary>
    /// Resolves the sample layout PDF roadmap §9.3 approved — DeviceGray at 1,
    /// 2, 4, or 8 bits, DeviceRGB at 8, and Indexed at 1, 2, 4, or 8 over a
    /// bounded DeviceGray or DeviceRGB palette — or names why this image falls
    /// outside it.
    /// </summary>
    /// <remarks>
    /// A refusal names a colour space only where the format reserves the name.
    /// A space a resource dictionary invented is reported as being outside the
    /// subset without repeating what the document called it: a construct this
    /// build recognizes is a fact about the format, and a name the author chose
    /// is a value (ADR 0009).
    /// </remarks>
    private bool TryResolveSamples(
        PdfDictionary dictionary,
        in PdfImageShape shape,
        PdfDictionary? resources,
        out PdfSampleFormat format,
        out string refusal)
    {
        format = default;
        refusal = string.Empty;

        int bits = shape.BitsPerComponent;
        if (bits is not (1 or 2 or 4 or 8))
        {
            refusal = "a bit depth outside the approved subset";
            return false;
        }

        PdfObject? space = ResolveColorSpace(dictionary["ColorSpace"], resources);
        string family = ColorSpaceFamily(space);

        switch (family)
        {
            case "DeviceGray":
                if (!TryDecodeArray(dictionary, components: 1, upper: 1, out double[]? gray))
                {
                    refusal = "a Decode array outside the range the format allows";
                    return false;
                }

                format = new PdfSampleFormat(shape.Width, shape.Height, bits, PdfSampleSpace.Gray, null, gray);
                return true;

            case "DeviceRGB":
                if (bits != 8)
                {
                    refusal = "DeviceRGB at a depth other than eight bits";
                    return false;
                }

                if (!TryDecodeArray(dictionary, components: 3, upper: 1, out double[]? rgb))
                {
                    refusal = "a Decode array outside the range the format allows";
                    return false;
                }

                format = new PdfSampleFormat(shape.Width, shape.Height, bits, PdfSampleSpace.Rgb, null, rgb);
                return true;

            case "Indexed":
                if (space is not PdfArray indexed || !TryPalette(indexed, out byte[]? palette, out refusal))
                {
                    if (refusal.Length == 0)
                        refusal = "an Indexed colour space this build cannot read";
                    return false;
                }

                // An Indexed image's Decode array remaps the indices themselves,
                // which is a different operation from remapping colour values.
                // Applying half of it would be worse than applying none, so only
                // the default mapping projects.
                if (!TryDecodeArray(dictionary, components: 1, upper: (1 << bits) - 1, out double[]? lookup) || lookup is not null)
                {
                    refusal = "an Indexed image that remaps its own indices";
                    return false;
                }

                format = new PdfSampleFormat(shape.Width, shape.Height, bits, PdfSampleSpace.Indexed, palette, null);
                return true;

            case "":
                refusal = "an unstated colour space";
                return false;

            default:
                // Named only where the format reserves the name, so a document's
                // own resource label never reaches a diagnostic.
                refusal = PdfColorSpaces.IsReserved(family)
                    ? "the colour space " + family
                    : "a colour space outside the approved subset";
                return false;
        }
    }

    /// <summary>
    /// The colour space an image names, following a resource-dictionary label to
    /// the space it stands for. The device families always mean themselves: the
    /// format reserves those names, and a resource entry cannot redefine one.
    /// </summary>
    private PdfObject? ResolveColorSpace(PdfObject? declared, PdfDictionary? resources)
    {
        PdfObject? space = _store.Resolve(declared);

        if (space is not PdfName name || PdfColorSpaces.IsReserved(name.Value) || resources is null)
            return space;

        return _store.Resolve(resources["ColorSpace"]) is PdfDictionary spaces &&
               _store.Resolve(spaces[name.Value]) is PdfObject bound and not PdfNull
            ? bound
            : space;
    }

    /// <summary>The family a colour space belongs to, or empty where it states none.</summary>
    private string ColorSpaceFamily(PdfObject? space) => space switch
    {
        PdfName name => name.Value,
        PdfArray array when array.Count > 0 && _store.Resolve(array[0]) is PdfName head => head.Value,
        _ => string.Empty,
    };

    /// <summary>
    /// The image's <c>/Decode</c> array, validated against the component count
    /// and the interval its colour space defines, or null where it is absent or
    /// states the default mapping. False means the array is present and outside
    /// what this build applies.
    /// </summary>
    /// <param name="upper">
    /// The top of the interval this space's components run over, which the
    /// default array runs to: 1 for the device spaces, and the largest value the
    /// depth holds for an Indexed one, whose components are indices rather than
    /// colour values.
    /// </param>
    private bool TryDecodeArray(PdfDictionary dictionary, int components, double upper, out double[]? decode)
    {
        decode = null;

        if (_store.Resolve(dictionary["Decode"]) is not PdfArray array)
            return true;

        if (array.Count != components * 2)
            return false;

        var values = new double[array.Count];
        bool isDefault = true;

        for (int i = 0; i < array.Count; i++)
        {
            if (_store.Resolve(array[i]) is not PdfNumber number ||
                !double.IsFinite(number.Value) ||
                number.Value < 0 ||
                number.Value > upper)
            {
                return false;
            }

            values[i] = number.Value;
            if (values[i] != (i % 2 == 0 ? 0 : upper))
                isDefault = false;
        }

        decode = isDefault ? null : values;
        return true;
    }

    /// <summary>
    /// The palette of an <c>[/Indexed base hival lookup]</c> space, expanded to
    /// RGB triples so the projection needs no second branch for a gray base.
    /// </summary>
    private bool TryPalette(PdfArray space, out byte[]? palette, out string refusal)
    {
        palette = null;
        refusal = string.Empty;

        if (space.Count != 4)
        {
            refusal = "an Indexed colour space this build cannot read";
            return false;
        }

        int components = ColorSpaceFamily(_store.Resolve(space[1])) switch
        {
            "DeviceGray" => 1,
            "DeviceRGB" => 3,
            _ => 0,
        };

        if (components == 0)
        {
            refusal = "an Indexed palette over a colour space outside the approved subset";
            return false;
        }

        // The format caps hival at 255, so the palette is bounded before a byte
        // of it is read.
        if (_store.Resolve(space[2]) is not PdfNumber high || !double.IsFinite(high.Value) || high.Value is < 0 or > 255)
        {
            refusal = "an Indexed palette past the size the format bounds it to";
            return false;
        }

        int entries = (int)high.Value + 1;
        byte[]? lookup = ReadPalette(space[3]);

        if (lookup is null)
        {
            refusal = "an Indexed palette this build cannot read";
            return false;
        }

        if (lookup.Length < entries * components)
        {
            refusal = "an Indexed palette shorter than it declares";
            return false;
        }

        palette = new byte[entries * 3];
        for (int i = 0; i < entries; i++)
        {
            int at = i * 3;
            if (components == 1)
            {
                byte level = lookup[i];
                palette[at] = level;
                palette[at + 1] = level;
                palette[at + 2] = level;
                continue;
            }

            palette[at] = lookup[at];
            palette[at + 1] = lookup[at + 1];
            palette[at + 2] = lookup[at + 2];
        }

        return true;
    }

    /// <summary>
    /// A palette's bytes, from either form the format allows. A stream one is
    /// decoded through the shared pipeline, so it is charged like every other
    /// stream; a limit met while reading it drops the image rather than the
    /// document.
    /// </summary>
    private byte[]? ReadPalette(PdfObject? lookup)
    {
        switch (_store.Resolve(lookup))
        {
            case PdfString text:
                return text.Bytes;

            case PdfStream stream:
                try
                {
                    PdfStreamDecodeResult decoded = _store.Filters.Decode(stream, _store.Resolve, _store.Budget);
                    return decoded.Succeeded ? decoded.Data : null;
                }
                catch (PdfLimitExceededException)
                {
                    return null;
                }

            default:
                return null;
        }
    }

    /// <summary>
    /// The box the unit square maps to under <paramref name="matrix"/>: an image
    /// is drawn by transforming [0,1]x[0,1], so its edges are the lengths of the
    /// transformed basis vectors and its position is the corner extent.
    /// </summary>
    private static (double Left, double Top, double Width, double Height) PlacementOf(PdfMatrix matrix)
    {
        (double x0, double y0) = matrix.Transform(0, 0);
        (double x1, double y1) = matrix.Transform(1, 0);
        (double x2, double y2) = matrix.Transform(0, 1);
        (double x3, double y3) = matrix.Transform(1, 1);

        double left = Math.Min(Math.Min(x0, x1), Math.Min(x2, x3));
        double top = Math.Max(Math.Max(y0, y1), Math.Max(y2, y3));
        double width = Math.Sqrt(((x1 - x0) * (x1 - x0)) + ((y1 - y0) * (y1 - y0)));
        double height = Math.Sqrt(((x2 - x0) * (x2 - x0)) + ((y2 - y0) * (y2 - y0)));

        return double.IsFinite(left) && double.IsFinite(top) && double.IsFinite(width) && double.IsFinite(height)
            ? (left, top, width, height)
            : (0, 0, 0, 0);
    }
}
