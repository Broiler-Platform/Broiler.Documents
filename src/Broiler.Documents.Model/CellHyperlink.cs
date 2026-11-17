using System;

namespace Broiler.Documents.Model;

/// <summary>
/// Represents a hyperlink attached to a spreadsheet cell or table cell.
/// </summary>
public sealed record CellHyperlink(string Target, string? Tooltip = null)
{
    /// <summary>
    /// Gets whether the target points to an internal sheet or cell reference
    /// (e.g. starting with '#' or pointing to Sheet!Cell).
    /// </summary>
    public bool IsInternal =>
        Target.StartsWith('#') ||
        (!Target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
         !Target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
         !Target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) &&
         !Target.StartsWith("file:", StringComparison.OrdinalIgnoreCase) &&
         (Target.Contains('!') || Target.Contains('.')));

    public override string ToString() => Target;
}
