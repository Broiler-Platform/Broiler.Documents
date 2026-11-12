using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly HashSet<DocumentShape> _shapes = new(ReferenceEqualityComparer.Instance);

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
    public void Add(DocumentShape shape, double top, double scale = 1, double? height = null, double? width = null) =>
        Add(shape, top, scale, height, width, shift: 0);

    /// <summary>
    /// Records a shape's box moved <paramref name="shift"/> to the right of
    /// where its <see cref="DocumentShape.OffsetX"/> puts it: the left of the
    /// text column it is placed from (<see cref="DocumentShape.OffsetXFromColumn"/>).
    /// </summary>
    public void Add(DocumentShape shape, double top, double scale, double? height, double? width, double shift)
    {
        double boxHeight = height ?? shape.Height;
        double boxWidth = width ?? shape.Width;
        if (shape.Wrap == ShapeWrap.None || boxWidth <= 0 || boxHeight <= 0 || !_shapes.Add(shape))
            return;

        double margin = Math.Max(0, shape.WrapDistance) * scale;
        double marginLeft = shape.WrapMargins is { } wmL ? Math.Max(0, wmL.Left) * scale : margin;
        double marginTop = shape.WrapMargins is { } wmT ? Math.Max(0, wmT.Top) * scale : margin;
        double marginRight = shape.WrapMargins is { } wmR ? Math.Max(0, wmR.Right) * scale : margin;
        double marginBottom = shape.WrapMargins is { } wmB ? Math.Max(0, wmB.Bottom) * scale : margin;
        double offsetX = shape.OffsetX + shift;
        var rect = new ExclusionRect(
            (offsetX * scale) - marginLeft,
            top - marginTop,
            ((offsetX + boxWidth) * scale) + marginRight,
            top + (boxHeight * scale) + marginBottom);
        IReadOnlyList<ExclusionRect> rects = shape.WrapPolygon is { Count: >= 3 } polygon && shape.Wrap == ShapeWrap.Square
            ? PolygonBands(polygon, offsetX * scale, top, boxWidth * scale, boxHeight * scale,
                marginLeft, marginTop, marginRight, marginBottom)
            : [rect];

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

        foreach (ExclusionRect band in rects)
            _kernel.Add(new ExclusionObstacle(band, wrapKind, sidePolicy, zOrder: shape.ZOrder, tag: shape)
            {
                MinimumSideRoom = Math.Max(0, shape.MinimumWrapRoom) * scale,
            });
    }

    /// <summary>
    /// A wrap polygon as a stack of rectangles, one per band between its
    /// vertices' heights, each as wide as the outline is within that band, so
    /// text runs into the parts of the box the outline leaves open.
    /// </summary>
    private static List<ExclusionRect> PolygonBands(IReadOnlyList<(double X, double Y)> polygon,
        double left, double top, double width, double height,
        double marginLeft, double marginTop, double marginRight, double marginBottom)
    {
        double[] levels = [.. polygon.Select(point => point.Y).Distinct().Order()];
        var bands = new List<ExclusionRect>();
        for (int i = 0; i + 1 < levels.Length; i++)
        {
            double from = levels[i], to = levels[i + 1];
            double min = double.MaxValue, max = double.MinValue;
            for (int k = 0; k < polygon.Count; k++)
            {
                var (ax, ay) = polygon[k];
                var (bx, by) = polygon[(k + 1) % polygon.Count];
                if (ay == by)
                    continue;
                double low = Math.Max(Math.Min(ay, by), from), high = Math.Min(Math.Max(ay, by), to);
                if (high <= low)
                    continue;
                foreach (double y in (double[])[low, high])
                {
                    double x = ax + ((bx - ax) * (y - ay) / (by - ay));
                    min = Math.Min(min, x);
                    max = Math.Max(max, x);
                }
            }

            if (max <= min)
                continue;
            bands.Add(new ExclusionRect(
                left + (min * width) - marginLeft,
                top + (from * height) - (i == 0 ? marginTop : 0),
                left + (max * width) + marginRight,
                top + (to * height) + (i + 2 == levels.Length ? marginBottom : 0)));
        }

        return bands.Count > 0 ? bands : [new ExclusionRect(left - marginLeft, top - marginTop, left + width + marginRight, top + height + marginBottom)];
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
    /// <remarks>
    /// A blocked line steps down to the nearest foot of what it meets, not past
    /// the deepest: where one of two frames side by side ends first, the line
    /// is written beside the other from there, as Writer writes it. Jumping
    /// past both sent tdf135056.odt's empty paragraph below a page-high table
    /// of contents frame and onto a page of its own, where LibreOffice sets it
    /// under the short frame beside it.
    /// </remarks>
    public TextBand Resolve(ref double top, double height, double columnWidth, out double skip, double columnLeft = 0.0)
    {
        var band = _kernel.ResolveLine(
            ref top,
            height,
            columnLeft,
            columnWidth,
            out skip,
            maxAttempts: 32,
            descentStep: ClearanceDescentStep.Shallowest,
            fallback: BlockedBandFallback.FullColumnAtCurrentY,
            minUsableWidth: 1.0e-9,
            defaultSidePolicy: ObstacleSidePolicy.Largest,
            clearanceNeedsOverlap: true);

        return new TextBand(band.Left - columnLeft, band.Width);
    }
    /// <summary>
    /// Resolves the earliest vertical position at or below <paramref name="top"/> where
    /// an object of width <paramref name="requiredWidth"/> and height <paramref name="height"/>
    /// can fit without overlapping obstacles.
    /// </summary>
    public double Clearance(double top, double height, double requiredWidth, double columnWidth)
    {
        if (IsEmpty)
            return top;
        double y = top;
        _kernel.ResolveLine(
            ref y,
            height,
            0.0,
            columnWidth,
            out _,
            maxAttempts: 8,
            descentStep: ClearanceDescentStep.Deepest,
            fallback: BlockedBandFallback.FullColumnAtCurrentY,
            minUsableWidth: requiredWidth,
            defaultSidePolicy: ObstacleSidePolicy.Largest);
        return y;
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
