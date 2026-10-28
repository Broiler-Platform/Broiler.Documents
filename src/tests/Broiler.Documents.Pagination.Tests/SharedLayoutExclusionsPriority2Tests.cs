using System;
using System.Collections.Generic;
using System.Linq;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Exclusions;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Resources;
using Xunit;

namespace Broiler.Documents.Pagination.Tests;

public sealed class SharedLayoutExclusionsPriority2Tests
{
    #region 1. CssFloatBandAdapter Characterization & Parity

    [Fact]
    public void CssFloatBandAdapter_Empty_ReturnsContentBounds()
    {
        var adapter = new CssFloatBandAdapter();
        Assert.True(adapter.IsEmpty);
        Assert.Equal(0, adapter.FloatCount);

        Assert.Equal(20, adapter.LeftAt(10, 15, contentLeft: 20));
        Assert.Equal(480, adapter.RightAt(10, 15, contentRight: 480));
        Assert.True(double.IsNaN(adapter.NextBandBottom(10, 15)));
        Assert.Equal(10, adapter.DropLineBelowNarrowBands(10, 15, 20, 480, needed: 100));
    }

    [Fact]
    public void CssFloatBandAdapter_LeftAndRightFloats_ConstrainBothEdges()
    {
        var adapter = new CssFloatBandAdapter();
        // Left float from X=0 to X=150, Y=0 to Y=60
        adapter.AddLeftFloat(ExclusionRect.FromLTRB(0, 0, 150, 60));
        // Right float from X=350 to X=500, Y=0 to Y=80
        adapter.AddRightFloat(ExclusionRect.FromLTRB(350, 0, 500, 80));

        Assert.False(adapter.IsEmpty);
        Assert.Equal(2, adapter.FloatCount);

        // At Y=10, H=15: left edge is 150, right edge is 350
        Assert.Equal(150, adapter.LeftAt(10, 15, contentLeft: 0));
        Assert.Equal(350, adapter.RightAt(10, 15, contentRight: 500));

        // At Y=65, H=15: left float has cleared, right float still active
        Assert.Equal(0, adapter.LeftAt(65, 15, contentLeft: 0));
        Assert.Equal(350, adapter.RightAt(65, 15, contentRight: 500));

        // At Y=85, H=15: both cleared
        Assert.Equal(0, adapter.LeftAt(85, 15, contentLeft: 0));
        Assert.Equal(500, adapter.RightAt(85, 15, contentRight: 500));
    }

    [Fact]
    public void CssFloatBandAdapter_NextBandBottom_FindsShallowestFloatBottom()
    {
        var adapter = new CssFloatBandAdapter();
        adapter.AddLeftFloat(ExclusionRect.FromLTRB(0, 0, 100, 45));
        adapter.AddRightFloat(ExclusionRect.FromLTRB(400, 0, 500, 75));

        // At Y=10, shallowest float bottom is 45
        Assert.Equal(45, adapter.NextBandBottom(10, 15));

        // At Y=50, shallowest float bottom is 75
        Assert.Equal(75, adapter.NextBandBottom(50, 15));

        // At Y=80, no floats overlap
        Assert.True(double.IsNaN(adapter.NextBandBottom(80, 15)));
    }

    [Fact]
    public void CssFloatBandAdapter_DropLineBelowNarrowBands_StepsPastNarrowFloats()
    {
        var adapter = new CssFloatBandAdapter();
        // Left float takes 0..300 for Y=0..50
        adapter.AddLeftFloat(ExclusionRect.FromLTRB(0, 0, 300, 50));
        // Right float takes 350..500 for Y=0..80
        adapter.AddRightFloat(ExclusionRect.FromLTRB(350, 0, 500, 80));

        // Between 0 and 50, available width is 350 - 300 = 50.
        // If line needs 100, it cannot fit!
        // Step 1 drops to NextBandBottom = 50.
        // Between 50 and 80, available width is 350 - 0 = 350 >= 100.
        // Line should settle at Y=50.
        double settledY = adapter.DropLineBelowNarrowBands(
            top: 0,
            height: 15,
            contentLeft: 0,
            contentRight: 500,
            needed: 100);

        Assert.Equal(50, settledY);
    }

    [Fact]
    public void CssFloatBandAdapter_Snapshot_IsIsolatedFromSourceMutations()
    {
        var adapter = new CssFloatBandAdapter();
        adapter.AddLeftFloat(ExclusionRect.FromLTRB(0, 0, 100, 50));

        var snapshot = adapter.CreateSnapshot();
        Assert.Equal(1, snapshot.FloatCount);

        // Mutate original adapter
        adapter.AddRightFloat(ExclusionRect.FromLTRB(400, 0, 500, 80));
        Assert.Equal(2, adapter.FloatCount);

        // Snapshot remains untouched
        Assert.Equal(1, snapshot.FloatCount);
        Assert.Equal(500, snapshot.RightAt(10, 15, contentRight: 500));
    }

    #endregion

    #region 2. TextWrapExclusions Adapter Equivalence & Word Processing Wrap

    [Fact]
    public void TextWrapExclusions_AddShape_AppliesMarginsAndScale()
    {
        var exclusions = new TextWrapExclusions();
        var shape = new DocumentShape(
            paragraphIndex: 0,
            offsetX: 20,
            offsetY: 0,
            width: 100,
            height: 50,
            wrap: ShapeWrap.Square,
            wrapDistance: 10);

        exclusions.Add(shape, top: 40, scale: 2);

        // With scale=2, margin = 10 * 2 = 20.
        // top = 40. Top edge = 40 - 20 = 20.
        // Bottom edge = 40 + (50 * 2) + 20 = 160.
        // Left edge = (20 * 2) - 20 = 20.
        // Right edge = ((20 + 100) * 2) + 20 = 260.
        var obs = Assert.Single(exclusions.Kernel.Obstacles);
        Assert.Equal(20, obs.Left);
        Assert.Equal(20, obs.Top);
        Assert.Equal(260, obs.Right);
        Assert.Equal(160, obs.Bottom);
    }

    [Fact]
    public void TextWrapExclusions_SequentialCentralObstacles_CalculatesDeterministicBands()
    {
        var exclusions = new TextWrapExclusions();
        // Obstacle 1: Left 50..150
        exclusions.AddBox(top: 0, bottom: 60, left: 50, right: 150, wrap: ShapeWrap.Square, side: WrapSide.Largest);
        // Obstacle 2: Right 350..450
        exclusions.AddBox(top: 0, bottom: 60, left: 350, right: 450, wrap: ShapeWrap.Square, side: WrapSide.Largest);

        // Column is 500 wide.
        // Obs 1: blocked 50..150. RoomLeft = 50 - 0 = 50. RoomRight = 500 - 150 = 350. Keep right => band becomes 150..500.
        // Obs 2: blocked 350..450. RoomLeft = 350 - 150 = 200. RoomRight = 500 - 450 = 50. Keep left => band becomes 150..350.
        var band = exclusions.Band(top: 10, bottom: 25, columnWidth: 500);
        Assert.Equal(150, band.Left);
        Assert.Equal(200, band.Width);
    }

    [Fact]
    public void TextWrapExclusions_Resolve_DescendsBelowTopAndBottomShape()
    {
        var exclusions = new TextWrapExclusions();
        exclusions.AddBox(top: 0, bottom: 75, left: 0, right: 200, wrap: ShapeWrap.TopAndBottom);

        double top = 0;
        var band = exclusions.Resolve(ref top, height: 12, columnWidth: 500, out double skip);

        Assert.Equal(75, top);
        Assert.Equal(75, skip);
        Assert.Equal(0, band.Left);
        Assert.Equal(500, band.Width);
    }

    #endregion

    #region 3. Integration with DocumentLayout

    private static LayoutResult RunLayout(RichTextDocument document, double width = 300, double height = 300)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(
            document,
            new DocumentPageBox(width, height, 10, 10, 10, 10));
    }

    [Fact]
    public void DocumentLayout_SquareAndTopAndBottomShapes_LayOutWithSharedExclusions()
    {
        var shapeSquare = new DocumentShape(
            paragraphIndex: 0,
            offsetX: 0,
            offsetY: 0,
            width: 80,
            height: 40,
            wrap: ShapeWrap.Square,
            anchorPage: 1);

        var shapeTopBottom = new DocumentShape(
            paragraphIndex: 1,
            offsetX: 0,
            offsetY: 0,
            width: 80,
            height: 30,
            wrap: ShapeWrap.TopAndBottom,
            anchorPage: 1);

        var doc = RichTextDocument.FromParagraphs([
            RichTextParagraph.Plain("First paragraph with text wrapping around square shape."),
            RichTextParagraph.Plain("Second paragraph pushed below top-and-bottom shape.")
        ]).WithShapes([shapeSquare, shapeTopBottom]);

        var result = RunLayout(doc, width: 400, height: 400);
        var page = Assert.Single(result.Pages);

        Assert.NotEmpty(page.Lines);
        // The first line beside the square shape should have its left edge moved past the shape (width 80)
        var firstLine = page.Lines[0];
        Assert.True(firstLine.Pieces.Min(p => p.X) >= 80 - 0.01);
    }

    #endregion
}
