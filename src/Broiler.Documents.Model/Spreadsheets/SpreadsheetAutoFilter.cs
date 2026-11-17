using System;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.Documents.Model.Spreadsheets;

/// <summary>
/// Represents an AutoFilter range and filter configuration on a spreadsheet worksheet.
/// </summary>
public sealed class SpreadsheetAutoFilter
{
    /// <summary>The cell range covering the AutoFilter (the first row is the header row).</summary>
    public CellRangeAddress Range { get; }

    /// <summary>The per-column filter criteria active within the filter range.</summary>
    public IReadOnlyList<SpreadsheetFilterColumn> Columns { get; }

    public SpreadsheetAutoFilter(CellRangeAddress range, IReadOnlyList<SpreadsheetFilterColumn>? columns = null)
    {
        Range = range;
        Columns = columns ?? [];
    }

    /// <summary>Gets the filter criteria for the specified column index, or null if unfiltered.</summary>
    public SpreadsheetFilterColumn? GetColumn(int columnIndex)
    {
        for (int i = 0; i < Columns.Count; i++)
        {
            if (Columns[i].ColumnIndex == columnIndex)
                return Columns[i];
        }
        return null;
    }

    /// <summary>Returns a copy of this AutoFilter with the specified column criteria added or updated.</summary>
    public SpreadsheetAutoFilter WithColumn(SpreadsheetFilterColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);
        var list = new List<SpreadsheetFilterColumn>(Columns.Count + 1);
        bool replaced = false;
        foreach (var col in Columns)
        {
            if (col.ColumnIndex == column.ColumnIndex)
            {
                list.Add(column);
                replaced = true;
            }
            else
            {
                list.Add(col);
            }
        }
        if (!replaced)
            list.Add(column);
        return new SpreadsheetAutoFilter(Range, list);
    }

    /// <summary>Returns a copy of this AutoFilter with criteria removed for the specified column index.</summary>
    public SpreadsheetAutoFilter WithoutColumn(int columnIndex)
    {
        var list = Columns.Where(c => c.ColumnIndex != columnIndex).ToList();
        return new SpreadsheetAutoFilter(Range, list);
    }
}

/// <summary>
/// Represents filter criteria applied to a specific column within an AutoFilter range.
/// </summary>
public sealed class SpreadsheetFilterColumn
{
    /// <summary>The 0-based column index.</summary>
    public int ColumnIndex { get; }

    /// <summary>The discrete string values permitted through the filter (checklist filter).</summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>Whether blank/empty cells are permitted through the filter.</summary>
    public bool IncludeBlanks { get; }

    public SpreadsheetFilterColumn(int columnIndex, IReadOnlyList<string>? values = null, bool includeBlanks = true)
    {
        ColumnIndex = columnIndex;
        Values = values ?? [];
        IncludeBlanks = includeBlanks;
    }
}
