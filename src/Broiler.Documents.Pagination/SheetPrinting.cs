using System;
using System.Collections.Generic;
using System.Linq;
using Broiler.Documents.Model;

namespace Broiler.Documents.Pagination;

/// <summary>
/// Lays a spreadsheet's sheets out the way Calc prints them
/// (<see cref="DocumentStyleDefaults.PrintsSheets"/>): a sheet wider than the
/// page is cut into runs of whole columns, each starting a page of its own;
/// text runs on over the empty cells beside it; and a cell's text keeps
/// Calc's one-point margin from its edges.
/// </summary>
/// <remarks>
/// <para>
/// The model holds each sheet as one table, so its cells and values read and
/// convert the same however wide the paper is. The cut is made here, against
/// the width of the page being laid out: <c>ScPrintFunc::CalcPages</c> fills a
/// page with the columns that fit across its text width and starts the next
/// page at the first that does not, and in the default top-to-bottom order
/// (<c>style:print-page-order="ttb"</c>) prints every row of those columns
/// before the next ones. A run of columns that holds nothing printable is left
/// out, as Calc leaves out empty pages. Each run becomes a table of its own
/// over the same paragraphs, reordered within the sheet's range so that each
/// run's cells are contiguous; nothing outside the range moves.
/// </para>
/// <para>
/// Text that does not fit its cell is not wrapped in Calc: it runs on over the
/// empty cells to the right of a left-aligned cell and to the left of a
/// right-aligned one, as far as the page's edge (<c>ScOutputData</c>'s
/// <c>GetOutputArea</c>). A cell is widened over the empty cells beside it to
/// give its text that room. A cell with a fill or a border on that side keeps
/// its own box, because Calc paints those on the cell alone.
/// </para>
/// </remarks>
internal static class SheetPrinting
{
    /// <summary>Calc's default cell margin: <c>SvxMarginItem</c>'s 20 twips on every side.</summary>
    internal const double CellMargin = 1.0;

    /// <summary>The width a column takes that states none.</summary>
    private const double DefaultColumnWidth = 72.0;

    /// <summary>
    /// <paramref name="document"/> with its sheets cut into runs of columns
    /// <paramref name="pageWidth"/> wide. <paramref name="origins"/> gives, for
    /// each paragraph of the result, its index in <paramref name="document"/>,
    /// or is null where nothing moved.
    /// </summary>
    public static RichTextDocument Lay(RichTextDocument document, double pageWidth, out int[]? origins)
    {
        origins = null;
        if (!document.StyleDefaults.PrintsSheets || document.Tables.Count == 0 ||
            !double.IsFinite(pageWidth) || pageWidth <= 0)
            return document;

        RichTextParagraph[] paragraphs = [.. document.Paragraphs];
        int[] from = [.. Enumerable.Range(0, paragraphs.Length)];
        var tables = new List<DocumentTable>(document.Tables.Count);
        bool changed = false;
        foreach (DocumentTable table in document.Tables)
        {
            if (Lay(table, document.Paragraphs, paragraphs, from, pageWidth) is { } runs)
            {
                tables.AddRange(runs);
                changed = true;
            }
            else
            {
                tables.Add(table);
            }
        }

        if (!changed)
            return document;

        origins = from;
        return RichTextDocument.FromParagraphs(paragraphs)
            .WithRunningContent(document.RunningContent)
            .WithShapes(document.Shapes)
            .WithPageGeometry(document.PageGeometry)
            .WithTables(tables)
            .WithSections(document.Sections)
            .WithPageBackground(document.PageBackground)
            .WithNoteNumbering(document.NoteNumbering)
            .WithStyleDefaults(document.StyleDefaults)
            .WithShowsRevisions(document.ShowsRevisions);
    }

    /// <summary>
    /// The tables <paramref name="table"/> prints as, writing the reordered
    /// paragraphs of its range into <paramref name="target"/>; null for a table
    /// this does not apply to.
    /// </summary>
    private static List<DocumentTable>? Lay(DocumentTable table, IReadOnlyList<RichTextParagraph> source,
        RichTextParagraph[] target, int[] origins, double pageWidth)
    {
        if (table.IsFloating || table.Rows.Count == 0 || table.ParagraphEnd > source.Count ||
            table.Rows.Any(row => row.Cells.Any(cell => cell.Tables.Count > 0 || cell.IsRowSpanContinuation ||
                cell.ParagraphIndex < table.ParagraphIndex || cell.ParagraphEnd > table.ParagraphEnd)))
            return null;

        int columnCount = Math.Max(table.ColumnWidths.Count,
            table.Rows.SelectMany(row => row.Cells).Select(cell => cell.ColumnIndex + cell.ColumnSpan).DefaultIfEmpty(0).Max());
        if (columnCount == 0)
            return null;

        double margin = 0;
        double Width(int column) => column == columnCount
            ? margin
            : column < table.ColumnWidths.Count && table.ColumnWidths[column] > 0
                ? table.ColumnWidths[column]
                : DefaultColumnWidth;

        var runs = new List<(int Start, int End)>();
        int start = 0;
        double filled = 0;
        for (int column = 0; column < columnCount; column++)
        {
            double width = Width(column);
            if (column > start && filled + width > pageWidth + 0.01)
            {
                runs.Add((start, column));
                start = column;
                filled = 0;
            }

            filled += width;
        }

        // Text in the last columns runs on past the sheet's used range, which
        // Calc widens for it (ScTable::MaybeAddExtraColumn): the last run
        // keeps the rest of the page as one more, empty column.
        margin = pageWidth - filled;
        runs.Add((start, margin > 0.01 ? columnCount + 1 : columnCount));

        // Which of the sheet's paragraphs a printed cell takes; the rest (empty
        // cells run over, cells of runs left out) follow at the range's end.
        var placed = new bool[table.ParagraphCount];
        int cursor = table.ParagraphIndex;
        var result = new List<DocumentTable>(runs.Count);
        foreach ((int first, int end) in runs)
        {
            List<List<TableCell>> rows = RowsOf(table, source, first, end);
            if (runs.Count > 1 && !rows.Any(row => row.Any(cell => !IsEmpty(cell, source))))
                continue;

            int runStart = cursor;
            var tableRows = new List<TableRow>(rows.Count);
            for (int r = 0; r < rows.Count; r++)
            {
                TableRow row = table.Rows[r];
                var cells = new List<TableCell>(rows[r].Count);
                foreach (TableCell cell in rows[r])
                {
                    int moved = cursor;
                    for (int p = 0; p < cell.ParagraphCount; p++)
                    {
                        origins[cursor] = cell.ParagraphIndex + p;
                        target[cursor++] = source[cell.ParagraphIndex + p];
                        placed[cell.ParagraphIndex + p - table.ParagraphIndex] = true;
                    }

                    cells.Add(new TableCell(moved, cell.ParagraphCount, cell.ColumnIndex, cell.ColumnSpan, cell.RowSpan,
                        cell.Shading, cell.Borders, cell.IsRowSpanContinuation, null,
                        cell.Padding ?? new TableCellPadding(CellMargin, CellMargin, CellMargin, CellMargin),
                        cell.VerticalAlignment, cell.Formula, cell.NumberFormat, cell.Value));
                }

                tableRows.Add(new TableRow(cells, row.IsHeader, row.MinHeight, row.CanSplit, row.ExactHeight, row.KeepWithNext));
            }

            result.Add(new DocumentTable(runStart, cursor - runStart, tableRows,
                [.. Enumerable.Range(first, end - first).Select(Width)], CellMargin,
                keepWithNext: table.KeepWithNext, alignment: table.Alignment, leftIndent: table.LeftIndent,
                pageBreakBefore: result.Count > 0 || table.PageBreakBefore, name: table.Name,
                spacingBefore: table.SpacingBefore, spacingAfter: table.SpacingAfter));
        }

        if (result.Count == 0)
            return null;

        for (int i = 0; i < placed.Length; i++)
        {
            if (!placed[i])
            {
                origins[cursor] = table.ParagraphIndex + i;
                target[cursor++] = source[table.ParagraphIndex + i];
            }
        }

        // The last run's range reaches the sheet's end, so the paragraphs no
        // printed cell took stay inside a table and are not laid out as text.
        DocumentTable last = result[^1];
        result[^1] = new DocumentTable(last.ParagraphIndex, table.ParagraphEnd - last.ParagraphIndex, last.Rows,
            last.ColumnWidths, last.CellPadding, keepWithNext: last.KeepWithNext, alignment: last.Alignment,
            leftIndent: last.LeftIndent, pageBreakBefore: last.PageBreakBefore, name: last.Name,
            spacingBefore: last.SpacingBefore, spacingAfter: last.SpacingAfter);
        return result;
    }

    /// <summary>
    /// Each row's cells within columns [<paramref name="first"/>,
    /// <paramref name="end"/>), renumbered from the run's first column, with
    /// the empty cells left out and the cells whose text runs on over them
    /// widened.
    /// </summary>
    private static List<List<TableCell>> RowsOf(DocumentTable table, IReadOnlyList<RichTextParagraph> source, int first, int end)
    {
        int width = end - first;
        var rows = new List<List<TableCell>>(table.Rows.Count);
        // The row each column is covered until by a cell merged down into it.
        var coveredUntil = new int[width];
        for (int r = 0; r < table.Rows.Count; r++)
        {
            var slots = new TableCell?[width];
            var taken = new bool[width];
            for (int c = 0; c < width; c++)
                taken[c] = coveredUntil[c] > r;

            foreach (TableCell original in table.Rows[r].Cells)
            {
                int from = Math.Max(first, original.ColumnIndex) - first;
                int to = Math.Min(end, original.ColumnIndex + original.ColumnSpan) - first;
                if (to <= from)
                    continue;

                if (original.ColumnIndex >= first && IsEmpty(original, source))
                    continue;

                for (int c = from; c < to; c++)
                {
                    taken[c] = true;
                    coveredUntil[c] = Math.Max(coveredUntil[c], r + original.RowSpan);
                }

                // A merged cell that starts on an earlier page is printed there.
                if (original.ColumnIndex >= first)
                    slots[from] = Moved(original, from, to - from);
            }

            RunOn(slots, taken, source);
            rows.Add([.. slots.OfType<TableCell>()]);
        }

        return rows;
    }

    /// <summary>
    /// Widens each cell whose text may run on over the free columns beside it:
    /// rightwards from a left-aligned cell, then leftwards from a right-aligned
    /// one, never over another cell or past the run's edge.
    /// </summary>
    private static void RunOn(TableCell?[] slots, bool[] taken, IReadOnlyList<RichTextParagraph> source)
    {
        for (int c = 0; c < slots.Length; c++)
        {
            if (slots[c] is not TableCell cell || !RunsOn(cell, source) || cell.Borders.Right.IsVisible ||
                Alignment(cell, source) is not (TextAlignment.Left or TextAlignment.Justify))
                continue;

            int end = c + cell.ColumnSpan;
            while (end < slots.Length && !taken[end])
                taken[end++] = true;
            if (end > c + cell.ColumnSpan)
                slots[c] = Moved(cell, c, end - c);
            c = end - 1;
        }

        for (int c = slots.Length - 1; c >= 0; c--)
        {
            if (slots[c] is not TableCell cell || !RunsOn(cell, source) || cell.Borders.Left.IsVisible ||
                Alignment(cell, source) != TextAlignment.Right)
                continue;

            int from = c;
            while (from > 0 && !taken[from - 1])
                taken[--from] = true;
            if (from < c)
            {
                slots[c] = null;
                slots[from] = Moved(cell, from, cell.ColumnSpan + c - from);
            }
        }
    }

    /// <summary>
    /// Whether a cell's text may run on over its neighbours: it has text, sits
    /// in one row, and paints no fill that would follow it.
    /// </summary>
    private static bool RunsOn(TableCell cell, IReadOnlyList<RichTextParagraph> source) =>
        cell.RowSpan == 1 && (cell.Shading.IsEmpty || cell.Shading.A == 0) && HasText(cell, source);

    private static TextAlignment Alignment(TableCell cell, IReadOnlyList<RichTextParagraph> source) =>
        source[cell.ParagraphIndex].Style.Alignment;

    private static bool HasText(TableCell cell, IReadOnlyList<RichTextParagraph> source)
    {
        for (int p = cell.ParagraphIndex; p < cell.ParagraphEnd; p++)
        {
            if (source[p].Length > 0)
                return true;
        }

        return false;
    }

    /// <summary>A cell that prints nothing: no text, no fill, no border, one column and row.</summary>
    private static bool IsEmpty(TableCell cell, IReadOnlyList<RichTextParagraph> source) =>
        cell.ColumnSpan == 1 && cell.RowSpan == 1 && (cell.Shading.IsEmpty || cell.Shading.A == 0) &&
        !cell.Borders.IsVisible && !HasText(cell, source);

    private static TableCell Moved(TableCell cell, int column, int span) =>
        column == cell.ColumnIndex && span == cell.ColumnSpan
            ? cell
            : new TableCell(cell.ParagraphIndex, cell.ParagraphCount, column, span, cell.RowSpan, cell.Shading,
                cell.Borders, cell.IsRowSpanContinuation, cell.Tables, cell.Padding, cell.VerticalAlignment, cell.Formula,
                cell.NumberFormat, cell.Value);
}
