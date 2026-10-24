using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.Documents.Model;

/// <summary>Alignment of the text following a paragraph tab.</summary>
public enum TabAlignment { Left, Center, Right, Decimal }

/// <summary>A tab position in points from the containing text area's left edge.</summary>
public readonly record struct ParagraphTabStop(double Position, TabAlignment Alignment = TabAlignment.Left,
    string? Leader = null, char DecimalCharacter = '.');

/// <summary>An immutable, value-comparable set of paragraph tab stops.</summary>
public sealed class ParagraphTabStops : IReadOnlyList<ParagraphTabStop>, IEquatable<ParagraphTabStops>
{
    private readonly ParagraphTabStop[] _stops;

    public ParagraphTabStops(IEnumerable<ParagraphTabStop> stops)
    {
        ArgumentNullException.ThrowIfNull(stops);
        _stops = stops.Take(256).Where(stop => double.IsFinite(stop.Position) && stop.Position >= 0 && stop.Position <= 20000)
            .GroupBy(stop => stop.Position).Select(group => group.Last() with
            { Leader = string.IsNullOrEmpty(group.Last().Leader) ? null : group.Last().Leader![..1] })
            .OrderBy(stop => stop.Position).ToArray();
    }

    public int Count => _stops.Length;
    public ParagraphTabStop this[int index] => _stops[index];
    public IEnumerator<ParagraphTabStop> GetEnumerator() => ((IEnumerable<ParagraphTabStop>)_stops).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public bool Equals(ParagraphTabStops? other) => other is not null && _stops.SequenceEqual(other._stops);
    public override bool Equals(object? obj) => obj is ParagraphTabStops other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (ParagraphTabStop stop in _stops) hash.Add(stop);
        return hash.ToHashCode();
    }
}
