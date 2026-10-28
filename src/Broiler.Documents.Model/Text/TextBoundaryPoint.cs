using System;
using System.Globalization;

namespace Broiler.Documents.Model.Text;

/// <summary>
/// A boundary classification point in source-text coordinates.
/// </summary>
public readonly record struct TextBoundaryPoint(
    int SourceOffset,
    int CodePoint,
    UnicodeBoundaryKind Kind,
    int ClusterLength) : IEquatable<TextBoundaryPoint>
{
    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "TextBoundaryPoint(Offset={0}, U+{1:X4}, Kind={2}, Len={3})", SourceOffset, CodePoint, Kind, ClusterLength);
}
