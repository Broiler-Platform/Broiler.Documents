using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Broiler.Documents.Model;
using Broiler.Graphics;

namespace Broiler.Documents.Cli.Rendering;

/// <summary>
/// One drawable piece of a line: a stretch of text in a single font and colour,
/// or one inline image. Positions are page-relative points.
/// </summary>
/// <remarks>
/// A piece is already resolved for drawing. <see cref="Text"/> carries the
/// capitalization transform applied, so the measured width and the drawn glyphs
/// cannot disagree, and small capitals arrive as two pieces at two sizes rather
/// than as a flag someone has to remember to honour.
/// </remarks>
public sealed class LayoutPiece
{
    public LayoutPiece(
        string text,
        BFontStyle font,
        BColor color,
        BColor highlight,
        bool underline,
        bool strikethrough,
        string? link,
        InlineImage? image,
        double width,
        double ascent,
        double descent,
        bool oblique = false,
        bool isTab = false)
    {
        IsTab = isTab;
        Text = text;
        Font = font;
        Color = color;
        Highlight = highlight;
        Underline = underline;
        Strikethrough = strikethrough;
        Link = link;
        Image = image;
        Width = width;
        Ascent = ascent;
        Descent = descent;
        Oblique = oblique;
    }

    public string Text { get; }

    public BFontStyle Font { get; }

    public BColor Color { get; }

    /// <summary>The highlight behind this piece, or <see cref="BColor.Empty"/> for none.</summary>
    public BColor Highlight { get; }

    public bool Underline { get; }

    public bool Strikethrough { get; }

    public string? Link { get; }

    /// <summary>The image this piece draws instead of text, or null.</summary>
    public InlineImage? Image { get; }

    public bool IsImage => Image is not null;

    /// <summary>
    /// True for a tab: a gap of measured width that draws no glyphs. Its width is
    /// only known once wrapping knows how far along its line it starts.
    /// </summary>
    public bool IsTab { get; }

    /// <summary>Horizontal advance in points.</summary>
    public double Width { get; internal set; }

    /// <summary>Height above the baseline in points.</summary>
    public double Ascent { get; }

    /// <summary>Height below the baseline in points.</summary>
    public double Descent { get; }

    /// <summary>
    /// True when this piece is italic but will be drawn with an upright face, so
    /// the rasterizer shears it. See <see cref="LayoutSettings.SynthesizeItalic"/>.
    /// </summary>
    public bool Oblique { get; }

    /// <summary>Where this piece starts, relative to the page's left edge. Filled in during placement.</summary>
    public double X { get; internal set; }
}

/// <summary>One laid-out line, positioned on its page.</summary>
public sealed class LayoutLine
{
    private readonly List<LayoutPiece> _pieces;

    internal LayoutLine(List<LayoutPiece> pieces, double top, double height, double baseline, int paragraphIndex)
    {
        _pieces = pieces;
        Top = top;
        Height = height;
        Baseline = baseline;
        ParagraphIndex = paragraphIndex;
    }

    public IReadOnlyList<LayoutPiece> Pieces => _pieces;

    /// <summary>Distance from the page's top edge to the line box, in points.</summary>
    public double Top { get; internal set; }

    /// <summary>
    /// Blank space the line leaves above itself, because a wrapping shape left no
    /// room beside it and it had to move past.
    /// </summary>
    /// <remarks>
    /// Carried on the line rather than folded into <see cref="Top"/>: the caller
    /// flows lines onto pages, so it has to see the skip to decide whether the
    /// line still fits on this one.
    /// </remarks>
    public double LeadingSkip { get; internal set; }

    public double Height { get; }

    /// <summary>Distance from <see cref="Top"/> down to the baseline, in points.</summary>
    public double Baseline { get; }

    /// <summary>Which model paragraph this line came from. Carried through for diagnostics.</summary>
    public int ParagraphIndex { get; }
}

/// <summary>One rendered page.</summary>
/// <summary>
/// A floating shape placed on a page: its box in page points, how it is painted,
/// and any text or picture it carries.
/// </summary>
public sealed class LayoutShape
{
    internal LayoutShape(
        BRect bounds,
        ShapeFill? fill,
        BColor outline,
        IReadOnlyList<LayoutLine> lines,
        InlineImage? image = null,
        bool behindText = true)
    {
        Bounds = bounds;
        Fill = fill;
        Outline = outline;
        Lines = lines;
        Image = image;
        BehindText = behindText;
    }

    public BRect Bounds { get; }

    public ShapeFill? Fill { get; }

    public BColor Outline { get; }

    /// <summary>The picture the box draws, filling <see cref="Bounds"/>, or null.</summary>
    public InlineImage? Image { get; }

    /// <summary>The shape's own text, already positioned inside its box.</summary>
    public IReadOnlyList<LayoutLine> Lines { get; }

    /// <summary>True when the shape is painted under the page's text rather than over it.</summary>
    public bool BehindText { get; }
}

/// <summary>
/// One table cell placed on a page: the box it occupies, how it is painted, and
/// the edges drawn around it. The text inside it is in the page's ordinary
/// lines - a cell is a box behind them, not a container of them.
/// </summary>
public sealed class LayoutCell
{
    internal LayoutCell(BRect bounds, BColor shading, CellBorders borders)
    {
        Bounds = bounds;
        Shading = shading;
        Borders = borders;
    }

    public BRect Bounds { get; }

    /// <summary>The cell's background, or <see cref="BColor.Empty"/> for none.</summary>
    public BColor Shading { get; }

    public CellBorders Borders { get; }
}

public sealed class LayoutPage
{
    private readonly List<LayoutLine> _lines;
    private readonly List<LayoutShape> _shapes;
    private readonly List<LayoutCell> _cells;

    internal LayoutPage(
        int number,
        double widthPoints,
        double heightPoints,
        List<LayoutLine> lines,
        List<LayoutShape>? shapes = null,
        List<LayoutCell>? cells = null)
    {
        Number = number;
        WidthPoints = widthPoints;
        HeightPoints = heightPoints;
        _lines = lines;
        _shapes = shapes ?? [];
        _cells = cells ?? [];
    }

    /// <summary>The floating shapes on this page, drawn under its text.</summary>
    public IReadOnlyList<LayoutShape> Shapes => _shapes;

    /// <summary>The table cells on this page, drawn under its text.</summary>
    public IReadOnlyList<LayoutCell> Cells => _cells;

    /// <summary>One-based page number.</summary>
    public int Number { get; }

    public double WidthPoints { get; }

    public double HeightPoints { get; }

    public IReadOnlyList<LayoutLine> Lines => _lines;
}

/// <summary>A document laid out onto pages, ready to rasterize.</summary>
public sealed class LayoutResult
{
    internal LayoutResult(
        IReadOnlyList<LayoutPage> pages,
        PageSetup setup,
        LayoutSettings settings,
        IReadOnlyList<string> notes,
        bool truncated)
    {
        Pages = pages;
        Setup = setup;
        Settings = settings;
        Notes = new ReadOnlyCollection<string>(new List<string>(notes));
        Truncated = truncated;
    }

    public IReadOnlyList<LayoutPage> Pages { get; }

    /// <summary>The page box the layout used, with the final height for a continuous render.</summary>
    public PageSetup Setup { get; }

    /// <summary>
    /// The typographic settings the layout used, after the document's own
    /// defaults were folded into the caller's. Exposed for the same reason as
    /// <see cref="Setup"/>: two renders are only comparable when these matched,
    /// so the manifest records them and a caller can read back what was resolved
    /// rather than what was asked for.
    /// </summary>
    public LayoutSettings Settings { get; }

    /// <summary>What the layout had to approximate or could not do, for the manifest.</summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>True when <c>--max-pages</c> cut the document short.</summary>
    public bool Truncated { get; }
}

/// <summary>
/// The typographic choices a layout needs, resolved for one render: the caller's
/// flags, with the document's own <see cref="DocumentStyleDefaults"/> filled in
/// where the caller asked for nothing.
/// </summary>
/// <remarks>
/// The model is a list of styled paragraphs and nothing else: no page, no
/// default font, no indent width, no list marker. Everything a reader would
/// nonetheless expect to see has to be supplied, and supplying it explicitly -
/// rather than burying constants in the layout - is what makes two renders
/// comparable and a difference between them attributable.
/// </remarks>
public sealed record LayoutSettings
{
    /// <summary>The family used by runs that name none.</summary>
    public string DefaultFontFamily { get; init; } = "sans-serif";

    /// <summary>The size in points used by runs that name none.</summary>
    public double DefaultFontSizePoints { get; init; } = 11.0;

    /// <summary>The colour used by runs that name none.</summary>
    public BColor DefaultForeground { get; init; } = BColor.Black;

    /// <summary>Width of one <c>IndentLevel</c>, in points. 18pt is a quarter inch.</summary>
    public double IndentStepPoints { get; init; } = 18.0;

    /// <summary>
    /// Distance between the default tab stops, in points, measured from where the
    /// paragraph's text starts. 36pt is the half inch a word processor starts a
    /// document with, and twice <see cref="IndentStepPoints"/> so tabs and indent
    /// levels share a grid.
    /// </summary>
    public double TabStopPoints { get; init; } = 36.0;

    /// <summary>Gap between a list marker and the text it introduces, in points.</summary>
    public double ListMarkerGapPoints { get; init; } = 6.0;

    /// <summary>Stop after this many pages. Guards against a document that never ends.</summary>
    public int MaxPages { get; init; } = 200;

    /// <summary>Draw a hairline box around the content area. A layout debugging aid.</summary>
    public bool ShowContentBox { get; init; }

    /// <summary>Underline and colour link runs the way a reader expects.</summary>
    public bool DecorateLinks { get; init; } = true;

    /// <summary>The colour link runs take when <see cref="DecorateLinks"/> is on and the run names none.</summary>
    public BColor LinkColor { get; init; } = new(0x00, 0x33, 0xCC);

    /// <summary>
    /// Shear italic runs whose family has no real italic face, so that italic is
    /// visible in the output instead of silently drawing upright.
    /// </summary>
    /// <remarks>
    /// A synthetic oblique is not a substitute for a designed italic and is not
    /// pretending to be. It exists because the alternative is worse for this
    /// tool's purpose: with no italic face mapped, an italic run draws exactly
    /// like the text around it, so a codec that dropped italic would produce a
    /// pixel-identical page and the comparison would pass. Shearing keeps the
    /// horizontal advances untouched, so nothing about the layout moves.
    /// </remarks>
    public bool SynthesizeItalic { get; init; } = true;

    /// <summary>The slant applied to a synthetic oblique, as a tangent. 0.21 is about 12 degrees.</summary>
    public double ObliqueSlant { get; init; } = 0.21;

    /// <summary>
    /// Answers whether a family will draw with a real italic face. Supplied by
    /// the font resolution; a null predicate means no family has one.
    /// </summary>
    public Func<string, bool>? ItalicFaceAvailable { get; init; }
}
