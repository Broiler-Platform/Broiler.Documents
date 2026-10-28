using System;
using System.Linq;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;
using Xunit;

namespace Broiler.Documents.Model.Tests;

public sealed class TableGridTests
{
    private static readonly BColor Black = new(0, 0, 0);
    private static readonly BColor Red = new(255, 0, 0);
    private static readonly BColor Blue = new(0, 0, 255);

    private static CellBorders Borders(TableBorder left = default, TableBorder top = default, TableBorder right = default, TableBorder bottom = default, TableBorder diagDown = default, TableBorder diagUp = default) =>
        new(left, top, right, bottom, diagDown, diagUp);

    private static TableCell Cell(int col, int colSpan = 1, int rowSpan = 1, CellBorders borders = default, BColor shading = default, bool isCont = false) =>
        new(paragraphIndex: col, paragraphCount: 1, columnIndex: col, columnSpan: colSpan, rowSpan: rowSpan,
            shading: shading, borders: borders, isRowSpanContinuation: isCont);

    [Fact]
    public void Empty_Grid_Has_Expected_Properties()
    {
        var grid = TableGrid.Empty(0, 0);
        Assert.Equal(0, grid.RowCount);
        Assert.Equal(0, grid.ColumnCount);
        Assert.Equal(0, grid.CellCount);
        Assert.Empty(grid.Cells);
        Assert.Empty(grid.NonEmptySlots);
        Assert.Null(grid.GetCellAt(0, 0));
        Assert.True(grid.IsEmpty(0, 0));

        var (root, r, c) = grid.GetMergedRoot(0, 0);
        Assert.Null(root);
        Assert.Equal(-1, r);
        Assert.Equal(-1, c);
    }

    [Fact]
    public void Simple_Table_Coordinate_Lookups_And_Properties()
    {
        var c00 = Cell(0);
        var c01 = Cell(1);
        var c10 = Cell(0);
        var c11 = Cell(1);

        var row0 = new TableRow([c00, c01]);
        var row1 = new TableRow([c10, c11]);
        var table = new DocumentTable(0, 4, [row0, row1], [50, 50]);

        var grid = table.Grid;
        Assert.Equal(2, grid.RowCount);
        Assert.Equal(2, grid.ColumnCount);
        Assert.Equal(4, grid.CellCount);
        Assert.Equal(100, grid.TotalWidth);

        Assert.Same(c00, grid.GetCellAt(0, 0));
        Assert.Same(c01, grid.GetCellAt(0, 1));
        Assert.Same(c10, grid.GetCellAt(1, 0));
        Assert.Same(c11, grid.GetCellAt(1, 1));

        // Out of bounds
        Assert.Null(grid.GetCellAt(-1, 0));
        Assert.Null(grid.GetCellAt(2, 0));
        Assert.Null(grid.GetCellAt(0, 2));
        Assert.Null(grid.GetCellAt(5, 5));

        // Indexer
        Assert.Same(c00, grid[0, 0]);
        Assert.Same(c11, grid[1, 1]);

        // Ranges
        var range00 = grid.GetCellRange(c00);
        Assert.NotNull(range00);
        Assert.Equal(new TableCellRange(0, 0, 1, 1), range00.Value);
    }

    [Fact]
    public void Sparse_Table_With_Gaps_And_Jagged_Rows()
    {
        // Row 0 has cell at col 0 and col 2 (col 1 is empty)
        var c00 = Cell(0);
        var c02 = Cell(2);
        // Row 1 has only cell at col 1
        var c11 = Cell(1);

        var table = new DocumentTable(0, 3, [new TableRow([c00, c02]), new TableRow([c11])], [30, 30, 30]);
        var grid = table.ToGrid();

        Assert.Equal(2, grid.RowCount);
        Assert.Equal(3, grid.ColumnCount);

        Assert.Same(c00, grid.GetCellAt(0, 0));
        Assert.Null(grid.GetCellAt(0, 1));
        Assert.True(grid.IsEmpty(0, 1));
        Assert.Same(c02, grid.GetCellAt(0, 2));

        Assert.Null(grid.GetCellAt(1, 0));
        Assert.True(grid.IsEmpty(1, 0));
        Assert.Same(c11, grid.GetCellAt(1, 1));
        Assert.Null(grid.GetCellAt(1, 2));
    }

    [Fact]
    public void Horizontal_Column_Span_Merge()
    {
        // Cell at (0, 0) spans 3 columns
        var c00 = Cell(0, colSpan: 3);
        var table = new DocumentTable(0, 1, [new TableRow([c00])], [20, 20, 20]);
        var grid = table.Grid;

        Assert.Equal(1, grid.RowCount);
        Assert.Equal(3, grid.ColumnCount);
        Assert.Equal(1, grid.CellCount);

        for (int col = 0; col < 3; col++)
        {
            Assert.Same(c00, grid.GetCellAt(0, col));
            var (root, rootR, rootC) = grid.GetMergedRoot(0, col);
            Assert.Same(c00, root);
            Assert.Equal(0, rootR);
            Assert.Equal(0, rootC);

            Assert.True(grid.IsMerged(0, col));
            if (col == 0)
                Assert.False(grid.IsCoveredContinuation(0, col));
            else
                Assert.True(grid.IsCoveredContinuation(0, col));
        }
    }

    [Fact]
    public void Vertical_Row_Span_Merge_And_Explicit_Continuations()
    {
        // Row 0: Cell at col 0 spans 3 rows, Cell at col 1 spans 1 row
        var rootCell = Cell(0, rowSpan: 3);
        var c01 = Cell(1);
        // Row 1: Explicit continuation cell at col 0, Cell at col 1
        var cont1 = Cell(0, isCont: true);
        var c11 = Cell(1);
        // Row 2: Virtual continuation (no cell in row 2 at col 0), Cell at col 1
        var c21 = Cell(1);

        var table = new DocumentTable(0, 6, [
            new TableRow([rootCell, c01]),
            new TableRow([cont1, c11]),
            new TableRow([c21])
        ], [50, 50]);

        var grid = table.Grid;
        Assert.Equal(3, grid.RowCount);
        Assert.Equal(2, grid.ColumnCount);

        // All 3 rows at column 0 should resolve to rootCell
        for (int r = 0; r < 3; r++)
        {
            Assert.Same(rootCell, grid.GetCellAt(r, 0));
            var (root, rootR, rootC) = grid.GetMergedRoot(r, 0);
            Assert.Same(rootCell, root);
            Assert.Equal(0, rootR);
            Assert.Equal(0, rootC);
            Assert.True(grid.IsMerged(r, 0));
        }

        // Slot at row 1 has cont1 as slot.Cell and rootCell as slot.RootCell
        var slot1 = grid.GetSlot(1, 0);
        Assert.Same(cont1, slot1.Cell);
        Assert.Same(rootCell, slot1.RootCell);
        Assert.True(slot1.IsContinuation);

        // Slot at row 2 has rootCell as both slot.Cell and slot.RootCell
        var slot2 = grid.GetSlot(2, 0);
        Assert.Same(rootCell, slot2.RootCell);
        Assert.True(slot2.IsContinuation);
    }

    [Fact]
    public void Block_Merge_2x3_Span()
    {
        var blockCell = Cell(1, colSpan: 3, rowSpan: 2);
        var table = new DocumentTable(0, 1, [
            new TableRow([Cell(0), blockCell]),
            new TableRow([Cell(0)])
        ], [30, 30, 30, 30]);

        var grid = table.Grid;
        Assert.Equal(2, grid.RowCount);
        Assert.Equal(4, grid.ColumnCount);

        var range = grid.GetCellRange(blockCell);
        Assert.NotNull(range);
        Assert.Equal(new TableCellRange(0, 1, 2, 3), range.Value);

        // Check all 6 merged coordinates
        for (int r = 0; r <= 1; r++)
        {
            for (int c = 1; c <= 3; c++)
            {
                Assert.Same(blockCell, grid.GetCellAt(r, c));
                var (root, rootR, rootC) = grid.GetMergedRoot(r, c);
                Assert.Same(blockCell, root);
                Assert.Equal(0, rootR);
                Assert.Equal(1, rootC);
            }
        }
    }

    [Fact]
    public void Effective_Borders_Collapsing_And_Arbitration()
    {
        // Cell (0, 0) has Right = 1pt Solid Red, Top = 2pt Solid Black
        var b00 = new CellBorders(TableBorder.None, TableBorder.Solid(Black) with { Width = 2 }, TableBorder.Solid(Red) with { Width = 1 }, TableBorder.None);
        var c00 = Cell(0, borders: b00);

        // Cell (0, 1) has Left = 3pt Solid Blue, Bottom = 1.5pt Double Black
        var b01 = new CellBorders(TableBorder.Solid(Blue) with { Width = 3 }, TableBorder.None, TableBorder.None, TableBorder.Double(Black));
        var c01 = Cell(1, borders: b01);

        var table = new DocumentTable(0, 2, [new TableRow([c00, c01])], [50, 50]);
        var grid = table.Grid;

        // Shared seam at column 0 / 1: 3pt Solid Blue beats 1pt Solid Red (thicker wins)
        var eff00 = grid.GetEffectiveBorders(0, 0);
        var eff01 = grid.GetEffectiveBorders(0, 1);

        Assert.Equal(3, eff00.Right.Width);
        Assert.Equal(Blue, eff00.Right.Color);
        Assert.Equal(eff00.Right, eff01.Left);

        // Outer borders preserved
        Assert.Equal(2, eff00.Top.Width);
        Assert.Equal(Black, eff00.Top.Color);

        Assert.Equal(TableBorderStyle.Double, eff01.Bottom.Style);
        Assert.Equal(1.5, eff01.Bottom.Width);
    }

    [Fact]
    public void Effective_Borders_Internal_Merge_Seams_Are_Removed()
    {
        // 2x2 merged cell with solid borders on all 4 sides
        var borders = CellBorders.All(TableBorder.Solid(Black) with { Width = 2 });
        var merged = Cell(0, colSpan: 2, rowSpan: 2, borders: borders);

        var table = new DocumentTable(0, 1, [new TableRow([merged]), new TableRow([])], [50, 50]);
        var grid = table.Grid;

        // (0, 0) is top-left: Top and Left are outer edges, Right and Bottom are internal seams
        var b00 = grid.GetEffectiveBorders(0, 0);
        Assert.True(b00.Top.IsVisible);
        Assert.True(b00.Left.IsVisible);
        Assert.False(b00.Right.IsVisible); // Internal seam
        Assert.False(b00.Bottom.IsVisible); // Internal seam

        // (0, 1) is top-right: Top and Right are outer edges, Left and Bottom are internal seams
        var b01 = grid.GetEffectiveBorders(0, 1);
        Assert.True(b01.Top.IsVisible);
        Assert.True(b01.Right.IsVisible);
        Assert.False(b01.Left.IsVisible);
        Assert.False(b01.Bottom.IsVisible);

        // (1, 1) is bottom-right: Bottom and Right are outer edges, Top and Left are internal seams
        var b11 = grid.GetEffectiveBorders(1, 1);
        Assert.True(b11.Bottom.IsVisible);
        Assert.True(b11.Right.IsVisible);
        Assert.False(b11.Top.IsVisible);
        Assert.False(b11.Left.IsVisible);
    }

    [Fact]
    public void Transpose_Transformation()
    {
        // 2 rows x 3 columns table:
        // Row 0: c00 (1x1), c01 (1x1), c02_span (rowSpan 2, colSpan 1)
        // Row 1: c10 (1x1), c11 (1x1)
        var c00 = Cell(0, borders: Borders(top: TableBorder.Solid(Red)));
        var c01 = Cell(1);
        var c02 = Cell(2, rowSpan: 2);
        var c10 = Cell(0);
        var c11 = Cell(1);

        var table = new DocumentTable(0, 5, [
            new TableRow([c00, c01, c02]),
            new TableRow([c10, c11])
        ], [10, 20, 30]);

        var grid = table.Grid;
        var transposed = grid.Transpose();

        Assert.Equal(3, transposed.RowCount);
        Assert.Equal(2, transposed.ColumnCount);

        // Old (0, 2) with rowSpan 2, colSpan 1 becomes (2, 0) with rowSpan 1, colSpan 2
        var transposedMerged = transposed.GetCellAt(2, 0);
        Assert.NotNull(transposedMerged);
        Assert.Equal(1, transposedMerged.RowSpan);
        Assert.Equal(2, transposedMerged.ColumnSpan);
        Assert.Same(transposedMerged, transposed.GetCellAt(2, 1));

        // Border transposition: Old Top becomes Left
        var transposedC00 = transposed.GetCellAt(0, 0);
        Assert.NotNull(transposedC00);
        Assert.Equal(TableBorder.Solid(Red), transposedC00.Borders.Left);
    }

    [Fact]
    public void FlipHorizontal_Transformation()
    {
        // 1 row x 3 columns:
        // Cell 0 spans 2 columns at col 0, Cell 1 spans 1 column at col 2
        var c0 = Cell(0, colSpan: 2, borders: Borders(left: TableBorder.Solid(Red)));
        var c2 = Cell(2, borders: Borders(right: TableBorder.Solid(Blue)));

        var table = new DocumentTable(0, 2, [new TableRow([c0, c2])], [10, 20, 30]);
        var grid = table.Grid;
        var flipped = grid.FlipHorizontal();

        Assert.Equal(1, flipped.RowCount);
        Assert.Equal(3, flipped.ColumnCount);

        // In 3 columns, cell c0 (cols 0..1) mirrors to cols (3 - 2) = 1..2
        // cell c2 (col 2) mirrors to col (3 - 3) = 0
        Assert.Equal(0, flipped.GetCellAt(0, 0)!.ColumnIndex);
        Assert.Equal(1, flipped.GetCellAt(0, 1)!.ColumnIndex);
        Assert.Equal(2, flipped.GetCellAt(0, 1)!.ColumnSpan);

        // Left and Right borders swapped: c0 had Left Red, now has Right Red
        Assert.Equal(TableBorder.Solid(Red), flipped.GetCellAt(0, 1)!.Borders.Right);
        // c2 had Right Blue, now has Left Blue
        Assert.Equal(TableBorder.Solid(Blue), flipped.GetCellAt(0, 0)!.Borders.Left);

        // ColumnWidths reversed: 30, 20, 10
        Assert.Equal([30, 20, 10], flipped.ColumnWidths);
    }

    [Fact]
    public void FlipVertical_Transformation()
    {
        // 3 rows x 1 column:
        // Cell 0 spans 2 rows at row 0, Cell 2 spans 1 row at row 2
        var c0 = Cell(0, rowSpan: 2, borders: Borders(top: TableBorder.Solid(Red)));
        var c2 = Cell(0, borders: Borders(bottom: TableBorder.Solid(Blue)));

        var table = new DocumentTable(0, 2, [
            new TableRow([c0]),
            new TableRow([]),
            new TableRow([c2])
        ], [50]);

        var grid = table.Grid;
        var flipped = grid.FlipVertical();

        Assert.Equal(3, flipped.RowCount);
        Assert.Equal(1, flipped.ColumnCount);

        // In 3 rows, c0 (rows 0..1) mirrors to rows (3 - 2) = 1..2
        // c2 (row 2) mirrors to row (3 - 3) = 0
        var topCell = flipped.GetCellAt(0, 0);
        Assert.NotNull(topCell);
        Assert.Equal(1, topCell.RowSpan);

        var bottomCell = flipped.GetCellAt(1, 0);
        Assert.NotNull(bottomCell);
        Assert.Equal(2, bottomCell.RowSpan);
        Assert.Same(bottomCell, flipped.GetCellAt(2, 0));

        // Borders swapped: c0 Top Red becomes Bottom Red
        Assert.Equal(TableBorder.Solid(Red), bottomCell.Borders.Bottom);
        // c2 Bottom Blue becomes Top Blue
        Assert.Equal(TableBorder.Solid(Blue), topCell.Borders.Top);
    }

    [Fact]
    public void SubGrid_Window_Extraction()
    {
        // 4x4 grid
        var cells = new (int Row, int Column, TableCell Cell)[]
        {
            (0, 0, Cell(0)),
            (0, 1, Cell(1, rowSpan: 2)), // rowSpan 2 into subgrid window
            (1, 1, Cell(1)),             // will be overridden by rowSpan
            (1, 2, Cell(2, colSpan: 2)), // colSpan 2
            (2, 2, Cell(2)),
            (3, 3, Cell(3))
        };

        var grid = TableGrid.FromCells(4, 4, cells, [10, 20, 30, 40]);

        // Extract 2x2 subgrid at (1, 1) -> rows [1..2], cols [1..2]
        var sub = grid.SubGrid(startRow: 1, startColumn: 1, rowCount: 2, columnCount: 2);

        Assert.Equal(2, sub.RowCount);
        Assert.Equal(2, sub.ColumnCount);
        Assert.Equal([20, 30], sub.ColumnWidths);

        // The cell from (0, 1) spanning rows 0..1 intersects the subgrid at sub-row 0, sub-col 0 with rowSpan 1!
        var clippedCell = sub.GetCellAt(0, 0);
        Assert.NotNull(clippedCell);
        Assert.Equal(1, clippedCell.RowSpan);

        // Cell (1, 2) spanning cols 2..3 intersects the subgrid at sub-row 0, sub-col 1 with colSpan 1!
        var clippedColCell = sub.GetCellAt(0, 1);
        Assert.NotNull(clippedColCell);
        Assert.Equal(1, clippedColCell.ColumnSpan);
    }

    [Fact]
    public void InsertRow_And_DeleteRow()
    {
        // 3 rows: row 0 has a cell spanning 2 rows (rows 0..1), row 2 has a cell
        var c0 = Cell(0, rowSpan: 2);
        var c2 = Cell(0);
        var grid = TableGrid.FromCells(3, 1, [(0, 0, c0), (2, 0, c2)]);

        // Insert row at index 1: c0 now spans across the inserted row (rowSpan becomes 3)
        var afterInsert = grid.InsertRow(1);
        Assert.Equal(4, afterInsert.RowCount);

        var insertedCell = afterInsert.GetCellAt(0, 0);
        Assert.NotNull(insertedCell);
        Assert.Equal(3, insertedCell.RowSpan);
        Assert.Same(insertedCell, afterInsert.GetCellAt(1, 0));
        Assert.Same(insertedCell, afterInsert.GetCellAt(2, 0));

        // c2 shifted from row 2 to row 3
        Assert.Same(c2, afterInsert.GetCellAt(3, 0));

        // Now delete row at index 1: returns to rowSpan 2 and c2 at row 2
        var afterDelete = afterInsert.DeleteRow(1);
        Assert.Equal(3, afterDelete.RowCount);
        Assert.Equal(2, afterDelete.GetCellAt(0, 0)!.RowSpan);
        Assert.Same(c2, afterDelete.GetCellAt(2, 0));
    }

    [Fact]
    public void InsertColumn_And_DeleteColumn()
    {
        var c0 = Cell(0, colSpan: 2);
        var c2 = Cell(2);
        var grid = TableGrid.FromCells(1, 3, [(0, 0, c0), (0, 2, c2)], [10, 20, 30]);

        // Insert column at index 1: c0 expands to colSpan 3
        var afterInsert = grid.InsertColumn(1, width: 15);
        Assert.Equal(4, afterInsert.ColumnCount);
        Assert.Equal([10, 15, 20, 30], afterInsert.ColumnWidths);

        var insertedCell = afterInsert.GetCellAt(0, 0);
        Assert.NotNull(insertedCell);
        Assert.Equal(3, insertedCell.ColumnSpan);

        // Delete column at index 1
        var afterDelete = afterInsert.DeleteColumn(1);
        Assert.Equal(3, afterDelete.ColumnCount);
        Assert.Equal([10, 20, 30], afterDelete.ColumnWidths);
        Assert.Equal(2, afterDelete.GetCellAt(0, 0)!.ColumnSpan);
    }

    [Fact]
    public void Sparse_Matrix_Scalability_Over_Large_Dimensions()
    {
        // 1,000 rows x 1,000 columns matrix with only 3 cells placed
        var c0 = Cell(0);
        var cMid = Cell(500, colSpan: 2, rowSpan: 2);
        var cEnd = Cell(999);

        var grid = TableGrid.FromCells(1000, 1000, [
            (0, 0, c0),
            (500, 500, cMid),
            (999, 999, cEnd)
        ]);

        Assert.Equal(1000, grid.RowCount);
        Assert.Equal(1000, grid.ColumnCount);
        Assert.Equal(3, grid.CellCount);
        Assert.Equal(6, grid.NonEmptySlots.Count); // c0 (1) + cMid (4) + cEnd (1) = 6 slots

        Assert.Same(c0, grid.GetCellAt(0, 0));
        Assert.Same(cMid, grid.GetCellAt(500, 500));
        Assert.Same(cMid, grid.GetCellAt(501, 501));
        Assert.Same(cEnd, grid.GetCellAt(999, 999));

        Assert.Null(grid.GetCellAt(100, 100));
        Assert.Null(grid.GetCellAt(502, 502));
        Assert.True(grid.IsEmpty(250, 250));
    }

    [Fact]
    public void Roundtrip_ToTable_Reconstructs_Table_Accurately()
    {
        var c00 = Cell(0, colSpan: 2);
        var c10 = Cell(0);
        var c11 = Cell(1);

        var original = new DocumentTable(10, 3, [
            new TableRow([c00]),
            new TableRow([c10, c11])
        ], [40, 60], cellPadding: 8);

        var grid = original.Grid;
        var reconstructed = grid.ToTable(paragraphIndex: original.ParagraphIndex, cellPadding: original.CellPadding);

        Assert.Equal(original.Rows.Count, reconstructed.Rows.Count);
        Assert.Equal(original.ColumnWidths, reconstructed.ColumnWidths);
        Assert.Equal(original.CellPadding, reconstructed.CellPadding);

        var reconGrid = reconstructed.Grid;
        Assert.Equal(grid.RowCount, reconGrid.RowCount);
        Assert.Equal(grid.ColumnCount, reconGrid.ColumnCount);
        Assert.Equal(grid.CellCount, reconGrid.CellCount);

        for (int r = 0; r < grid.RowCount; r++)
        {
            for (int c = 0; c < grid.ColumnCount; c++)
            {
                var origCell = grid.GetCellAt(r, c);
                var reconCell = reconGrid.GetCellAt(r, c);
                if (origCell is null)
                {
                    Assert.Null(reconCell);
                }
                else
                {
                    Assert.NotNull(reconCell);
                    Assert.Equal(origCell.ColumnSpan, reconCell.ColumnSpan);
                    Assert.Equal(origCell.RowSpan, reconCell.RowSpan);
                }
            }
        }
    }
}
