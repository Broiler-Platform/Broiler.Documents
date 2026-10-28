using System;
using System.Globalization;

namespace Broiler.Documents.Model.Tables;

/// <summary>
/// Orientation of a boundary edge between adjacent table slots.
/// </summary>
public enum TableEdgeOrientation
{
    /// <summary>Horizontal edge separating a row from the row below it.</summary>
    Horizontal = 0,

    /// <summary>Vertical edge separating a column from the column to its right.</summary>
    Vertical = 1,
}

/// <summary>
/// Represents a shared edge segment between two adjacent grid coordinates.
/// Used for border arbitration, collapse resolution, and perimeter detection.
/// </summary>
public readonly record struct TopologyEdgeSegment<TCell> : IEquatable<TopologyEdgeSegment<TCell>> where TCell : class
{
    public TableEdgeOrientation Orientation { get; }

    /// <summary>
    /// For a horizontal edge, the boundary row index (0 is top table perimeter, RowCount is bottom perimeter).
    /// For a vertical edge, the boundary column index (0 is left table perimeter, ColumnCount is right perimeter).
    /// </summary>
    public int BoundaryIndex { get; }

    /// <summary>
    /// For a horizontal edge, the column coordinate this segment spans.
    /// For a vertical edge, the row coordinate this segment spans.
    /// </summary>
    public int SlotIndex { get; }

    /// <summary>The cell on the primary side (top for horizontal edges, left for vertical edges), or null if outer perimeter.</summary>
    public TCell? PrimaryCell { get; }

    /// <summary>The cell on the neighbor side (bottom for horizontal edges, right for vertical edges), or null if outer perimeter.</summary>
    public TCell? NeighborCell { get; }

    /// <summary>True when this segment lies on the outer bounding perimeter of the entire table.</summary>
    public bool IsOuterBoundary { get; }

    public TopologyEdgeSegment(
        TableEdgeOrientation orientation,
        int boundaryIndex,
        int slotIndex,
        TCell? primaryCell,
        TCell? neighborCell,
        bool isOuterBoundary = false)
    {
        Orientation = orientation;
        BoundaryIndex = boundaryIndex;
        SlotIndex = slotIndex;
        PrimaryCell = primaryCell;
        NeighborCell = neighborCell;
        IsOuterBoundary = isOuterBoundary;
    }

    /// <summary>True when this segment is inside a merged cell span, where internal border lines should not be drawn.</summary>
    public bool IsInternalSeam =>
        PrimaryCell is not null && ReferenceEquals(PrimaryCell, NeighborCell);

    /// <summary>True when this segment lies on the outer perimeter of the table or borders an empty cell.</summary>
    public bool IsPerimeter =>
        IsOuterBoundary || PrimaryCell is null || NeighborCell is null;

    /// <summary>True when this segment separates two distinct non-empty cells.</summary>
    public bool IsSharedEdge =>
        PrimaryCell is not null && NeighborCell is not null && !ReferenceEquals(PrimaryCell, NeighborCell);

    /// <summary>True when this segment borders an interior empty slot.</summary>
    public bool IsBorderingEmpty =>
        !IsOuterBoundary && (PrimaryCell is null ^ NeighborCell is null);

    /// <summary>
    /// Returns the two cells in order of priority for border collapse tie-breaking.
    /// In LTR, top/left cell wins ties. In RTL, right cell wins ties over left cell.
    /// </summary>
    public (TCell? FirstTieCell, TCell? SecondTieCell) GetTieBreakOrder(bool isRtl = false) =>
        Orientation == TableEdgeOrientation.Vertical && isRtl
            ? (NeighborCell, PrimaryCell)
            : (PrimaryCell, NeighborCell);

    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "TopologyEdgeSegment({0}, Boundary={1}, Slot={2}, Seam={3}, Perimeter={4}, Outer={5})",
            Orientation,
            BoundaryIndex,
            SlotIndex,
            IsInternalSeam,
            IsPerimeter,
            IsOuterBoundary);
}
