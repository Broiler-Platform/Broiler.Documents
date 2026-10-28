using System;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.Documents.Model.Tables;

/// <summary>
/// Engine-neutral, normalized 2D table topology map.
/// Manages slot occupancy, merged span tracking, overlap normalization, and neighboring edge enumeration.
/// </summary>
public sealed class TableTopologyMap<TCell> where TCell : class
{
    private readonly int _rowCount;
    private readonly int _columnCount;
    private readonly Dictionary<long, TopologySlot<TCell>> _sparseSlots;
    private readonly TopologySlot<TCell>[]? _flatCache;
    private readonly List<TCell> _distinctCells;
    private readonly Dictionary<TCell, (int StartRow, int StartColumn, int RowSpan, int ColumnSpan)> _cellRanges;

    private TableTopologyMap(
        int rowCount,
        int columnCount,
        Dictionary<long, TopologySlot<TCell>> sparseSlots,
        TopologySlot<TCell>[]? flatCache,
        List<TCell> distinctCells,
        Dictionary<TCell, (int StartRow, int StartColumn, int RowSpan, int ColumnSpan)> cellRanges)
    {
        _rowCount = rowCount;
        _columnCount = columnCount;
        _sparseSlots = sparseSlots;
        _flatCache = flatCache;
        _distinctCells = distinctCells;
        _cellRanges = cellRanges;
    }

    /// <summary>Total number of grid rows.</summary>
    public int RowCount => _rowCount;

    /// <summary>Total number of grid columns.</summary>
    public int ColumnCount => _columnCount;

    /// <summary>Total number of distinct root cells placed in this topology map.</summary>
    public int CellCount => _distinctCells.Count;

    /// <summary>Distinct cells in the map in row-major order.</summary>
    public IReadOnlyList<TCell> DistinctCells => _distinctCells;

    /// <summary>All occupied slots in row-major order.</summary>
    public IReadOnlyList<TopologySlot<TCell>> OccupiedSlots =>
        _sparseSlots.Values
            .Where(s => s.IsOccupied)
            .OrderBy(s => s.Row)
            .ThenBy(s => s.Column)
            .ToList();

    /// <summary>Gets the slot information at (row, column).</summary>
    public TopologySlot<TCell> GetSlot(int row, int column)
    {
        if ((uint)row >= (uint)_rowCount || (uint)column >= (uint)_columnCount)
            return TopologySlot<TCell>.Empty(row, column);

        if (_flatCache is not null)
            return _flatCache[row * _columnCount + column];

        if (_sparseSlots.TryGetValue(Key(row, column), out var slot))
            return slot;

        return TopologySlot<TCell>.Empty(row, column);
    }

    /// <summary>Gets the cell covering (row, column), or null if empty or out of bounds.</summary>
    public TCell? GetCellAt(int row, int column) =>
        GetSlot(row, column).RootCell;

    /// <summary>Gets the merged root cell and its top-left coordinate (RootRow, RootColumn).</summary>
    public (TCell? RootCell, int RootRow, int RootColumn) GetMergedRoot(int row, int column)
    {
        var slot = GetSlot(row, column);
        if (slot.IsEmpty)
            return (null, -1, -1);
        return (slot.RootCell, slot.RootRow, slot.RootColumn);
    }

    /// <summary>True if the slot at (row, column) is empty.</summary>
    public bool IsEmpty(int row, int column) =>
        GetSlot(row, column).IsEmpty;

    /// <summary>True if the cell covering (row, column) has RowSpan &gt; 1 or ColumnSpan &gt; 1.</summary>
    public bool IsMerged(int row, int column)
    {
        var slot = GetSlot(row, column);
        return slot.IsOccupied && (slot.RowSpan > 1 || slot.ColumnSpan > 1);
    }

    /// <summary>True if (row, column) is a continuation slot covered by a merge originating elsewhere.</summary>
    public bool IsContinuation(int row, int column) =>
        GetSlot(row, column).IsContinuation;

    /// <summary>Gets the range of coordinates covered by the cell.</summary>
    public (int StartRow, int StartColumn, int RowSpan, int ColumnSpan)? GetCellRange(TCell cell)
    {
        if (cell is not null && _cellRanges.TryGetValue(cell, out var range))
            return range;
        return null;
    }

    /// <summary>Returns all distinct cells intersecting the specified row.</summary>
    public IReadOnlyList<TCell> GetRowCells(int row)
    {
        if ((uint)row >= (uint)_rowCount)
            return [];

        var seen = new HashSet<TCell>(ReferenceEqualityComparer.Instance);
        var result = new List<TCell>();
        for (int c = 0; c < _columnCount; c++)
        {
            var cell = GetCellAt(row, c);
            if (cell is not null && seen.Add(cell))
                result.Add(cell);
        }
        return result;
    }

    /// <summary>Returns all distinct cells intersecting the specified column.</summary>
    public IReadOnlyList<TCell> GetColumnCells(int column)
    {
        if ((uint)column >= (uint)_columnCount)
            return [];

        var seen = new HashSet<TCell>(ReferenceEqualityComparer.Instance);
        var result = new List<TCell>();
        for (int r = 0; r < _rowCount; r++)
        {
            var cell = GetCellAt(r, column);
            if (cell is not null && seen.Add(cell))
                result.Add(cell);
        }
        return result;
    }

    /// <summary>
    /// Returns the neighboring cells immediately adjacent to slot (row, column)
    /// in the 4 cardinal directions (Top, Right, Bottom, Left).
    /// </summary>
    public (TCell? Top, TCell? Right, TCell? Bottom, TCell? Left) GetAdjacentCells(int row, int column)
    {
        TCell? top = row > 0 ? GetCellAt(row - 1, column) : null;
        TCell? right = column + 1 < _columnCount ? GetCellAt(row, column + 1) : null;
        TCell? bottom = row + 1 < _rowCount ? GetCellAt(row + 1, column) : null;
        TCell? left = column > 0 ? GetCellAt(row, column - 1) : null;
        return (top, right, bottom, left);
    }

    #region Edge Enumeration

    /// <summary>
    /// Enumerates all horizontal edge segments separating rows (from row 0 top perimeter through RowCount bottom perimeter).
    /// </summary>
    public List<TopologyEdgeSegment<TCell>> EnumerateHorizontalEdges()
    {
        var result = new List<TopologyEdgeSegment<TCell>>((_rowCount + 1) * _columnCount);

        for (int r = 0; r <= _rowCount; r++)
        {
            for (int c = 0; c < _columnCount; c++)
            {
                TCell? primary = r > 0 ? GetCellAt(r - 1, c) : null;
                TCell? neighbor = r < _rowCount ? GetCellAt(r, c) : null;

                result.Add(new TopologyEdgeSegment<TCell>(
                    TableEdgeOrientation.Horizontal,
                    boundaryIndex: r,
                    slotIndex: c,
                    primaryCell: primary,
                    neighborCell: neighbor,
                    isOuterBoundary: r == 0 || r == _rowCount));
            }
        }

        return result;
    }

    /// <summary>
    /// Enumerates all vertical edge segments separating columns (from column 0 left perimeter through ColumnCount right perimeter).
    /// </summary>
    public List<TopologyEdgeSegment<TCell>> EnumerateVerticalEdges()
    {
        var result = new List<TopologyEdgeSegment<TCell>>(_rowCount * (_columnCount + 1));

        for (int c = 0; c <= _columnCount; c++)
        {
            for (int r = 0; r < _rowCount; r++)
            {
                TCell? primary = c > 0 ? GetCellAt(r, c - 1) : null;
                TCell? neighbor = c < _columnCount ? GetCellAt(r, c) : null;

                result.Add(new TopologyEdgeSegment<TCell>(
                    TableEdgeOrientation.Vertical,
                    boundaryIndex: c,
                    slotIndex: r,
                    primaryCell: primary,
                    neighborCell: neighbor,
                    isOuterBoundary: c == 0 || c == _columnCount));
            }
        }

        return result;
    }

    /// <summary>
    /// Enumerates all edge segments (both horizontal and vertical) in the grid.
    /// </summary>
    public List<TopologyEdgeSegment<TCell>> EnumerateAllEdges()
    {
        var h = EnumerateHorizontalEdges();
        var v = EnumerateVerticalEdges();
        var all = new List<TopologyEdgeSegment<TCell>>(h.Count + v.Count);
        all.AddRange(h);
        all.AddRange(v);
        return all;
    }

    #endregion

    #region Factory & Normalization

    /// <summary>
    /// Creates an empty topology map of the given dimensions.
    /// </summary>
    public static TableTopologyMap<TCell> CreateEmpty(int rowCount = 0, int columnCount = 0)
    {
        var (rows, cols) = TableTopologyLimits.ClampDimensions(rowCount, columnCount);
        return new TableTopologyMap<TCell>(
            rows,
            cols,
            sparseSlots: [],
            flatCache: null,
            distinctCells: [],
            cellRanges: new Dictionary<TCell, (int, int, int, int)>(ReferenceEqualityComparer.Instance));
    }

    /// <summary>
    /// Builds a normalized topology map from cell placements.
    /// Clamps spans, resolves overlaps by pushing conflicting cells rightward,
    /// and bounds sparse allocations against adversarial limits.
    /// </summary>
    public static TableTopologyMap<TCell> Build(
        int statedRowCount,
        int statedColumnCount,
        IEnumerable<(int Row, int Column, int RowSpan, int ColumnSpan, TCell Cell)> cellPlacements)
    {
        var (rows, cols) = TableTopologyLimits.ClampDimensions(statedRowCount, statedColumnCount);

        var sparseSlots = new Dictionary<long, TopologySlot<TCell>>();
        var cellRanges = new Dictionary<TCell, (int StartRow, int StartColumn, int RowSpan, int ColumnSpan)>(ReferenceEqualityComparer.Instance);
        var distinctCells = new List<TCell>();

        // Track occupied slots to resolve overlaps
        HashSet<long> occupied = [];

        foreach (var placement in cellPlacements)
        {
            if (placement.Cell is null)
                continue;

            TCell cell = placement.Cell;
            int r = Math.Clamp(placement.Row, 0, TableTopologyLimits.MaxRows - 1);
            int c = Math.Clamp(placement.Column, 0, TableTopologyLimits.MaxColumns - 1);
            var (rs, cs) = TableTopologyLimits.ClampSpans(placement.RowSpan, placement.ColumnSpan);

            // Overlap resolution: if starting slot is already occupied, advance column to first free slot in this row
            while (occupied.Contains(Key(r, c)) && c < TableTopologyLimits.MaxColumns - 1)
            {
                c++;
            }

            // Bound effective coverage
            int effectiveRowEnd = Math.Min(r + rs, TableTopologyLimits.MaxRows);
            int effectiveColEnd = Math.Min(c + cs, TableTopologyLimits.MaxColumns);
            int finalRs = Math.Max(1, effectiveRowEnd - r);
            int finalCs = Math.Max(1, effectiveColEnd - c);

            var (boundedRs, boundedCs) = TableTopologyLimits.ClampSpans(finalRs, finalCs);
            finalRs = boundedRs;
            finalCs = boundedCs;

            rows = Math.Max(rows, r + finalRs);
            cols = Math.Max(cols, c + finalCs);
            var (clampedRows, clampedCols) = TableTopologyLimits.ClampDimensions(rows, cols);
            rows = clampedRows;
            cols = clampedCols;

            distinctCells.Add(cell);
            cellRanges[cell] = (r, c, finalRs, finalCs);

            for (int ro = 0; ro < finalRs; ro++)
            {
                if (sparseSlots.Count >= TableTopologyLimits.MaxSlots)
                    break;

                for (int co = 0; co < finalCs; co++)
                {
                    if (sparseSlots.Count >= TableTopologyLimits.MaxSlots)
                        break;

                    long k = Key(r + ro, c + co);
                    // First occupant retains slot; subsequent overlaps ignored
                    if (occupied.Add(k))
                    {
                        sparseSlots[k] = new TopologySlot<TCell>(
                            r + ro,
                            c + co,
                            rootCell: cell,
                            rootRow: r,
                            rootColumn: c,
                            rowSpan: finalRs,
                            columnSpan: finalCs);
                    }
                }
            }
        }

        // Build flat cache if grid is within threshold
        TopologySlot<TCell>[]? flatCache = null;
        long totalSlots = (long)rows * cols;
        if (totalSlots > 0 && totalSlots <= TableTopologyLimits.MaxFlatCacheSlots)
        {
            flatCache = new TopologySlot<TCell>[rows * cols];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    flatCache[r * cols + c] = sparseSlots.TryGetValue(Key(r, c), out var slot)
                        ? slot
                        : TopologySlot<TCell>.Empty(r, c);
                }
            }
        }

        return new TableTopologyMap<TCell>(rows, cols, sparseSlots, flatCache, distinctCells, cellRanges);
    }

    private static long Key(int row, int column) =>
        ((long)row << 32) | (uint)column;

    #endregion
}
