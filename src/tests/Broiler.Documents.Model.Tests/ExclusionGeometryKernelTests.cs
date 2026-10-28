using System;
using System.Collections.Generic;
using System.Diagnostics;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Exclusions;
using Xunit;

namespace Broiler.Documents.Model.Tests;

public sealed class ExclusionGeometryKernelTests
{
    #region 1. ExclusionRect Validation & Geometry

    [Fact]
    public void ExclusionRect_ValidCoordinates_CalculatesCorrectDimensions()
    {
        var rect = ExclusionRect.FromLTRB(10, 20, 110, 80);
        Assert.True(rect.IsValid);
        Assert.False(rect.IsEmpty);
        Assert.Equal(10, rect.Left);
        Assert.Equal(20, rect.Top);
        Assert.Equal(110, rect.Right);
        Assert.Equal(80, rect.Bottom);
        Assert.Equal(100, rect.Width);
        Assert.Equal(60, rect.Height);
        Assert.Equal(10, rect.X);
        Assert.Equal(20, rect.Y);
    }

    [Theory]
    [InlineData(double.NaN, 0, 100, 100)]
    [InlineData(0, double.NaN, 100, 100)]
    [InlineData(0, 0, double.PositiveInfinity, 100)]
    [InlineData(0, 0, 100, double.NegativeInfinity)]
    [InlineData(100, 0, 50, 100)] // Inverted X: right < left
    [InlineData(0, 100, 100, 50)] // Inverted Y: bottom < top
    [InlineData(50, 0, 50, 100)]  // Zero width
    [InlineData(0, 50, 100, 50)]  // Zero height
    public void ExclusionRect_InvalidOrDegenerate_IsReportedInvalidAndEmpty(double l, double t, double r, double b)
    {
        var rect = ExclusionRect.FromLTRB(l, t, r, b);
        Assert.False(rect.IsValid);
        Assert.True(rect.IsEmpty);
    }

    [Fact]
    public void ExclusionRect_Margins_ExpandsCorrectly()
    {
        var rect = ExclusionRect.FromLTRB(20, 30, 80, 90).WithMargins(5, 10, 15, 20);
        Assert.Equal(15, rect.Left);
        Assert.Equal(20, rect.Top);
        Assert.Equal(95, rect.Right);
        Assert.Equal(110, rect.Bottom);
        Assert.Equal(80, rect.Width);
        Assert.Equal(90, rect.Height);
    }

    [Fact]
    public void ExclusionRect_TouchingEdges_DoNotIntersect()
    {
        var rect = ExclusionRect.FromLTRB(10, 20, 100, 80);

        // Touching top edge (line bottom == obstacle top)
        Assert.False(rect.IntersectsY(0, 20));

        // Touching bottom edge (line top == obstacle bottom)
        Assert.False(rect.IntersectsY(80, 100));

        // Touching left edge
        Assert.False(rect.IntersectsX(0, 10));

        // Touching right edge
        Assert.False(rect.IntersectsX(100, 120));

        // Interior overlaps DO intersect
        Assert.True(rect.IntersectsY(19.99, 20.01));
        Assert.True(rect.IntersectsY(79.99, 80.01));
        Assert.True(rect.IntersectsX(9.99, 10.01));
        Assert.True(rect.IntersectsX(99.99, 100.01));
    }

    #endregion

    #region 2. Neutral Query Engine: CSS Floats & Word Wrapping

    [Fact]
    public void EmptyEngine_ReturnsFullBand()
    {
        var engine = new ExclusionBandQueryEngine();
        Assert.True(engine.IsEmpty);
        Assert.Equal(0, engine.Count);

        var band = engine.ComputeBand(top: 10, height: 15, containerLeft: 0, containerWidth: 500);
        Assert.Equal(0, band.Left);
        Assert.Equal(500, band.Width);
        Assert.Equal(500, band.Right);
    }

    [Fact]
    public void CssFloats_LeftAndRight_NarrowSingleBandSimultaneously()
    {
        var engine = new ExclusionBandQueryEngine();
        // Left float taking 0..120
        engine.Add(ExclusionObstacle.CssFloat(ExclusionRect.FromLTRB(0, 0, 120, 50), isLeft: true));
        // Right float taking 380..500
        engine.Add(ExclusionObstacle.CssFloat(ExclusionRect.FromLTRB(380, 0, 500, 70), isLeft: false));

        // At slice 10..25: both floats active
        var band = engine.ComputeBand(top: 10, height: 15, containerLeft: 0, containerWidth: 500);
        Assert.Equal(120, band.Left);
        Assert.Equal(260, band.Width); // 380 - 120
        Assert.Equal(380, band.Right);

        // At slice 55..65: left float cleared, right float still active
        var bandLower = engine.ComputeBand(top: 55, height: 10, containerLeft: 0, containerWidth: 500);
        Assert.Equal(0, bandLower.Left);
        Assert.Equal(380, bandLower.Width);

        // At slice 75..90: both cleared
        var bandClear = engine.ComputeBand(top: 75, height: 15, containerLeft: 0, containerWidth: 500);
        Assert.Equal(0, bandClear.Left);
        Assert.Equal(500, bandClear.Width);
    }

    [Fact]
    public void WordSquareWrap_CentralObstacle_ChoosesLargestSideByDefault()
    {
        var engine = new ExclusionBandQueryEngine();
        // Central obstacle at Left=100, Right=250 in a 500-wide column
        // Room left = 100 - 0 = 100; Room right = 500 - 250 = 250.
        // Largest side is Right!
        engine.Add(ExclusionObstacle.Square(ExclusionRect.FromLTRB(100, 0, 250, 100), ObstacleSidePolicy.Largest));

        var band = engine.ComputeBand(top: 10, height: 15, containerLeft: 0, containerWidth: 500);
        Assert.Equal(250, band.Left);
        Assert.Equal(250, band.Width);
    }

    [Fact]
    public void WordSquareWrap_CentralObstacle_RespectsForcedLeftAndRightSide()
    {
        var engineLeft = new ExclusionBandQueryEngine();
        engineLeft.Add(ExclusionObstacle.Square(ExclusionRect.FromLTRB(100, 0, 250, 100), ObstacleSidePolicy.LeftOnly));

        var bandLeft = engineLeft.ComputeBand(top: 10, height: 15, containerLeft: 0, containerWidth: 500);
        Assert.Equal(0, bandLeft.Left);
        Assert.Equal(100, bandLeft.Width);

        var engineRight = new ExclusionBandQueryEngine();
        engineRight.Add(ExclusionObstacle.Square(ExclusionRect.FromLTRB(100, 0, 250, 100), ObstacleSidePolicy.RightOnly));

        var bandRight = engineRight.ComputeBand(top: 10, height: 15, containerLeft: 0, containerWidth: 500);
        Assert.Equal(250, bandRight.Left);
        Assert.Equal(250, bandRight.Width);
    }

    [Fact]
    public void BothSidesWrapping_ProducesAllDisjointFreeIntervals()
    {
        var engine = new ExclusionBandQueryEngine();
        // Central obstacle at 150..250 in a 400-wide container
        engine.Add(ExclusionObstacle.Square(ExclusionRect.FromLTRB(150, 0, 250, 100), ObstacleSidePolicy.BothSides));

        var intervals = engine.ComputeAllBands(top: 20, height: 15, containerLeft: 0, containerWidth: 400);
        Assert.Equal(2, intervals.Count);

        // Left interval: 0..150
        Assert.Equal(0, intervals[0].Left);
        Assert.Equal(150, intervals[0].Width);

        // Right interval: 250..400
        Assert.Equal(250, intervals[1].Left);
        Assert.Equal(150, intervals[1].Width);
    }

    #endregion

    #region 3. Clearance, Touching Edges & Zero-Height Queries

    [Fact]
    public void ZeroHeightLine_TreatedAsHairline_DetectsObstaclesAtSameY()
    {
        var engine = new ExclusionBandQueryEngine();
        engine.Add(ExclusionObstacle.Clearance(ExclusionRect.FromLTRB(0, 50, 200, 100)));

        // Line with height 0 starting at 60 (inside obstacle)
        var intersecting = engine.QueryIntersecting(top: 60, height: 0);
        Assert.Single(intersecting);

        // Line with height 0 starting at exactly 50 (top edge of obstacle)
        var intersectingTopEdge = engine.QueryIntersecting(top: 50, height: 0);
        Assert.Single(intersectingTopEdge);

        // Line with height 0 starting at exactly 100 (bottom edge of obstacle)
        // Obstacle is [50, 100). Slice starting at 100 is below the obstacle!
        var intersectingBottomEdge = engine.QueryIntersecting(top: 100, height: 0);
        Assert.Empty(intersectingBottomEdge);
    }

    [Fact]
    public void ClearanceObstacle_BlocksBandCompletely()
    {
        var engine = new ExclusionBandQueryEngine();
        engine.Add(ExclusionObstacle.Clearance(ExclusionRect.FromLTRB(50, 20, 150, 60)));

        var band = engine.ComputeBand(top: 30, height: 10, containerLeft: 0, containerWidth: 500);
        Assert.Equal(0, band.Width);
    }

    [Fact]
    public void ShallowestVsDeepestLowerEdge_DistinguishesCssAndDocumentClearance()
    {
        var engine = new ExclusionBandQueryEngine();
        // Obstacle 1: Bottom at 40
        engine.Add(ExclusionObstacle.CssFloat(ExclusionRect.FromLTRB(0, 0, 50, 40), isLeft: true));
        // Obstacle 2: Bottom at 80
        engine.Add(ExclusionObstacle.CssFloat(ExclusionRect.FromLTRB(0, 0, 70, 80), isLeft: true));

        // At slice 10..20: both are overlapping
        double shallowest = engine.ShallowestLowerEdge(top: 10, height: 10);
        double deepest = engine.DeepestLowerEdge(top: 10, height: 10);

        Assert.Equal(40, shallowest); // CSS step: next band widening
        Assert.Equal(80, deepest);    // Document step: push below all
    }

    [Fact]
    public void OutOfColumnObstacles_DoNotConstrainBand()
    {
        var engine = new ExclusionBandQueryEngine();
        // Obstacle entirely to the left in the margin (-60..-20)
        engine.Add(ExclusionObstacle.Square(ExclusionRect.FromLTRB(-60, 0, -20, 50)));
        // Obstacle entirely to the right (520..600 in a 500-wide column)
        engine.Add(ExclusionObstacle.Square(ExclusionRect.FromLTRB(520, 0, 600, 50)));

        var band = engine.ComputeBand(top: 10, height: 15, containerLeft: 0, containerWidth: 500);
        Assert.Equal(0, band.Left);
        Assert.Equal(500, band.Width);
    }

    #endregion

    #region 4. Bounded Retry & Pathological Resistance

    [Fact]
    public void ResolveLine_CascadingObstacles_StepsDownCorrectly()
    {
        var engine = new ExclusionBandQueryEngine();
        // Stacked clearance obstacles: [0..30], [30..60], [60..90]
        engine.Add(ExclusionObstacle.Clearance(ExclusionRect.FromLTRB(0, 0, 500, 30)));
        engine.Add(ExclusionObstacle.Clearance(ExclusionRect.FromLTRB(0, 30, 500, 60)));
        engine.Add(ExclusionObstacle.Clearance(ExclusionRect.FromLTRB(0, 60, 500, 90)));

        double top = 0;
        var band = engine.ResolveLine(
            ref top,
            height: 10,
            containerLeft: 0,
            containerWidth: 500,
            out double skip,
            maxAttempts: 8);

        Assert.Equal(90, top);
        Assert.Equal(90, skip);
        Assert.Equal(0, band.Left);
        Assert.Equal(500, band.Width);
    }

    [Fact]
    public void ResolveLine_AdversarialDenseOverlaps_TerminatesWithinAttemptLimit()
    {
        var engine = new ExclusionBandQueryEngine();
        // Add 200 overlapping full-width obstacles covering 0 to 2000
        for (int i = 0; i < 200; i++)
        {
            engine.Add(ExclusionObstacle.Clearance(ExclusionRect.FromLTRB(0, i * 10, 500, (i + 2) * 10)));
        }

        double top = 0;
        int maxAttempts = 16;
        var sw = Stopwatch.StartNew();

        // Must terminate within maxAttempts without hanging or runaway loop
        var band = engine.ResolveLine(
            ref top,
            height: 10,
            containerLeft: 0,
            containerWidth: 500,
            out double skip,
            maxAttempts: maxAttempts,
            fallback: BlockedBandFallback.FullColumnAtCurrentY);

        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 500, $"Expected fast termination, took {sw.ElapsedMilliseconds}ms");
        Assert.True(skip > 0);
        Assert.Equal(500, band.Width);
    }

    [Fact]
    public void RepresentativePerformance_EmptyVsManyObstacles_IsSubMillisecond()
    {
        var emptyEngine = new ExclusionBandQueryEngine();
        var denseEngine = new ExclusionBandQueryEngine();

        for (int i = 0; i < 100; i++)
        {
            denseEngine.Add(ExclusionObstacle.Square(
                ExclusionRect.FromLTRB(50 + (i % 5) * 40, i * 15, 120 + (i % 5) * 40, (i + 2) * 15),
                ObstacleSidePolicy.Largest));
        }

        // Warm up JIT
        for (int i = 0; i < 100; i++)
        {
            emptyEngine.ComputeBand(i * 10, 15, 0, 500);
            denseEngine.ComputeBand(i * 10, 15, 0, 500);
        }

        var sw = Stopwatch.StartNew();
        const int iterations = 1000;
        for (int i = 0; i < iterations; i++)
        {
            denseEngine.ComputeBand(50, 15, 0, 500);
        }
        sw.Stop();

        double usPerQuery = (sw.Elapsed.TotalMicroseconds) / iterations;
        // Each query over 100 obstacles should take mere microseconds (< 50 us)
        Assert.True(usPerQuery < 100, $"Query too slow: {usPerQuery:F2} µs per query");
    }

    #endregion
}
