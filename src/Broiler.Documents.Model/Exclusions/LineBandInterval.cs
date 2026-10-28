using System;
using System.Globalization;

namespace Broiler.Documents.Model.Exclusions;

/// <summary>
/// A horizontal interval representing available space on a line in neutral layout coordinates.
/// </summary>
/// <param name="Left">Distance from the container's left coordinate edge.</param>
/// <param name="Width">Available width of the band from Left.</param>
public readonly record struct LineBandInterval(double Left, double Width) : IEquatable<LineBandInterval>
{
    public double Right => Left + Math.Max(0.0, Width);
    public bool IsEmpty => Width <= 0.0;

    public static LineBandInterval Empty => new(0, 0);

    public static implicit operator LineBandInterval(TextBand textBand) =>
        new(textBand.Left, textBand.Width);

    public static implicit operator TextBand(LineBandInterval interval) =>
        new(interval.Left, interval.Width);

    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "LineBandInterval(Left={0}, Width={1}, Right={2})", Left, Width, Right);
}
