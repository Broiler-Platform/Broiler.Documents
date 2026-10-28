using System;

namespace Broiler.Documents.Model.Exclusions;

/// <summary>
/// Policy adapter adapting the neutral <see cref="ExclusionBandQueryEngine"/> to CSS float semantics
/// (CSS2.1 §9.5, §9.4.2), mirroring the behavior of Layout's LineFloatBands.
/// </summary>
public sealed class CssFloatBandAdapter
{
    private readonly ExclusionBandQueryEngine _kernel;

    public ExclusionBandQueryEngine Kernel => _kernel;

    public bool IsEmpty => _kernel.IsEmpty;

    public int FloatCount => _kernel.Count;

    public CssFloatBandAdapter()
    {
        _kernel = new ExclusionBandQueryEngine();
    }

    public CssFloatBandAdapter(ExclusionBandQueryEngine kernel)
    {
        _kernel = kernel;
    }

    /// <summary>Creates an isolated snapshot of this adapter and its underlying kernel.</summary>
    public CssFloatBandAdapter CreateSnapshot() =>
        new(_kernel.CreateSnapshot());

    /// <summary>Adds a CSS float with its resolved margin box.</summary>
    public void AddFloat(ExclusionRect marginBox, bool isLeft)
    {
        if (marginBox.IsEmpty || !marginBox.IsValid)
            return;

        _kernel.Add(ExclusionObstacle.CssFloat(marginBox, isLeft));
    }

    /// <summary>Adds a CSS float: left obstacle.</summary>
    public void AddLeftFloat(ExclusionRect marginBox) =>
        AddFloat(marginBox, isLeft: true);

    /// <summary>Adds a CSS float: right obstacle.</summary>
    public void AddRightFloat(ExclusionRect marginBox) =>
        AddFloat(marginBox, isLeft: false);

    /// <summary>
    /// The leftmost x a line occupying [top, top + height) may use, given that it may not
    /// start before <paramref name="contentLeft"/>.
    /// </summary>
    public double LeftAt(double top, double height, double contentLeft)
    {
        if (IsEmpty)
            return contentLeft;

        double edge = contentLeft;
        double sliceHeight = Math.Max(height, _kernel.HairlineTolerance);
        double sliceBottom = top + sliceHeight;

        foreach (var obs in _kernel.Obstacles)
        {
            if (obs.WrapKind == ObstacleWrapKind.CssFloatLeft && obs.IntersectsY(top, sliceBottom, _kernel.EpsilonTolerance))
                edge = Math.Max(edge, obs.Right);
        }

        return edge;
    }

    /// <summary>
    /// The rightmost x a line occupying [top, top + height) may use, given that it may not
    /// end after <paramref name="contentRight"/>.
    /// </summary>
    public double RightAt(double top, double height, double contentRight)
    {
        if (IsEmpty)
            return contentRight;

        double edge = contentRight;
        double sliceHeight = Math.Max(height, _kernel.HairlineTolerance);
        double sliceBottom = top + sliceHeight;

        foreach (var obs in _kernel.Obstacles)
        {
            if (obs.WrapKind == ObstacleWrapKind.CssFloatRight && obs.IntersectsY(top, sliceBottom, _kernel.EpsilonTolerance))
                edge = Math.Min(edge, obs.Left);
        }

        return edge;
    }

    /// <summary>
    /// The next y below <paramref name="top"/> at which the band gets wider — the bottom margin
    /// edge of the shallowest float still overlapping the line — or <see cref="double.NaN"/> when
    /// no float does.
    /// </summary>
    public double NextBandBottom(double top, double height) =>
        _kernel.ShallowestLowerEdge(top, height);

    /// <summary>
    /// CSS2.1 §9.5: moves a line down past the floats beside it when <paramref name="needed"/>
    /// does not fit in the band there, and returns the y it settles at.
    /// Walks up to <paramref name="maxSteps"/> shallowest float bottoms.
    /// </summary>
    public double DropLineBelowNarrowBands(
        double top,
        double height,
        double contentLeft,
        double contentRight,
        double needed,
        int maxSteps = 64)
    {
        if (needed <= 0 || contentRight >= 90999 || IsEmpty)
            return top;

        for (int step = 0; step < maxSteps; step++)
        {
            double left = LeftAt(top, height, contentLeft);
            double right = RightAt(top, height, contentRight);

            if (right - left >= needed || (left <= contentLeft && right >= contentRight))
                return top;

            double next = NextBandBottom(top, height);
            if (double.IsNaN(next) || next <= top)
                return top;

            top = next;
        }

        return top;
    }
}
