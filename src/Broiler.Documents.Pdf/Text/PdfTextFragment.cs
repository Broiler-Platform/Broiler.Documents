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
    bool turned = false)
{
    public string Text { get; } = text;

    /// <summary>Left edge of the run in page space, in points.</summary>
    public double X { get; } = x;

    /// <summary>Baseline position in page space, in points, y increasing upward.</summary>
    public double Y { get; } = y;

    /// <summary>Right edge of the run in page space.</summary>
    public double EndX { get; } = endX;

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
    /// Everything downstream sets text on horizontal lines, and a turned run is
    /// placed there all the same - usually one letter to a fragment, since its
    /// pen leaves the baseline after every glyph. It is carried so the read can
    /// say how much text that happened to, rather than hand back scattered
    /// letters as if they were what the page said.
    /// </remarks>
    public bool IsTurned { get; } = turned;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"'{Text}' @ ({X:F1},{Y:F1}) {FontSize:F1}pt");
}
