using System;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Model;

/// <summary>
/// A page's line text grid: the lines of its body are set on a grid of equal
/// rows, each <see cref="BaseHeight"/> for the text and <see cref="RubyHeight"/>
/// for the ruby above it (below where <see cref="RubyBelow"/> says so). All in
/// points.
/// </summary>
/// <remarks>
/// <para>
/// ODF's <c>style:layout-grid-mode</c> <c>line</c> or <c>both</c> on a page
/// layout, which East Asian documents use for manuscript paper. Writer sets a
/// body line that snaps to the grid one or more rows tall
/// (<c>SwTextFormatter::CalcRealHeight</c>): as many rows as its text needs, its
/// text centred in them, and only proportional line spacing (at least single)
/// changing that height. Each line starts on a row, so space above a paragraph
/// moves it to the next row rather than by its own amount.
/// </para>
/// <para>
/// The rows fill the body to at most <see cref="Lines"/> of them, and Writer
/// 24.2 centres them in the body (<c>SwBodyFrame::Format</c>), which
/// <see cref="CentredInBody"/> says. A document with Word's grid metrics
/// (<c>MsWordCompGridMetrics</c>) starts them at the body's top, as Writer
/// does from 25.8 (tdf#165089).
/// </para>
/// <para>
/// A grid the document asks to print (<see cref="Printed"/>) is drawn in
/// <see cref="Color"/> under the text, as Writer paints it
/// (<c>SwPageFrame::PaintGrid</c>): each row's ruby strip and text strip, and
/// in the <c>both</c> mode the text strip divided into character cells
/// <see cref="CharWidth"/> wide. Those cells are only drawn: the spacing of
/// East Asian characters across them is not modelled.
/// </para>
/// </remarks>
public sealed record TextGrid(double BaseHeight, double RubyHeight, int Lines, bool RubyBelow = false, bool CentredInBody = true)
{
    /// <summary>True when the grid is drawn on the printed page.</summary>
    public bool Printed { get; init; }

    /// <summary>The colour the grid is drawn in.</summary>
    public BColor Color { get; init; } = new(192, 192, 192);

    /// <summary>
    /// The width of a character cell in the <c>both</c> mode, or nought for a
    /// grid of rows only.
    /// </summary>
    public double CharWidth { get; init; }

    /// <summary>The height of one row of the grid: its text and ruby together.</summary>
    public double Pitch => BaseHeight + RubyHeight;

    /// <summary>True when the grid has rows a line can be set on.</summary>
    public bool IsUsable =>
        double.IsFinite(BaseHeight) && double.IsFinite(RubyHeight) &&
        BaseHeight >= 0 && RubyHeight >= 0 && Pitch >= 1 && Lines >= 1;

    /// <summary>
    /// The top and bottom of the rows in a body from <paramref name="top"/> to
    /// <paramref name="bottom"/>: as many whole rows as fit there, at most
    /// <see cref="Lines"/>, centred where <see cref="CentredInBody"/> says so.
    /// </summary>
    public (double Top, double Bottom) RowsIn(double top, double bottom)
    {
        double height = Math.Max(0, bottom - top);
        int rows = Math.Min(Lines, (int)Math.Floor((height / Pitch) + 1e-6));
        if (rows < 1)
            return (top, bottom);

        double used = rows * Pitch;
        double border = CentredInBody ? (height - used) / 2 : 0;
        return (top + border, top + border + used);
    }

    /// <summary>
    /// The height of a line whose text is <paramref name="natural"/> points
    /// tall: the rows it needs, at least one.
    /// </summary>
    public double RowsFor(double natural) =>
        Math.Max(1, Math.Ceiling((natural / Pitch) - 1e-6)) * Pitch;

    /// <summary>
    /// <paramref name="y"/> moved down to the next row of a grid whose first row
    /// starts at <paramref name="origin"/>, or left where it is when it is on one.
    /// </summary>
    public double Snap(double y, double origin)
    {
        double rows = (y - origin) / Pitch;
        double whole = Math.Ceiling(rows - 1e-6);
        return origin + (Math.Max(0, whole) * Pitch);
    }
}
