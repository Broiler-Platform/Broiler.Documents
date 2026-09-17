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
/// A table drawn inside a cell is emitted inside that cell's paragraphs and held
/// by it, which is the convention the other codecs already write: the cell's
/// range covers its own text and its nested tables together, and the nested
/// table's own range sits inside it.
/// </para>
/// <para>
/// A cell that drew no text and holds no table still becomes one empty
/// paragraph. A grid with a hole in it is not the grid the page drew, and a
/// reader moving through cells must find the same number of them the rules
/// described.
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
    /// top-level tables to <paramref name="tables"/> with document-relative
    /// ranges. Nested tables are held by the cells that drew them.
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
        // Every fragment goes to the innermost grid that holds it, so a run
        // inside a nested table belongs to that table's cell rather than to the
        // outer cell the whole of it sits in.
        var buckets = new Dictionary<PdfTableGrid, List<PdfTextFragment>?[]>();
        var outside = new List<PdfTextFragment>();

        foreach (PdfTextFragment fragment in fragments)
        {
            PdfTableGrid? owner = null;
            foreach (PdfTableGrid root in grids)
            {
                if (Deepest(root, fragment) is { } found)
                {
                    owner = found;
                    break;
                }
            }

            if (owner is null)
            {
                outside.Add(fragment);
                continue;
            }

            (int Row, int Column) = owner.CellAt(fragment.X + Inset, fragment.Y)!.Value;
            (int Row, int Column) at = owner.AnchorAt(Row, Column);

            if (!buckets.TryGetValue(owner, out List<PdfTextFragment>?[]? cells))
                buckets[owner] = cells = new List<PdfTextFragment>?[owner.Rows * owner.Columns];

            (cells[(at.Row * owner.Columns) + at.Column] ??= []).Add(fragment);
        }

        var paragraphs = new List<RichTextParagraph>();
        var pendingImages = new List<PdfPlacedImage>(images);
        var placed = new List<PdfTextFragment>();

        foreach (PdfTableGrid grid in grids)
        {
            // Everything drawn above this grid and not already emitted above an
            // earlier one.
            var before = new List<PdfTextFragment>();
            foreach (PdfTextFragment fragment in outside)
            {
                if (fragment.Y > grid.Top && !placed.Contains(fragment))
                {
                    before.Add(fragment);
                    placed.Add(fragment);
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

            tables.Add(AddTable(grid, buckets, links, paragraphs, maxParagraphs, paragraphBase));
        }

        var after = new List<PdfTextFragment>();
        foreach (PdfTextFragment fragment in outside)
        {
            if (!placed.Contains(fragment))
                after.Add(fragment);
        }

        paragraphs.AddRange(PdfModelProjector.Project(
            PdfReadingOrder.BuildLines(after, links),
            pendingImages,
            false,
            Remaining(maxParagraphs, paragraphs.Count)));

        return paragraphs;
    }

    /// <summary>
    /// The innermost grid holding this run, or null where none does. A nested
    /// table sits inside its parent's cell by construction, so the walk goes down
    /// as far as it can.
    /// </summary>
    private static PdfTableGrid? Deepest(PdfTableGrid grid, PdfTextFragment fragment)
    {
        if (grid.CellAt(fragment.X + Inset, fragment.Y) is null)
            return null;

        foreach (PdfTableGrid child in grid.Children)
        {
            if (Deepest(child, fragment) is { } deeper)
                return deeper;
        }

        return grid;
    }

    /// <summary>
    /// Appends one grid's cells as paragraphs, row-major, and returns the table
    /// over the range they occupy. A cell holding a nested grid emits that grid's
    /// paragraphs inside its own range, and holds the table.
    /// </summary>
    private static DocumentTable AddTable(
        PdfTableGrid grid,
        Dictionary<PdfTableGrid, List<PdfTextFragment>?[]> buckets,
        IReadOnlyList<PdfLinkRegion> links,
        List<RichTextParagraph> paragraphs,
        int maxParagraphs,
        int paragraphBase)
    {
        buckets.TryGetValue(grid, out List<PdfTextFragment>?[]? cells);

        int tableStart = paragraphs.Count;
        var rows = new List<TableRow>(grid.Rows);

        for (int row = 0; row < grid.Rows; row++)
        {
            var rowCells = new List<TableCell>(grid.Columns);
            int column = 0;

            while (column < grid.Columns)
            {
                (int Row, int Column) = grid.AnchorAt(row, column);
                int columnSpan = grid.ColumnSpanAt(Row, Column);

                if (Row != row)
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
                List<PdfTextFragment>? cellFragments = cells?[(row * grid.Columns) + column];

                if (cellFragments is not null)
                {
                    paragraphs.AddRange(PdfModelProjector.Project(
                        PdfReadingOrder.BuildLines(cellFragments, links),
                        [],
                        false,
                        Remaining(maxParagraphs, paragraphs.Count)));
                }

                // A table drawn in this cell belongs to it, and its paragraphs
                // belong to the cell's range: the cell's own text first, then what
                // it contains, which is the order a reader meets them in.
                List<DocumentTable>? nested = null;
                foreach (PdfTableGrid child in grid.Children)
                {
                    if (!Holds(grid, row, column, child))
                        continue;

                    (nested ??= []).Add(
                        AddTable(child, buckets, links, paragraphs, maxParagraphs, paragraphBase));
                }

                // Empty is a cell the page drew and left blank, which is content.
                if (paragraphBase + paragraphs.Count == start)
                    paragraphs.Add(RichTextParagraph.Empty);

                rowCells.Add(new TableCell(
                    start,
                    paragraphBase + paragraphs.Count - start,
                    column,
                    columnSpan,
                    grid.RowSpanAt(row, column),
                    grid.ShadingAt(row, column),
                    grid.BordersAt(row, column),
                    tables: nested));

                column += columnSpan;
            }

            rows.Add(new TableRow(rowCells, minHeight: grid.RowEdges[row] - grid.RowEdges[row + 1]));
        }

        return new DocumentTable(
            paragraphBase + tableStart,
            paragraphs.Count - tableStart,
            rows,
            grid.ColumnWidths());
    }

    /// <summary>Whether this cell of the grid is where the child grid was drawn.</summary>
    private static bool Holds(PdfTableGrid grid, int row, int column, PdfTableGrid child)
    {
        double centreX = (child.Left + child.Right) / 2;
        double centreY = (child.Top + child.Bottom) / 2;

        if (grid.CellAt(centreX, centreY) is not { } square)
            return false;

        (int Row, int Column) = grid.AnchorAt(square.Row, square.Column);
        return Row == row && Column == column;
    }

    /// <summary>
    /// What is left of the page's paragraph allowance. A cell never gets a
    /// negative budget, and a document that runs out stops gaining paragraphs
    /// rather than failing: the limit is a ceiling, not an error.
    /// </summary>
    private static int Remaining(int maxParagraphs, int used) => Math.Max(0, maxParagraphs - used);
}
