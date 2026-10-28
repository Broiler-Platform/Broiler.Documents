using System;
using System.Globalization;

namespace Broiler.Documents.Model.Exclusions;

/// <summary>
/// An immutable axis-aligned rectangle in neutral layout units representing an exclusion boundary.
/// Coordinates are measured in a Cartesian frame (x increases to the right, y increases downward).
/// </summary>
public readonly record struct ExclusionRect : IEquatable<ExclusionRect>
{
    public double Left { get; }
    public double Top { get; }
    public double Right { get; }
    public double Bottom { get; }

    public double X => Left;
    public double Y => Top;
    public double Width => Math.Max(0.0, Right - Left);
    public double Height => Math.Max(0.0, Bottom - Top);

    public static ExclusionRect Empty => new(0, 0, 0, 0);

    public ExclusionRect(double left, double top, double right, double bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    /// <summary>Creates an exclusion rectangle from left, top, right, and bottom coordinates.</summary>
    public static ExclusionRect FromLTRB(double left, double top, double right, double bottom) =>
        new(left, top, right, bottom);

    /// <summary>Creates an exclusion rectangle from (x, y, width, height).</summary>
    public static ExclusionRect FromXYWH(double x, double y, double width, double height) =>
        new(x, y, x + Math.Max(0.0, width), y + Math.Max(0.0, height));

    /// <summary>
    /// True when coordinates are non-NaN, finite, and describe a non-empty area (Right &gt; Left and Bottom &gt; Top).
    /// </summary>
    public bool IsValid =>
        !double.IsNaN(Left) && !double.IsNaN(Top) &&
        !double.IsNaN(Right) && !double.IsNaN(Bottom) &&
        !double.IsInfinity(Left) && !double.IsInfinity(Top) &&
        !double.IsInfinity(Right) && !double.IsInfinity(Bottom) &&
        Right > Left && Bottom > Top;

    /// <summary>True when the rectangle has zero or negative area or is invalid.</summary>
    public bool IsEmpty => !IsValid || Width <= 0 || Height <= 0;

    /// <summary>
    /// Tests whether the vertical span [top, bottom) intersects this exclusion's [Top, Bottom) within an epsilon tolerance.
    /// Touching edges (e.g. bottom == Top or top == Bottom) do not intersect.
    /// </summary>
    public bool IntersectsY(double top, double bottom, double tolerance = 1e-4) =>
        (Top + tolerance) < bottom && (Bottom - tolerance) > top;

    /// <summary>
    /// Tests whether the horizontal span [left, right) intersects this exclusion's [Left, Right) within an epsilon tolerance.
    /// Touching edges do not intersect.
    /// </summary>
    public bool IntersectsX(double left, double right, double tolerance = 1e-4) =>
        (Left + tolerance) < right && (Right - tolerance) > left;

    /// <summary>Tests whether another rectangle intersects this exclusion on both axes.</summary>
    public bool Intersects(ExclusionRect other, double tolerance = 1e-4) =>
        IntersectsX(other.Left, other.Right, tolerance) && IntersectsY(other.Top, other.Bottom, tolerance);

    /// <summary>Expands this rectangle by the specified margins.</summary>
    public ExclusionRect WithMargins(double leftMargin, double topMargin, double rightMargin, double bottomMargin) =>
        new(
            Left - Math.Max(0.0, leftMargin),
            Top - Math.Max(0.0, topMargin),
            Right + Math.Max(0.0, rightMargin),
            Bottom + Math.Max(0.0, bottomMargin));

    /// <summary>Expands this rectangle by a uniform margin on all four sides.</summary>
    public ExclusionRect WithUniformMargin(double margin) =>
        WithMargins(margin, margin, margin, margin);

    /// <summary>Returns a new rectangle clamped horizontally to [containerLeft, containerRight].</summary>
    public ExclusionRect ClampHorizontal(double containerLeft, double containerRight) =>
        new(
            Math.Max(containerLeft, Left),
            Top,
            Math.Min(containerRight, Right),
            Bottom);

    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "ExclusionRect(L={0}, T={1}, R={2}, B={3})", Left, Top, Right, Bottom);
}
