using System;
using System.Collections.Generic;
using Broiler.Graphics;

namespace Broiler.Documents.Model;

/// <summary>
/// A table over a run of the document's paragraphs: the grid they are arranged
/// in, and how each cell is bounded and painted.
/// </summary>
/// <remarks>
/// <para>
/// The cells hold no text of their own. A cell names a range of the document's
/// paragraphs, and those paragraphs are the ordinary body paragraphs they always
/// were - read in row-major order, which is the order a table's text reads in.
/// That is the whole of the design: a caret, a selection, a find, a style, and
/// every codec's text handling go on working through one flat list of
/// paragraphs, and the table says how to arrange what is already there.
/// </para>
/// <para>
/// The alternative was a block tree, where a paragraph is either a body
/// paragraph or lives inside a cell inside a row inside a table. That is what a
/// word processor does, and it means every position, every edit, every codec,
/// and every renderer learns to walk two shapes instead of one. This carries the
/// same information for reading, drawing, and writing a table back out.
/// </para>
/// <para>
/// What it gives up is what a paragraph-anchored object gives up here already
/// (see <see cref="DocumentShape"/>): the ranges are indices, so an edit that
/// adds or removes paragraphs moves them. <see cref="RichTextDocument"/> shifts
/// every range through the one place paragraph counts change, so typing in a
/// cell, splitting its paragraph, or deleting inside it keep the grid; an edit
/// that spans out of a cell and into the body does not, and cannot.
/// </para>
/// <para>
/// A table inside a cell is held by that cell, in <see cref="TableCell.Tables"/>.
/// Nesting is stated rather than worked out from the ranges, because the ranges
/// cannot always tell: a single-cell table holding a single-cell table covers
/// exactly the paragraphs the inner one does, and asking which table starts at a
/// paragraph would have no answer. A renderer walks a cell's tables the same way
/// it walks the document's, so it never has to know how deep it is.
/// </para>
/// </remarks>
public sealed class DocumentTable
{
    public DocumentTable(
        int paragraphIndex,
        int paragraphCount,
        IReadOnlyList<TableRow> rows,
        IReadOnlyList<double>? columnWidths = null,
        double cellPadding = DefaultCellPadding)
    {
        ParagraphIndex = Math.Max(0, paragraphIndex);
        ParagraphCount = Math.Max(0, paragraphCount);
        Rows = rows is null || rows.Count == 0 ? [] : [.. rows];
        ColumnWidths = columnWidths is null || columnWidths.Count == 0 ? [] : [.. columnWidths];
        CellPadding = Math.Max(0, cellPadding);
    }

    /// <summary>Word's own default cell margin: 0.08 inch on the left and right.</summary>
    public const double DefaultCellPadding = 5.4;

    /// <summary>The document paragraph the table starts at.</summary>
    public int ParagraphIndex { get; }

    /// <summary>How many paragraphs the table covers, cells and nested tables together.</summary>
    public int ParagraphCount { get; }

    /// <summary>One past the last paragraph the table covers.</summary>
    public int ParagraphEnd => ParagraphIndex + ParagraphCount;

    public IReadOnlyList<TableRow> Rows { get; }

    /// <summary>
    /// The grid's column widths in points, empty when the document stated none.
    /// A renderer with no widths divides the space it has evenly, which is what a
    /// word processor does with a table that states no grid.
    /// </summary>
    public IReadOnlyList<double> ColumnWidths { get; }

    /// <summary>The space between a cell's edge and its text, in points.</summary>
    public double CellPadding { get; }

    /// <summary>The sum of the stated column widths; zero when there are none.</summary>
    public double TotalWidth
    {
        get
        {
            double total = 0;
            foreach (double width in ColumnWidths)
                total += Math.Max(0, width);
            return total;
        }
    }

    /// <summary>True when <paramref name="paragraphIndex"/> falls inside this table.</summary>
    public bool Covers(int paragraphIndex) =>
        paragraphIndex >= ParagraphIndex && paragraphIndex < ParagraphEnd;

    /// <summary>
    /// The table in <paramref name="tables"/> that starts at
    /// <paramref name="paragraphIndex"/>, or null when none does. This is the
    /// whole of what walking block content takes: at every paragraph, ask, and
    /// lay out a grid instead of a paragraph when the answer is not null.
    /// </summary>
    public static DocumentTable? StartingAt(IReadOnlyList<DocumentTable> tables, int paragraphIndex)
    {
        for (int i = 0; i < tables.Count; i++)
        {
            if (tables[i].ParagraphIndex == paragraphIndex && tables[i].ParagraphCount > 0)
                return tables[i];
        }

        return null;
    }

    /// <summary>
    /// This table with its paragraph ranges moved for an edit that replaced
    /// <paramref name="removed"/> paragraphs at <paramref name="at"/> with
    /// <paramref name="inserted"/> of them. Returns null when the edit removed the
    /// table outright.
    /// </summary>
    /// <remarks>
    /// A range grows or shrinks when the edit fell inside it and moves when the
    /// edit fell before it, which is the rule that keeps a cell around the
    /// paragraphs it held while they are typed in and split.
    /// </remarks>
    public DocumentTable? Shifted(int at, int removed, int inserted)
    {
        if (removed == inserted)
            return this;

        (int index, int count) = ShiftRange(ParagraphIndex, ParagraphCount, at, removed, inserted);
        if (count <= 0)
            return null;

        var rows = new List<TableRow>(Rows.Count);
        int held = 0;
        foreach (TableRow row in Rows)
        {
            TableRow shifted = row.Shifted(at, removed, inserted);
            foreach (TableCell cell in shifted.Cells)
                held += cell.ParagraphCount;

            rows.Add(shifted);
        }

        // An edit that took every paragraph out of every cell took the table with
        // them. What the edit put back is body text: leaving the grid over it
        // would draw an empty table and lay the paragraph out inside no cell,
        // which is to say nowhere.
        return held == 0 ? null : new DocumentTable(index, count, rows, ColumnWidths, CellPadding);
    }

    /// <summary>
    /// Moves one paragraph range for an edit, by moving the two boundaries that
    /// define it. A range the edit fell inside grows or shrinks by what the edit
    /// changed; one it fell before slides; one it fell after does not move.
    /// </summary>
    internal static (int Index, int Count) ShiftRange(int index, int count, int at, int removed, int inserted)
    {
        int start = MapBoundary(index, at, removed, inserted);
        int end = MapBoundary(index + count, at, removed, inserted);
        return (start, Math.Max(0, end - start));
    }

    /// <summary>
    /// Where a boundary between paragraphs lands after an edit replaced
    /// <paramref name="removed"/> paragraphs at <paramref name="at"/> with
    /// <paramref name="inserted"/>. A boundary the edit swallowed lands at the
    /// edit's own start, which is what collapses a range the edit removed whole.
    /// </summary>
    private static int MapBoundary(int position, int at, int removed, int inserted)
    {
        if (position <= at)
            return position;

        return position >= at + removed ? position + inserted - removed : at;
    }
}

/// <summary>One row of a <see cref="DocumentTable"/>.</summary>
public sealed class TableRow
{
    public TableRow(IReadOnlyList<TableCell> cells, bool isHeader = false, double minHeight = 0)
    {
        Cells = cells is null || cells.Count == 0 ? [] : [.. cells];
        IsHeader = isHeader;
        MinHeight = double.IsFinite(minHeight) && minHeight > 0 ? minHeight : 0;
    }

    public IReadOnlyList<TableCell> Cells { get; }

    /// <summary>True for a row the document marks as repeating at the top of a page.</summary>
    public bool IsHeader { get; }

    /// <summary>
    /// The height in points the row asks for, or zero when it asks for none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <em>minimum</em>, deliberately, and never a ceiling: a row is at least
    /// this tall and grows for content that does not fit. A layout that clipped
    /// text to honour a stated height would lose the one thing the document is
    /// for, so a format's exact-height rule is read as a floor too and diagnosed
    /// rather than obeyed.
    /// </para>
    /// <para>
    /// It matters more than a row of a data table suggests. The page-layout
    /// tables every CV and letterhead template is built from use a tall first row
    /// to place the block beneath it; without the height the row collapses to its
    /// content and everything below rides up.
    /// </para>
    /// </remarks>
    public double MinHeight { get; }

    internal TableRow Shifted(int at, int removed, int inserted)
    {
        var cells = new List<TableCell>(Cells.Count);
        foreach (TableCell cell in Cells)
            cells.Add(cell.Shifted(at, removed, inserted));
        return new TableRow(cells, IsHeader, MinHeight);
    }
}

/// <summary>
/// One cell: which paragraphs it holds, where it sits in the grid, and how it is
/// bounded and painted.
/// </summary>
public sealed class TableCell
{
    public TableCell(
        int paragraphIndex,
        int paragraphCount,
        int columnIndex,
        int columnSpan = 1,
        int rowSpan = 1,
        BColor shading = default,
        CellBorders borders = default,
        bool isRowSpanContinuation = false,
        IReadOnlyList<DocumentTable>? tables = null)
    {
        ParagraphIndex = Math.Max(0, paragraphIndex);
        ParagraphCount = Math.Max(0, paragraphCount);
        ColumnIndex = Math.Max(0, columnIndex);
        ColumnSpan = Math.Max(1, columnSpan);
        RowSpan = Math.Max(1, rowSpan);
        Shading = shading;
        Borders = borders;
        IsRowSpanContinuation = isRowSpanContinuation;
        Tables = tables is null || tables.Count == 0 ? [] : [.. tables];
    }

    /// <summary>The document paragraph this cell's text starts at.</summary>
    public int ParagraphIndex { get; }

    /// <summary>How many paragraphs the cell holds; zero for a cell with no content of its own.</summary>
    public int ParagraphCount { get; }

    /// <summary>One past the last paragraph the cell holds.</summary>
    public int ParagraphEnd => ParagraphIndex + ParagraphCount;

    /// <summary>The grid column the cell starts in.</summary>
    public int ColumnIndex { get; }

    /// <summary>How many grid columns the cell covers.</summary>
    public int ColumnSpan { get; }

    /// <summary>How many rows the cell covers, counting its own.</summary>
    public int RowSpan { get; }

    /// <summary>The cell's background, or <see cref="BColor.Empty"/> for none.</summary>
    public BColor Shading { get; }

    public CellBorders Borders { get; }

    /// <summary>
    /// True for the lower half of a vertical merge: a cell the document writes so
    /// the row has the right number of columns, whose box the cell above covers.
    /// It is drawn by no one and holds, by the format's own rule, no text.
    /// </summary>
    public bool IsRowSpanContinuation { get; }

    /// <summary>The tables nested directly in this cell, in the order they start.</summary>
    public IReadOnlyList<DocumentTable> Tables { get; }

    internal TableCell Shifted(int at, int removed, int inserted)
    {
        (int index, int count) = DocumentTable.ShiftRange(ParagraphIndex, ParagraphCount, at, removed, inserted);

        List<DocumentTable>? tables = null;
        if (Tables.Count > 0)
        {
            tables = new List<DocumentTable>(Tables.Count);
            foreach (DocumentTable table in Tables)
            {
                if (table.Shifted(at, removed, inserted) is DocumentTable shifted)
                    tables.Add(shifted);
            }
        }

        return new TableCell(
            index,
            count,
            ColumnIndex,
            ColumnSpan,
            RowSpan,
            Shading,
            Borders,
            IsRowSpanContinuation,
            tables);
    }
}

/// <summary>One edge of a cell's border: its colour and how thick it is drawn.</summary>
public readonly record struct TableBorder(BColor Color, double Width)
{
    /// <summary>No border at all.</summary>
    public static TableBorder None => default;

    /// <summary>A hairline border in one colour, which is what most tables state.</summary>
    public static TableBorder Solid(BColor color) => new(color, 0.5);

    /// <summary>True when the edge would actually draw something.</summary>
    public bool IsVisible => Width > 0 && !Color.IsEmpty && Color.A > 0;
}

/// <summary>The four edges of a cell.</summary>
public readonly record struct CellBorders(
    TableBorder Left,
    TableBorder Top,
    TableBorder Right,
    TableBorder Bottom)
{
    /// <summary>The same edge on all four sides.</summary>
    public static CellBorders All(TableBorder border) => new(border, border, border, border);

    /// <summary>True when any edge would draw something.</summary>
    public bool IsVisible => Left.IsVisible || Top.IsVisible || Right.IsVisible || Bottom.IsVisible;
}
