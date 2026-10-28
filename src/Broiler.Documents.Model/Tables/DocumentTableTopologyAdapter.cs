using System;
using System.Collections.Generic;

namespace Broiler.Documents.Model.Tables;

/// <summary>
/// Adapts Broiler.Documents model structures (<see cref="DocumentTable"/> and <see cref="TableGrid"/>)
/// into an engine-neutral <see cref="TableTopologyMap{TableCell}"/>.
/// </summary>
public static class DocumentTableTopologyAdapter
{
    /// <summary>
    /// Creates an engine-neutral topology map from a <see cref="DocumentTable"/>.
    /// Ignores virtual row-span continuation cells and relies on the root cell's spans.
    /// </summary>
    public static TableTopologyMap<TableCell> FromTable(DocumentTable table)
    {
        if (table is null || table.Rows.Count == 0)
        {
            int defaultCols = table?.ColumnWidths?.Count ?? 0;
            return TableTopologyMap<TableCell>.CreateEmpty(0, defaultCols);
        }

        int rowCount = table.Rows.Count;
        int columnCount = table.ColumnWidths.Count;

        for (int r = 0; r < table.Rows.Count; r++)
        {
            TableRow row = table.Rows[r];
            foreach (TableCell cell in row.Cells)
            {
                columnCount = Math.Max(columnCount, cell.ColumnIndex + Math.Max(1, cell.ColumnSpan));
            }
        }

        var placements = new List<(int Row, int Column, int RowSpan, int ColumnSpan, TableCell Cell)>();

        for (int r = 0; r < table.Rows.Count; r++)
        {
            TableRow row = table.Rows[r];
            foreach (TableCell cell in row.Cells)
            {
                if (cell.IsRowSpanContinuation)
                    continue;

                placements.Add((r, cell.ColumnIndex, Math.Max(1, cell.RowSpan), Math.Max(1, cell.ColumnSpan), cell));
            }
        }

        return TableTopologyMap<TableCell>.Build(rowCount, columnCount, placements);
    }

    /// <summary>
    /// Creates an engine-neutral topology map from an existing <see cref="TableGrid"/>.
    /// </summary>
    public static TableTopologyMap<TableCell> FromGrid(TableGrid grid)
    {
        if (grid is null || grid.RowCount == 0 && grid.ColumnCount == 0)
            return TableTopologyMap<TableCell>.CreateEmpty();

        var placements = new List<(int Row, int Column, int RowSpan, int ColumnSpan, TableCell Cell)>(grid.CellCount);

        foreach (TableCell cell in grid.Cells)
        {
            var range = grid.GetCellRange(cell);
            if (range is not null)
            {
                placements.Add((range.Value.StartRow, range.Value.StartColumn, range.Value.RowSpan, range.Value.ColumnSpan, cell));
            }
        }

        return TableTopologyMap<TableCell>.Build(grid.RowCount, grid.ColumnCount, placements);
    }

    /// <summary>
    /// Creates an engine-neutral topology map from explicit cell coordinate placements.
    /// </summary>
    public static TableTopologyMap<TableCell> FromCells(
        int rowCount,
        int columnCount,
        IEnumerable<(int Row, int Column, TableCell Cell)> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        var placements = new List<(int Row, int Column, int RowSpan, int ColumnSpan, TableCell Cell)>();
        foreach (var (r, c, cell) in cells)
        {
            if (cell is not null)
            {
                placements.Add((r, c, Math.Max(1, cell.RowSpan), Math.Max(1, cell.ColumnSpan), cell));
            }
        }

        return TableTopologyMap<TableCell>.Build(rowCount, columnCount, placements);
    }
}
