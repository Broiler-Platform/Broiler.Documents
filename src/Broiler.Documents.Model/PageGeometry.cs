using System;

namespace Broiler.Documents.Model;

/// <summary>
/// The page a document says it is written for: its size, its margins, and how
/// far its header and footer sit from the edge. All in points.
/// </summary>
/// <remarks>
/// <para>
/// One geometry per document rather than one per section. Every format can
/// express several — DOCX gives each section its own <c>w:sectPr</c> — and a
/// document that uses more than one raises a diagnostic and keeps the last,
/// which is the one its final section and its running content already agree
/// with.
/// </para>
/// <para>
/// This is what a renderer needs to place a page rather than invent one. Before
/// it, a header sat centred in whatever margin the caller happened to ask for
/// and a shape in the left margin needed a margin wide enough to hold it; the
/// document knew both numbers all along and nothing read them.
/// </para>
/// </remarks>
public sealed record PageGeometry(
    double Width,
    double Height,
    double MarginLeft,
    double MarginRight,
    double MarginTop,
    double MarginBottom,
    double HeaderDistance = 0,
    double FooterDistance = 0)
{
    /// <summary>A4 portrait with 1 inch margins, for a document that states none.</summary>
    public static PageGeometry A4 { get; } = new(595.276, 841.89, 72, 72, 72, 72, 36, 36);

    /// <summary>The width left for text between the side margins.</summary>
    public double ContentWidth => Math.Max(0, Width - MarginLeft - MarginRight);

    /// <summary>The height left for text between the top and bottom margins.</summary>
    public double ContentHeight => Math.Max(0, Height - MarginTop - MarginBottom);

    /// <summary>True when the page is wider than it is tall.</summary>
    public bool IsLandscape => Width > Height;

    /// <summary>
    /// True when every number is finite, positive where it must be, and the
    /// margins leave a column to write in. A producer that states nonsense is
    /// better ignored than honoured.
    /// </summary>
    public bool IsUsable =>
        double.IsFinite(Width) && Width > 0 &&
        double.IsFinite(Height) && Height > 0 &&
        MarginLeft >= 0 && MarginRight >= 0 && MarginTop >= 0 && MarginBottom >= 0 &&
        ContentWidth > 0 && ContentHeight > 0;
}
