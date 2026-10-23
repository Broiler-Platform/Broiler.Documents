using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;

namespace Broiler.Documents.Pagination.Tests;

public sealed class DocumentPageDrawingGeometryTests
{
    private static IReadOnlyList<BRenderCommand> Paint(ShapeGeometry geometry, double width = 80, double height = 40, ShapeFill? fill = null)
    {
        var box = new DocumentPageBox(200, 150, 10, 10, 10, 10);
        var document = RichTextDocument.FromPlainText("anchor").WithShapes([new DocumentShape(0, 10, 20, width, height,
            fill, BColor.Black, geometry: geometry, outlineWidth: 3)]);
        using var images = new ImageStore();
        var page = Assert.Single(new DocumentLayout(new LayoutSettings(), images).Layout(document, box).Pages);
        var list = new BRenderList();
        new DocumentPagePainter(new LayoutSettings(), images).Paint(page, box, list, _ => null);
        return list.Commands;
    }

    [Fact]
    public void An_Ellipse_Uses_An_Elliptical_Fill_And_Outline()
    {
        var commands = Paint(ShapeGeometry.Ellipse, fill: ShapeFill.Solid(BColor.Black));
        var fill = Assert.Single(commands.OfType<BRenderCommand.FillRoundedRect>());
        Assert.Equal(40, fill.RadiusX);
        Assert.Equal(20, fill.RadiusY);
        Assert.Equal(3, Assert.Single(commands.OfType<BRenderCommand.StrokeRoundedRect>()).Thickness);
        Assert.Empty(commands.OfType<BRenderCommand.StrokeRect>());
    }

    [Theory]
    [InlineData(ShapeGeometry.Line)]
    [InlineData(ShapeGeometry.LineAscending)]
    public void Diagonal_Lines_Are_Stroked_Instead_Of_Filling_Their_Bounding_Box(ShapeGeometry geometry)
    {
        var commands = Paint(geometry);
        Assert.Single(commands.OfType<BRenderCommand.PushTransform>());
        Assert.Single(commands.OfType<BRenderCommand.PopTransform>());
        Assert.Single(commands.OfType<BRenderCommand.FillRect>());
        Assert.Empty(commands.OfType<BRenderCommand.StrokeRect>());
    }

    [Fact]
    public void A_Degenerate_Rule_Uses_Its_Stated_Width()
    {
        var command = Assert.Single(Paint(ShapeGeometry.Rectangle, height: 0).OfType<BRenderCommand.FillRect>());
        Assert.Equal(3, command.Rect.Height);
    }

    [Fact]
    public void A_Rectangle_Uses_Its_Stated_Outline_Width()
    {
        var command = Assert.Single(Paint(ShapeGeometry.Rectangle).OfType<BRenderCommand.StrokeRect>());
        Assert.Equal(3, command.Thickness);
    }

    [Fact]
    public void Elliptical_Gradients_Are_Clipped_And_Bounded()
    {
        var commands = Paint(ShapeGeometry.Ellipse, fill: new ShapeFill(BColor.Black, BColor.White, 0));
        var clips = commands.OfType<BRenderCommand.PushClip>().ToArray();
        Assert.Equal(128, clips.Length);
        Assert.All(clips, clip => Assert.True(clip.Rect.Width < 80));
        Assert.Equal(clips.Length, commands.OfType<BRenderCommand.PopClip>().Count());
    }
}
