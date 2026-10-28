using System.Linq;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Model.Tests;

public sealed class TableShiftTests
{
    private static DocumentTable Create2x2Table(int startPara = 0) =>
        new(
            startPara,
            4,
            [
                new TableRow([new TableCell(startPara, 1, 0), new TableCell(startPara + 1, 1, 1)]),
                new TableRow([new TableCell(startPara + 2, 1, 0), new TableCell(startPara + 3, 1, 1)]),
            ],
            [100, 100]);

    private static RichTextDocument Create2x2Doc()
    {
        return RichTextDocument.FromParagraphs([
            RichTextParagraph.Plain("r0c0"),
            RichTextParagraph.Plain("r0c1"),
            RichTextParagraph.Plain("r1c0"),
            RichTextParagraph.Plain("r1c1"),
        ]).WithTables([Create2x2Table(0)]);
    }

    [Fact]
    public void ShiftRange_DefaultAffinity_ExpandsOnStart_DoesNotExpandOnEnd()
    {
        // Insert at start: expands
        (int start, int count) = DocumentTable.ShiftRange(10, 4, 10, 0, 2, RangeAffinity.Default);
        Assert.Equal(10, start);
        Assert.Equal(6, count);

        // Insert at end: does not expand
        (start, count) = DocumentTable.ShiftRange(10, 4, 14, 0, 2, RangeAffinity.Default);
        Assert.Equal(10, start);
        Assert.Equal(4, count);
    }

    [Fact]
    public void ShiftRange_ShiftAtStart_SlidesRangeForward()
    {
        (int start, int count) = DocumentTable.ShiftRange(10, 4, 10, 0, 2, RangeAffinity.ShiftAtStart);
        Assert.Equal(12, start);
        Assert.Equal(4, count);
    }

    [Fact]
    public void ShiftRange_ExpandAtEnd_ExpandsRange()
    {
        (int start, int count) = DocumentTable.ShiftRange(10, 4, 14, 0, 2, RangeAffinity.ExpandAtEnd);
        Assert.Equal(10, start);
        Assert.Equal(6, count);
    }

    [Fact]
    public void ShiftRange_StrictInterior_DoesNotExpandAtEitherBoundary()
    {
        (int start, int count) = DocumentTable.ShiftRange(10, 4, 10, 0, 2, RangeAffinity.StrictInterior);
        Assert.Equal(12, start);
        Assert.Equal(4, count);

        (start, count) = DocumentTable.ShiftRange(10, 4, 14, 0, 2, RangeAffinity.StrictInterior);
        Assert.Equal(10, start);
        Assert.Equal(4, count);
    }

    [Fact]
    public void ShiftRange_EmptyCell_RelocatesCleanly()
    {
        // Empty cell before insertion
        (int start, int count) = DocumentTable.ShiftRange(5, 0, 2, 0, 3);
        Assert.Equal(8, start);
        Assert.Equal(0, count);

        // Empty cell after insertion
        (start, count) = DocumentTable.ShiftRange(5, 0, 10, 0, 3);
        Assert.Equal(5, start);
        Assert.Equal(0, count);

        // Empty cell at insertion boundary with ShiftAtStart
        (start, count) = DocumentTable.ShiftRange(5, 0, 5, 0, 3, RangeAffinity.ShiftAtStart);
        Assert.Equal(8, start);
        Assert.Equal(0, count);
    }

    [Fact]
    public void RelocateRange_BackwardMove_CalculatesExactIndices()
    {
        // Moving block [4, 6) to 0
        Assert.Equal((0, 2), DocumentTable.RelocateRange(4, 2, 4, 2, 0)); // moved block
        Assert.Equal((2, 2), DocumentTable.RelocateRange(0, 2, 4, 2, 0)); // shifted forward
        Assert.Equal((4, 2), DocumentTable.RelocateRange(2, 2, 4, 2, 0)); // shifted forward
        Assert.Equal((10, 2), DocumentTable.RelocateRange(10, 2, 4, 2, 0)); // outside, unaffected
    }

    [Fact]
    public void RelocateRange_ForwardMove_CalculatesExactIndices()
    {
        // Moving block [0, 2) to 6
        Assert.Equal((4, 2), DocumentTable.RelocateRange(0, 2, 0, 2, 6)); // moved block
        Assert.Equal((0, 2), DocumentTable.RelocateRange(2, 2, 0, 2, 6)); // shifted backward
        Assert.Equal((2, 2), DocumentTable.RelocateRange(4, 2, 0, 2, 6)); // shifted backward
    }

    [Fact]
    public void ShiftedBy_RecursivelyOffsetsTableAndNestedTables()
    {
        var innerTable = new DocumentTable(
            1,
            1,
            [new TableRow([new TableCell(1, 1, 0)])],
            [50]);

        var outerTable = new DocumentTable(
            0,
            2,
            [
                new TableRow([
                    new TableCell(0, 1, 0, tables: [innerTable]),
                    new TableCell(1, 1, 1),
                ]),
            ],
            [100, 100]);

        DocumentTable shifted = outerTable.ShiftedBy(5);
        Assert.Equal(5, shifted.ParagraphIndex);
        Assert.Equal(2, shifted.ParagraphCount);
        Assert.Equal(5, shifted.Rows[0].Cells[0].ParagraphIndex);
        Assert.Equal(6, shifted.Rows[0].Cells[1].ParagraphIndex);

        DocumentTable nestedShifted = Assert.Single(shifted.Rows[0].Cells[0].Tables);
        Assert.Equal(6, nestedShifted.ParagraphIndex);
        Assert.Equal(1, nestedShifted.ParagraphCount);
        Assert.Equal(6, nestedShifted.Rows[0].Cells[0].ParagraphIndex);
    }

    [Fact]
    public void ValidateParagraphInvariants_DetectsValidAndInvalidTables()
    {
        DocumentTable valid = Create2x2Table(0);
        Assert.True(valid.ValidateParagraphInvariants(out string? error), error);

        // Invalid: cell paragraph index does not match expected row-major running index
        var corrupted = new DocumentTable(
            0,
            4,
            [
                new TableRow([new TableCell(0, 1, 0), new TableCell(2, 1, 1)]), // gap at 1
                new TableRow([new TableCell(3, 1, 0), new TableCell(4, 1, 1)]),
            ],
            [100, 100]);

        Assert.False(corrupted.ValidateParagraphInvariants(out string? err));
        Assert.Contains("expected 1", err);
    }

    [Fact]
    public void MoveRow_ReordersRowsAndRelocatesCellParagraphs()
    {
        DocumentTable table = new(
            0,
            6,
            [
                new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)]), // Row 0: [0, 2)
                new TableRow([new TableCell(2, 1, 0), new TableCell(3, 1, 1)]), // Row 1: [2, 4)
                new TableRow([new TableCell(4, 1, 0), new TableCell(5, 1, 1)]), // Row 2: [4, 6)
            ],
            [100, 100]);

        // Move Row 2 to Row 0
        DocumentTable reordered = table.MoveRow(2, 0);
        Assert.True(reordered.ValidateParagraphInvariants(out string? error), error);
        Assert.Equal(6, reordered.ParagraphCount);

        // Row 0 now holds former Row 2 cells
        Assert.Equal(0, reordered.Rows[0].Cells[0].ParagraphIndex);
        Assert.Equal(1, reordered.Rows[0].Cells[1].ParagraphIndex);

        // Row 1 now holds former Row 0 cells
        Assert.Equal(2, reordered.Rows[1].Cells[0].ParagraphIndex);
        Assert.Equal(3, reordered.Rows[1].Cells[1].ParagraphIndex);

        // Row 2 now holds former Row 1 cells
        Assert.Equal(4, reordered.Rows[2].Cells[0].ParagraphIndex);
        Assert.Equal(5, reordered.Rows[2].Cells[1].ParagraphIndex);
    }

    [Fact]
    public void MoveRow_WithNestedTable_PreservesNestedTableAnchor()
    {
        var nested = new DocumentTable(4, 1, [new TableRow([new TableCell(4, 1, 0)])], [50]);
        DocumentTable table = new(
            0,
            6,
            [
                new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)]),
                new TableRow([new TableCell(2, 1, 0), new TableCell(3, 1, 1)]),
                new TableRow([new TableCell(4, 1, 0, tables: [nested]), new TableCell(5, 1, 1)]),
            ],
            [100, 100]);

        DocumentTable reordered = table.MoveRow(2, 0);
        Assert.True(reordered.ValidateParagraphInvariants(out string? error), error);

        TableCell cellWithNested = reordered.Rows[0].Cells[0];
        Assert.Equal(0, cellWithNested.ParagraphIndex);

        DocumentTable relocatedNested = Assert.Single(cellWithNested.Tables);
        Assert.Equal(0, relocatedNested.ParagraphIndex);
        Assert.Equal(0, relocatedNested.Rows[0].Cells[0].ParagraphIndex);
    }

    [Fact]
    public void InsertRow_ExpandsTableAndShiftsSubsequentRows()
    {
        DocumentTable table = Create2x2Table(0); // 2 rows, 4 paragraphs

        // Insert row at index 1 with 1 paragraph per cell
        DocumentTable expanded = table.InsertRow(1, paragraphsPerCell: 1);
        Assert.True(expanded.ValidateParagraphInvariants(out string? error), error);
        Assert.Equal(3, expanded.Rows.Count);
        Assert.Equal(6, expanded.ParagraphCount);

        // Row 0: unchanged
        Assert.Equal(0, expanded.Rows[0].Cells[0].ParagraphIndex);
        Assert.Equal(1, expanded.Rows[0].Cells[1].ParagraphIndex);

        // Row 1 (inserted): [2, 4)
        Assert.Equal(2, expanded.Rows[1].Cells[0].ParagraphIndex);
        Assert.Equal(3, expanded.Rows[1].Cells[1].ParagraphIndex);

        // Row 2 (former Row 1): shifted by +2
        Assert.Equal(4, expanded.Rows[2].Cells[0].ParagraphIndex);
        Assert.Equal(5, expanded.Rows[2].Cells[1].ParagraphIndex);
    }

    [Fact]
    public void InsertRow_AcrossRowSpan_IncrementsRowSpan()
    {
        var table = new DocumentTable(
            0,
            4,
            [
                new TableRow([new TableCell(0, 1, 0, rowSpan: 2), new TableCell(1, 1, 1)]),
                new TableRow([new TableCell(0, 0, 0, isRowSpanContinuation: true), new TableCell(2, 1, 1)]),
            ],
            [100, 100]);

        DocumentTable expanded = table.InsertRow(1, paragraphsPerCell: 1);
        Assert.Equal(3, expanded.Rows.Count);
        // Cell in Row 0 now spans 3 rows
        Assert.Equal(3, expanded.Rows[0].Cells[0].RowSpan);
    }

    [Fact]
    public void DeleteRow_ShrinksTableAndPromotesRootCells()
    {
        var table = new DocumentTable(
            0,
            3,
            [
                new TableRow([new TableCell(0, 1, 0, rowSpan: 2), new TableCell(1, 1, 1)]),
                new TableRow([new TableCell(0, 0, 0, isRowSpanContinuation: true), new TableCell(2, 1, 1)]),
            ],
            [100, 100]);

        // Delete Row 0 (where root of rowSpan: 2 sits)
        DocumentTable? remaining = table.DeleteRow(0);
        Assert.NotNull(remaining);
        Assert.Single(remaining.Rows);
        // The continuation cell in Row 1 should have been promoted to root cell
        TableCell promoted = remaining.Rows[0].Cells[0];
        Assert.False(promoted.IsRowSpanContinuation);
        Assert.Equal(1, promoted.RowSpan);
    }

    [Fact]
    public void InsertColumn_AddsColumnAndUpdatesWidths()
    {
        DocumentTable table = Create2x2Table(0); // 2 cols, widths [100, 100]

        // Insert column at index 1 with width 50
        DocumentTable expanded = table.InsertColumn(1, width: 50, paragraphsPerCell: 1);
        Assert.True(expanded.ValidateParagraphInvariants(out string? error), error);
        Assert.Equal(3, expanded.ColumnWidths.Count);
        Assert.Equal(100, expanded.ColumnWidths[0]);
        Assert.Equal(50, expanded.ColumnWidths[1]);
        Assert.Equal(100, expanded.ColumnWidths[2]);

        Assert.Equal(6, expanded.ParagraphCount);
        Assert.Equal(3, expanded.Rows[0].Cells.Count);
        Assert.Equal(3, expanded.Rows[1].Cells.Count);
    }

    [Fact]
    public void DeleteColumn_RemovesColumnAndShiftsParagraphs()
    {
        DocumentTable table = Create2x2Table(0);
        DocumentTable? remaining = table.DeleteColumn(0);
        Assert.NotNull(remaining);
        Assert.True(remaining.ValidateParagraphInvariants(out string? error), error);
        Assert.Single(remaining.ColumnWidths);
        Assert.Equal(100, remaining.ColumnWidths[0]);
        Assert.Equal(2, remaining.ParagraphCount);
        Assert.Single(remaining.Rows[0].Cells);
        Assert.Single(remaining.Rows[1].Cells);
    }

    [Fact]
    public void MergeCells_And_SplitCell_RoundTrip()
    {
        DocumentTable table = Create2x2Table(0);
        DocumentTable merged = table.MergeCells(0, 0, 2, 2);
        Assert.True(merged.ValidateParagraphInvariants(out string? error), error);
        Assert.Equal(4, merged.ParagraphCount);

        TableCell root = merged.Rows[0].Cells[0];
        Assert.Equal(2, root.RowSpan);
        Assert.Equal(2, root.ColumnSpan);
        Assert.Equal(4, root.ParagraphCount);

        // Split back
        DocumentTable split = merged.SplitCell(0, 0);
        Assert.True(split.ValidateParagraphInvariants(out string? splitErr), splitErr);
        Assert.Equal(2, split.Rows.Count);
        Assert.Equal(2, split.Rows[0].Cells.Count);
        Assert.Equal(2, split.Rows[1].Cells.Count);
    }

    [Fact]
    public void SplitCell_1x1Cell_HorizontalSplit_CreatesMultipleCells()
    {
        DocumentTable table = Create2x2Table(0);
        // Split Cell (0, 0) horizontally into 2 cells
        DocumentTable split = table.SplitCell(0, 0, horizontalSplit: 2, newParagraphsPerSplit: 1);
        Assert.True(split.ValidateParagraphInvariants(out string? error), error);
        Assert.Equal(3, split.Rows[0].Cells.Count);
        Assert.Equal(2, split.Rows[1].Cells.Count);
        Assert.Equal(5, split.ParagraphCount);
    }

    [Fact]
    public void RichTextDocument_MoveTableRow_RearrangesParagraphsAndTable()
    {
        RichTextDocument doc = Create2x2Doc();
        DocumentTable table = doc.Tables[0];

        RichTextDocument updated = doc.MoveTableRow(table, 1, 0);
        DocumentTable updatedTable = Assert.Single(updated.Tables);
        Assert.True(updatedTable.ValidateParagraphInvariants(out string? error), error);

        // Check paragraph text rearranged in document
        Assert.Equal("r1c0", updated.Paragraphs[0].Text);
        Assert.Equal("r1c1", updated.Paragraphs[1].Text);
        Assert.Equal("r0c0", updated.Paragraphs[2].Text);
        Assert.Equal("r0c1", updated.Paragraphs[3].Text);
    }

    [Fact]
    public void RichTextDocument_InsertTableRow_InsertsParagraphsAndShifts()
    {
        RichTextDocument doc = Create2x2Doc();
        DocumentTable table = doc.Tables[0];

        RichTextDocument updated = doc.InsertTableRow(table, 1, row: null, paragraphsPerCell: 1, paragraphFactory: p => RichTextParagraph.Plain($"new_{p}"));
        Assert.Equal(6, updated.ParagraphCount);
        DocumentTable updatedTable = Assert.Single(updated.Tables);
        Assert.True(updatedTable.ValidateParagraphInvariants(out string? error), error);

        Assert.Equal("r0c0", updated.Paragraphs[0].Text);
        Assert.Equal("r0c1", updated.Paragraphs[1].Text);
        Assert.Equal("new_0", updated.Paragraphs[2].Text);
        Assert.Equal("new_1", updated.Paragraphs[3].Text);
        Assert.Equal("r1c0", updated.Paragraphs[4].Text);
        Assert.Equal("r1c1", updated.Paragraphs[5].Text);
    }

    [Fact]
    public void RichTextDocument_DeleteTableRow_RemovesParagraphsAndShifts()
    {
        RichTextDocument doc = Create2x2Doc();
        DocumentTable table = doc.Tables[0];

        RichTextDocument updated = doc.DeleteTableRow(table, 0);
        Assert.Equal(2, updated.ParagraphCount);
        DocumentTable updatedTable = Assert.Single(updated.Tables);
        Assert.True(updatedTable.ValidateParagraphInvariants(out string? error), error);

        Assert.Equal("r1c0", updated.Paragraphs[0].Text);
        Assert.Equal("r1c1", updated.Paragraphs[1].Text);
    }

    [Fact]
    public void RichTextDocument_MultipleTables_SubsequentTableShifts()
    {
        // Two tables: Table 1 at 0..4, Table 2 at 4..8
        DocumentTable t1 = Create2x2Table(0);
        DocumentTable t2 = Create2x2Table(4);

        var doc = RichTextDocument.FromParagraphs(
            Enumerable.Range(0, 8).Select(i => RichTextParagraph.Plain($"p{i}")).ToList())
            .WithTables([t1, t2]);

        // Insert row in table 1 (+2 paragraphs)
        RichTextDocument updated = doc.InsertTableRow(t1, 1, paragraphsPerCell: 1);
        Assert.Equal(2, updated.Tables.Count);

        DocumentTable updatedT1 = updated.Tables[0];
        DocumentTable updatedT2 = updated.Tables[1];

        Assert.Equal(0, updatedT1.ParagraphIndex);
        Assert.Equal(6, updatedT1.ParagraphCount);

        // Table 2 shifted by +2
        Assert.Equal(6, updatedT2.ParagraphIndex);
        Assert.Equal(4, updatedT2.ParagraphCount);
        Assert.True(updatedT2.ValidateParagraphInvariants(out string? error), error);
    }

    [Fact]
    public void FloatingTable_PositionPreservedAcrossShiftAndMoves()
    {
        var position = new TablePosition(
            TableAnchor.Margin,
            0,
            TableAnchor.Page,
            50,
            HorizontalAlignment: TableAlignment.Center,
            VerticalAlignment: TableAlignment.Start,
            AllowOverlap: false,
            ZOrder: 1);

        var table = new DocumentTable(
            0,
            4,
            [
                new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)]),
                new TableRow([new TableCell(2, 1, 0), new TableCell(3, 1, 1)]),
            ],
            [100, 100],
            position: position);

        // Shift table by offset
        DocumentTable shifted = table.ShiftedBy(10);
        Assert.NotNull(shifted.Position);
        Assert.False(shifted.Position.AllowOverlap);
        Assert.Equal(TableAlignment.Center, shifted.Position.HorizontalAlignment);

        // Move row in floating table
        DocumentTable moved = shifted.MoveRow(1, 0);
        Assert.NotNull(moved.Position);
        Assert.Equal(1, moved.Position.ZOrder);
        Assert.True(moved.ValidateParagraphInvariants(out string? error), error);
    }

    [Fact]
    public void DeeplyNestedTable_Level2Nesting_PreservesAnchorsOnRowMove()
    {
        // Level 2 (deepest) table
        var innerTable = new DocumentTable(4, 1, [new TableRow([new TableCell(4, 1, 0)])], [25]);

        // Level 1 table containing innerTable
        var middleTable = new DocumentTable(4, 2, [new TableRow([
            new TableCell(4, 1, 0, tables: [innerTable]),
            new TableCell(5, 1, 1),
        ])], [50, 50]);

        // Outer table containing middleTable in Row 2
        var outerTable = new DocumentTable(
            0,
            6,
            [
                new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)]),
                new TableRow([new TableCell(2, 1, 0), new TableCell(3, 1, 1)]),
                new TableRow([new TableCell(4, 2, 0, tables: [middleTable]), new TableCell(6, 0, 1, isRowSpanContinuation: true)]),
            ],
            [100, 100]);

        // Move Row 2 of outer table to Row 0
        DocumentTable moved = outerTable.MoveRow(2, 0);
        Assert.True(moved.ValidateParagraphInvariants(out string? error), error);

        TableCell cellWithMiddle = moved.Rows[0].Cells[0];
        Assert.Equal(0, cellWithMiddle.ParagraphIndex);

        DocumentTable movedMiddle = Assert.Single(cellWithMiddle.Tables);
        Assert.Equal(0, movedMiddle.ParagraphIndex);
        Assert.True(movedMiddle.ValidateParagraphInvariants(out string? middleErr), middleErr);

        TableCell cellWithInner = movedMiddle.Rows[0].Cells[0];
        Assert.Equal(0, cellWithInner.ParagraphIndex);

        DocumentTable movedInner = Assert.Single(cellWithInner.Tables);
        Assert.Equal(0, movedInner.ParagraphIndex);
        Assert.True(movedInner.ValidateParagraphInvariants(out string? innerErr), innerErr);
    }
}
