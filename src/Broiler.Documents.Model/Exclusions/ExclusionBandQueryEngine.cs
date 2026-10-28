using System;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.Documents.Model.Exclusions;

/// <summary>
/// High-performance, unit-neutral exclusion geometry kernel.
/// Resolves obstacle intersections, computes available horizontal line bands,
/// calculates clearance lower edges, and bounds descent search against pathological hangs.
/// </summary>
public sealed class ExclusionBandQueryEngine
{
    private readonly List<ExclusionObstacle> _obstacles;
    private int _nextSequence;

    /// <summary>Epsilon tolerance used for float comparisons and touching edge detection.</summary>
    public double EpsilonTolerance { get; init; } = 1e-4;

    /// <summary>
    /// Minimum vertical slice thickness used for zero-height or sub-pixel line queries
    /// (CSS2.1 hairline rule, prevents zero-height lines from missing obstacles).
    /// </summary>
    public double HairlineTolerance { get; init; } = 0.01;

    public ExclusionBandQueryEngine()
    {
        _obstacles = [];
        _nextSequence = 0;
    }

    public ExclusionBandQueryEngine(IEnumerable<ExclusionObstacle> obstacles, double epsilonTolerance = 1e-4, double hairlineTolerance = 0.01)
    {
        EpsilonTolerance = epsilonTolerance;
        HairlineTolerance = hairlineTolerance;
        _obstacles = [];
        _nextSequence = 0;
        foreach (var obstacle in obstacles)
            Add(obstacle);
    }

    private ExclusionBandQueryEngine(List<ExclusionObstacle> obstacles, int nextSequence, double epsilonTolerance, double hairlineTolerance)
    {
        _obstacles = obstacles;
        _nextSequence = nextSequence;
        EpsilonTolerance = epsilonTolerance;
        HairlineTolerance = hairlineTolerance;
    }

    /// <summary>An empty engine instance containing no obstacles.</summary>
    public static ExclusionBandQueryEngine Empty => new();

    /// <summary>True when no active wrapping obstacles are registered.</summary>
    public bool IsEmpty => _obstacles.Count == 0;

    /// <summary>Number of registered obstacles.</summary>
    public int Count => _obstacles.Count;

    /// <summary>Current list of registered obstacles.</summary>
    public IReadOnlyList<ExclusionObstacle> Obstacles => _obstacles;

    /// <summary>
    /// Registers an obstacle. If the obstacle bounds are invalid or wrap kind is None, the obstacle is ignored.
    /// </summary>
    public void Add(ExclusionObstacle obstacle)
    {
        if (obstacle.IsEmpty || obstacle.WrapKind == ObstacleWrapKind.None || !obstacle.Bounds.IsValid)
            return;

        int seq = obstacle.SequenceNumber == 0 ? ++_nextSequence : obstacle.SequenceNumber;
        _obstacles.Add(new ExclusionObstacle(
            obstacle.Bounds,
            obstacle.WrapKind,
            obstacle.SidePolicy,
            obstacle.ZOrder,
            seq,
            obstacle.Tag));
    }

    /// <summary>Clears all obstacles from the engine.</summary>
    public void Clear()
    {
        _obstacles.Clear();
        _nextSequence = 0;
    }

    /// <summary>Creates an isolated snapshot of this engine.</summary>
    public ExclusionBandQueryEngine CreateSnapshot() =>
        new([.. _obstacles], _nextSequence, EpsilonTolerance, HairlineTolerance);

    /// <summary>
    /// Finds all registered obstacles whose vertical bounds overlap the line slice [top, top + height).
    /// Zero-height lines are treated as a hairline slice of height <see cref="HairlineTolerance"/>.
    /// Touching edges (e.g. obstacle bottom == line top) do not intersect.
    /// </summary>
    public List<ExclusionObstacle> QueryIntersecting(double top, double height, double? tolerance = null)
    {
        if (IsEmpty)
            return [];

        double tol = tolerance ?? EpsilonTolerance;
        double sliceHeight = Math.Max(height, HairlineTolerance);
        double sliceBottom = top + sliceHeight;

        var list = new List<ExclusionObstacle>();
        foreach (var obs in _obstacles)
        {
            if (obs.IntersectsY(top, sliceBottom, tol))
                list.Add(obs);
        }

        return list;
    }

    /// <summary>
    /// CSS2.1 §9.5: Finds the shallowest bottom edge (minimum Bottom &gt; top + tolerance)
    /// among all intersecting obstacles, or <see cref="double.NaN"/> if none intersect.
    /// Used to drop lines step-by-step past floats.
    /// </summary>
    public double ShallowestLowerEdge(double top, double height, double? tolerance = null)
    {
        if (IsEmpty)
            return double.NaN;

        double tol = tolerance ?? EpsilonTolerance;
        double sliceHeight = Math.Max(height, HairlineTolerance);
        double sliceBottom = top + sliceHeight;
        double next = double.NaN;

        foreach (var obs in _obstacles)
        {
            if (!obs.IntersectsY(top, sliceBottom, tol))
                continue;

            double bottom = obs.Bottom;
            if (bottom > top + tol && (double.IsNaN(next) || bottom < next))
                next = bottom;
        }

        return next;
    }

    /// <summary>
    /// Document layout clearance: finds the deepest bottom edge (maximum Bottom)
    /// among intersecting obstacles, pushing the line completely below them.
    /// </summary>
    public double DeepestLowerEdge(
        double top,
        double height,
        bool includeSideRestricted = true,
        double? tolerance = null)
    {
        if (IsEmpty)
            return top;

        double tol = tolerance ?? EpsilonTolerance;
        double sliceHeight = Math.Max(height, HairlineTolerance);
        double sliceBottom = top + sliceHeight;
        double moved = top;

        foreach (var obs in _obstacles)
        {
            if (obs.WrapKind == ObstacleWrapKind.SideRestricted && !includeSideRestricted)
                continue;

            if (obs.IntersectsY(moved, sliceBottom - top + moved, tol) && obs.Bottom > moved)
                moved = obs.Bottom;
        }

        return moved;
    }

    /// <summary>
    /// Computes the single horizontal line band available in a container [containerLeft, containerLeft + containerWidth]
    /// at vertical slice [top, top + height).
    /// </summary>
    public LineBandInterval ComputeBand(
        double top,
        double height,
        double containerLeft,
        double containerWidth,
        ObstacleSidePolicy defaultSidePolicy = ObstacleSidePolicy.Largest,
        MultiObstaclePolicy multiPolicy = MultiObstaclePolicy.SequentialClamping,
        double? tolerance = null)
    {
        if (containerWidth <= 0.0)
            return new LineBandInterval(containerLeft, 0.0);

        if (IsEmpty)
            return new LineBandInterval(containerLeft, containerWidth);

        double tol = tolerance ?? EpsilonTolerance;
        double sliceHeight = Math.Max(height, HairlineTolerance);
        double sliceBottom = top + sliceHeight;

        double bandLeft = containerLeft;
        double bandRight = containerLeft + containerWidth;

        // Pass 1: CSS Floats (both left and right simultaneously narrowing the band)
        foreach (var obs in _obstacles)
        {
            if (!obs.IntersectsY(top, sliceBottom, tol))
                continue;

            if (obs.WrapKind == ObstacleWrapKind.CssFloatLeft)
                bandLeft = Math.Max(bandLeft, obs.Right);
            else if (obs.WrapKind == ObstacleWrapKind.CssFloatRight)
                bandRight = Math.Min(bandRight, obs.Left);
            else if (obs.WrapKind == ObstacleWrapKind.ClearanceOnly)
                return new LineBandInterval(bandLeft, 0.0);
        }

        if (bandRight <= bandLeft + tol)
            return new LineBandInterval(bandLeft, 0.0);

        // Pass 2: Side-restricted (Word wrapSquare) obstacles
        if (multiPolicy == MultiObstaclePolicy.GlobalIntervals)
        {
            var intervals = ComputeAllBands(top, height, bandLeft, bandRight - bandLeft, defaultSidePolicy, tol);
            if (intervals.Count == 0)
                return new LineBandInterval(bandLeft, 0.0);

            return defaultSidePolicy switch
            {
                ObstacleSidePolicy.LeftOnly => intervals[0],
                ObstacleSidePolicy.RightOnly => intervals[^1],
                _ => intervals.OrderByDescending(i => i.Width).First(),
            };
        }

        // Sequential clamping policy (exact historical Word/Writer TextWrapExclusions behavior)
        foreach (var obs in _obstacles)
        {
            if (obs.WrapKind != ObstacleWrapKind.SideRestricted || !obs.IntersectsY(top, sliceBottom, tol))
                continue;

            double blockedLeft = Math.Max(bandLeft, obs.Left);
            double blockedRight = Math.Min(bandRight, obs.Right);
            if (blockedRight <= blockedLeft + tol)
                continue;

            double roomLeft = blockedLeft - bandLeft;
            double roomRight = bandRight - blockedRight;
            var policy = obs.SidePolicy == ObstacleSidePolicy.Largest ? defaultSidePolicy : obs.SidePolicy;

            bool keepLeft = policy switch
            {
                ObstacleSidePolicy.LeftOnly => true,
                ObstacleSidePolicy.RightOnly => false,
                _ => roomLeft >= roomRight,
            };

            if (keepLeft)
                bandRight = blockedLeft;
            else
                bandLeft = blockedRight;

            if (bandRight <= bandLeft + tol)
                return new LineBandInterval(bandLeft, 0.0);
        }

        return new LineBandInterval(bandLeft, Math.Max(0.0, bandRight - bandLeft));
    }

    /// <summary>
    /// Computes all disjoint available horizontal bands in the container slice.
    /// Useful for wrap modes that allow text on both sides of obstacles.
    /// </summary>
    public List<LineBandInterval> ComputeAllBands(
        double top,
        double height,
        double containerLeft,
        double containerWidth,
        ObstacleSidePolicy sidePolicy = ObstacleSidePolicy.BothSides,
        double? tolerance = null)
    {
        if (containerWidth <= 0.0)
            return [];

        double tol = tolerance ?? EpsilonTolerance;
        double sliceHeight = Math.Max(height, HairlineTolerance);
        double sliceBottom = top + sliceHeight;

        var available = new List<(double Left, double Right)> { (containerLeft, containerLeft + containerWidth) };

        foreach (var obs in _obstacles)
        {
            if (!obs.IntersectsY(top, sliceBottom, tol))
                continue;

            if (obs.WrapKind == ObstacleWrapKind.ClearanceOnly)
                return [];

            double obsLeft = Math.Max(containerLeft, obs.Left);
            double obsRight = Math.Min(containerLeft + containerWidth, obs.Right);

            if (obsRight <= obsLeft + tol)
                continue;

            var next = new List<(double Left, double Right)>();
            foreach (var interval in available)
            {
                double overlapLeft = Math.Max(interval.Left, obsLeft);
                double overlapRight = Math.Min(interval.Right, obsRight);

                if (overlapRight <= overlapLeft + tol)
                {
                    next.Add(interval);
                    continue;
                }

                if (overlapLeft > interval.Left + tol)
                    next.Add((interval.Left, overlapLeft));

                if (interval.Right > overlapRight + tol)
                    next.Add((overlapRight, interval.Right));
            }

            available = next;
            if (available.Count == 0)
                break;
        }

        var result = new List<LineBandInterval>();
        foreach (var interval in available)
        {
            double width = interval.Right - interval.Left;
            if (width > tol)
                result.Add(new LineBandInterval(interval.Left, width));
        }

        return result;
    }

    /// <summary>
    /// Resolves line placement starting at <paramref name="top"/>, descending past obstacles
    /// until a band with at least <paramref name="minUsableWidth"/> room is found or retry limit is exhausted.
    /// </summary>
    public LineBandInterval ResolveLine(
        ref double top,
        double height,
        double containerLeft,
        double containerWidth,
        out double skip,
        int maxAttempts = 16,
        ClearanceDescentStep descentStep = ClearanceDescentStep.Deepest,
        BlockedBandFallback fallback = BlockedBandFallback.FullColumnAtCurrentY,
        double minUsableWidth = 1.0,
        ObstacleSidePolicy defaultSidePolicy = ObstacleSidePolicy.Largest,
        double? tolerance = null)
    {
        skip = 0.0;
        if (IsEmpty)
            return new LineBandInterval(containerLeft, containerWidth);

        double tol = tolerance ?? EpsilonTolerance;
        double initialTop = top;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            // Step 1: Outright clearance (clearance-only / top-and-bottom obstacles)
            double cleared = DeepestLowerEdge(top, height, includeSideRestricted: false, tol);
            if (cleared > top + tol)
            {
                skip += cleared - top;
                top = cleared;
                continue;
            }

            // Step 2: Query band at current top
            var band = ComputeBand(top, height, containerLeft, containerWidth, defaultSidePolicy, tolerance: tol);
            if (band.Width >= minUsableWidth)
                return band;

            // Step 3: Descend past obstacles blocking the line
            if (descentStep == ClearanceDescentStep.Deepest)
            {
                double moved = DeepestLowerEdge(top, height, includeSideRestricted: true, tol);
                if (moved <= top + tol)
                    break;

                skip += moved - top;
                top = moved;
            }
            else
            {
                double next = ShallowestLowerEdge(top, height, tol);
                if (double.IsNaN(next) || next <= top + tol)
                    break;

                skip += next - top;
                top = next;
            }
        }

        // Fallback policy on exhaustion
        return fallback switch
        {
            BlockedBandFallback.FullColumnAtInitialY => ResetToInitial(initialTop, ref top, out skip, containerLeft, containerWidth),
            BlockedBandFallback.ZeroWidth => new LineBandInterval(containerLeft, 0.0),
            _ => new LineBandInterval(containerLeft, containerWidth),
        };

        static LineBandInterval ResetToInitial(double initTop, ref double curTop, out double s, double cLeft, double cWidth)
        {
            curTop = initTop;
            s = 0.0;
            return new LineBandInterval(cLeft, cWidth);
        }
    }
}
