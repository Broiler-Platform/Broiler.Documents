using System;
using System.Globalization;

namespace Broiler.Documents.Model.Exclusions;

/// <summary>
/// A resolved obstacle registered with the exclusion geometry kernel.
/// Holds the resolved bounding rectangle, wrapping policy, and optional model tag.
/// </summary>
public readonly record struct ExclusionObstacle : IEquatable<ExclusionObstacle>
{
    public ExclusionRect Bounds { get; }
    public ObstacleWrapKind WrapKind { get; }
    public ObstacleSidePolicy SidePolicy { get; }
    public int ZOrder { get; }
    public int SequenceNumber { get; }
    public object? Tag { get; }

    public double Left => Bounds.Left;
    public double Top => Bounds.Top;
    public double Right => Bounds.Right;
    public double Bottom => Bounds.Bottom;
    public double Width => Bounds.Width;
    public double Height => Bounds.Height;

    public bool IsEmpty => Bounds.IsEmpty || WrapKind == ObstacleWrapKind.None;

    public ExclusionObstacle(
        ExclusionRect bounds,
        ObstacleWrapKind wrapKind,
        ObstacleSidePolicy sidePolicy = ObstacleSidePolicy.Largest,
        int zOrder = 0,
        int sequenceNumber = 0,
        object? tag = null)
    {
        Bounds = bounds;
        WrapKind = wrapKind;
        SidePolicy = sidePolicy;
        ZOrder = zOrder;
        SequenceNumber = sequenceNumber;
        Tag = tag;
    }

    /// <summary>Creates a clearance-only obstacle (e.g. Word wrapTopAndBottom) that clears text below it.</summary>
    public static ExclusionObstacle Clearance(ExclusionRect bounds, int sequence = 0, int zOrder = 0, object? tag = null) =>
        new(bounds, ObstacleWrapKind.ClearanceOnly, ObstacleSidePolicy.Largest, zOrder, sequence, tag);

    /// <summary>Creates a side-restricted obstacle (e.g. Word wrapSquare) allowing text on specified side(s).</summary>
    public static ExclusionObstacle Square(
        ExclusionRect bounds,
        ObstacleSidePolicy side = ObstacleSidePolicy.Largest,
        int sequence = 0,
        int zOrder = 0,
        object? tag = null) =>
        new(bounds, ObstacleWrapKind.SideRestricted, side, zOrder, sequence, tag);

    /// <summary>Creates a CSS float obstacle (left or right float).</summary>
    public static ExclusionObstacle CssFloat(
        ExclusionRect bounds,
        bool isLeft,
        int sequence = 0,
        int zOrder = 0,
        object? tag = null) =>
        new(
            bounds,
            isLeft ? ObstacleWrapKind.CssFloatLeft : ObstacleWrapKind.CssFloatRight,
            ObstacleSidePolicy.CssFloats,
            zOrder,
            sequence,
            tag);

    /// <summary>Creates a non-wrapping (through / overlay) obstacle that does not exclude lines.</summary>
    public static ExclusionObstacle Through(ExclusionRect bounds, int sequence = 0, int zOrder = 0, object? tag = null) =>
        new(bounds, ObstacleWrapKind.None, ObstacleSidePolicy.Largest, zOrder, sequence, tag);

    /// <summary>Checks whether this obstacle's vertical slice intersects [top, bottom) within tolerance.</summary>
    public bool IntersectsY(double top, double bottom, double tolerance = 1e-4) =>
        Bounds.IntersectsY(top, bottom, tolerance);

    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "ExclusionObstacle({0}, Wrap={1}, Side={2}, Seq={3})",
            Bounds,
            WrapKind,
            SidePolicy,
            SequenceNumber);
}
