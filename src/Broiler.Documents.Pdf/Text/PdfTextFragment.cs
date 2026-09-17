using System.Globalization;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Pdf.Text;

/// <summary>
/// A run of glyphs the interpreter placed contiguously on one baseline, with the
/// geometry the reading-order pass needs and nothing more.
/// </summary>
/// <remarks>
/// Fragments exist only between content interpretation and model projection. No
/// coordinate survives into <c>RichTextDocument</c>: the rich-text model is a
/// logical model, and keeping a hidden geometry side channel in it would be a
/// fixed-layout claim the format cannot honour on re-pagination.
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
    bool artifact = false)
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

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"'{Text}' @ ({X:F1},{Y:F1}) {FontSize:F1}pt");
}
