using System;
using System.Globalization;

namespace Broiler.Documents.Model.Tables;

/// <summary>
/// Represents the topological state of a single (row, column) coordinate within a table grid.
/// Tracks whether the slot is empty, an anchor root of a cell, or a continuation of a merged span.
/// </summary>
public readonly record struct TopologySlot<TCell> : IEquatable<TopologySlot<TCell>> where TCell : class
{
    public int Row { get; }
    public int Column { get; }
    public TCell? RootCell { get; }
    public int RootRow { get; }
    public int RootColumn { get; }
    public int RowSpan { get; }
    public int ColumnSpan { get; }

    public TopologySlot(
        int row,
        int column,
        TCell? rootCell,
        int rootRow,
        int rootColumn,
        int rowSpan = 1,
        int columnSpan = 1)
    {
        Row = row;
        Column = column;
        RootCell = rootCell;
        RootRow = rootRow;
        RootColumn = rootColumn;
        RowSpan = Math.Max(1, rowSpan);
        ColumnSpan = Math.Max(1, columnSpan);
    }

    /// <summary>Creates an empty slot at the given coordinate.</summary>
    public static TopologySlot<TCell> Empty(int row, int column) =>
        new(row, column, null, -1, -1, 1, 1);

    public bool IsEmpty => RootCell is null;
    public bool IsOccupied => RootCell is not null;
    public bool IsRoot => IsOccupied && Row == RootRow && Column == RootColumn;
    public bool IsContinuation => IsOccupied && !IsRoot;

    public int RowOffset => IsOccupied ? Row - RootRow : 0;
    public int ColumnOffset => IsOccupied ? Column - RootColumn : 0;

    public bool IsTopEdge => !IsOccupied || Row == RootRow;
    public bool IsBottomEdge => !IsOccupied || Row == RootRow + RowSpan - 1;
    public bool IsLeftEdge => !IsOccupied || Column == RootColumn;
    public bool IsRightEdge => !IsOccupied || Column == RootColumn + ColumnSpan - 1;

    public override string ToString() =>
        IsEmpty
            ? string.Format(CultureInfo.InvariantCulture, "TopologySlot({0}, {1}) [Empty]", Row, Column)
            : string.Format(CultureInfo.InvariantCulture, "TopologySlot({0}, {1}) [Root=({2}, {3}), Span={4}x{5}]", Row, Column, RootRow, RootColumn, RowSpan, ColumnSpan);
}
