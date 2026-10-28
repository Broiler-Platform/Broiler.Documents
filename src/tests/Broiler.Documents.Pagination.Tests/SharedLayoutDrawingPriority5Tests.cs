using System;
using System.Collections.Generic;
using System.Linq;
using Broiler.Documents.Model.Fonts;
using Broiler.Documents.Model.Text;
using Broiler.Documents.Pagination.Drawing;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.RenderList;
using Xunit;

namespace Broiler.Documents.Pagination.Tests;

public sealed class SharedLayoutDrawingPriority5Tests
{
    private static readonly BColor Red = new(255, 0, 0);
    private static readonly BColor Blue = new(0, 0, 255);
    private static readonly BColor Black = new(0, 0, 0);

    [Fact]
    public void DisplayListInventory_CoversAll23Primitives_WithStrictMetadata()
    {
        var mappings = DisplayListInventory.Mappings;
        Assert.Equal(23, mappings.Count);

        var distinctTypes = mappings.Select(m => m.DisplayItemType).Distinct().ToList();
        Assert.Equal(23, distinctTypes.Count);

        foreach (var mapping in mappings)
        {
            Assert.False(string.IsNullOrWhiteSpace(mapping.DisplayItemType));
            Assert.False(string.IsNullOrWhiteSpace(mapping.TargetRenderCommand));
            Assert.False(string.IsNullOrWhiteSpace(mapping.CoordinateRule));
            Assert.False(string.IsNullOrWhiteSpace(mapping.ClippingRule));
            Assert.False(string.IsNullOrWhiteSpace(mapping.TransformRule));
            Assert.False(string.IsNullOrWhiteSpace(mapping.FallbackOutcome));
            Assert.False(string.IsNullOrWhiteSpace(mapping.Notes));
        }

        // Verify resource limits
        Assert.Equal(4096, DisplayListInventory.MaxRasterDimension);
        Assert.Equal(300.0, DisplayListInventory.DefaultRasterDpi);
        Assert.Equal(96.0, DisplayListInventory.ScreenDpi);
        Assert.Equal(64, DisplayListInventory.MaxClipDepth);
        Assert.Equal(64, DisplayListInventory.MaxTransformDepth);
    }

    [Fact]
    public void CssPointsBridge_ConvertsUnitsAccurately()
    {
        // 96 px @ 96 DPI == 1 inch == 72 pt @ 72 DPI
        Assert.Equal(72.0, CssPointsBridge.PixelsToPoints(96.0));
        Assert.Equal(96.0, CssPointsBridge.PointsToPixels(72.0));

        // 0 px -> 0 pt
        Assert.Equal(0.0, CssPointsBridge.PixelsToPoints(0));
        Assert.Equal(0.0, CssPointsBridge.PointsToPixels(0));

        // Rect conversion
        var rectPt = CssPointsBridge.RectToPoints(100, 200, 300, 400);
        Assert.Equal(75.0, rectPt.Left);
        Assert.Equal(150.0, rectPt.Top);
        Assert.Equal(225.0, rectPt.Width);
        Assert.Equal(300.0, rectPt.Height);
    }

    [Fact]
    public void DisplayListBridge_ReplaysFillRect_WithExactScale()
    {
        var pageSize = new BSize(612, 792);
        var items = new BridgedDisplayItem[]
        {
            new BridgedFillRect(96, 96, 192, 192, Red)
        };

        var page = DisplayListBridge.Replay(pageSize, items);
        Assert.Equal(612, page.Size.Width);
        Assert.Equal(792, page.Size.Height);

        Assert.Single(page.Commands);
        var fill = Assert.IsType<BRenderCommand.FillRect>(page.Commands[0]);
        Assert.Equal(Red, fill.Color);
        Assert.Equal(72.0, fill.Rect.Left);
        Assert.Equal(72.0, fill.Rect.Top);
        Assert.Equal(144.0, fill.Rect.Width);
        Assert.Equal(144.0, fill.Rect.Height);
    }

    [Fact]
    public void DisplayListBridge_ReplaysBorder_DecomposedToSideRectangles()
    {
        var pageSize = new BSize(500, 500);
        var items = new BridgedDisplayItem[]
        {
            new BridgedBorder(
                X: 100, Y: 100, Width: 200, Height: 200,
                TopWidth: 10, RightWidth: 10, BottomWidth: 10, LeftWidth: 10,
                TopColor: Red, RightColor: Blue, BottomColor: Red, LeftColor: Blue)
        };

        var page = DisplayListBridge.Replay(pageSize, items);
        Assert.Equal(4, page.Commands.Count);

        foreach (var cmd in page.Commands)
        {
            Assert.IsType<BRenderCommand.FillRect>(cmd);
        }
    }

    [Fact]
    public void DisplayListBridge_ReplaysText_AndShadowPass()
    {
        var pageSize = new BSize(500, 500);
        var items = new BridgedDisplayItem[]
        {
            new BridgedText(
                "Hello World",
                FontFamily: "Helvetica",
                FontSizePixels: 16,
                Color: Black,
                OriginX: 50,
                OriginY: 100,
                Bold: true,
                Italic: false,
                ShadowOffsetX: 2,
                ShadowOffsetY: 2,
                ShadowColor: new BColor(128, 128, 128))
        };

        var page = DisplayListBridge.Replay(pageSize, items);
        // Should emit 2 commands: shadow first, text second
        Assert.Equal(2, page.Commands.Count);

        var shadowCmd = Assert.IsType<BRenderCommand.DrawText>(page.Commands[0]);
        Assert.Equal("Hello World", shadowCmd.Text.Text);
        Assert.Equal(new BColor(128, 128, 128), shadowCmd.Text.Color);

        var mainCmd = Assert.IsType<BRenderCommand.DrawText>(page.Commands[1]);
        Assert.Equal("Hello World", mainCmd.Text.Text);
        Assert.Equal(Black, mainCmd.Text.Color);
        Assert.Equal(12.0, mainCmd.Text.Font.Size); // 16 px * 0.75 = 12 pt
    }

    [Fact]
    public void DisplayListBridge_ReplaysImage_WithOwnershipAndDeduplication()
    {
        var pageSize = new BSize(500, 500);
        var rawPixels = new byte[4 * 4 * 4]; // 4x4 RGBA
        var pixelBuffer = new BPixelBuffer(4, 4, rawPixels);

        var items = new BridgedDisplayItem[]
        {
            new BridgedImage(pixelBuffer, 0, 0, 4, 4, 10, 10, 40, 40),
            new BridgedImage(pixelBuffer, 0, 0, 4, 4, 100, 100, 40, 40)
        };

        var page = DisplayListBridge.Replay(pageSize, items);
        Assert.Equal(2, page.Commands.Count);

        var img1 = Assert.IsType<BRenderCommand.DrawImage>(page.Commands[0]);
        var img2 = Assert.IsType<BRenderCommand.DrawImage>(page.Commands[1]);

        // Same image buffer reused should map to the same image handle
        Assert.Equal(img1.Image, img2.Image);
        Assert.Single(page.Images);
    }

    [Fact]
    public void DisplayListBridge_AutoBalances_UnclosedClipsAndTransforms()
    {
        var pageSize = new BSize(500, 500);
        var diagnostics = new DrawingDiagnosticSink();

        var items = new BridgedDisplayItem[]
        {
            new BridgedClip(10, 10, 100, 100),
            new BridgedTransform(1, 0, 0, 1, 50, 50),
            new BridgedFillRect(0, 0, 20, 20, Red)
            // Unclosed clip and transform!
        };

        var page = DisplayListBridge.Replay(pageSize, items, diagnostics);

        // Verify commands: PushClip, PushTransform, FillRect, PopTransform, PopClip
        Assert.Equal(5, page.Commands.Count);
        Assert.IsType<BRenderCommand.PushClip>(page.Commands[0]);
        Assert.IsType<BRenderCommand.PushTransform>(page.Commands[1]);
        Assert.IsType<BRenderCommand.FillRect>(page.Commands[2]);
        Assert.IsType<BRenderCommand.PopTransform>(page.Commands[3]);
        Assert.IsType<BRenderCommand.PopClip>(page.Commands[4]);

        // Diagnostics should report balancing pops
        Assert.True(diagnostics.HasWarnings);
        Assert.Contains(diagnostics.Diagnostics, d => d.Code == "UNBALANCED_CLIP_RESTORED");
        Assert.Contains(diagnostics.Diagnostics, d => d.Code == "UNBALANCED_TRANSFORM_RESTORED");
    }

    [Fact]
    public void DisplayListBridge_Diagnoses_UnsupportedEffectsSafely()
    {
        var pageSize = new BSize(500, 500);
        var diagnostics = new DrawingDiagnosticSink();

        var items = new BridgedDisplayItem[]
        {
            new BridgedUnsupportedEffect("mix-blend-mode", "multiply"),
            new BridgedUnsupportedEffect("filter", "blur(5px)"),
            new BridgedFillRect(0, 0, 10, 10, Red)
        };

        var page = DisplayListBridge.Replay(pageSize, items, diagnostics);

        // Drawing continues without failing
        Assert.Single(page.Commands);
        Assert.IsType<BRenderCommand.FillRect>(page.Commands[0]);

        // Diagnostics captured both unsupported effects
        Assert.Equal(2, diagnostics.Diagnostics.Count);
        Assert.Equal("mix-blend-mode", diagnostics.Diagnostics[0].ItemKind);
        Assert.Equal("filter", diagnostics.Diagnostics[1].ItemKind);
    }

    [Fact]
    public void DisplayListBridge_ReplaysPolygon_AsTriangleFan()
    {
        var pageSize = new BSize(500, 500);
        var points = new (double X, double Y)[]
        {
            (0, 0),
            (100, 0),
            (100, 100),
            (0, 100)
        };

        var items = new BridgedDisplayItem[]
        {
            new BridgedPolygon(points, Fill: Red, Stroke: BColor.Empty)
        };

        var page = DisplayListBridge.Replay(pageSize, items);
        // 4-vertex polygon triangulates into 2 triangles
        Assert.Equal(2, page.Commands.Count);
        Assert.IsType<BRenderCommand.FillTriangle>(page.Commands[0]);
        Assert.IsType<BRenderCommand.FillTriangle>(page.Commands[1]);
    }

    [Fact]
    public void PageShapingManifest_HoldsDetachedGlyphData_SurvivingDisposal()
    {
        var identity = new FontFaceIdentity("prog1", "CustomFont", "Regular", 0);
        var glyphs = new[]
        {
            new ShapedGlyphInfo(10, 10.0, 0, 0),
            new ShapedGlyphInfo(11, 10.0, 0, 0),
            new ShapedGlyphInfo(12, 10.0, 0, 0),
            new ShapedGlyphInfo(13, 10.0, 0, 0),
            new ShapedGlyphInfo(14, 10.0, 0, 0)
        };
        var clusters = new[]
        {
            new ShapedCluster(0, 5, glyphs, 50.0, TextDirection.LeftToRight, identity)
        };

        var run = new ShapedPageRun(commandIndex: 3, identity, clusters, glyphs);
        var manifest = new PageShapingManifest([run]);

        Assert.Single(manifest.Runs);
        Assert.Equal(50.0, run.TotalAdvance);

        var retrieved = manifest.GetRunForCommand(3);
        Assert.NotNull(retrieved);
        Assert.Same(run, retrieved);
        Assert.Null(manifest.GetRunForCommand(99));
    }
}
