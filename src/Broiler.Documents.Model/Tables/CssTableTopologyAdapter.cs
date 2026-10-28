using System;
using System.Collections.Generic;

namespace Broiler.Documents.Model.Tables;

/// <summary>
/// Descriptor for a cell in the CSS / HTML table model.
/// </summary>
/// <typeparam name="TCell">Type of the cell element (e.g. CssBox or DOM node).</typeparam>
public readonly record struct CssCellDescriptor<TCell> where TCell : class
{
    public TCell Cell { get; }
    public int RowSpan { get; }
    public int ColumnSpan { get; }
    public bool IsSpacingBox { get; }

    public CssCellDescriptor(TCell cell, int rowSpan = 1, int columnSpan = 1, bool isSpacingBox = false)
    {
        Cell = cell;
        RowSpan = Math.Max(1, rowSpan);
        ColumnSpan = Math.Max(1, columnSpan);
        IsSpacingBox = isSpacingBox;
    }

    /// <summary>Creates a standard cell descriptor.</summary>
    public static CssCellDescriptor<TCell> ForCell(TCell cell, int rowSpan = 1, int columnSpan = 1) =>
        new(cell, rowSpan, columnSpan, isSpacingBox: false);

    /// <summary>Creates a placeholder descriptor representing a row-span continuation (e.g. CssSpacingBox).</summary>
    public static CssCellDescriptor<TCell> ForSpacingBox(TCell extendedCell, int rowSpan = 1, int columnSpan = 1) =>
        new(extendedCell, rowSpan, columnSpan, isSpacingBox: true);
}

/// <summary>
/// Represents an adjacent cell seam between two distinct real cells requiring CSS border collapse arbitration.
/// </summary>
public readonly record struct CssBorderSeam<TCell>(
    TableEdgeOrientation Orientation,
    int BoundaryIndex,
    int SlotIndex,
    TCell PrimaryCell,
    TCell NeighborCell,
    TCell TieWinnerCell,
    TCell TieLoserCell) where TCell : class;

/// <summary>
/// Adapts CSS / HTML table structures (with colspans, rowspans, spacing box placeholders, and RTL direction)
/// into an engine-neutral <see cref="TableTopologyMap{TCell}"/>.
/// </summary>
public static class CssTableTopologyAdapter
{
    /// <summary>
    /// Builds a <see cref="TableTopologyMap{TCell}"/> from a sequence of HTML table rows according to the W3C HTML table model.
    /// Handles colspans, rowspans, virtual spacing box continuation placeholders, and enforces safety bounds.
    /// </summary>
    public static TableTopologyMap<TCell> FromRows<TCell>(
        IEnumerable<IEnumerable<CssCellDescriptor<TCell>>> rows,
        bool isRtl = false) where TCell : class
    {
        ArgumentNullException.ThrowIfNull(rows);

        var rowList = new List<List<CssCellDescriptor<TCell>>>();
        foreach (var r in rows)
        {
            rowList.Add(r is List<CssCellDescriptor<TCell>> l ? l : [.. r]);
        }

        if (rowList.Count == 0)
            return TableTopologyMap<TCell>.CreateEmpty();

        int statedRowCount = rowList.Count;
        var occupiedSlots = new HashSet<long>();
        var placements = new List<(int Row, int Column, int RowSpan, int ColumnSpan, TCell Cell)>();
        int maxCol = 0;

        for (int r = 0; r < rowList.Count && r < TableTopologyLimits.MaxRows; r++)
        {
            var row = rowList[r];
            int c = 0;

            for (int i = 0; i < row.Count; i++)
            {
                var descriptor = row[i];
                if (descriptor.Cell is null)
                    continue;

                if (descriptor.IsSpacingBox)
                {
                    // A spacing box placeholder corresponds to a slot covered by an earlier rowspan.
                    // If not yet occupied, mark it and advance.
                    if (c < TableTopologyLimits.MaxColumns && !occupiedSlots.Contains(Key(r, c)))
                    {
                        occupiedSlots.Add(Key(r, c));
                        c++;
                    }
                    continue;
                }

                // Advance over slots already occupied by previous rowspans
                while (c < TableTopologyLimits.MaxColumns && occupiedSlots.Contains(Key(r, c)))
                {
                    c++;
                }

                if (c >= TableTopologyLimits.MaxColumns)
                    break;

                var (rs, cs) = TableTopologyLimits.ClampSpans(descriptor.RowSpan, descriptor.ColumnSpan);

                // Bound effective coverage against table limits
                int effectiveRowEnd = Math.Min(r + rs, TableTopologyLimits.MaxRows);
                int effectiveColEnd = Math.Min(c + cs, TableTopologyLimits.MaxColumns);
                int finalRs = Math.Max(1, effectiveRowEnd - r);
                int finalCs = Math.Max(1, effectiveColEnd - c);

                var (boundedRs, boundedCs) = TableTopologyLimits.ClampSpans(finalRs, finalCs);
                finalRs = boundedRs;
                finalCs = boundedCs;

                placements.Add((r, c, finalRs, finalCs, descriptor.Cell));

                for (int ro = 0; ro < finalRs; ro++)
                {
                    if (occupiedSlots.Count >= TableTopologyLimits.MaxSlots)
                        break;

                    for (int co = 0; co < finalCs; co++)
                    {
                        if (occupiedSlots.Count >= TableTopologyLimits.MaxSlots)
                            break;

                        occupiedSlots.Add(Key(r + ro, c + co));
                    }
                }

                c += finalCs;
                maxCol = Math.Max(maxCol, c);
            }
        }

        return TableTopologyMap<TCell>.Build(statedRowCount, maxCol, placements);
    }

    /// <summary>
    /// Builds a <see cref="TableTopologyMap{TCell}"/> from raw cell coordinate placements.
    /// </summary>
    public static TableTopologyMap<TCell> FromPlacements<TCell>(
        int statedRowCount,
        int statedColumnCount,
        IEnumerable<(int Row, int Column, int RowSpan, int ColumnSpan, TCell Cell)> placements) where TCell : class
    {
        return TableTopologyMap<TCell>.Build(statedRowCount, statedColumnCount, placements);
    }

    /// <summary>
    /// Enumerates all collapsible seams between adjacent distinct cells in the topology map.
    /// Internal seams (within the same merged cell) and seams bordering outer perimeter or empty slots are excluded.
    /// In RTL tables, vertical seams break ties toward the right cell according to CSS 2.1 §17.6.2.1.
    /// </summary>
    public static List<CssBorderSeam<TCell>> EnumerateCollapsibleSeams<TCell>(
        TableTopologyMap<TCell> map,
        bool isRtl = false) where TCell : class
    {
        ArgumentNullException.ThrowIfNull(map);

        var result = new List<CssBorderSeam<TCell>>();

        // 1. Horizontal seams (top cell vs bottom cell)
        for (int r = 1; r < map.RowCount; r++)
        {
            for (int c = 0; c < map.ColumnCount; c++)
            {
                var top = map.GetCellAt(r - 1, c);
                var bottom = map.GetCellAt(r, c);

                if (top is null || bottom is null || ReferenceEquals(top, bottom))
                    continue;

                // For horizontal seams, top cell wins ties in both LTR and RTL
                result.Add(new CssBorderSeam<TCell>(
                    TableEdgeOrientation.Horizontal,
                    BoundaryIndex: r,
                    SlotIndex: c,
                    PrimaryCell: top,
                    NeighborCell: bottom,
                    TieWinnerCell: top,
                    TieLoserCell: bottom));
            }
        }

        // 2. Vertical seams (left cell vs right cell)
        for (int c = 1; c < map.ColumnCount; c++)
        {
            for (int r = 0; r < map.RowCount; r++)
            {
                var left = map.GetCellAt(r, c - 1);
                var right = map.GetCellAt(r, c);

                if (left is null || right is null || ReferenceEquals(left, right))
                    continue;

                // In RTL, right cell wins ties; in LTR, left cell wins ties
                var winner = isRtl ? right : left;
                var loser = isRtl ? left : right;

                result.Add(new CssBorderSeam<TCell>(
                    TableEdgeOrientation.Vertical,
                    BoundaryIndex: c,
                    SlotIndex: r,
                    PrimaryCell: left,
                    NeighborCell: right,
                    TieWinnerCell: winner,
                    TieLoserCell: loser));
            }
        }

        return result;
    }

    private static long Key(int row, int column) =>
        ((long)row << 32) | (uint)column;
}
