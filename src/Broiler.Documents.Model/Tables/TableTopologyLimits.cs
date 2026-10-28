using System;

namespace Broiler.Documents.Model.Tables;

/// <summary>
/// Guardrails and limits for table topology engines to prevent memory exhaustion
/// or runaway execution on pathological or adversarial table inputs.
/// </summary>
public static class TableTopologyLimits
{
    /// <summary>Maximum number of rows supported in a table grid (matches HTML/Office limits).</summary>
    public const int MaxRows = 65_536;

    /// <summary>Maximum number of columns supported in a table grid (matches HTML MaximumColSpan).</summary>
    public const int MaxColumns = 1_024;

    /// <summary>Maximum number of total grid slots allocated before rejecting or clamping.</summary>
    public const int MaxSlots = 2_000_000;

    /// <summary>Maximum number of slots a single cell can cover to bound loop execution.</summary>
    public const int MaxSlotsPerCell = 100_000;

    /// <summary>Threshold below which flat 1D array caching is permitted; above this, sparse maps are used.</summary>
    public const int MaxFlatCacheSlots = 250_000;

    /// <summary>Clamps a row span to valid non-zero limits [1, MaxRows].</summary>
    public static int ClampRowSpan(int rowSpan) =>
        rowSpan <= 0 ? 1 : Math.Min(rowSpan, MaxRows);

    /// <summary>Clamps a column span to valid non-zero limits [1, MaxColumns].</summary>
    public static int ClampColumnSpan(int colSpan) =>
        colSpan <= 0 ? 1 : Math.Min(colSpan, MaxColumns);

    /// <summary>Clamps row count within [0, MaxRows].</summary>
    public static int ClampRowCount(int rowCount) =>
        Math.Clamp(rowCount, 0, MaxRows);

    /// <summary>Clamps column count within [0, MaxColumns].</summary>
    public static int ClampColumnCount(int columnCount) =>
        Math.Clamp(columnCount, 0, MaxColumns);

    /// <summary>
    /// Clamps cell spans and bounds their total area so (RowSpan * ColumnSpan &lt;= MaxSlotsPerCell).
    /// Prevents adversarial inputs from triggering runaway nested loops.
    /// </summary>
    public static (int RowSpan, int ColumnSpan) ClampSpans(int rowSpan, int colSpan)
    {
        int rs = ClampRowSpan(rowSpan);
        int cs = ClampColumnSpan(colSpan);
        if ((long)rs * cs > MaxSlotsPerCell)
        {
            rs = Math.Max(1, MaxSlotsPerCell / cs);
        }
        return (rs, cs);
    }

    /// <summary>
    /// Clamps stated grid dimensions so that (RowCount * ColumnCount &lt;= MaxSlots).
    /// </summary>
    public static (int RowCount, int ColumnCount) ClampDimensions(int rowCount, int columnCount)
    {
        int rows = ClampRowCount(rowCount);
        int cols = ClampColumnCount(columnCount);
        if (cols > 0 && (long)rows * cols > MaxSlots)
        {
            rows = Math.Max(1, MaxSlots / cols);
        }
        return (rows, cols);
    }
}
