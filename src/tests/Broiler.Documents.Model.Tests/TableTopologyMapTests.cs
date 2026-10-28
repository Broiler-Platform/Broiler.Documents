using System;
using System.Collections.Generic;
using System.Linq;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Tables;
using Xunit;

namespace Broiler.Documents.Model.Tests;

public sealed class TableTopologyMapTests
{
    private sealed class TestCell
    {
        public string Name { get; }
        public TestCell(string name) => Name = name;
        public override string ToString() => Name;
    }

    [Fact]
    public void TableTopologyLimits_Clamping_WorksAsExpected()
    {
        Assert.Equal(1, TableTopologyLimits.ClampRowSpan(-10));
        Assert.Equal(1, TableTopologyLimits.ClampRowSpan(0));
        Assert.Equal(5, TableTopologyLimits.ClampRowSpan(5));
        Assert.Equal(TableTopologyLimits.MaxRows, TableTopologyLimits.ClampRowSpan(100_000));

        Assert.Equal(1, TableTopologyLimits.ClampColumnSpan(-1));
        Assert.Equal(1, TableTopologyLimits.ClampColumnSpan(0));
        Assert.Equal(3, TableTopologyLimits.ClampColumnSpan(3));
        Assert.Equal(TableTopologyLimits.MaxColumns, TableTopologyLimits.ClampColumnSpan(5_000));

        Assert.Equal(0, TableTopologyLimits.ClampRowCount(-5));
        Assert.Equal(10, TableTopologyLimits.ClampRowCount(10));
        Assert.Equal(TableTopologyLimits.MaxRows, TableTopologyLimits.ClampRowCount(100_000));

        Assert.Equal(0, TableTopologyLimits.ClampColumnCount(-2));
        Assert.Equal(4, TableTopologyLimits.ClampColumnCount(4));
        Assert.Equal(TableTopologyLimits.MaxColumns, TableTopologyLimits.ClampColumnCount(2_000));
    }

    [Fact]
    public void Empty_TopologyMap_HasExpectedProperties()
    {
        var map = TableTopologyMap<TestCell>.CreateEmpty(0, 0);

        Assert.Equal(0, map.RowCount);
        Assert.Equal(0, map.ColumnCount);
        Assert.Equal(0, map.CellCount);
        Assert.Empty(map.DistinctCells);
        Assert.Empty(map.OccupiedSlots);

        var slot = map.GetSlot(0, 0);
        Assert.True(slot.IsEmpty);
        Assert.False(slot.IsOccupied);
        Assert.Null(slot.RootCell);
        Assert.Equal(-1, slot.RootRow);
        Assert.Equal(-1, slot.RootColumn);
        Assert.Null(map.GetCellAt(0, 0));
        Assert.True(map.IsEmpty(0, 0));
        Assert.False(map.IsMerged(0, 0));
        Assert.False(map.IsContinuation(0, 0));

        Assert.Empty(map.EnumerateHorizontalEdges());
        Assert.Empty(map.EnumerateVerticalEdges());
        Assert.Empty(map.EnumerateAllEdges());
    }

    [Fact]
    public void Basic_Occupancy_And_Coordinate_Lookups()
    {
        var c00 = new TestCell("c00");
        var c01 = new TestCell("c01");
        var c10 = new TestCell("c10");
        var c11 = new TestCell("c11");

        var map = TableTopologyMap<TestCell>.Build(2, 2, [
            (0, 0, 1, 1, c00),
            (0, 1, 1, 1, c01),
            (1, 0, 1, 1, c10),
            (1, 1, 1, 1, c11)
        ]);

        Assert.Equal(2, map.RowCount);
        Assert.Equal(2, map.ColumnCount);
        Assert.Equal(4, map.CellCount);
        Assert.Equal(4, map.OccupiedSlots.Count);

        var slot00 = map.GetSlot(0, 0);
        Assert.True(slot00.IsOccupied);
        Assert.True(slot00.IsRoot);
        Assert.False(slot00.IsContinuation);
        Assert.Equal(1, slot00.RowSpan);
        Assert.Equal(1, slot00.ColumnSpan);
        Assert.Same(c00, slot00.RootCell);

        Assert.Same(c00, map.GetCellAt(0, 0));
        Assert.Same(c01, map.GetCellAt(0, 1));
        Assert.Same(c10, map.GetCellAt(1, 0));
        Assert.Same(c11, map.GetCellAt(1, 1));

        var (top, right, bottom, left) = map.GetAdjacentCells(0, 0);
        Assert.Null(top);
        Assert.Same(c01, right);
        Assert.Same(c10, bottom);
        Assert.Null(left);

        var row0 = map.GetRowCells(0);
        Assert.Equal([c00, c01], row0);

        var col1 = map.GetColumnCells(1);
        Assert.Equal([c01, c11], col1);
    }

    [Fact]
    public void MergedCells_And_ContinuationSlots_Tracking()
    {
        var merged = new TestCell("Merged2x2");
        var rightTop = new TestCell("RightTop");
        var rightBottom = new TestCell("RightBottom");

        var map = TableTopologyMap<TestCell>.Build(2, 3, [
            (0, 0, 2, 2, merged),
            (0, 2, 1, 1, rightTop),
            (1, 2, 1, 1, rightBottom)
        ]);

        Assert.Equal(2, map.RowCount);
        Assert.Equal(3, map.ColumnCount);
        Assert.Equal(3, map.CellCount);

        // Root slot (0, 0)
        var s00 = map.GetSlot(0, 0);
        Assert.True(s00.IsRoot);
        Assert.False(s00.IsContinuation);
        Assert.Equal(0, s00.RowOffset);
        Assert.Equal(0, s00.ColumnOffset);
        Assert.True(s00.IsTopEdge);
        Assert.False(s00.IsBottomEdge);
        Assert.True(s00.IsLeftEdge);
        Assert.False(s00.IsRightEdge);
        Assert.Equal(2, s00.RowSpan);
        Assert.Equal(2, s00.ColumnSpan);

        // Continuation slot (0, 1)
        var s01 = map.GetSlot(0, 1);
        Assert.False(s01.IsRoot);
        Assert.True(s01.IsContinuation);
        Assert.Equal(0, s01.RowOffset);
        Assert.Equal(1, s01.ColumnOffset);
        Assert.True(s01.IsTopEdge);
        Assert.False(s01.IsBottomEdge);
        Assert.False(s01.IsLeftEdge);
        Assert.True(s01.IsRightEdge);

        // Continuation slot (1, 0)
        var s10 = map.GetSlot(1, 0);
        Assert.True(s10.IsContinuation);
        Assert.Equal(1, s10.RowOffset);
        Assert.Equal(0, s10.ColumnOffset);
        Assert.False(s10.IsTopEdge);
        Assert.True(s10.IsBottomEdge);
        Assert.True(s10.IsLeftEdge);
        Assert.False(s10.IsRightEdge);

        // Continuation slot (1, 1)
        var s11 = map.GetSlot(1, 1);
        Assert.True(s11.IsContinuation);
        Assert.Equal(1, s11.RowOffset);
        Assert.Equal(1, s11.ColumnOffset);
        Assert.False(s11.IsTopEdge);
        Assert.True(s11.IsBottomEdge);
        Assert.False(s11.IsLeftEdge);
        Assert.True(s11.IsRightEdge);

        // All 4 slots point to the same root cell
        Assert.Same(merged, map.GetCellAt(0, 0));
        Assert.Same(merged, map.GetCellAt(0, 1));
        Assert.Same(merged, map.GetCellAt(1, 0));
        Assert.Same(merged, map.GetCellAt(1, 1));

        var (root, rootR, rootC) = map.GetMergedRoot(1, 1);
        Assert.Same(merged, root);
        Assert.Equal(0, rootR);
        Assert.Equal(0, rootC);

        var range = map.GetCellRange(merged);
        Assert.NotNull(range);
        Assert.Equal((0, 0, 2, 2), range.Value);
    }

    [Fact]
    public void Overlap_Resolution_PushesCellToNextAvailableSlot()
    {
        var first = new TestCell("First1x2");
        var conflicting = new TestCell("Conflicting1x1");

        // Both placed at (0, 0)
        var map = TableTopologyMap<TestCell>.Build(1, 3, [
            (0, 0, 1, 2, first),
            (0, 0, 1, 1, conflicting)
        ]);

        // First cell retains (0, 0) and (0, 1)
        Assert.Same(first, map.GetCellAt(0, 0));
        Assert.Same(first, map.GetCellAt(0, 1));

        // Conflicting cell was pushed to next free slot (0, 2)
        Assert.Same(conflicting, map.GetCellAt(0, 2));

        Assert.Equal(2, map.CellCount);
        Assert.Equal(3, map.ColumnCount);
    }

    [Fact]
    public void Adversarial_Limits_GracefullyClamped()
    {
        var cell = new TestCell("ExtremeCell");

        // Extreme requested dimensions and spans
        var map = TableTopologyMap<TestCell>.Build(
            statedRowCount: 200_000,
            statedColumnCount: 20_000,
            cellPlacements: [(0, 0, 1_000_000, 50_000, cell)]);

        Assert.True(map.RowCount <= TableTopologyLimits.MaxRows);
        Assert.True(map.ColumnCount <= TableTopologyLimits.MaxColumns);
        Assert.True((long)map.RowCount * map.ColumnCount <= TableTopologyLimits.MaxSlots);
        Assert.Equal(1, map.CellCount);

        var slot = map.GetSlot(0, 0);
        Assert.True(slot.IsOccupied);
        Assert.True((long)slot.RowSpan * slot.ColumnSpan <= TableTopologyLimits.MaxSlotsPerCell);
    }

    [Fact]
    public void Edge_Enumeration_Perimeter_InternalSeam_And_SharedEdge()
    {
        var merged = new TestCell("MergedVertical2x1");
        var right0 = new TestCell("Right0");
        var right1 = new TestCell("Right1");

        // 2 rows, 2 cols:
        // Col 0: merged spans rows 0 and 1
        // Col 1: row 0 has right0, row 1 has right1
        var map = TableTopologyMap<TestCell>.Build(2, 2, [
            (0, 0, 2, 1, merged),
            (0, 1, 1, 1, right0),
            (1, 1, 1, 1, right1)
        ]);

        var hEdges = map.EnumerateHorizontalEdges();
        // 3 boundaries (0, 1, 2) * 2 columns = 6 horizontal edge segments
        Assert.Equal(6, hEdges.Count);

        // Boundary 0 (top perimeter)
        var top0 = hEdges.First(e => e.BoundaryIndex == 0 && e.SlotIndex == 0);
        Assert.True(top0.IsOuterBoundary);
        Assert.True(top0.IsPerimeter);
        Assert.Null(top0.PrimaryCell);
        Assert.Same(merged, top0.NeighborCell);

        // Boundary 1 (internal seam for col 0)
        var midCol0 = hEdges.First(e => e.BoundaryIndex == 1 && e.SlotIndex == 0);
        Assert.False(midCol0.IsOuterBoundary);
        Assert.True(midCol0.IsInternalSeam);
        Assert.False(midCol0.IsSharedEdge);
        Assert.Same(merged, midCol0.PrimaryCell);
        Assert.Same(merged, midCol0.NeighborCell);

        // Boundary 1 (shared edge between right0 and right1 for col 1)
        var midCol1 = hEdges.First(e => e.BoundaryIndex == 1 && e.SlotIndex == 1);
        Assert.False(midCol1.IsOuterBoundary);
        Assert.False(midCol1.IsInternalSeam);
        Assert.True(midCol1.IsSharedEdge);
        Assert.Same(right0, midCol1.PrimaryCell);
        Assert.Same(right1, midCol1.NeighborCell);

        // Boundary 2 (bottom perimeter)
        var bot0 = hEdges.First(e => e.BoundaryIndex == 2 && e.SlotIndex == 0);
        Assert.True(bot0.IsOuterBoundary);
        Assert.True(bot0.IsPerimeter);
        Assert.Same(merged, bot0.PrimaryCell);
        Assert.Null(bot0.NeighborCell);

        var vEdges = map.EnumerateVerticalEdges();
        // 3 boundaries (0, 1, 2) * 2 rows = 6 vertical edge segments
        Assert.Equal(6, vEdges.Count);

        // Boundary 1, Row 0 (shared edge between merged and right0)
        var vMidRow0 = vEdges.First(e => e.BoundaryIndex == 1 && e.SlotIndex == 0);
        Assert.True(vMidRow0.IsSharedEdge);
        Assert.False(vMidRow0.IsInternalSeam);
        Assert.Same(merged, vMidRow0.PrimaryCell);
        Assert.Same(right0, vMidRow0.NeighborCell);

        // Boundary 1, Row 1 (shared edge between merged and right1)
        var vMidRow1 = vEdges.First(e => e.BoundaryIndex == 1 && e.SlotIndex == 1);
        Assert.True(vMidRow1.IsSharedEdge);
        Assert.False(vMidRow1.IsInternalSeam);
        Assert.Same(merged, vMidRow1.PrimaryCell);
        Assert.Same(right1, vMidRow1.NeighborCell);
    }

    [Fact]
    public void DocumentTableTopologyAdapter_FromTable_And_FromGrid()
    {
        var c00 = new TableCell(0, 1, columnIndex: 0, columnSpan: 2, rowSpan: 1);
        var c02 = new TableCell(1, 1, columnIndex: 2, columnSpan: 1, rowSpan: 2);
        var c10 = new TableCell(2, 1, columnIndex: 0, columnSpan: 1, rowSpan: 1);
        var c11 = new TableCell(3, 1, columnIndex: 1, columnSpan: 1, rowSpan: 1);

        var row0 = new TableRow([c00, c02]);
        var row1 = new TableRow([c10, c11]);
        var table = new DocumentTable(0, 4, [row0, row1], [50, 50, 50]);

        var mapFromTable = DocumentTableTopologyAdapter.FromTable(table);
        Assert.Equal(2, mapFromTable.RowCount);
        Assert.Equal(3, mapFromTable.ColumnCount);
        Assert.Equal(4, mapFromTable.CellCount);

        var slot00 = mapFromTable.GetSlot(0, 0);
        Assert.True(slot00.IsRoot);
        Assert.Equal(2, slot00.ColumnSpan);

        var slot01 = mapFromTable.GetSlot(0, 1);
        Assert.True(slot01.IsContinuation);
        Assert.Same(c00, slot01.RootCell);

        var slot12 = mapFromTable.GetSlot(1, 2);
        Assert.True(slot12.IsContinuation);
        Assert.Same(c02, slot12.RootCell);

        // Also test TableGrid integration
        var grid = TableGrid.FromTable(table);
        var mapFromGrid = grid.ToTopologyMap();

        Assert.Equal(mapFromTable.RowCount, mapFromGrid.RowCount);
        Assert.Equal(mapFromTable.ColumnCount, mapFromGrid.ColumnCount);
        Assert.Equal(mapFromTable.CellCount, mapFromGrid.CellCount);

        for (int r = 0; r < mapFromTable.RowCount; r++)
        {
            for (int c = 0; c < mapFromTable.ColumnCount; c++)
            {
                var s1 = mapFromTable.GetSlot(r, c);
                var s2 = mapFromGrid.GetSlot(r, c);
                Assert.Same(s1.RootCell, s2.RootCell);
                Assert.Equal(s1.IsRoot, s2.IsRoot);
                Assert.Equal(s1.IsContinuation, s2.IsContinuation);
                Assert.Equal(s1.RowSpan, s2.RowSpan);
                Assert.Equal(s1.ColumnSpan, s2.ColumnSpan);
            }
        }
    }

    [Fact]
    public void CssTableTopologyAdapter_FromRows_And_SpacingBoxes()
    {
        var cellA = new TestCell("CellA_RowSpan2");
        var cellB = new TestCell("CellB_ColSpan2");
        var cellC = new TestCell("CellC_Normal");

        // Row 0: cellA (rowSpan 2, colSpan 1), cellB (rowSpan 1, colSpan 2)
        // Row 1: SpacingBox placeholder for cellA, cellC (normal 1x1)
        var row0 = new List<CssCellDescriptor<TestCell>>
        {
            CssCellDescriptor<TestCell>.ForCell(cellA, rowSpan: 2, columnSpan: 1),
            CssCellDescriptor<TestCell>.ForCell(cellB, rowSpan: 1, columnSpan: 2)
        };

        var row1 = new List<CssCellDescriptor<TestCell>>
        {
            CssCellDescriptor<TestCell>.ForSpacingBox(cellA),
            CssCellDescriptor<TestCell>.ForCell(cellC, rowSpan: 1, columnSpan: 1)
        };

        var map = CssTableTopologyAdapter.FromRows([row0, row1]);

        Assert.Equal(2, map.RowCount);
        Assert.Equal(3, map.ColumnCount);
        Assert.Equal(3, map.CellCount);

        // Slot (0, 0) is root of cellA
        var s00 = map.GetSlot(0, 0);
        Assert.True(s00.IsRoot);
        Assert.Same(cellA, s00.RootCell);

        // Slot (1, 0) is continuation of cellA
        var s10 = map.GetSlot(1, 0);
        Assert.True(s10.IsContinuation);
        Assert.Same(cellA, s10.RootCell);

        // Slot (0, 1) is root of cellB
        var s01 = map.GetSlot(0, 1);
        Assert.True(s01.IsRoot);
        Assert.Same(cellB, s01.RootCell);

        // Slot (0, 2) is continuation of cellB
        var s02 = map.GetSlot(0, 2);
        Assert.True(s02.IsContinuation);
        Assert.Same(cellB, s02.RootCell);

        // Slot (1, 1) is root of cellC
        var s11 = map.GetSlot(1, 1);
        Assert.True(s11.IsRoot);
        Assert.Same(cellC, s11.RootCell);

        // Slot (1, 2) is empty
        var s12 = map.GetSlot(1, 2);
        Assert.True(s12.IsEmpty);
    }

    [Fact]
    public void CrossAdapter_Equivalence_Documents_And_CSS()
    {
        // Define a 3x3 table:
        // (0, 0): 2x1 merge
        // (0, 1): 1x2 merge
        // (1, 1): 1x1 cell
        // (1, 2): 1x1 cell
        // (2, 0): 1x1 cell
        // (2, 1): 1x1 cell
        // (2, 2): 1x1 cell

        // 1. Build via Documents
        var d00 = new TableCell(0, 1, columnIndex: 0, columnSpan: 1, rowSpan: 2);
        var d01 = new TableCell(1, 1, columnIndex: 1, columnSpan: 2, rowSpan: 1);
        var d11 = new TableCell(2, 1, columnIndex: 1, columnSpan: 1, rowSpan: 1);
        var d12 = new TableCell(3, 1, columnIndex: 2, columnSpan: 1, rowSpan: 1);
        var d20 = new TableCell(4, 1, columnIndex: 0, columnSpan: 1, rowSpan: 1);
        var d21 = new TableCell(5, 1, columnIndex: 1, columnSpan: 1, rowSpan: 1);
        var d22 = new TableCell(6, 1, columnIndex: 2, columnSpan: 1, rowSpan: 1);

        var docTable = new DocumentTable(
            0,
            7,
            [
                new TableRow([d00, d01]),
                new TableRow([d11, d12]),
                new TableRow([d20, d21, d22])
            ],
            [100, 100, 100]);

        var docMap = DocumentTableTopologyAdapter.FromTable(docTable);

        // 2. Build via CSS adapter
        var c00 = new TestCell("c00");
        var c01 = new TestCell("c01");
        var c11 = new TestCell("c11");
        var c12 = new TestCell("c12");
        var c20 = new TestCell("c20");
        var c21 = new TestCell("c21");
        var c22 = new TestCell("c22");

        var cssRows = new List<List<CssCellDescriptor<TestCell>>>
        {
            new() { CssCellDescriptor<TestCell>.ForCell(c00, rowSpan: 2, columnSpan: 1), CssCellDescriptor<TestCell>.ForCell(c01, rowSpan: 1, columnSpan: 2) },
            new() { CssCellDescriptor<TestCell>.ForCell(c11, rowSpan: 1, columnSpan: 1), CssCellDescriptor<TestCell>.ForCell(c12, rowSpan: 1, columnSpan: 1) },
            new() { CssCellDescriptor<TestCell>.ForCell(c20, rowSpan: 1, columnSpan: 1), CssCellDescriptor<TestCell>.ForCell(c21, rowSpan: 1, columnSpan: 1), CssCellDescriptor<TestCell>.ForCell(c22, rowSpan: 1, columnSpan: 1) }
        };

        var cssMap = CssTableTopologyAdapter.FromRows(cssRows);

        // 3. Compare top-level metrics
        Assert.Equal(docMap.RowCount, cssMap.RowCount);
        Assert.Equal(docMap.ColumnCount, cssMap.ColumnCount);
        Assert.Equal(docMap.CellCount, cssMap.CellCount);

        // 4. Compare all 3x3 slots
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                var ds = docMap.GetSlot(r, c);
                var cs = cssMap.GetSlot(r, c);

                Assert.Equal(ds.IsOccupied, cs.IsOccupied);
                Assert.Equal(ds.IsEmpty, cs.IsEmpty);
                Assert.Equal(ds.IsRoot, cs.IsRoot);
                Assert.Equal(ds.IsContinuation, cs.IsContinuation);
                Assert.Equal(ds.RowSpan, cs.RowSpan);
                Assert.Equal(ds.ColumnSpan, cs.ColumnSpan);
                Assert.Equal(ds.RootRow, cs.RootRow);
                Assert.Equal(ds.RootColumn, cs.RootColumn);
                Assert.Equal(ds.RowOffset, cs.RowOffset);
                Assert.Equal(ds.ColumnOffset, cs.ColumnOffset);
                Assert.Equal(ds.IsTopEdge, cs.IsTopEdge);
                Assert.Equal(ds.IsBottomEdge, cs.IsBottomEdge);
                Assert.Equal(ds.IsLeftEdge, cs.IsLeftEdge);
                Assert.Equal(ds.IsRightEdge, cs.IsRightEdge);
            }
        }

        // 5. Compare edge enumeration agreement
        var docHEdges = docMap.EnumerateHorizontalEdges();
        var cssHEdges = cssMap.EnumerateHorizontalEdges();
        Assert.Equal(docHEdges.Count, cssHEdges.Count);

        for (int i = 0; i < docHEdges.Count; i++)
        {
            Assert.Equal(docHEdges[i].BoundaryIndex, cssHEdges[i].BoundaryIndex);
            Assert.Equal(docHEdges[i].SlotIndex, cssHEdges[i].SlotIndex);
            Assert.Equal(docHEdges[i].IsInternalSeam, cssHEdges[i].IsInternalSeam);
            Assert.Equal(docHEdges[i].IsPerimeter, cssHEdges[i].IsPerimeter);
            Assert.Equal(docHEdges[i].IsSharedEdge, cssHEdges[i].IsSharedEdge);
            Assert.Equal(docHEdges[i].IsOuterBoundary, cssHEdges[i].IsOuterBoundary);
        }

        var docVEdges = docMap.EnumerateVerticalEdges();
        var cssVEdges = cssMap.EnumerateVerticalEdges();
        Assert.Equal(docVEdges.Count, cssVEdges.Count);

        for (int i = 0; i < docVEdges.Count; i++)
        {
            Assert.Equal(docVEdges[i].BoundaryIndex, cssVEdges[i].BoundaryIndex);
            Assert.Equal(docVEdges[i].SlotIndex, cssVEdges[i].SlotIndex);
            Assert.Equal(docVEdges[i].IsInternalSeam, cssVEdges[i].IsInternalSeam);
            Assert.Equal(docVEdges[i].IsPerimeter, cssVEdges[i].IsPerimeter);
            Assert.Equal(docVEdges[i].IsSharedEdge, cssVEdges[i].IsSharedEdge);
            Assert.Equal(docVEdges[i].IsOuterBoundary, cssVEdges[i].IsOuterBoundary);
        }
    }

    [Fact]
    public void CssTableTopologyAdapter_RTL_TieBreak_Arbitration()
    {
        var left = new TestCell("LeftCell");
        var right = new TestCell("RightCell");

        var map = TableTopologyMap<TestCell>.Build(1, 2, [
            (0, 0, 1, 1, left),
            (0, 1, 1, 1, right)
        ]);

        // LTR seams
        var ltrSeams = CssTableTopologyAdapter.EnumerateCollapsibleSeams(map, isRtl: false);
        Assert.Single(ltrSeams);
        var ltrSeam = ltrSeams[0];
        Assert.Equal(TableEdgeOrientation.Vertical, ltrSeam.Orientation);
        Assert.Same(left, ltrSeam.PrimaryCell);
        Assert.Same(right, ltrSeam.NeighborCell);
        Assert.Same(left, ltrSeam.TieWinnerCell);
        Assert.Same(right, ltrSeam.TieLoserCell);

        // RTL seams
        var rtlSeams = CssTableTopologyAdapter.EnumerateCollapsibleSeams(map, isRtl: true);
        Assert.Single(rtlSeams);
        var rtlSeam = rtlSeams[0];
        Assert.Equal(TableEdgeOrientation.Vertical, rtlSeam.Orientation);
        Assert.Same(left, rtlSeam.PrimaryCell);
        Assert.Same(right, rtlSeam.NeighborCell);
        Assert.Same(right, rtlSeam.TieWinnerCell); // In RTL, right cell wins ties!
        Assert.Same(left, rtlSeam.TieLoserCell);
    }
}
