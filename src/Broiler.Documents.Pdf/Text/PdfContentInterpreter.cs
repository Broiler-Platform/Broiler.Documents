using System;
using System.Collections.Generic;
using System.Text;
using Broiler.Documents.Model;
using Broiler.Documents.Pdf.Filters;
using Broiler.Documents.Pdf.Structure;
using Broiler.Documents.Pdf.Syntax;
using Broiler.Documents.Resources;
using Broiler.Graphics.Color;
using Broiler.Graphics.Imaging;

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
internal sealed class PdfContentInterpreter(
    PdfObjectStore store,
    DocumentConversionContextBuilder? resources = null,
    PdfOptionalContent? optionalContent = null)
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
    /// How far a run's baseline may lean off the horizontal of the page as
    /// displayed, as a slope, and still be upright: about one degree.
    /// </summary>
    /// <remarks>
    /// The reading-order pass sets text on horizontal lines and keeps a line
    /// together only while its baseline stays within about a third of its size.
    /// A line of body text leaning further than this has drifted past that before
    /// it ends, and text turned further still - a sideways label, a slanted
    /// stamp - is read letter by letter wherever each letter lands.
    /// </remarks>
    private const double MaxUprightSlope = 0.0175;

    /// <summary>
    /// How many parameters an inline image's abbreviated dictionary is read for.
    /// The dictionary is a description of a construct that is being skipped, so
    /// it is bounded well below anything a real one uses.
    /// </summary>
    private const int MaxInlineImageParameters = 32;

    private readonly PdfObjectStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly List<PdfTextFragment> _fragments = [];
    private readonly Dictionary<PdfDictionary, PdfFont> _fontCache = [];

    // One conversion per profile and intent, built once however many images
    // name it - and one refusal, so a profile that failed is not read again.
    private readonly Dictionary<(PdfStream Profile, PdfRenderingIntent Intent), (PdfColorTransform? Transform, string Refusal)> _colorTransforms = [];
    private readonly HashSet<PdfDictionary> _activeForms = [];

    private readonly Stack<GraphicsState> _stack = new();
    private GraphicsState _state = GraphicsState.Initial;
    private readonly DocumentConversionContextBuilder? _resources = resources;
    private readonly List<PdfPlacedImage> _placedImages = [];

    /// <summary>
    /// Counts what the page paints — text runs, paths, shadings, pictures — in
    /// the order it paints them. A fill's meaning depends on it: under a run it
    /// is the run's background, over one it hides it, and on bare paper it is
    /// the paper.
    /// </summary>
    private int _paintOrder;

    /// <summary>
    /// The box of every non-text mark the page painted, with its place in the
    /// paint order: kept paths, the paths too irregular to keep, shadings, and
    /// pictures whether or not they decoded. Text runs are marks too, and the
    /// fragments already carry their own box and order.
    /// </summary>
    private readonly List<PdfPaintedMark> _marks = [];

    /// <summary>How many marks one page keeps before it stops keeping them.</summary>
    private const int MaxMarks = 16384;

    /// <summary>
    /// True when a page painted more marks than <see cref="MaxMarks"/>. Past
    /// that the page's record of what lies under a fill is incomplete, and a
    /// question that needs it has to be answered conservatively.
    /// </summary>
    private bool _marksTruncated;
    private PdfMatrix _textMatrix = PdfMatrix.Identity;
    private PdfMatrix _lineMatrix = PdfMatrix.Identity;
    private string? _pendingActualText;

    /// <summary>The value of <see cref="_hiddenDepth"/> when nothing is hidden.</summary>
    private const int NotHidden = -1;

    private readonly PdfOptionalContent _optionalContent = optionalContent ?? PdfOptionalContent.None;

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

    /// <summary>
    /// The marked-content id at each open level. A sequence that states none
    /// inherits the one it is nested in, which is what makes a `/Span` inside a
    /// tagged paragraph part of that paragraph rather than untagged content.
    /// </summary>
    private readonly List<int> _mcidStack = [];

    /// <summary>The innermost marked-content id, or -1 outside any.</summary>
    private int Mcid => _mcidStack.Count > 0 ? _mcidStack[^1] : -1;

    /// <summary>
    /// Whether each open level is artifact content. An <c>/Artifact</c> sequence
    /// makes every level inside it one too: a tag nested in page furniture is
    /// still page furniture, and the structure tree covers none of it.
    /// </summary>
    private readonly List<bool> _artifactStack = [];

    /// <summary>True while the interpreter is inside an <c>/Artifact</c> sequence.</summary>
    private bool IsArtifact => _artifactStack.Count > 0 && _artifactStack[^1];

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

    /// <summary>
    /// True when the path's box may not hold all of it: a curve bulges past its
    /// endpoints, which is all that is tracked, and a point that is not a number
    /// was never added to the box at all.
    /// </summary>
    private bool _pathUnbounded;

    /// <summary>
    /// The rules and areas this page painted, kept rather than only counted. A
    /// table is a grid of them, and a grid is the one arrangement of vector
    /// artwork a logical model can carry.
    /// </summary>
    private readonly List<PdfPaintedPath> _paintedPaths = [];

    /// <summary>How many painted paths one page keeps before it stops keeping them.</summary>
    private const int MaxPaintedPaths = 4096;

    // The run being accumulated; flushed when style, baseline, or spacing breaks.
    private readonly StringBuilder _runText = new();
    private double _runStartX;
    private double _runY;
    private double _runEndX;
    private GraphicsState _runState = GraphicsState.Initial;
    private double _runFontSize;
    private double _runSpaceWidth;
    private int _runMcid = -1;
    private bool _runArtifact;
    private bool _runTurned;
    private int _runOrder;
    private bool _runOpen;

    /// <summary>
    /// The images this page drew that the caller's policy allowed into the model,
    /// with the box each is drawn in. Empty when no policy permits extraction, or
    /// when nothing decoded to samples the model can take.
    /// </summary>
    public IReadOnlyList<PdfPlacedImage> PlacedImages => _placedImages;

    /// <summary>The rules and filled areas the last page painted, in paint order.</summary>
    public IReadOnlyList<PdfPaintedPath> PaintedPaths => _paintedPaths;

    /// <summary>Every non-text mark the last page painted, in paint order.</summary>
    public IReadOnlyList<PdfPaintedMark> Marks => _marks;

    /// <summary>
    /// True when the last page painted more marks than were kept, so
    /// <see cref="Marks"/> cannot say for certain that nothing lies under a fill.
    /// </summary>
    public bool MarksTruncated => _marksTruncated;

    /// <summary>Runs a page's content and returns the text runs it placed.</summary>
    /// <remarks>
    /// The content starts out on the page as a viewer displays it - turned by the
    /// page's <c>/Rotate</c> and in points - so every run, path and picture comes
    /// back measured there, and a page turned to landscape reads across rather
    /// than up (<see cref="PdfPage.Display"/>).
    /// </remarks>
    public IReadOnlyList<PdfTextFragment> Run(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        _fragments.Clear();
        _placedImages.Clear();
        _paintedPaths.Clear();
        _marks.Clear();
        _marksTruncated = false;
        _paintOrder = 0;
        _state = GraphicsState.Initial.WithMatrix(page.Display);
        _stack.Clear();
        ResetPath();

        // A page's marked content cannot span pages, so an unbalanced BDC on one
        // must not leave the next hidden.
        _markedContentDepth = 0;
        _hiddenDepth = NotHidden;
        _mcidStack.Clear();
        _artifactStack.Clear();

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

        return [.. joined];
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
                    _state = _state.WithColor(FromComponents(operands));
                    break;
                case "scn":
                    _state = operands.Count > 0 && operands[^1] is PdfName
                        ? _state.WithPatternFill(FromComponents(operands))
                        : _state.WithColor(FromComponents(operands));
                    break;
                case "cs":
                    // Selecting a colour space resets the colour to its initial black.
                    _state = _state.WithColor(BColor.Black);
                    break;

                // The stroking side of the same state. A stroke is painted in its
                // own colour and at its own width, and reading a stroked rule in
                // the fill colour gave a black-ruled table the grey of its shading.
                case "G":
                    _state = _state.WithStrokeColor(Gray(operands, 0));
                    break;
                case "RG":
                    _state = _state.WithStrokeColor(Rgb(operands));
                    break;
                case "K":
                    _state = _state.WithStrokeColor(Cmyk(operands));
                    break;
                case "SC":
                case "SCN":
                    _state = _state.WithStrokeColor(FromComponents(operands));
                    break;
                case "CS":
                    _state = _state.WithStrokeColor(BColor.Black);
                    break;
                case "w":
                    _state = _state.WithLineWidth(Number(operands, 0));
                    break;
                case "gs":
                    ApplyGraphicsStateParameters(operands, resources);
                    break;
                case "ri":
                    if (operands.Count > 0 && operands[^1] is PdfName intent)
                        _state = _state.WithRenderingIntent(IntentNamed(intent.Value));
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
                    // The tag is still read, because `/Artifact BMC` — furniture
                    // with no property list — is the commonest artifact of all.
                    PushMarkedContentLevel(
                        operands.Count >= 1 && operands[^1] is PdfName bmcTag ? bmcTag.Value : string.Empty);
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
                    {
                        PdfArtworkKind painted = ClassifyPath();
                        NoteVectorArtwork(painted);

                        // Whether the operator filled decides what the shape can
                        // mean: a stroked rectangle is four rules, a filled one is
                        // a shade. S and s stroke and nothing else, and f, F and
                        // f* fill and nothing else.
                        bool filled = token.Text is not ("S" or "s");
                        bool stroked = token.Text is not ("f" or "F" or "f*");
                        int order = ++_paintOrder;

                        NotePathMark(order, filled, stroked);
                        KeepPaintedPath(painted, filled, stroked, order);
                    }

                    ResetPath();
                    break;
                case "sh":
                    // A shading paints without a path of its own, over whatever
                    // the clip allows - which is not tracked, so it is taken to
                    // cover the page.
                    if (!Hidden)
                    {
                        NoteVectorArtwork(PdfArtworkKind.Shading);
                        NoteMark(PdfPaintedMark.Everywhere(++_paintOrder));
                    }

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
            _runMcid = Mcid;
            _runArtifact = IsArtifact;
            _runTurned = IsTurned(_textMatrix.Concat(_state.Matrix));

            // A run takes its place in the paint order where its first glyph
            // lands. Anything painted before that is under all of it.
            _runOrder = ++_paintOrder;
        }

        _store.Budget.ChargeCharacters(text.Length);
        _runText.Append(text);
        AdvanceText(advance);
        _runEndX = CurrentPen().X;
    }

    /// <summary>
    /// Whether text set through this matrix runs anywhere but left to right along
    /// the page as displayed: sideways, upside down, mirrored, or at a slant.
    /// </summary>
    private static bool IsTurned(PdfMatrix combined) =>
        !(combined.A > 0 && Math.Abs(combined.B) <= combined.A * MaxUprightSlope);

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
            _runState.RenderMode,
            _runMcid,
            _runArtifact,
            _runOrder,
            _runTurned));

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

    /// <summary>
    /// Opens one marked-content level, whichever operator opened it, and records
    /// whether the level is artifact content.
    /// </summary>
    private void PushMarkedContentLevel(string tag)
    {
        _markedContentDepth++;
        _mcidStack.Add(Mcid);

        // Read before the push, so `inherited` is the enclosing level's answer.
        bool inherited = IsArtifact;
        bool artifact = inherited || string.Equals(tag, "Artifact", StringComparison.Ordinal);

        // A run never straddles the boundary, for the reason an MCID change
        // flushes one: the flag is recorded per fragment, and a run that began
        // in the body and ended in a folio would have to claim to be one or the
        // other.
        if (artifact != inherited)
            FlushRun();

        _artifactStack.Add(artifact);
    }

    private void BeginMarkedContent(List<PdfObject> operands, PdfDictionary? resources)
    {
        // BDC carries a tag and either an inline dictionary or a name into
        // /Properties. ActualText on it replaces the glyphs it encloses; an /OC
        // tag names the layer they belong to.
        PdfObject? properties = operands.Count >= 1 ? operands[^1] : null;
        string tag = operands.Count >= 2 && operands[^2] is PdfName tagName ? tagName.Value : string.Empty;

        // Pushed before the property dictionary is examined so that every exit
        // from here leaves the stack matching the depth an EMC will pop.
        PushMarkedContentLevel(tag);

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

        // A new marked-content id starts a new run, so a fragment never straddles
        // two of them and the structure tree can place every one it emits.
        if (_store.Resolve(dictionary["MCID"]) is PdfNumber mcid)
        {
            int value = mcid.ToInt32();
            if (value >= 0 && value != Mcid)
            {
                FlushRun();
                _mcidStack[^1] = value;
            }
        }

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

        if (_mcidStack.Count > 0)
            _mcidStack.RemoveAt(_mcidStack.Count - 1);

        // The EMC operator flushes the run before it gets here, so leaving an
        // artifact sequence needs no flush of its own.
        if (_artifactStack.Count > 0)
            _artifactStack.RemoveAt(_artifactStack.Count - 1);
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
            // Painted whether or not it decodes: a picture this build cannot read
            // still covers whatever it was drawn over.
            NotePlacementMark();
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
        {
            NotePlacementMark();
            NoteInlineImage(data, parametersStart, parametersEnd);
        }
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
        if (CanDecode(filter))
        {
            if (DecodeToDescribe(stream) is PdfStreamDecodeResult decoded)
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

            // Composed and not spent. The image is reported from its dictionary
            // exactly as one this build cannot reach would be, but not under the
            // same sentence: saying a build that composed a decoder composes none
            // is false, and it is the sentence a host acts on.
            _store.Features.NoteImage(ImageDiagnosticFor(filter), shape, _store.CurrentPage, undecoded: true);
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
    /// code an image this build cannot reach would use — the code names the
    /// filter that would have to be decoded, and that is true either way — but
    /// not under the same sentence. It still does not say how much allowance was
    /// left when a given image was reached: that is a fact about the read, not
    /// about the document, and a diagnostic names constructs. What it does say is
    /// that a decoder was composed, because a build that composed one and a build
    /// that has none are fixed by entirely different work, and only the first
    /// sentence of that note tells them apart.
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
    /// Keeps the geometry of a shape a table could have been drawn with. Only
    /// the two axis-aligned classes are kept: a curve or a diagonal cannot be a
    /// cell boundary, and keeping it would only cost memory on a page of charts.
    /// </summary>
    private void KeepPaintedPath(PdfArtworkKind kind, bool filled, bool stroked, int order)
    {
        if (kind is not (PdfArtworkKind.Rule or PdfArtworkKind.Block))
            return;

        if (!_pathOpen || _paintedPaths.Count >= MaxPaintedPaths)
            return;

        // A fill is in the fill colour and has no pen. Everything else is kept
        // as the stroke it was, in the stroking colour and at the pen's width -
        // which is what a table's border is, and what an underline's weight is.
        _paintedPaths.Add(new PdfPaintedPath(
            _pathMinX,
            _pathMinY,
            _pathMaxX,
            _pathMaxY,
            kind,
            filled,
            filled ? _state.Color : _state.StrokeColor,
            filled ? 0 : StrokeWidth(),
            order,
            stroked));
    }

    /// <summary>
    /// The pen's width in device space, measured across the line: a horizontal
    /// line is as heavy as the pen's vertical extent, a vertical one as its
    /// horizontal one.
    /// </summary>
    private double StrokeWidth()
    {
        PdfMatrix matrix = _state.Matrix;
        double scale = _pathMaxX - _pathMinX >= _pathMaxY - _pathMinY
            ? matrix.VerticalScale
            : matrix.HorizontalScale;

        double width = _state.LineWidth * scale;
        return double.IsFinite(width) && width > 0 ? width : 0;
    }

    /// <summary>
    /// Records the box a painted path covered, widened by half the pen where it
    /// was stroked, since that is how far the ink reaches either side of it.
    /// </summary>
    private void NotePathMark(int order, bool filled, bool stroked)
    {
        if (!_pathOpen)
            return;

        // Where the box may not hold the path, the mark is taken to cover the
        // page. That can only make something painted later look less like bare
        // paper, which is the safe direction to be wrong in.
        if (_pathUnbounded)
        {
            NoteMark(PdfPaintedMark.Everywhere(order));
            return;
        }

        double reach = stroked ? _state.LineWidth * Math.Max(_state.Matrix.HorizontalScale, _state.Matrix.VerticalScale) / 2 : 0;
        if (!double.IsFinite(reach))
            reach = 0;

        NoteMark(new PdfPaintedMark(
            _pathMinX - reach,
            _pathMinY - reach,
            _pathMaxX + reach,
            _pathMaxY + reach,
            order,
            IsPaper: filled && !stroked && _state.Color == PdfPaintedMark.Paper));
    }

    /// <summary>Records a picture's box, decoded or not, at the next place in the paint order.</summary>
    private void NotePlacementMark()
    {
        (double left, double top, double width, double height) = PlacementOf(_state.Matrix);
        int order = ++_paintOrder;

        NoteMark(width > 0 && height > 0
            ? new PdfPaintedMark(left, top - height, left + width, top, order, IsPaper: false)
            : PdfPaintedMark.Everywhere(order));
    }

    private void NoteMark(in PdfPaintedMark mark)
    {
        if (_marks.Count >= MaxMarks)
        {
            _marksTruncated = true;
            return;
        }

        _marks.Add(mark);
    }

    /// <summary>
    /// Applies the entries of a named graphics-state dictionary this interpreter
    /// tracks and <c>gs</c> can set: the line width, <c>/LW</c>, and the
    /// rendering intent, <c>/RI</c>.
    /// </summary>
    private void ApplyGraphicsStateParameters(List<PdfObject> operands, PdfDictionary? resources)
    {
        if (operands.Count == 0 || operands[^1] is not PdfName name || resources is null)
            return;

        if (_store.Resolve(resources["ExtGState"]) is not PdfDictionary states ||
            _store.Resolve(states[name.Value]) is not PdfDictionary parameters)
        {
            return;
        }

        if (_store.Resolve(parameters["LW"]) is PdfNumber width)
            _state = _state.WithLineWidth(width.Value);

        if (_store.Resolve(parameters["RI"]) is PdfName intent)
            _state = _state.WithRenderingIntent(IntentNamed(intent.Value));
    }

    /// <summary>
    /// The rendering intent a name selects. PDF 32000-1 8.6.5.8 has a reader
    /// treat a name it does not recognize as relative colorimetric.
    /// </summary>
    private static PdfRenderingIntent IntentNamed(string name) => name switch
    {
        "AbsoluteColorimetric" => PdfRenderingIntent.AbsoluteColorimetric,
        "Saturation" => PdfRenderingIntent.Saturation,
        "Perceptual" => PdfRenderingIntent.Perceptual,
        _ => PdfRenderingIntent.RelativeColorimetric,
    };

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
        _pathUnbounded = false;
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
        _pathUnbounded = true;
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
            _pathUnbounded = true;
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
            BColor color,
            BColor strokeColor,
            double lineWidth,
            bool fillIsPattern = false,
            PdfRenderingIntent renderingIntent = PdfRenderingIntent.RelativeColorimetric)
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
            StrokeColor = strokeColor;
            LineWidth = lineWidth;
            FillIsPattern = fillIsPattern;
            RenderingIntent = renderingIntent;
        }

        /// <summary>The format's initial state: black for both colours, and a one-unit pen.</summary>
        public static GraphicsState Initial { get; } = new(
            PdfMatrix.Identity, PdfFont.Fallback, 0, 0, 0, 1, 0, 0, 0, BColor.Black, BColor.Black, 1);

        public PdfMatrix Matrix { get; }

        public PdfFont Font { get; }

        public double FontSize { get; }

        public double CharSpacing { get; }

        public double WordSpacing { get; }

        public double HorizontalScale { get; }

        public double Leading { get; }

        public double Rise { get; }

        public int RenderMode { get; }

        /// <summary>The fill colour, which is also the colour text is drawn in.</summary>
        public BColor Color { get; }

        /// <summary>The stroking colour, which rules and outlines are drawn in.</summary>
        public BColor StrokeColor { get; }

        /// <summary>The pen width in user space. Zero is the thinnest line the device can draw.</summary>
        public double LineWidth { get; }

        /// <summary>
        /// True while the fill is a pattern rather than a colour: <c>scn</c> named
        /// one. <see cref="Color"/> then holds whatever components came with the
        /// name, which is not a colour anything is painted in, so a stencil mask
        /// drawn now has no colour this interpreter can state.
        /// </summary>
        public bool FillIsPattern { get; }

        /// <summary>
        /// The rendering intent <c>ri</c> or a graphics state's <c>/RI</c> set,
        /// which chooses the table an ICC profile converts through.
        /// </summary>
        public PdfRenderingIntent RenderingIntent { get; }

        public GraphicsState WithMatrix(PdfMatrix matrix) => matrix.IsFinite
            ? new GraphicsState(matrix, Font, FontSize, CharSpacing, WordSpacing, HorizontalScale, Leading, Rise, RenderMode, Color, StrokeColor, LineWidth, FillIsPattern, RenderingIntent)
            : this;

        public GraphicsState WithFont(PdfFont font, double size) =>
            new(Matrix, font, double.IsFinite(size) ? size : 0, CharSpacing, WordSpacing, HorizontalScale, Leading, Rise, RenderMode, Color, StrokeColor, LineWidth, FillIsPattern, RenderingIntent);

        public GraphicsState WithCharSpacing(double value) =>
            new(Matrix, Font, FontSize, Finite(value), WordSpacing, HorizontalScale, Leading, Rise, RenderMode, Color, StrokeColor, LineWidth, FillIsPattern, RenderingIntent);

        public GraphicsState WithWordSpacing(double value) =>
            new(Matrix, Font, FontSize, CharSpacing, Finite(value), HorizontalScale, Leading, Rise, RenderMode, Color, StrokeColor, LineWidth, FillIsPattern, RenderingIntent);

        public GraphicsState WithHorizontalScale(double value) =>
            new(Matrix, Font, FontSize, CharSpacing, WordSpacing, value is > 0 and < 100 ? value : 1, Leading, Rise, RenderMode, Color, StrokeColor, LineWidth, FillIsPattern, RenderingIntent);

        public GraphicsState WithLeading(double value) =>
            new(Matrix, Font, FontSize, CharSpacing, WordSpacing, HorizontalScale, Finite(value), Rise, RenderMode, Color, StrokeColor, LineWidth, FillIsPattern, RenderingIntent);

        public GraphicsState WithRise(double value) =>
            new(Matrix, Font, FontSize, CharSpacing, WordSpacing, HorizontalScale, Leading, Finite(value), RenderMode, Color, StrokeColor, LineWidth, FillIsPattern, RenderingIntent);

        public GraphicsState WithRenderMode(int value) =>
            new(Matrix, Font, FontSize, CharSpacing, WordSpacing, HorizontalScale, Leading, Rise, value is >= 0 and <= 7 ? value : 0, Color, StrokeColor, LineWidth, FillIsPattern, RenderingIntent);

        public GraphicsState WithColor(BColor value) =>
            new(Matrix, Font, FontSize, CharSpacing, WordSpacing, HorizontalScale, Leading, Rise, RenderMode, value, StrokeColor, LineWidth, false, RenderingIntent);

        /// <summary>A fill naming a pattern, with whatever components came with the name.</summary>
        public GraphicsState WithPatternFill(BColor value) =>
            new(Matrix, Font, FontSize, CharSpacing, WordSpacing, HorizontalScale, Leading, Rise, RenderMode, value, StrokeColor, LineWidth, true, RenderingIntent);

        public GraphicsState WithStrokeColor(BColor value) =>
            new(Matrix, Font, FontSize, CharSpacing, WordSpacing, HorizontalScale, Leading, Rise, RenderMode, Color, value, LineWidth, FillIsPattern, RenderingIntent);

        public GraphicsState WithRenderingIntent(PdfRenderingIntent value) =>
            new(Matrix, Font, FontSize, CharSpacing, WordSpacing, HorizontalScale, Leading, Rise, RenderMode, Color, StrokeColor, LineWidth, FillIsPattern, value);

        /// <summary>A negative or non-finite width is malformed, and the pen keeps its current width.</summary>
        public GraphicsState WithLineWidth(double value) => double.IsFinite(value) && value >= 0
            ? new(Matrix, Font, FontSize, CharSpacing, WordSpacing, HorizontalScale, Leading, Rise, RenderMode, Color, StrokeColor, value, FillIsPattern, RenderingIntent)
            : this;

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
    /// colour space outside the approved raw-sample subset needs a transform this
    /// project does not own, and a mask this build cannot read states a
    /// transparency it cannot reproduce.
    /// </para>
    /// <para>
    /// <strong>Transparency is carried, not composited.</strong> The model holds
    /// straight-alpha RGBA, so every mask the format defines is read into the
    /// alpha channel and nothing is blended against a backdrop - that is the
    /// renderer's business, and it needs a page this model does not keep. A
    /// stencil is painted in the fill colour through its one-bit shape, a
    /// colour-key <c>/Mask</c> makes its ranges transparent, and an explicit
    /// <c>/Mask</c> or a soft mask is a picture of its own read onto the same
    /// unit square. PDF roadmap §9.3 added this tuple to the approved matrix on
    /// 2026-09-24. Before that, a logo drawn on a transparent ground was
    /// refused outright, because carrying it opaque would have put a solid box
    /// where the ground belongs.
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
        bool stencil = _store.Resolve(dictionary["ImageMask"]) is PdfBoolean imageMask && imageMask.Value;

        // A stencil is painted in the fill colour, and a pattern is not a colour
        // this interpreter reads.
        if (stencil && _state.FillIsPattern)
        {
            NotProjected("a stencil mask painted with a pattern");
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

        string? refusal;
        byte[]? rgba = stencil
            ? StencilPixels(dictionary, shape, samples, out refusal)
            : PicturePixels(dictionary, shape, samples, codec, rgbaBytes, resources, out refusal);

        if (rgba is null)
        {
            NotProjected(refusal!);
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
    /// A stencil mask's pixels: the fill colour wherever its one-bit shape
    /// paints, and transparent everywhere else - or null with the reason.
    /// </summary>
    /// <remarks>
    /// Under the default <c>/Decode [0 1]</c> a zero sample paints and a one
    /// leaves the page as it was, and <c>[1 0]</c> swaps them (PDF 32000-1
    /// 8.9.6.2). The colour is the one in force when the stencil is drawn,
    /// which is why it is read here and at no later point.
    /// </remarks>
    private byte[]? StencilPixels(PdfDictionary dictionary, in PdfImageShape shape, byte[] samples, out string? refusal)
    {
        refusal = null;

        // An image mask is one bit deep by definition, and may say so or not.
        if (shape.BitsPerComponent is not (0 or 1))
        {
            refusal = "a stencil mask deeper than one bit";
            return null;
        }

        if (!TryMaskDecode(dictionary, out bool inverted))
        {
            refusal = "a mask Decode array other than the two the format defines";
            return null;
        }

        long stride = ((long)shape.Width + 7) / 8;
        if (samples.LongLength != stride * shape.Height)
        {
            refusal = "a sample count its declaration does not account for";
            return null;
        }

        BColor colour = _state.Color;
        int painted = inverted ? 1 : 0;
        byte[] rgba = new byte[(long)shape.Width * shape.Height * BPixelBuffer.BytesPerPixel];
        int output = 0;

        for (int y = 0; y < shape.Height; y++)
        {
            long row = y * stride;
            for (int x = 0; x < shape.Width; x++)
            {
                int bit = (samples[row + (x >> 3)] >> (7 - (x & 7))) & 1;
                rgba[output] = colour.R;
                rgba[output + 1] = colour.G;
                rgba[output + 2] = colour.B;
                rgba[output + 3] = bit == painted ? (byte)255 : (byte)0;
                output += BPixelBuffer.BytesPerPixel;
            }
        }

        return rgba;
    }

    /// <summary>
    /// A picture's pixels with its transparency read into the alpha channel, or
    /// null with the reason.
    /// </summary>
    /// <remarks>
    /// Resolved, not merely present. The indexer hands back the raw entry, so
    /// <c>/SMask null</c> - which PDF 32000-1 7.3.9 defines as equivalent to
    /// the key being absent - and a reference to a free object both used to
    /// arrive as something non-null. Resolve normalizes both to null. A soft
    /// mask outranks a <c>/Mask</c> entry (11.6.5.3), so a colour key or an
    /// explicit mask is only read where there is none.
    /// </remarks>
    private byte[]? PicturePixels(
        PdfDictionary dictionary,
        in PdfImageShape shape,
        byte[] samples,
        bool codec,
        long rgbaBytes,
        PdfDictionary? resources,
        out string? refusal)
    {
        refusal = null;
        PdfObject? soft = _store.Resolve(dictionary["SMask"]);
        PdfObject? mask = soft is null ? _store.Resolve(dictionary["Mask"]) : null;

        byte[]? rgba;
        int matteComponents;

        if (codec && samples.LongLength == rgbaBytes)
        {
            // A codec's pixels are colour already, gray repeated across the
            // three channels, so a key is matched against the channels it names.
            rgba = samples;
            matteComponents = DeviceComponents(dictionary, resources);

            // Colour in an ICC-based space's own values, which the profile
            // converts where a reader is composed. Without one the codec's
            // colours stand as they always have: the space's alternate, which
            // is what PDF 32000-1 8.6.5.5 has a reader without profiles use.
            if (!TryConvertCodecPixels(dictionary, resources, rgba, out refusal))
                return null;

            if (mask is PdfArray keyArray &&
                !(keyArray.Count is 2 or 6 && TryColourKey(keyArray, keyArray.Count / 2, 8, out int[]? codecKey) && ApplyColourKey(codecKey!, rgba)))
            {
                refusal = "a colour-key mask this build cannot read";
                return null;
            }
        }
        else if (TryResolveSamples(dictionary, shape, resources, out PdfSampleFormat format, out string resolveRefusal))
        {
            int[]? colourKey = null;
            if (mask is PdfArray keyArray && !TryColourKey(keyArray, format.Components, format.BitsPerComponent, out colourKey))
            {
                refusal = "a colour-key mask this build cannot read";
                return null;
            }

            rgba = PdfImageSamples.ToRgba(format, samples, colourKey);
            if (rgba is null)
            {
                refusal = "a sample count its declaration does not account for";
                return null;
            }

            // A matte is stated in the picture's own colour components, and an
            // index is not a colour.
            matteComponents = format.Space switch
            {
                PdfSampleSpace.Gray => 1,
                PdfSampleSpace.Rgb => 3,
                _ => 0,
            };
        }
        else
        {
            refusal = resolveRefusal;
            return null;
        }

        if (soft is not null)
            return TryApplySoftMask(soft, shape, rgba, matteComponents, out refusal) ? rgba : null;

        if (mask is PdfStream explicitMask)
            return TryApplyExplicitMask(explicitMask, shape, rgba, out refusal) ? rgba : null;

        if (mask is not null and not PdfArray)
        {
            refusal = "a mask entry this build cannot read";
            return null;
        }

        return rgba;
    }

    /// <summary>
    /// A colour-key mask as a minimum and a maximum per component, clamped to
    /// the values the depth can hold, or false where the array is not one.
    /// </summary>
    private bool TryColourKey(PdfArray array, int components, int bits, out int[]? key)
    {
        key = null;
        if (array.Count != components * 2)
            return false;

        int maximum = (1 << bits) - 1;
        var values = new int[array.Count];
        for (int i = 0; i < array.Count; i++)
        {
            if (_store.Resolve(array[i]) is not PdfNumber number || !double.IsFinite(number.Value))
                return false;

            values[i] = (int)Math.Clamp(Math.Round(number.Value), 0, maximum);
        }

        key = values;
        return true;
    }

    /// <summary>
    /// Makes a codec's pixels transparent where they fall in a colour key of
    /// one component - gray, repeated in each channel - or three.
    /// </summary>
    private static bool ApplyColourKey(int[] key, byte[] rgba)
    {
        int components = key.Length / 2;
        for (int at = 0; at < rgba.Length; at += BPixelBuffer.BytesPerPixel)
        {
            bool keyed = true;
            for (int c = 0; c < components && keyed; c++)
                keyed = rgba[at + c] >= key[c * 2] && rgba[at + c] <= key[(c * 2) + 1];

            if (keyed)
                rgba[at + 3] = 0;
        }

        return true;
    }

    /// <summary>
    /// Reads a soft mask into the alpha channel: each of its samples, through
    /// its own <c>/Decode</c>, is the alpha of the part of the picture it covers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mask is a picture of its own, mapped onto the same unit square as the
    /// one it masks, so it need not share its size: each pixel takes the mask
    /// sample over its own position (PDF 32000-1 11.6.5.3). It is DeviceGray
    /// and not a stencil, and one that is either contradicts the format.
    /// </para>
    /// <para>
    /// A <c>/Matte</c> entry says the picture's colours were blended with that
    /// colour, by the alpha, before they were stored. Left in, every soft edge
    /// carries a fringe of the matte. It is undone here - arithmetic on the
    /// colour the pixel holds, not a composite against anything - wherever the
    /// picture's colours are its own device components; an index is not one.
    /// </para>
    /// </remarks>
    /// <param name="matteComponents">
    /// How many components a matte states for this picture: one for gray, three
    /// for RGB, and zero where a matte cannot be undone.
    /// </param>
    private bool TryApplySoftMask(PdfObject soft, in PdfImageShape shape, byte[] rgba, int matteComponents, out string? refusal)
    {
        refusal = null;
        if (soft is not PdfStream stream)
        {
            refusal = "a soft mask that is not an image";
            return false;
        }

        PdfDictionary dictionary = stream.Dictionary;

        if (_store.Resolve(dictionary["ImageMask"]) is PdfBoolean stencil && stencil.Value)
        {
            refusal = "a soft mask declared as a stencil";
            return false;
        }

        if (_store.Resolve(dictionary["ColorSpace"]) is { } space && !(space is PdfName gray && gray.Value == "DeviceGray"))
        {
            refusal = "a soft mask outside DeviceGray";
            return false;
        }

        int width = Integer(dictionary, "Width", "W");
        int height = Integer(dictionary, "Height", "H");
        int bits = Integer(dictionary, "BitsPerComponent", "BPC");
        if (width <= 0 || height <= 0 || bits is not (1 or 2 or 4 or 8 or 16))
        {
            refusal = "a mask with a sample layout this build does not read";
            return false;
        }

        if (!TrySoftMaskDecode(dictionary, out double low, out double high))
        {
            refusal = "a soft mask Decode array this build cannot read";
            return false;
        }

        double[]? matte = null;
        if (_store.Resolve(dictionary["Matte"]) is { } matteEntry &&
            (matteComponents == 0 || !TryNumbers(matteEntry, matteComponents, out matte)))
        {
            refusal = "a premultiplied soft mask over colours this build cannot undo it on";
            return false;
        }

        if (!TryMaskSamples(stream, width, height, bits, out MaskSamples levels, out refusal))
            return false;

        int output = 0;
        for (int y = 0; y < shape.Height; y++)
        {
            int maskY = (int)((long)y * height / shape.Height);
            for (int x = 0; x < shape.Width; x++)
            {
                int maskX = (int)((long)x * width / shape.Width);
                double alpha = Clamp(low + (levels.At(maskX, maskY) * (high - low)));

                if (matte is not null && alpha > 0)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        double m = matte[matte.Length == 1 ? 0 : c];
                        double stored = rgba[output + c] / 255d;
                        rgba[output + c] = ToByte(m + ((stored - m) / alpha));
                    }
                }

                rgba[output + 3] = ToByte(alpha);
                output += BPixelBuffer.BytesPerPixel;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads an explicit mask - a one-bit picture of its own - into the alpha
    /// channel: where it says not to paint, the picture is transparent.
    /// </summary>
    /// <remarks>
    /// The same rule as a stencil (PDF 32000-1 8.9.6.3): under the default
    /// <c>/Decode [0 1]</c> a zero sample paints and a one masks, and
    /// <c>[1 0]</c> swaps them. Like a soft mask it is mapped onto the unit
    /// square the picture is, and need not share its size.
    /// </remarks>
    private bool TryApplyExplicitMask(PdfStream mask, in PdfImageShape shape, byte[] rgba, out string? refusal)
    {
        refusal = null;
        PdfDictionary dictionary = mask.Dictionary;

        int width = Integer(dictionary, "Width", "W");
        int height = Integer(dictionary, "Height", "H");
        int bits = Integer(dictionary, "BitsPerComponent", "BPC");
        if (width <= 0 || height <= 0 || bits is not (0 or 1))
        {
            refusal = "a mask with a sample layout this build does not read";
            return false;
        }

        if (!TryMaskDecode(dictionary, out bool inverted))
        {
            refusal = "a mask Decode array other than the two the format defines";
            return false;
        }

        if (!TryMaskSamples(mask, width, height, 1, out MaskSamples levels, out refusal))
            return false;

        int output = 0;
        for (int y = 0; y < shape.Height; y++)
        {
            int maskY = (int)((long)y * height / shape.Height);
            for (int x = 0; x < shape.Width; x++)
            {
                int maskX = (int)((long)x * width / shape.Width);
                bool set = levels.At(maskX, maskY) >= 0.5;
                if (set != inverted)
                    rgba[output + 3] = 0;

                output += BPixelBuffer.BytesPerPixel;
            }
        }

        return true;
    }

    /// <summary>
    /// A mask's samples, decoded through the same pipeline and budget as any
    /// picture's - so a mask a composed fax or JBIG2 filter compressed decodes
    /// as that filter's packed rows, and one a composed JPEG filter compressed
    /// as its pixels.
    /// </summary>
    private bool TryMaskSamples(PdfStream mask, int width, int height, int bits, out MaskSamples levels, out string? refusal)
    {
        levels = default;
        refusal = null;
        PdfObject? filter = mask.Dictionary["Filter"];

        if (!CanDecode(filter))
        {
            refusal = "a mask whose filter is not composed";
            return false;
        }

        PdfStreamDecodeResult decoded;
        try
        {
            decoded = _store.Filters.DecodeImage(mask, _store.Resolve, _store.Budget);
        }
        catch (PdfLimitExceededException)
        {
            refusal = "a mask past the read's remaining allowance";
            return false;
        }

        if (!decoded.Succeeded || decoded.Data is not { } data)
        {
            refusal = "a mask that could not be decoded";
            return false;
        }

        // Each row is packed at the declared depth and padded to a byte
        // boundary, so the padding bits at the end of a short row are never read
        // as the document's.
        long stride = (((long)width * bits) + 7) / 8;
        if (HasImageFilter(filter))
        {
            if (data.LongLength == (long)width * height * BPixelBuffer.BytesPerPixel)
            {
                levels = new MaskSamples(data, bits: 8, (long)width * BPixelBuffer.BytesPerPixel, pixels: true);
                return true;
            }

            if (data.LongLength == stride * height)
            {
                levels = new MaskSamples(data, bits, stride, pixels: false);
                return true;
            }
        }
        else if (data.LongLength >= stride * height)
        {
            levels = new MaskSamples(data, bits, stride, pixels: false);
            return true;
        }

        refusal = "a mask whose samples do not fill its declaration";
        return false;
    }

    /// <summary>
    /// A stencil or explicit mask's <c>/Decode</c>: absent or <c>[0 1]</c>, or
    /// <c>[1 0]</c>, which inverts it. Anything else is not a one-bit mapping.
    /// </summary>
    private bool TryMaskDecode(PdfDictionary dictionary, out bool inverted)
    {
        inverted = false;
        if (_store.Resolve(dictionary["Decode"]) is not PdfArray decode)
            return true;

        if (decode.Count != 2 ||
            _store.Resolve(decode[0]) is not PdfNumber low ||
            _store.Resolve(decode[1]) is not PdfNumber high)
        {
            return false;
        }

        if (low.Value == 0 && high.Value == 1)
            return true;

        inverted = low.Value == 1 && high.Value == 0;
        return inverted;
    }

    /// <summary>
    /// A soft mask's <c>/Decode</c>: the alpha its lowest and highest samples
    /// stand for, <c>[0 1]</c> when absent. <c>[1 0]</c> is the ordinary way a
    /// PDF writes an inverted mask, and every other interval maps the same way.
    /// </summary>
    private bool TrySoftMaskDecode(PdfDictionary dictionary, out double low, out double high)
    {
        low = 0;
        high = 1;
        if (_store.Resolve(dictionary["Decode"]) is not PdfArray decode)
            return true;

        if (decode.Count != 2 ||
            _store.Resolve(decode[0]) is not PdfNumber first || !double.IsFinite(first.Value) ||
            _store.Resolve(decode[1]) is not PdfNumber second || !double.IsFinite(second.Value))
        {
            return false;
        }

        low = first.Value;
        high = second.Value;
        return true;
    }

    /// <summary>Exactly <paramref name="count"/> finite numbers, as a matte states its colour.</summary>
    private bool TryNumbers(PdfObject entry, int count, out double[]? values)
    {
        values = null;
        if (entry is not PdfArray array || array.Count != count)
            return false;

        var numbers = new double[count];
        for (int i = 0; i < count; i++)
        {
            if (_store.Resolve(array[i]) is not PdfNumber number || !double.IsFinite(number.Value))
                return false;

            numbers[i] = Clamp(number.Value);
        }

        values = numbers;
        return true;
    }

    /// <summary>
    /// How many components a device-space picture's colour has - one for gray,
    /// three for RGB - or zero for any other space.
    /// </summary>
    private int DeviceComponents(PdfDictionary dictionary, PdfDictionary? resources) =>
        ColorSpaceFamily(ResolveColorSpace(dictionary["ColorSpace"], resources)) switch
        {
            "DeviceGray" => 1,
            "DeviceRGB" => 3,
            _ => 0,
        };

    /// <summary>
    /// Converts a codec's pixels through the profile of the ICC-based space the
    /// image names, where a reader is composed; false with the reason where the
    /// profile was declined, since its colours are then unstated.
    /// </summary>
    private bool TryConvertCodecPixels(PdfDictionary dictionary, PdfDictionary? resources, byte[] rgba, out string? refusal)
    {
        refusal = null;
        PdfObject? space = ResolveColorSpace(dictionary["ColorSpace"], resources);
        if (_store.ColorProfileReader is null || ColorSpaceFamily(space) != "ICCBased")
            return true;

        if (!TryColorTransform(space, IntentFor(dictionary), out PdfColorTransform? transform, out string reason))
        {
            refusal = reason;
            return false;
        }

        // A codec hands back one component as gray in every channel, or three.
        int components = transform!.Components;
        if (components is not (1 or 3))
        {
            refusal = "an ICC-based picture a codec decoded to fewer components than its profile converts";
            return false;
        }

        Span<double> levels = stackalloc double[3];
        for (int at = 0; at < rgba.Length; at += BPixelBuffer.BytesPerPixel)
        {
            for (int c = 0; c < components; c++)
                levels[c] = rgba[at + c] / 255d;

            transform.ToRgb(levels[..components], rgba.AsSpan(at, 3));
        }

        return true;
    }

    /// <summary>
    /// The rendering intent an image is drawn with: its own <c>/Intent</c>
    /// where it states one, and otherwise the graphics state's.
    /// </summary>
    private PdfRenderingIntent IntentFor(PdfDictionary dictionary) =>
        _store.Resolve(dictionary["Intent"]) is PdfName intent ? IntentNamed(intent.Value) : _state.RenderingIntent;

    /// <summary>
    /// The conversion an <c>[/ICCBased stream]</c> space's profile describes,
    /// built by the composed reader once per profile and intent, or false with
    /// the reason.
    /// </summary>
    private bool TryColorTransform(PdfObject? space, PdfRenderingIntent intent, out PdfColorTransform? transform, out string refusal)
    {
        transform = null;
        if (space is not PdfArray array || array.Count < 2 || _store.Resolve(array[1]) is not PdfStream profile)
        {
            refusal = "an ICC-based colour space with no profile";
            return false;
        }

        if (!_colorTransforms.TryGetValue((profile, intent), out (PdfColorTransform? Transform, string Refusal) built))
        {
            built = BuildColorTransform(profile, intent);
            _colorTransforms[(profile, intent)] = built;
        }

        transform = built.Transform;
        refusal = built.Refusal;
        return transform is not null;
    }

    /// <summary>
    /// Reads one profile through the composed reader. The profile is decoded
    /// through the same pipeline and budget as any stream, bounded by
    /// <see cref="PdfLimits.MaxColorProfileBytes"/>, and a reader that faults
    /// costs the images that name the profile rather than the document.
    /// </summary>
    private (PdfColorTransform? Transform, string Refusal) BuildColorTransform(PdfStream profile, PdfRenderingIntent intent)
    {
        PdfDictionary dictionary = profile.Dictionary;
        int components = Integer(dictionary, "N", "N");
        if (components is not (1 or 3 or 4))
            return (null, "an ICC-based colour space whose component count this build does not convert");

        // /Range reinterprets the components before the profile sees them,
        // which the default leaves alone and anything else would need applying.
        if (_store.Resolve(dictionary["Range"]) is PdfArray range && !IsUnitRange(range, components))
            return (null, "an ICC-based colour space with a Range other than the default");

        if (_store.ColorProfileReader is not IPdfColorProfileReader reader)
            return (null, "the colour space ICCBased, whose profile no composed reader converts");

        PdfStreamDecodeResult decoded;
        try
        {
            decoded = _store.Filters.Decode(profile, _store.Resolve, _store.Budget);
        }
        catch (PdfLimitExceededException)
        {
            return (null, "an ICC profile past the read's remaining allowance");
        }

        if (!decoded.Succeeded || decoded.Data is not { } bytes)
            return (null, "an ICC profile that could not be decoded");

        long ceiling = _store.Budget.Limits.MaxColorProfileBytes;
        if (bytes.LongLength > ceiling)
            return (null, "an ICC profile past the size a reader is handed");

        try
        {
            PdfColorTransform? transform = reader.Read(
                bytes, components, intent, new PdfColorProfileContext(ceiling, _store.Budget.Cancellation), out string? declined);

            if (transform is null)
            {
                return (null, declined is { Length: > 0 }
                    ? "an ICC profile the composed reader declined (" + declined + ")"
                    : "an ICC profile the composed reader declined");
            }

            if (transform.Components != components)
                return (null, "an ICC profile whose conversion does not take the components the space declares");

            return (transform, string.Empty);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
        {
            return (null, "an ICC profile the composed reader failed on");
        }
    }

    /// <summary>Whether a <c>/Range</c> array is [0 1] for every component.</summary>
    private bool IsUnitRange(PdfArray range, int components)
    {
        if (range.Count != components * 2)
            return false;

        for (int i = 0; i < range.Count; i++)
        {
            if (_store.Resolve(range[i]) is not PdfNumber number || number.Value != (i % 2 == 0 ? 0 : 1))
                return false;
        }

        return true;
    }

    /// <summary>
    /// A decoded mask's samples, read as levels in [0, 1]: packed at their own
    /// depth, or the first channel of a codec's pixels.
    /// </summary>
    private readonly struct MaskSamples(byte[] data, int bits, long stride, bool pixels)
    {
        public double At(int x, int y)
        {
            long row = y * stride;
            if (pixels)
                return data[row + ((long)x * BPixelBuffer.BytesPerPixel)] / 255d;

            if (bits == 16)
            {
                long at = row + ((long)x * 2);
                return ((data[at] << 8) | data[at + 1]) / 65535d;
            }

            if (bits == 8)
                return data[row + x] / 255d;

            int perByte = 8 / bits;
            byte packed = data[row + (x / perByte)];
            int shift = 8 - bits - (x % perByte * bits);
            return ((packed >> shift) & ((1 << bits) - 1)) / (double)((1 << bits) - 1);
        }
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
        PdfRenderingIntent intent = IntentFor(dictionary);

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

            case "ICCBased":
                if (!TryColorTransform(space, intent, out PdfColorTransform? transform, out refusal))
                    return false;

                if (transform!.Components != 1 && bits != 8)
                {
                    refusal = "an ICC-based image of several components at a depth other than eight bits";
                    return false;
                }

                if (!TryDecodeArray(dictionary, components: transform.Components, upper: 1, out double[]? profileDecode))
                {
                    refusal = "a Decode array outside the range the format allows";
                    return false;
                }

                format = new PdfSampleFormat(shape.Width, shape.Height, bits, PdfSampleSpace.Profile, null, profileDecode, transform);
                return true;

            case "Indexed":
                if (space is not PdfArray indexed || !TryPalette(indexed, intent, out byte[]? palette, out refusal))
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
    private bool TryPalette(PdfArray space, PdfRenderingIntent intent, out byte[]? palette, out string refusal)
    {
        palette = null;
        refusal = string.Empty;

        if (space.Count != 4)
        {
            refusal = "an Indexed colour space this build cannot read";
            return false;
        }

        // An ICC-based base converts each entry once, here, so the lookup that
        // follows is the same one a device palette gets.
        PdfObject? baseSpace = _store.Resolve(space[1]);
        PdfColorTransform? transform = null;
        int components = ColorSpaceFamily(baseSpace) switch
        {
            "DeviceGray" => 1,
            "DeviceRGB" => 3,
            "ICCBased" => TryColorTransform(baseSpace, intent, out transform, out refusal) ? transform!.Components : -1,
            _ => 0,
        };

        if (components < 0)
            return false;

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
        if (transform is not null)
        {
            Span<double> levels = stackalloc double[4];
            for (int i = 0; i < entries; i++)
            {
                for (int c = 0; c < components; c++)
                    levels[c] = lookup[(i * components) + c] / 255d;

                transform.ToRgb(levels[..components], palette.AsSpan(i * 3, 3));
            }

            return true;
        }

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
