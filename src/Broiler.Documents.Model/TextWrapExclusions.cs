using System;
using System.Collections.Generic;
using Broiler.Documents.Model.Exclusions;

namespace Broiler.Documents.Model;

/// <summary>The horizontal span a line of text has left, in column coordinates.</summary>
/// <param name="Left">Points from the text column's left edge.</param>
/// <param name="Width">How much room the line has from there.</param>
public readonly record struct TextBand(double Left, double Width);

/// <summary>
/// The boxes floating shapes keep body text out of, and the span a line has left
/// once they are taken into account.
/// Adapts document shapes and floating tables to the shared <see cref="ExclusionBandQueryEngine"/>.
/// </summary>
/// <remarks>
/// <para>
/// One implementation for all three layout engines - the editing surface, the
/// CLI rasterizer and the PDF writer - because three answers to "where does this
/// line start and how wide is it" would be three different documents.
/// </para>
/// <para>
/// A line keeps one span. A shape narrows it from the left or the right,
/// whichever leaves more room, rather than splitting the line into runs on both
/// sides of the shape. That is <c>wrapText="largest"</c> rather than
/// <c>bothSides</c>, and it is what keeps a line one measurable thing all the
/// way through wrapping, alignment, the caret and hit-testing.
/// </para>
/// <para>
/// Everything here is in column coordinates: x from the text column's left edge,
/// y down from the top of the body. A shape's y is resolved by the caller, which
/// is the one that knows where the anchoring paragraph landed.
/// </para>
/// </remarks>
public sealed class TextWrapExclusions
{
    private readonly ExclusionBandQueryEngine _kernel;

    /// <summary>The underlying unit-neutral geometry engine.</summary>
    public ExclusionBandQueryEngine Kernel => _kernel;

    public TextWrapExclusions()
    {
        _kernel = new ExclusionBandQueryEngine();
    }

    public TextWrapExclusions(ExclusionBandQueryEngine kernel)
    {
        _kernel = kernel;
    }

    /// <summary>True when nothing wraps, which is the common case and the fast path.</summary>
    public bool IsEmpty => _kernel.IsEmpty;

    /// <summary>Register a shape using its declared size.</summary>
    /// <remarks>Retains the binary signature used by published rich-edit implementations.</remarks>
    public void Add(DocumentShape shape, double top, double scale = 1)
        => Add(shape, top, scale, height: null, width: null);

    /// <summary>Register a shape with a layout-provided height.</summary>
    /// <remarks>Retains the four-parameter signature used before width overrides were introduced.</remarks>
    public void Add(DocumentShape shape, double top, double scale, double? height)
        => Add(shape, top, scale, height, width: null);

    /// <summary>
    /// Records a shape's box. <paramref name="top"/> is where the shape starts
    /// once its anchoring paragraph is placed; the margin the shape keeps around
    /// itself is already added by the caller.
    /// </summary>
    /// <remarks>
    /// <paramref name="scale"/> is the zoom a surface draws at. A shape states
    /// points and a zoomed surface lays out in its own units, so the box is
    /// scaled here rather than in each caller - <paramref name="top"/> is already
    /// in those units, being where the paragraph landed.
    /// <paramref name="height"/>, in points, is the box's height where the
    /// layout made it taller than it states, as it does for a shape that
    /// <see cref="DocumentShape.GrowsToText"/>.
    /// </remarks>
    public void Add(DocumentShape shape, double top, double scale = 1, double? height = null, double? width = null)
    {
        double boxHeight = height ?? shape.Height;
        double boxWidth = width ?? shape.Width;
        if (shape.Wrap == ShapeWrap.None || boxWidth <= 0 || boxHeight <= 0)
            return;

        double margin = Math.Max(0, shape.WrapDistance) * scale;
        var rect = new ExclusionRect(
            (shape.OffsetX * scale) - margin,
            top - margin,
            ((shape.OffsetX + boxWidth) * scale) + margin,
            top + (boxHeight * scale) + margin);

        var wrapKind = shape.Wrap switch
        {
            ShapeWrap.TopAndBottom => ObstacleWrapKind.ClearanceOnly,
            ShapeWrap.Square => ObstacleWrapKind.SideRestricted,
            _ => ObstacleWrapKind.None,
        };

        var sidePolicy = shape.WrapSide switch
        {
            WrapSide.Left => ObstacleSidePolicy.LeftOnly,
            WrapSide.Right => ObstacleSidePolicy.RightOnly,
            _ => ObstacleSidePolicy.Largest,
        };

        _kernel.Add(new ExclusionObstacle(rect, wrapKind, sidePolicy, zOrder: shape.ZOrder, tag: shape));
    }

    /// <summary>
    /// Records a box that is not a shape - a floating table - already resolved to
    /// column coordinates, the clearance it keeps included.
    /// </summary>
    public void AddBox(double top, double bottom, double left, double right, ShapeWrap wrap, WrapSide side = WrapSide.Largest)
    {
        if (wrap == ShapeWrap.None || bottom <= top || right <= left)
            return;

        var rect = ExclusionRect.FromLTRB(left, top, right, bottom);
        var wrapKind = wrap switch
        {
            ShapeWrap.TopAndBottom => ObstacleWrapKind.ClearanceOnly,
            ShapeWrap.Square => ObstacleWrapKind.SideRestricted,
            _ => ObstacleWrapKind.None,
        };

        var sidePolicy = side switch
        {
            WrapSide.Left => ObstacleSidePolicy.LeftOnly,
            WrapSide.Right => ObstacleSidePolicy.RightOnly,
            _ => ObstacleSidePolicy.Largest,
        };

        _kernel.Add(new ExclusionObstacle(rect, wrapKind, sidePolicy));
    }

    /// <summary>
    /// The span a line starting at <paramref name="top"/> has, after moving it
    /// down past anything that leaves it no room to be written in.
    /// </summary>
    public TextBand Resolve(ref double top, double height, double columnWidth, out double skip)
    {
        var band = _kernel.ResolveLine(
            ref top,
            height,
            0.0,
            columnWidth,
            out skip,
            maxAttempts: 8,
            descentStep: ClearanceDescentStep.Deepest,
            fallback: BlockedBandFallback.FullColumnAtCurrentY,
            minUsableWidth: 1.0e-9,
            defaultSidePolicy: ObstacleSidePolicy.Largest);

        return new TextBand(band.Left, band.Width);
    }

    /// <summary>
    /// The span a line occupying <paramref name="top"/> to <paramref name="bottom"/>
    /// has left in a column <paramref name="columnWidth"/> wide.
    /// </summary>
    public TextBand Band(double top, double bottom, double columnWidth)
    {
        double height = Math.Max(0.0, bottom - top);
        var band = _kernel.ComputeBand(
            top,
            height,
            0.0,
            columnWidth,
            defaultSidePolicy: ObstacleSidePolicy.Largest,
            multiPolicy: MultiObstaclePolicy.SequentialClamping);

        return new TextBand(band.Left, band.Width);
    }

    /// <summary>
    /// Where a line has to start when the band it wanted is blocked outright: past
    /// the bottom of whatever is in the way, or where it already was.
    /// </summary>
    public double PushBelow(double top, double bottom, bool squareToo) =>
        _kernel.DeepestLowerEdge(top, Math.Max(0.0, bottom - top), includeSideRestricted: squareToo);
}
