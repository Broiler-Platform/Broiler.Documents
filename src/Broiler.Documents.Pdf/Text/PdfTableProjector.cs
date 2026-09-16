using System;
using System.Collections.Generic;
using Broiler.Documents.Model;

namespace Broiler.Documents.Pdf.Text;

/// <summary>
/// Projects a page that drew a ruled table, turning the grid and the text inside
/// it into cells over the document's ordinary paragraphs.
/// </summary>
/// <remarks>
/// <para>
/// The model holds a table as a range of flat body paragraphs plus a grid that
/// says how to arrange them, read row-major. That is exactly what a page can be
/// re-ordered into, which is why this is a projection and not a second document
/// shape: the cells' paragraphs are the same paragraphs everything else in the
/// pipeline already handles.
/// </para>
/// <para>
/// <strong>Fragments, not lines.</strong> The split has to happen before lines
/// are assembled, because assembling them is itself the thing a table breaks: two
/// cells side by side in one row share a baseline, and the line builder joins
/// runs on a shared baseline into a single line. Handed lines, this would find
/// "Total 41.20" sitting in the first cell and the second cell empty. Handed
/// fragments, each cell assembles its own lines and each one spans only its own
/// column.
/// </para>
/// <para>
/// Row-major is also the fix for the reading order, not just the borders. A
/// table defeats the geometric pass — its column histogram either splits the page
/// down a gutter that is really a cell boundary, or runs two cells of one row
/// together. Placing each cell's text in its own block, in the order the grid
/// states, replaces that guess with the arrangement the document drew.
/// </para>
/// <para>
/// A cell that drew no text still becomes one empty paragraph. A grid with a hole
/// in it is not the grid the page drew, and a reader moving through cells must
/// find the same number of them the rules described.
/// </para>
/// </remarks>
internal static class PdfTableProjector
{
    /// <summary>
    /// How far inside its left edge a run is probed for. A rule is painted with
    /// width, and a glyph set flush against one can start a fraction to its left.
    /// </summary>
    private const double Inset = 1.0;

    /// <summary>
    /// Projects one page's fragments, with the grids found on it, and appends any
    /// tables to <paramref name="tables"/> with document-relative ranges.
    /// </summary>
    /// <param name="paragraphBase">
    /// How many paragraphs the document already holds. Cell ranges are indices
    /// into the finished document, so the page has to know where it starts.
    /// </param>
    public static List<RichTextParagraph> Project(
        IReadOnlyList<PdfTextFragment> fragments,
        IReadOnlyList<PdfLinkRegion> links,
        IReadOnlyList<PdfPlacedImage> images,
        IReadOnlyList<PdfTableGrid> grids,
        int maxParagraphs,
        int paragraphBase,
        List<DocumentTable> tables)
    {
        // Grids arrive top to bottom. Each fragment goes to the first grid that
        // holds it, and everything else waits for the point in the walk down the
        // page where it belongs.
        var owner = new int[fragments.Count];
        var cells = new List<PdfTextFragment>?[grids.Count][];
        for (int g = 0; g < grids.Count; g++)
            cells[g] = new List<PdfTextFragment>?[grids[g].Rows * grids[g].Columns];

        for (int f = 0; f < fragments.Count; f++)
        {
            PdfTextFragment fragment = fragments[f];
            owner[f] = -1;

            for (int g = 0; g < grids.Count; g++)
            {
                // The left end of the baseline is where the run was placed, so it
                // is what decides which cell holds it. A run wider than its cell
                // has overflowed the rules, and belongs to the cell it started in.
                if (grids[g].CellAt(fragment.X + Inset, fragment.Y) is not { } square)
                    continue;

                // A merge makes several lattice squares one cell, and the text in
                // any of them belongs to the cell that covers them all.
                (int Row, int Column) at = grids[g].AnchorAt(square.Row, square.Column);
                owner[f] = g;
                int index = (at.Row * grids[g].Columns) + at.Column;
                (cells[g][index] ??= []).Add(fragment);
                break;
            }
        }

        var paragraphs = new List<RichTextParagraph>();
        var pendingImages = new List<PdfPlacedImage>(images);

        for (int g = 0; g < grids.Count; g++)
        {
            PdfTableGrid grid = grids[g];

            // Everything drawn above this grid and not inside an earlier one.
            var before = new List<PdfTextFragment>();
            for (int f = 0; f < fragments.Count; f++)
            {
                if (owner[f] == -1 && fragments[f].Y > grid.Top)
                {
                    before.Add(fragments[f]);
                    owner[f] = -2;
                }
            }

            var imagesBefore = new List<PdfPlacedImage>();
            for (int i = pendingImages.Count - 1; i >= 0; i--)
            {
                if (pendingImages[i].Top > grid.Top)
                {
                    imagesBefore.Add(pendingImages[i]);
                    pendingImages.RemoveAt(i);
                }
            }

            paragraphs.AddRange(PdfModelProjector.Project(
                PdfReadingOrder.BuildLines(before, links),
                imagesBefore,
                false,
                Remaining(maxParagraphs, paragraphs.Count)));

            AddTable(grid, cells[g], links, paragraphs, maxParagraphs, paragraphBase, tables);
        }

        var after = new List<PdfTextFragment>();
        for (int f = 0; f < fragments.Count; f++)
        {
            if (owner[f] == -1)
                after.Add(fragments[f]);
        }

        paragraphs.AddRange(PdfModelProjector.Project(
            PdfReadingOrder.BuildLines(after, links),
            pendingImages,
            false,
            Remaining(maxParagraphs, paragraphs.Count)));

        return paragraphs;
    }

    /// <summary>
    /// Appends one grid's cells as paragraphs, row-major, and records the table
    /// over the range they occupy.
    /// </summary>
    private static void AddTable(
        PdfTableGrid grid,
        List<PdfTextFragment>?[] cells,
        IReadOnlyList<PdfLinkRegion> links,
        List<RichTextParagraph> paragraphs,
        int maxParagraphs,
        int paragraphBase,
        List<DocumentTable> tables)
    {
        int tableStart = paragraphs.Count;
        var rows = new List<TableRow>(grid.Rows);

        for (int row = 0; row < grid.Rows; row++)
        {
            var rowCells = new List<TableCell>(grid.Columns);
            int column = 0;

            while (column < grid.Columns)
            {
                (int Row, int Column) anchor = grid.AnchorAt(row, column);
                int columnSpan = grid.ColumnSpanAt(anchor.Row, anchor.Column);

                if (anchor.Row != row)
                {
                    // The lower half of a vertical merge. The row carries a cell
                    // so its column count is right; the cell above holds the text
                    // and draws the box.
                    rowCells.Add(new TableCell(
                        paragraphBase + paragraphs.Count,
                        0,
                        column,
                        columnSpan,
                        isRowSpanContinuation: true));
                    column += columnSpan;
                    continue;
                }

                int start = paragraphBase + paragraphs.Count;
                List<PdfTextFragment>? cellFragments = cells[(row * grid.Columns) + column];

                List<RichTextParagraph> cell = cellFragments is null
                    ? []
                    : PdfModelProjector.Project(
                        PdfReadingOrder.BuildLines(cellFragments, links),
                        [],
                        false,
                        Remaining(maxParagraphs, paragraphs.Count));

                // Empty is a cell the page drew and left blank, which is content.
                if (cell.Count == 0)
                    cell.Add(RichTextParagraph.Empty);

                paragraphs.AddRange(cell);

                rowCells.Add(new TableCell(
                    start,
                    cell.Count,
                    column,
                    columnSpan,
                    grid.RowSpanAt(row, column),
                    grid.ShadingAt(row, column),
                    grid.BordersAt(row, column)));

                column += columnSpan;
            }

            rows.Add(new TableRow(rowCells, minHeight: grid.RowEdges[row] - grid.RowEdges[row + 1]));
        }

        tables.Add(new DocumentTable(
            paragraphBase + tableStart,
            paragraphs.Count - tableStart,
            rows,
            grid.ColumnWidths()));
    }

    /// <summary>
    /// What is left of the page's paragraph allowance. A cell never gets a
    /// negative budget, and a document that runs out stops gaining paragraphs
    /// rather than failing: the limit is a ceiling, not an error.
    /// </summary>
    private static int Remaining(int maxParagraphs, int used) => Math.Max(0, maxParagraphs - used);
}
