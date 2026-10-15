using System;
using System.Globalization;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Pdf.Text;

/// <summary>
/// A run of glyphs the interpreter placed contiguously on one baseline, with the
/// geometry the reading-order pass needs and nothing more.
/// </summary>
/// <remarks>
/// <para>
/// Fragments exist only between content interpretation and model projection. No
/// coordinate survives into <c>RichTextDocument</c>: the rich-text model is a
/// logical model, and keeping a hidden geometry side channel in it would be a
/// fixed-layout claim the format cannot honour on re-pagination.
/// </para>
/// <para>
/// Page space is the page as a viewer displays it: turned by its <c>/Rotate</c>,
/// in points, from the visible box's lower-left corner (<c>PdfPage.Display</c>).
/// </para>
/// <para>
/// A run turned against that page is measured in its own <see cref="Frame"/>
/// first, because that is where it is a line of text: <c>x</c>, <c>y</c> and
/// <c>endX</c> are where its baseline starts, sits and stops in that frame.
/// <see cref="X"/>, <see cref="Y"/> and <see cref="EndX"/> are still page space
/// for every run, so everything that asks where a run is on the page - which
/// cell holds it, what was painted under it - asks the same question of a
/// turned run as of any other.
/// </para>
/// </remarks>
internal sealed class PdfTextFragment(
    string text,
    double x,
    double y,
    double endX,
    double fontSize,
    double spaceWidth,
    string fontFamily,
    bool bold,
    bool italic,
    BColor color,
    int renderMode,
    int mcid = -1,
    bool artifact = false,
    int order = 0,
    bool turned = false,
    double clipLeft = double.NegativeInfinity,
    double clipRight = double.PositiveInfinity,
    PdfTextFrame? frame = null)
{
    /// <summary>
    /// How far a turned run's letters reach above and below its baseline, in
    /// ems, for the box <see cref="X"/>, <see cref="Y"/> and <see cref="EndX"/>
    /// describe: about where a Latin face's ascenders and descenders end.
    /// </summary>
    private const double BoxAscent = 0.8;

    /// <inheritdoc cref="BoxAscent"/>
    private const double BoxDescent = 0.2;

    public string Text { get; } = text;

    /// <summary>The frame the run reads in: <see cref="PdfTextFrame.Upright"/> unless it is turned.</summary>
    public PdfTextFrame Frame { get; } = frame ?? PdfTextFrame.Upright;

    /// <summary>Where the run's baseline starts, measured along its <see cref="Frame"/>.</summary>
    public double FrameX { get; } = x;

    /// <summary>Where the run's baseline sits, measured across its <see cref="Frame"/>.</summary>
    public double FrameY { get; } = y;

    /// <summary>Where the run's baseline stops, measured along its <see cref="Frame"/>.</summary>
    public double FrameEndX { get; } = endX;

    /// <summary>Left edge of the run in page space, in points.</summary>
    /// <remarks>For a turned run, the left edge of the box its letters cover.</remarks>
    public double X { get; } = OnPage(frame, x, y, endX, fontSize).X;

    /// <summary>Baseline position in page space, in points, y increasing upward.</summary>
    /// <remarks>
    /// For a turned run, the height of the middle of the box its letters cover:
    /// the one point that puts a label running up a margin in the band, the
    /// cell and the column it was drawn in, where its baseline's start would
    /// put it at the foot of all three.
    /// </remarks>
    public double Y { get; } = OnPage(frame, x, y, endX, fontSize).Y;

    /// <summary>Right edge of the run in page space.</summary>
    /// <remarks>For a turned run, the right edge of the box its letters cover.</remarks>
    public double EndX { get; } = OnPage(frame, x, y, endX, fontSize).EndX;

    /// <summary>
    /// The page-space run a turned run was measured in its own frame from, where
    /// this one is that measurement (<see cref="InFrame"/>); null for a run read
    /// off the page.
    /// </summary>
    public PdfTextFragment? Source { get; private init; }

    /// <summary>Effective font size in points, after the text and current matrices.</summary>
    public double FontSize { get; } = fontSize;

    /// <summary>Width of one space in this run's font and size, for gap detection.</summary>
    public double SpaceWidth { get; } = spaceWidth;

    public string FontFamily { get; } = fontFamily;

    public bool Bold { get; } = bold;

    public bool Italic { get; } = italic;

    /// <summary>True where a rule was painted along this run's baseline.</summary>
    /// <remarks>
    /// Settable, and the only thing about a fragment that is. PDF has no
    /// text-decoration operator: an underline is a separate path-painting
    /// operation, drawn before or after the run it belongs to and related to it
    /// by nothing but coordinates. <see cref="PdfTextDecorations"/> matches the
    /// two up once the page's paths are all known, which cannot happen while the
    /// run is being built.
    /// </remarks>
    public bool Underline { get; set; }

    /// <summary>True where a rule was painted across this run's glyphs.</summary>
    /// <remarks>Set the same way, and for the same reason, as <see cref="Underline"/>.</remarks>
    public bool Strikethrough { get; set; }

    /// <summary>
    /// The colour of a fill painted beneath this run, or <see cref="BColor.Empty"/>
    /// for none.
    /// </summary>
    /// <remarks>
    /// Set the same way, and for the same reason, as <see cref="Underline"/>: a
    /// panel behind a heading is a separate fill, painted before the words and
    /// related to them by nothing but coordinates. White text on a dark band is
    /// the case that makes it matter. Without the band it is white on white.
    /// </remarks>
    public BColor Background { get; set; }

    public BColor Color { get; } = color;

    /// <summary>
    /// The text rendering mode (Tr). Mode 3 is invisible and mode 7 is
    /// clipping-only; both are extracted but flagged, because deciding they are
    /// not "really" in the document would be a visibility claim this release does
    /// not make.
    /// </summary>
    public int RenderMode { get; } = renderMode;

    /// <summary>True for a rendering mode that paints nothing.</summary>
    public bool IsInvisible => RenderMode is 3 or 7;

    /// <summary>
    /// The marked-content id this run was drawn under, or -1 where it was drawn
    /// outside any marked content.
    /// </summary>
    /// <remarks>
    /// Carried only so a tagged document's structure tree can put the run in the
    /// order its author declared. It is page-scoped, meaningless on its own, and
    /// like every other coordinate here it stops at model projection.
    /// </remarks>
    public int Mcid { get; } = mcid;

    /// <summary>
    /// True for a run drawn inside an <c>/Artifact</c> marked-content sequence:
    /// a running head, a folio, a table rule, a watermark.
    /// </summary>
    /// <remarks>
    /// Carried for one reason. PDF 32000-1 &#167;14.8.2.2 defines an artifact as
    /// content that is <em>not</em> part of the author's logical content, and a
    /// structure tree therefore never places one. A page's tagged runs can be
    /// fully accounted for while its header is not, so a coverage test that
    /// counted artifacts would fail on almost every real tagged document and
    /// send the page back to geometry that the tree could have ordered.
    /// </remarks>
    public bool IsArtifact { get; } = artifact;

    /// <summary>
    /// Where the run falls in the page's paint order, counted with the paths and
    /// pictures on the same page. Zero where nothing counted it.
    /// </summary>
    public int Order { get; } = order;

    /// <summary>
    /// True for a run drawn anywhere but left to right along the page as
    /// displayed: sideways, upside down, mirrored, or at a slant.
    /// </summary>
    /// <remarks>
    /// A turned run is read along its own baseline (<see cref="Frame"/>), and
    /// the model it is read into holds no text direction, so the turn itself
    /// goes no further than this. It is carried so the read can say how much
    /// text that happened to.
    /// </remarks>
    public bool IsTurned { get; } = turned;

    /// <summary>
    /// The left and right edges, in page space, of the clip the run was drawn
    /// under, or infinities where nothing narrowed it. Glyphs outside them are
    /// set and never shown.
    /// </summary>
    /// <remarks>
    /// Only how far the clip reaches across the page is carried, and only where
    /// its path was straight lines: the box of a curved clip is not known, and
    /// a run is never cut short where it may be showing. A producer that clips
    /// every table cell lets an item name longer than its cell run on under the
    /// price beside it, and the page shows a gap there that the run's advance
    /// does not.
    /// </remarks>
    public double ClipLeft { get; } = clipLeft;

    /// <inheritdoc cref="ClipLeft"/>
    public double ClipRight { get; } = clipRight;

    /// <summary>Where the run starts showing: its left edge, or the clip's where that cuts it.</summary>
    public double VisibleX => Math.Max(X, Math.Min(ClipLeft, EndX));

    /// <summary>Where the run stops showing: its right edge, or the clip's where that cuts it.</summary>
    public double VisibleEndX => Math.Min(EndX, Math.Max(ClipRight, X));

    /// <summary>
    /// The box on the page the run's letters cover, reaching
    /// <paramref name="descent"/> ems below its baseline and
    /// <paramref name="ascent"/> above it.
    /// </summary>
    /// <remarks>
    /// An upright run's box runs from <see cref="X"/> to <see cref="EndX"/> as
    /// they are, so a run set backwards keeps the edges it always had. A turned
    /// run's box is the smallest upright one around its letters.
    /// </remarks>
    public (double Left, double Bottom, double Right, double Top) Box(double descent, double ascent) =>
        BoxOf(Frame, FrameX, FrameY, FrameEndX, FontSize, descent, ascent);

    /// <summary>
    /// This run as it reads in <paramref name="frame"/>: the same letters and
    /// style, with <see cref="X"/>, <see cref="Y"/> and <see cref="EndX"/>
    /// measured along and across that frame instead of the page.
    /// </summary>
    /// <remarks>
    /// The line and paragraph passes are written for text that runs across the
    /// page, and in its own frame a turned run does. The measurement carries no
    /// clip - a clip's reach across the page says nothing about a run that does
    /// not run across it - and keeps this run as its <see cref="Source"/>, for
    /// the questions that are asked on the page, such as which link covers it.
    /// </remarks>
    public PdfTextFragment InFrame(PdfTextFrame frame)
    {
        (double pageX, double pageY) = Frame.ToPage(FrameX, FrameY);
        double along = frame.Along(pageX, pageY);

        return new PdfTextFragment(
            Text,
            along,
            frame.Across(pageX, pageY),
            along + (FrameEndX - FrameX),
            FontSize,
            SpaceWidth,
            FontFamily,
            Bold,
            Italic,
            Color,
            RenderMode,
            Mcid,
            IsArtifact,
            Order,
            IsTurned)
        {
            Underline = Underline,
            Strikethrough = Strikethrough,
            Background = Background,
            Source = this,
        };
    }

    /// <summary>Where a run measured in <paramref name="frame"/> is described on the page.</summary>
    private static (double X, double Y, double EndX) OnPage(PdfTextFrame? frame, double x, double y, double endX, double fontSize)
    {
        if (frame is not { } turned || turned == PdfTextFrame.Upright)
            return (x, y, endX);

        (double left, double bottom, double right, double top) = BoxOf(turned, x, y, endX, fontSize, BoxDescent, BoxAscent);
        return (left, (bottom + top) / 2, right);
    }

    private static (double Left, double Bottom, double Right, double Top) BoxOf(
        PdfTextFrame frame, double x, double y, double endX, double fontSize, double descent, double ascent)
    {
        double below = y - (fontSize * descent);
        double above = y + (fontSize * ascent);

        if (frame == PdfTextFrame.Upright)
            return (x, below, endX, above);

        (double x1, double y1) = frame.ToPage(x, below);
        (double x2, double y2) = frame.ToPage(x, above);
        (double x3, double y3) = frame.ToPage(endX, below);
        (double x4, double y4) = frame.ToPage(endX, above);

        return (
            Math.Min(Math.Min(x1, x2), Math.Min(x3, x4)),
            Math.Min(Math.Min(y1, y2), Math.Min(y3, y4)),
            Math.Max(Math.Max(x1, x2), Math.Max(x3, x4)),
            Math.Max(Math.Max(y1, y2), Math.Max(y3, y4)));
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"'{Text}' @ ({X:F1},{Y:F1}) {FontSize:F1}pt");
}
