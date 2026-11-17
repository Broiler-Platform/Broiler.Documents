using System;

namespace Broiler.Documents.Model;

/// <summary>
/// Represents a user note or comment attached to a spreadsheet cell or table cell.
/// </summary>
public sealed record CellNote(string Text, string? Author = null, DateTimeOffset? Timestamp = null)
{
    public override string ToString() => Text;
}
