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
/// <para>
/// The text area can be divided into <see cref="ColumnCount"/> columns of equal
/// width, <see cref="ColumnGap"/> apart, which text fills in turn before moving
/// to the next page. That is the newsletter and glossary layout every format
/// states on the page or the section, and a renderer that ignores it draws a
/// six-column page as six pages of one narrow column's worth of text each.
/// Columns of unequal width are held as equal ones, and a column break, which
/// moves to the next column rather than the next page, is not modelled.
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
    double FooterDistance = 0,
    int ColumnCount = 1,
    double ColumnGap = 0)
{
    /// <summary>A4 portrait with 1 inch margins, for a document that states none.</summary>
    public static PageGeometry A4 { get; } = new(595.276, 841.89, 72, 72, 72, 72, 36, 36);

    /// <summary>The width left for text between the side margins.</summary>
    public double ContentWidth => Math.Max(0, Width - MarginLeft - MarginRight);

    /// <summary>The height left for text between the top and bottom margins.</summary>
    public double ContentHeight => Math.Max(0, Height - MarginTop - MarginBottom);

    /// <summary>True when the page is wider than it is tall.</summary>
    public bool IsLandscape => Width > Height;

    /// <summary>True when the text area is divided into more than one column.</summary>
    public bool HasColumns => ColumnCount > 1;

    /// <summary>
    /// The width of one text column: the whole text area when there is one,
    /// otherwise what is left of it after the gaps, shared equally.
    /// </summary>
    public double ColumnWidth => ColumnCount <= 1
        ? ContentWidth
        : Math.Max(0, (ContentWidth - (ColumnGap * (ColumnCount - 1))) / ColumnCount);

    /// <summary>
    /// True when every number is finite, positive where it must be, and the
    /// margins leave a column to write in. A producer that states nonsense is
    /// better ignored than honoured.
    /// </summary>
    public bool IsUsable =>
        double.IsFinite(Width) && Width > 0 &&
        double.IsFinite(Height) && Height > 0 &&
        MarginLeft >= 0 && MarginRight >= 0 && MarginTop >= 0 && MarginBottom >= 0 &&
        ContentWidth > 0 && ContentHeight > 0 &&
        ColumnCount >= 1 && double.IsFinite(ColumnGap) && ColumnGap >= 0 && ColumnWidth > 0;

    /// <summary>
    /// This page divided into <paramref name="count"/> columns
    /// <paramref name="gap"/> points apart, or the page as it is when that would
    /// leave no room in a column. A reader states the columns it found through
    /// here, so a column count a producer got wrong costs the columns and not
    /// the page size and margins read beside it.
    /// </summary>
    public PageGeometry WithColumns(int count, double gap)
    {
        PageGeometry divided = this with { ColumnCount = count, ColumnGap = gap };
        return divided.IsUsable ? divided : this with { ColumnCount = 1, ColumnGap = 0 };
    }
}
