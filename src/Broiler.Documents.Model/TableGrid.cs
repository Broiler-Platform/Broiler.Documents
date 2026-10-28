using System;
using System.Collections.Generic;
using System.Linq;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Model;

/// <summary>
/// A rectangular range of cells within a <see cref="TableGrid"/>.
/// </summary>
public readonly record struct TableCellRange(int StartRow, int StartColumn, int RowSpan, int ColumnSpan)
{
    public int EndRow => StartRow + RowSpan;
    public int EndColumn => StartColumn + ColumnSpan;

    public bool Contains(int row, int column) =>
        row >= StartRow && row < EndRow && column >= StartColumn && column < EndColumn;

    public bool Intersects(TableCellRange other) =>
        StartRow < other.EndRow && EndRow > other.StartRow &&
        StartColumn < other.EndColumn && EndColumn > other.StartColumn;
}

/// <summary>
/// State and mapping of a single (row, column) coordinate within a <see cref="TableGrid"/>.
/// </summary>
public sealed class TableGridSlot
{
    internal TableGridSlot(int row, int column, TableCell? rootCell, int rootRow, int rootColumn, TableCell? cell = null)
    {
        Row = row;
        Column = column;
        RootCell = rootCell;
        RootRow = rootRow;
        RootColumn = rootColumn;
        Cell = cell ?? rootCell;
    }

    public int Row { get; }
    public int Column { get; }

    /// <summary>The cell object directly placed in this row/column slot, or null if empty or virtual continuation.</summary>
    public TableCell? Cell { get; }

    /// <summary>The root cell that anchors this slot's content and merge extent, or null if empty.</summary>
    public TableCell? RootCell { get; }

    /// <summary>The row coordinate where the root cell originated.</summary>
    public int RootRow { get; }

    /// <summary>The column coordinate where the root cell originated.</summary>
    public int RootColumn { get; }

    public bool IsEmpty => RootCell is null;
    public bool IsOccupied => RootCell is not null;
    public bool IsRoot => IsOccupied && Row == RootRow && Column == RootColumn;
    public bool IsContinuation => IsOccupied && !IsRoot;

    public int RowSpan => RootCell?.RowSpan ?? 1;
    public int ColumnSpan => RootCell?.ColumnSpan ?? 1;

    public int RowOffset => IsOccupied ? Row - RootRow : 0;
    public int ColumnOffset => IsOccupied ? Column - RootColumn : 0;

    public bool IsTopEdge => !IsOccupied || Row == RootRow;
    public bool IsBottomEdge => !IsOccupied || Row == RootRow + RowSpan - 1;
    public bool IsLeftEdge => !IsOccupied || Column == RootColumn;
    public bool IsRightEdge => !IsOccupied || Column == RootColumn + ColumnSpan - 1;
}

/// <summary>
/// Represents an immutable 2D grid transformation and sparse matrix coordinate engine for tabular layouts.
/// Provides O(1) coordinate lookups, merged cell tracking, border collapse arbitration, range extraction,
/// and geometric grid transformations (transpose, horizontal/vertical flip, row/column insertion/deletion).
/// </summary>
public sealed class TableGrid
{
    private const int MaxFlatCacheSize = 250_000;

    internal readonly record struct TableCellEntry(int Row, int Column, TableCell Cell);

    private readonly int _rowCount;
    private readonly int _columnCount;
    private readonly IReadOnlyList<double> _columnWidths;
    private readonly Dictionary<long, TableGridSlot> _sparseSlots;
    private readonly TableGridSlot?[]? _flatCache;
    private readonly IReadOnlyList<TableCellEntry> _entries;
    private readonly Dictionary<TableCell, TableCellRange> _cellRanges;
    private readonly IReadOnlyList<TableCell> _distinctCells;

    private TableGrid(
        int rowCount,
        int columnCount,
        IReadOnlyList<double> columnWidths,
        Dictionary<long, TableGridSlot> sparseSlots,
        TableGridSlot?[]? flatCache,
        IReadOnlyList<TableCellEntry> entries,
        Dictionary<TableCell, TableCellRange> cellRanges,
        IReadOnlyList<TableCell> distinctCells)
    {
        _rowCount = rowCount;
        _columnCount = columnCount;
        _columnWidths = columnWidths;
        _sparseSlots = sparseSlots;
        _flatCache = flatCache;
        _entries = entries;
        _cellRanges = cellRanges;
        _distinctCells = distinctCells;
    }

    /// <summary>Total number of grid rows.</summary>
    public int RowCount => _rowCount;

    /// <summary>Total number of grid columns.</summary>
    public int ColumnCount => _columnCount;

    /// <summary>Total number of distinct root cells placed in this grid.</summary>
    public int CellCount => _distinctCells.Count;

    /// <summary>The distinct root cells in this grid in row-major order.</summary>
    public IReadOnlyList<TableCell> Cells => _distinctCells;

    /// <summary>The stated column widths in points, or empty if none stated.</summary>
    public IReadOnlyList<double> ColumnWidths => _columnWidths;

    /// <summary>Sum of all positive column widths in points.</summary>
    public double TotalWidth
    {
        get
        {
            double sum = 0;
            foreach (double w in _columnWidths)
                sum += Math.Max(0, w);
            return sum;
        }
    }

    /// <summary>Enumerates all occupied 2D grid slots in row-major order.</summary>
    public IReadOnlyList<TableGridSlot> NonEmptySlots =>
        _sparseSlots.Values
            .Where(slot => slot.IsOccupied)
            .OrderBy(slot => slot.Row)
            .ThenBy(slot => slot.Column)
            .ToList();

    /// <summary>Indexed access to the cell covering (row, column).</summary>
    public TableCell? this[int row, int column] => GetCellAt(row, column);

    /// <summary>
    /// Returns the <see cref="TableCell"/> occupying the specified (row, column) coordinate,
    /// or null if the slot is empty or out of grid bounds.
    /// </summary>
    public TableCell? GetCellAt(int row, int column)
    {
        if ((uint)row >= (uint)_rowCount || (uint)column >= (uint)_columnCount)
            return null;

        if (_flatCache is not null)
            return _flatCache[row * _columnCount + column]?.RootCell;

        if (_sparseSlots.TryGetValue(Key(row, column), out TableGridSlot? slot))
            return slot.RootCell;

        return null;
    }

    /// <summary>
    /// Returns the merged root cell and its top-left root coordinate (RootRow, RootColumn)
    /// covering (row, column). Returns (null, -1, -1) if empty or out of bounds.
    /// </summary>
    public (TableCell? RootCell, int RootRow, int RootColumn) GetMergedRoot(int row, int column)
    {
        TableGridSlot slot = GetSlot(row, column);
        if (slot.IsEmpty)
            return (null, -1, -1);
        return (slot.RootCell, slot.RootRow, slot.RootColumn);
    }

    /// <summary>
    /// Returns detailed slot information for the given coordinate.
    /// </summary>
    public TableGridSlot GetSlot(int row, int column)
    {
        if ((uint)row >= (uint)_rowCount || (uint)column >= (uint)_columnCount)
            return new TableGridSlot(row, column, null, -1, -1);

        if (_flatCache is not null)
            return _flatCache[row * _columnCount + column] ?? new TableGridSlot(row, column, null, -1, -1);

        if (_sparseSlots.TryGetValue(Key(row, column), out TableGridSlot? slot))
            return slot;

        return new TableGridSlot(row, column, null, -1, -1);
    }

    /// <summary>
    /// Returns the <see cref="TableCellRange"/> of the given cell, or null if the cell is not in this grid.
    /// </summary>
    public TableCellRange? GetCellRange(TableCell cell)
    {
        if (cell is not null && _cellRanges.TryGetValue(cell, out TableCellRange range))
            return range;
        return null;
    }

    /// <summary>
    /// Returns the <see cref="TableCellRange"/> of the cell covering (row, column), or null if empty or out of bounds.
    /// </summary>
    public TableCellRange? GetCellRange(int row, int column)
    {
        TableGridSlot slot = GetSlot(row, column);
        if (slot.IsEmpty || slot.RootCell is null)
            return null;
        return GetCellRange(slot.RootCell);
    }

    /// <summary>True if the cell covering (row, column) has RowSpan > 1 or ColumnSpan > 1.</summary>
    public bool IsMerged(int row, int column)
    {
        TableGridSlot slot = GetSlot(row, column);
        return slot.IsOccupied && (slot.RowSpan > 1 || slot.ColumnSpan > 1);
    }

    /// <summary>
    /// Converts this grid into an engine-neutral <see cref="Broiler.Documents.Model.Tables.TableTopologyMap{TableCell}"/>.
    /// </summary>
    public Broiler.Documents.Model.Tables.TableTopologyMap<TableCell> ToTopologyMap() =>
        Broiler.Documents.Model.Tables.DocumentTableTopologyAdapter.FromGrid(this);

    /// <summary>True if (row, column) is covered by a merge originating at a different coordinate.</summary>
    public bool IsCoveredContinuation(int row, int column)
    {
        TableGridSlot slot = GetSlot(row, column);
        return slot.IsContinuation;
    }

    /// <summary>True if (row, column) is within bounds and has no cell occupying it.</summary>
    public bool IsEmpty(int row, int column)
    {
        return GetSlot(row, column).IsEmpty;
    }

    /// <summary>
    /// Computes the effective collapsed borders at coordinate (row, column),
    /// arbitrating shared edges against neighboring cells and eliminating internal merge seams.
    /// </summary>
    public CellBorders GetEffectiveBorders(int row, int column)
    {
        TableGridSlot slot = GetSlot(row, column);
        if (slot.IsEmpty || slot.RootCell is null)
            return default;

        TableCell cell = slot.RootCell;
        int r0 = slot.RootRow;
        int c0 = slot.RootColumn;
        int r1 = r0 + cell.RowSpan;
        int c1 = c0 + cell.ColumnSpan;

        // Top edge:
        TableBorder topBorder;
        if (row > r0)
        {
            topBorder = TableBorder.None;
        }
        else
        {
            TableBorder ownTop = cell.Borders.Top;
            TableBorder neighborBottom = TableBorder.None;
            if (row > 0)
            {
                TableGridSlot neighborAbove = GetSlot(row - 1, column);
                if (neighborAbove.IsOccupied && neighborAbove.RootCell != cell)
                {
                    if (neighborAbove.IsBottomEdge)
                        neighborBottom = neighborAbove.RootCell!.Borders.Bottom;
                }
            }
            topBorder = TableBorder.Arbitrate(ownTop, neighborBottom);
        }

        // Bottom edge:
        TableBorder bottomBorder;
        if (row < r1 - 1)
        {
            bottomBorder = TableBorder.None;
        }
        else
        {
            TableBorder ownBottom = cell.Borders.Bottom;
            TableBorder neighborTop = TableBorder.None;
            if (row + 1 < _rowCount)
            {
                TableGridSlot neighborBelow = GetSlot(row + 1, column);
                if (neighborBelow.IsOccupied && neighborBelow.RootCell != cell)
                {
                    if (neighborBelow.IsTopEdge)
                        neighborTop = neighborBelow.RootCell!.Borders.Top;
                }
            }
            bottomBorder = TableBorder.Arbitrate(ownBottom, neighborTop);
        }

        // Left edge:
        TableBorder leftBorder;
        if (column > c0)
        {
            leftBorder = TableBorder.None;
        }
        else
        {
            TableBorder ownLeft = cell.Borders.Left;
            TableBorder neighborRight = TableBorder.None;
            if (column > 0)
            {
                TableGridSlot neighborLeft = GetSlot(row, column - 1);
                if (neighborLeft.IsOccupied && neighborLeft.RootCell != cell)
                {
                    if (neighborLeft.IsRightEdge)
                        neighborRight = neighborLeft.RootCell!.Borders.Right;
                }
            }
            leftBorder = TableBorder.Arbitrate(ownLeft, neighborRight);
        }

        // Right edge:
        TableBorder rightBorder;
        if (column < c1 - 1)
        {
            rightBorder = TableBorder.None;
        }
        else
        {
            TableBorder ownRight = cell.Borders.Right;
            TableBorder neighborLeft = TableBorder.None;
            if (column + 1 < _columnCount)
            {
                TableGridSlot neighborRightSlot = GetSlot(row, column + 1);
                if (neighborRightSlot.IsOccupied && neighborRightSlot.RootCell != cell)
                {
                    if (neighborRightSlot.IsLeftEdge)
                        neighborLeft = neighborRightSlot.RootCell!.Borders.Left;
                }
            }
            rightBorder = TableBorder.Arbitrate(ownRight, neighborLeft);
        }

        TableBorder diagDown = slot.IsRoot ? cell.Borders.DiagonalDown : TableBorder.None;
        TableBorder diagUp = slot.IsRoot ? cell.Borders.DiagonalUp : TableBorder.None;

        return new CellBorders(leftBorder, topBorder, rightBorder, bottomBorder, diagDown, diagUp);
    }

    /// <summary>
    /// Returns all distinct cells intersecting the specified row.
    /// </summary>
    public IReadOnlyList<TableCell> GetRowCells(int row)
    {
        if ((uint)row >= (uint)_rowCount)
            return [];

        var seen = new HashSet<TableCell>(ReferenceEqualityComparer.Instance);
        var result = new List<TableCell>();
        for (int c = 0; c < _columnCount; c++)
        {
            TableCell? cell = GetCellAt(row, c);
            if (cell is not null && seen.Add(cell))
                result.Add(cell);
        }
        return result;
    }

    /// <summary>
    /// Returns all distinct cells intersecting the specified column.
    /// </summary>
    public IReadOnlyList<TableCell> GetColumnCells(int column)
    {
        if ((uint)column >= (uint)_columnCount)
            return [];

        var seen = new HashSet<TableCell>(ReferenceEqualityComparer.Instance);
        var result = new List<TableCell>();
        for (int r = 0; r < _rowCount; r++)
        {
            TableCell? cell = GetCellAt(r, column);
            if (cell is not null && seen.Add(cell))
                result.Add(cell);
        }
        return result;
    }

    /// <summary>True if all slots in the row are empty.</summary>
    public bool IsRowEmpty(int row)
    {
        if ((uint)row >= (uint)_rowCount)
            return true;
        for (int c = 0; c < _columnCount; c++)
            if (GetCellAt(row, c) is not null)
                return false;
        return true;
    }

    /// <summary>True if all slots in the column are empty.</summary>
    public bool IsColumnEmpty(int column)
    {
        if ((uint)column >= (uint)_columnCount)
            return true;
        for (int r = 0; r < _rowCount; r++)
            if (GetCellAt(r, column) is not null)
                return false;
        return true;
    }

    /// <summary>
    /// Extracts a rectangular subgrid window [startRow .. startRow + rowCount - 1] x [startColumn .. startColumn + columnCount - 1].
    /// Cells partially clipped by the window boundary have their spans clamped to fit.
    /// </summary>
    public TableGrid SubGrid(int startRow, int startColumn, int rowCount, int columnCount)
    {
        if (rowCount <= 0 || columnCount <= 0)
            return Empty(Math.Max(0, rowCount), Math.Max(0, columnCount));

        int endRow = startRow + rowCount;
        int endColumn = startColumn + columnCount;

        var subEntries = new List<TableCellEntry>();
        var seen = new HashSet<TableCell>(ReferenceEqualityComparer.Instance);

        for (int r = startRow; r < endRow; r++)
        {
            for (int c = startColumn; c < endColumn; c++)
            {
                TableGridSlot slot = GetSlot(r, c);
                if (slot.IsEmpty || slot.RootCell is null)
                    continue;

                TableCell cell = slot.RootCell;
                if (!seen.Add(cell))
                    continue;

                int r0 = slot.RootRow;
                int c0 = slot.RootColumn;
                int r1 = r0 + cell.RowSpan;
                int c1 = c0 + cell.ColumnSpan;

                int subR0 = Math.Max(startRow, r0);
                int subR1 = Math.Min(endRow, r1);
                int subC0 = Math.Max(startColumn, c0);
                int subC1 = Math.Min(endColumn, c1);

                if (subR1 <= subR0 || subC1 <= subC0)
                    continue;

                int newRow = subR0 - startRow;
                int newCol = subC0 - startColumn;
                int newRowSpan = subR1 - subR0;
                int newColSpan = subC1 - subC0;

                TableBorder left = c0 >= startColumn ? cell.Borders.Left : TableBorder.None;
                TableBorder top = r0 >= startRow ? cell.Borders.Top : TableBorder.None;
                TableBorder right = c1 <= endColumn ? cell.Borders.Right : TableBorder.None;
                TableBorder bottom = r1 <= endRow ? cell.Borders.Bottom : TableBorder.None;

                var subCell = new TableCell(
                    cell.ParagraphIndex,
                    cell.ParagraphCount,
                    newCol,
                    newColSpan,
                    newRowSpan,
                    cell.Shading,
                    new CellBorders(left, top, right, bottom, cell.Borders.DiagonalDown, cell.Borders.DiagonalUp),
                    isRowSpanContinuation: false,
                    tables: cell.Tables,
                    padding: cell.Padding,
                    verticalAlignment: cell.VerticalAlignment);

                subEntries.Add(new TableCellEntry(newRow, newCol, subCell));
            }
        }

        var subWidths = _columnWidths.Count > 0
            ? _columnWidths.Skip(startColumn).Take(columnCount).ToList()
            : null;

        return Build(rowCount, columnCount, subWidths, subEntries);
    }

    /// <summary>
    /// Transposes the grid: rows become columns, columns become rows, and RowSpan/ColumnSpan swap.
    /// </summary>
    public TableGrid Transpose()
    {
        var transposedEntries = new List<TableCellEntry>(_entries.Count);
        foreach (TableCellEntry entry in _entries)
        {
            TableCell c = entry.Cell;
            var borders = new CellBorders(
                Left: c.Borders.Top,
                Top: c.Borders.Left,
                Right: c.Borders.Bottom,
                Bottom: c.Borders.Right,
                DiagonalDown: c.Borders.DiagonalDown,
                DiagonalUp: c.Borders.DiagonalUp);

            TableCellPadding? padding = c.Padding is { } p
                ? new TableCellPadding(p.Top, p.Left, p.Bottom, p.Right)
                : null;

            var newCell = new TableCell(
                c.ParagraphIndex,
                c.ParagraphCount,
                columnIndex: entry.Row,
                columnSpan: c.RowSpan,
                rowSpan: c.ColumnSpan,
                c.Shading,
                borders,
                isRowSpanContinuation: false,
                tables: c.Tables,
                padding: padding,
                verticalAlignment: c.VerticalAlignment);

            transposedEntries.Add(new TableCellEntry(entry.Column, entry.Row, newCell));
        }

        return Build(_columnCount, _rowCount, null, transposedEntries);
    }

    /// <summary>
    /// Mirrors the grid horizontally, reversing column order and swapping left/right borders.
    /// </summary>
    public TableGrid FlipHorizontal()
    {
        var flippedEntries = new List<TableCellEntry>(_entries.Count);
        foreach (TableCellEntry entry in _entries)
        {
            TableCell c = entry.Cell;
            int newCol = _columnCount - (entry.Column + c.ColumnSpan);

            var borders = new CellBorders(
                Left: c.Borders.Right,
                Top: c.Borders.Top,
                Right: c.Borders.Left,
                Bottom: c.Borders.Bottom,
                DiagonalDown: c.Borders.DiagonalUp,
                DiagonalUp: c.Borders.DiagonalDown);

            TableCellPadding? padding = c.Padding is { } p
                ? new TableCellPadding(p.Right, p.Top, p.Left, p.Bottom)
                : null;

            var newCell = new TableCell(
                c.ParagraphIndex,
                c.ParagraphCount,
                columnIndex: newCol,
                columnSpan: c.ColumnSpan,
                rowSpan: c.RowSpan,
                c.Shading,
                borders,
                isRowSpanContinuation: false,
                tables: c.Tables,
                padding: padding,
                verticalAlignment: c.VerticalAlignment);

            flippedEntries.Add(new TableCellEntry(entry.Row, newCol, newCell));
        }

        IReadOnlyList<double>? flippedWidths = _columnWidths.Count > 0 ? [.. _columnWidths.Reverse()] : null;
        return Build(_rowCount, _columnCount, flippedWidths, flippedEntries);
    }

    /// <summary>
    /// Mirrors the grid vertically, reversing row order and swapping top/bottom borders.
    /// </summary>
    public TableGrid FlipVertical()
    {
        var flippedEntries = new List<TableCellEntry>(_entries.Count);
        foreach (TableCellEntry entry in _entries)
        {
            TableCell c = entry.Cell;
            int newRow = _rowCount - (entry.Row + c.RowSpan);

            var borders = new CellBorders(
                Left: c.Borders.Left,
                Top: c.Borders.Bottom,
                Right: c.Borders.Right,
                Bottom: c.Borders.Top,
                DiagonalDown: c.Borders.DiagonalUp,
                DiagonalUp: c.Borders.DiagonalDown);

            TableCellPadding? padding = c.Padding is { } p
                ? new TableCellPadding(p.Left, p.Bottom, p.Right, p.Top)
                : null;

            var newCell = new TableCell(
                c.ParagraphIndex,
                c.ParagraphCount,
                columnIndex: entry.Column,
                columnSpan: c.ColumnSpan,
                rowSpan: c.RowSpan,
                c.Shading,
                borders,
                isRowSpanContinuation: false,
                tables: c.Tables,
                padding: padding,
                verticalAlignment: c.VerticalAlignment);

            flippedEntries.Add(new TableCellEntry(newRow, entry.Column, newCell));
        }

        return Build(_rowCount, _columnCount, _columnWidths, flippedEntries);
    }

    /// <summary>
    /// Inserts a blank row at <paramref name="atIndex"/>. Cells spanning across <paramref name="atIndex"/> have RowSpan incremented.
    /// </summary>
    public TableGrid InsertRow(int atIndex)
    {
        int target = Math.Clamp(atIndex, 0, _rowCount);
        var newEntries = new List<TableCellEntry>(_entries.Count);

        foreach (TableCellEntry entry in _entries)
        {
            TableCell c = entry.Cell;
            int r0 = entry.Row;
            int rs = c.RowSpan;

            if (r0 < target && r0 + rs > target)
            {
                // Expands across the inserted row
                var updated = new TableCell(
                    c.ParagraphIndex, c.ParagraphCount, c.ColumnIndex, c.ColumnSpan,
                    rowSpan: rs + 1, c.Shading, c.Borders, c.IsRowSpanContinuation, c.Tables, c.Padding, c.VerticalAlignment);
                newEntries.Add(new TableCellEntry(r0, entry.Column, updated));
            }
            else if (r0 >= target)
            {
                // Shift down
                newEntries.Add(new TableCellEntry(r0 + 1, entry.Column, c));
            }
            else
            {
                newEntries.Add(entry);
            }
        }

        return Build(_rowCount + 1, _columnCount, _columnWidths, newEntries);
    }

    /// <summary>
    /// Deletes the row at <paramref name="atIndex"/>. Cells spanning across <paramref name="atIndex"/> have RowSpan decremented.
    /// </summary>
    public TableGrid DeleteRow(int atIndex)
    {
        if (atIndex < 0 || atIndex >= _rowCount)
            return this;

        var newEntries = new List<TableCellEntry>(_entries.Count);
        foreach (TableCellEntry entry in _entries)
        {
            TableCell c = entry.Cell;
            int r0 = entry.Row;
            int rs = c.RowSpan;

            if (r0 == atIndex && rs == 1)
            {
                // Cell entirely deleted
                continue;
            }
            else if (r0 < atIndex && r0 + rs > atIndex)
            {
                var updated = new TableCell(
                    c.ParagraphIndex, c.ParagraphCount, c.ColumnIndex, c.ColumnSpan,
                    rowSpan: rs - 1, c.Shading, c.Borders, c.IsRowSpanContinuation, c.Tables, c.Padding, c.VerticalAlignment);
                newEntries.Add(new TableCellEntry(r0, entry.Column, updated));
            }
            else if (r0 == atIndex && rs > 1)
            {
                // Starts on deleted row but extends below: shift down to atIndex with reduced span
                var updated = new TableCell(
                    c.ParagraphIndex, c.ParagraphCount, c.ColumnIndex, c.ColumnSpan,
                    rowSpan: rs - 1, c.Shading, c.Borders, c.IsRowSpanContinuation, c.Tables, c.Padding, c.VerticalAlignment);
                newEntries.Add(new TableCellEntry(atIndex, entry.Column, updated));
            }
            else if (r0 > atIndex)
            {
                newEntries.Add(new TableCellEntry(r0 - 1, entry.Column, c));
            }
            else
            {
                newEntries.Add(entry);
            }
        }

        return Build(Math.Max(0, _rowCount - 1), _columnCount, _columnWidths, newEntries);
    }

    /// <summary>
    /// Inserts a blank column at <paramref name="atIndex"/>. Cells spanning across <paramref name="atIndex"/> have ColumnSpan incremented.
    /// </summary>
    public TableGrid InsertColumn(int atIndex, double width = 0)
    {
        int target = Math.Clamp(atIndex, 0, _columnCount);
        var newEntries = new List<TableCellEntry>(_entries.Count);

        foreach (TableCellEntry entry in _entries)
        {
            TableCell c = entry.Cell;
            int c0 = entry.Column;
            int cs = c.ColumnSpan;

            if (c0 < target && c0 + cs > target)
            {
                var updated = new TableCell(
                    c.ParagraphIndex, c.ParagraphCount, c0, columnSpan: cs + 1,
                    c.RowSpan, c.Shading, c.Borders, c.IsRowSpanContinuation, c.Tables, c.Padding, c.VerticalAlignment);
                newEntries.Add(new TableCellEntry(entry.Row, c0, updated));
            }
            else if (c0 >= target)
            {
                var updated = new TableCell(
                    c.ParagraphIndex, c.ParagraphCount, columnIndex: c0 + 1, c.ColumnSpan,
                    c.RowSpan, c.Shading, c.Borders, c.IsRowSpanContinuation, c.Tables, c.Padding, c.VerticalAlignment);
                newEntries.Add(new TableCellEntry(entry.Row, c0 + 1, updated));
            }
            else
            {
                newEntries.Add(entry);
            }
        }

        List<double>? newWidths = null;
        if (_columnWidths.Count > 0)
        {
            newWidths = new List<double>(_columnWidths);
            if (target <= newWidths.Count)
                newWidths.Insert(target, Math.Max(0, width));
        }

        return Build(_rowCount, _columnCount + 1, newWidths, newEntries);
    }

    /// <summary>
    /// Deletes the column at <paramref name="atIndex"/>. Cells spanning across <paramref name="atIndex"/> have ColumnSpan decremented.
    /// </summary>
    public TableGrid DeleteColumn(int atIndex)
    {
        if (atIndex < 0 || atIndex >= _columnCount)
            return this;

        var newEntries = new List<TableCellEntry>(_entries.Count);
        foreach (TableCellEntry entry in _entries)
        {
            TableCell c = entry.Cell;
            int c0 = entry.Column;
            int cs = c.ColumnSpan;

            if (c0 == atIndex && cs == 1)
            {
                continue;
            }
            else if (c0 < atIndex && c0 + cs > atIndex)
            {
                var updated = new TableCell(
                    c.ParagraphIndex, c.ParagraphCount, c0, columnSpan: cs - 1,
                    c.RowSpan, c.Shading, c.Borders, c.IsRowSpanContinuation, c.Tables, c.Padding, c.VerticalAlignment);
                newEntries.Add(new TableCellEntry(entry.Row, c0, updated));
            }
            else if (c0 == atIndex && cs > 1)
            {
                var updated = new TableCell(
                    c.ParagraphIndex, c.ParagraphCount, columnIndex: atIndex, columnSpan: cs - 1,
                    c.RowSpan, c.Shading, c.Borders, c.IsRowSpanContinuation, c.Tables, c.Padding, c.VerticalAlignment);
                newEntries.Add(new TableCellEntry(entry.Row, atIndex, updated));
            }
            else if (c0 > atIndex)
            {
                var updated = new TableCell(
                    c.ParagraphIndex, c.ParagraphCount, columnIndex: c0 - 1, c.ColumnSpan,
                    c.RowSpan, c.Shading, c.Borders, c.IsRowSpanContinuation, c.Tables, c.Padding, c.VerticalAlignment);
                newEntries.Add(new TableCellEntry(entry.Row, c0 - 1, updated));
            }
            else
            {
                newEntries.Add(entry);
            }
        }

        List<double>? newWidths = null;
        if (_columnWidths.Count > 0)
        {
            newWidths = new List<double>(_columnWidths);
            if (atIndex < newWidths.Count)
                newWidths.RemoveAt(atIndex);
        }

        return Build(_rowCount, Math.Max(0, _columnCount - 1), newWidths, newEntries);
    }

    /// <summary>
    /// Reconstructs a <see cref="DocumentTable"/> from this grid's cells, structure and dimensions.
    /// </summary>
    public DocumentTable ToTable(
        int paragraphIndex = 0,
        int paragraphCount = 0,
        double cellPadding = DocumentTable.DefaultCellPadding,
        TablePosition? position = null,
        bool keepWithNext = false,
        double? preferredWidth = null,
        double? widthPercent = null,
        TableAlignment alignment = TableAlignment.Start,
        double leftIndent = 0,
        bool pageBreakBefore = false,
        bool columnBreakBefore = false,
        bool autoFit = false,
        bool keepTogether = false)
    {
        var rows = new List<TableRow>(_rowCount);
        int totalParas = 0;
        for (int r = 0; r < _rowCount; r++)
        {
            var rowCells = _entries
                .Where(e => e.Row == r)
                .OrderBy(e => e.Column)
                .Select(e => e.Cell)
                .ToList();

            foreach (TableCell cell in rowCells)
                totalParas += cell.ParagraphCount;

            rows.Add(new TableRow(rowCells));
        }

        int pCount = paragraphCount > 0 ? paragraphCount : totalParas;
        return new DocumentTable(
            paragraphIndex,
            pCount,
            rows,
            _columnWidths.Count > 0 ? _columnWidths : null,
            cellPadding,
            position,
            keepWithNext,
            preferredWidth,
            widthPercent,
            alignment,
            leftIndent,
            pageBreakBefore,
            columnBreakBefore,
            autoFit,
            keepTogether);
    }

    /// <summary>Creates a 2D sparse matrix grid from an existing <see cref="DocumentTable"/>.</summary>
    public static TableGrid FromTable(DocumentTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        int rowCount = table.Rows.Count;
        int columnCount = table.ColumnWidths.Count;
        for (int r = 0; r < table.Rows.Count; r++)
        {
            TableRow row = table.Rows[r];
            foreach (TableCell cell in row.Cells)
                columnCount = Math.Max(columnCount, cell.ColumnIndex + cell.ColumnSpan);
        }

        var entries = new List<TableCellEntry>();
        for (int r = 0; r < table.Rows.Count; r++)
        {
            TableRow row = table.Rows[r];
            foreach (TableCell cell in row.Cells)
            {
                if (cell.IsRowSpanContinuation)
                    continue;
                entries.Add(new TableCellEntry(r, cell.ColumnIndex, cell));
            }
        }

        TableGrid grid = Build(rowCount, columnCount, table.ColumnWidths, entries);

        // Map any explicit row continuation cells to their slots
        for (int r = 0; r < table.Rows.Count; r++)
        {
            TableRow row = table.Rows[r];
            foreach (TableCell cell in row.Cells)
            {
                if (cell.IsRowSpanContinuation)
                {
                    long key = Key(r, cell.ColumnIndex);
                    if (grid._sparseSlots.TryGetValue(key, out TableGridSlot? existingSlot))
                    {
                        var updated = new TableGridSlot(r, cell.ColumnIndex, existingSlot.RootCell, existingSlot.RootRow, existingSlot.RootColumn, cell);
                        grid._sparseSlots[key] = updated;
                        if (grid._flatCache is not null && (uint)r < (uint)rowCount && (uint)cell.ColumnIndex < (uint)columnCount)
                            grid._flatCache[r * columnCount + cell.ColumnIndex] = updated;
                    }
                }
            }
        }

        return grid;
    }

    /// <summary>Creates a 2D sparse matrix grid from an arbitrary collection of cell coordinate placements.</summary>
    public static TableGrid FromCells(int rowCount, int columnCount, IEnumerable<(int Row, int Column, TableCell Cell)> cells, IReadOnlyList<double>? columnWidths = null)
    {
        ArgumentNullException.ThrowIfNull(cells);
        int rows = Math.Max(0, rowCount);
        int cols = Math.Max(0, columnCount);

        var entries = new List<TableCellEntry>();
        foreach (var (r, c, cell) in cells)
        {
            rows = Math.Max(rows, r + cell.RowSpan);
            cols = Math.Max(cols, c + cell.ColumnSpan);
            entries.Add(new TableCellEntry(r, c, cell));
        }

        return Build(rows, cols, columnWidths, entries);
    }

    /// <summary>Creates an empty 2D grid matrix of the specified dimensions.</summary>
    public static TableGrid Empty(int rowCount = 0, int columnCount = 0) =>
        Build(Math.Max(0, rowCount), Math.Max(0, columnCount), null, []);

    private static TableGrid Build(int rowCount, int columnCount, IReadOnlyList<double>? columnWidths, List<TableCellEntry> entries)
    {
        var widths = columnWidths is not null && columnWidths.Count > 0 ? [.. columnWidths] : Array.Empty<double>();
        var sparseSlots = new Dictionary<long, TableGridSlot>(entries.Count);
        var cellRanges = new Dictionary<TableCell, TableCellRange>(ReferenceEqualityComparer.Instance);
        var distinctCells = new List<TableCell>(entries.Count);

        foreach (TableCellEntry entry in entries)
        {
            TableCell cell = entry.Cell;
            int r0 = entry.Row;
            int c0 = entry.Column;
            int rs = Math.Max(1, cell.RowSpan);
            int cs = Math.Max(1, cell.ColumnSpan);

            cellRanges[cell] = new TableCellRange(r0, c0, rs, cs);
            distinctCells.Add(cell);

            for (int r = r0; r < r0 + rs && r < rowCount; r++)
            {
                for (int c = c0; c < c0 + cs && c < columnCount; c++)
                {
                    long key = Key(r, c);
                    if (!sparseSlots.ContainsKey(key))
                    {
                        TableCell? directCell = (r == r0 && c == c0) ? cell : null;
                        sparseSlots[key] = new TableGridSlot(r, c, cell, r0, c0, directCell);
                    }
                }
            }
        }

        TableGridSlot?[]? flatCache = null;
        long totalSlots = (long)rowCount * columnCount;
        if (totalSlots > 0 && totalSlots <= MaxFlatCacheSize)
        {
            flatCache = new TableGridSlot[totalSlots];
            foreach (var kvp in sparseSlots)
            {
                int r = (int)(kvp.Key >> 32);
                int c = (int)(kvp.Key & 0xFFFFFFFF);
                if ((uint)r < (uint)rowCount && (uint)c < (uint)columnCount)
                    flatCache[r * columnCount + c] = kvp.Value;
            }
        }

        return new TableGrid(rowCount, columnCount, widths, sparseSlots, flatCache, entries, cellRanges, distinctCells);
    }

    private static long Key(int row, int column) => ((long)row << 32) | (uint)column;
}
