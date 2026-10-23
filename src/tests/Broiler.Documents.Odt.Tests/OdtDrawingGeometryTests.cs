using Broiler.Graphics.Color;

namespace Broiler.Documents.Odt.Tests;

public sealed class OdtDrawingGeometryTests
{
    private static RichTextDocument Read(string drawing, string properties) => OdtTestPackage.ReadStyled(
        "<text:p>anchor" + drawing + "</text:p>",
        "<office:styles><style:style style:name=\"g\" style:family=\"graphic\"><style:graphic-properties " + properties +
        "/></style:style></office:styles>").Document;

    [Fact]
    public void An_Unfilled_Empty_Rectangle_Keeps_Its_Outline()
    {
        var shape = Assert.Single(Read("<draw:rect draw:style-name=\"g\" svg:width=\"80pt\" svg:height=\"40pt\"/>",
            "draw:fill=\"none\" draw:stroke=\"solid\" svg:stroke-color=\"#ff0000\" svg:stroke-width=\"3pt\"").Shapes);
        Assert.Null(shape.Fill);
        Assert.Equal(BColor.Red, shape.Outline);
        Assert.Equal(3, shape.OutlineWidth);
    }

    [Theory]
    [InlineData("0pt", "40pt", ShapeGeometry.Line)]
    [InlineData("40pt", "0pt", ShapeGeometry.LineAscending)]
    [InlineData("0pt", "0pt", ShapeGeometry.Line)]
    public void Lines_Retain_Endpoints_Direction_And_An_Automatic_Black_Stroke(string y1, string y2, ShapeGeometry geometry)
    {
        var shape = Assert.Single(Read($"<draw:line draw:style-name=\"g\" svg:x1=\"10pt\" svg:y1=\"{y1}\" svg:x2=\"90pt\" svg:y2=\"{y2}\"/>",
            "draw:stroke=\"solid\" svg:stroke-width=\"2pt\" style:horizontal-rel=\"paragraph\" style:vertical-rel=\"paragraph\"").Shapes);
        Assert.Equal(geometry, shape.Geometry);
        Assert.Equal(80, shape.Width);
        Assert.Equal(y1 == y2 ? 0 : 40, shape.Height);
        Assert.Equal(BColor.Black, shape.Outline);
        Assert.Equal(2, shape.OutlineWidth);
    }

    [Fact]
    public void Explicit_No_Stroke_Does_Not_Create_A_Line()
    {
        Assert.Empty(Read("<draw:line draw:style-name=\"g\" svg:x1=\"0pt\" svg:y1=\"0pt\" svg:x2=\"90pt\" svg:y2=\"40pt\"/>",
            "draw:stroke=\"none\"").Shapes);
    }

    [Theory]
    [InlineData("3pt solid #661900", 3d)]
    [InlineData("none", 0d)]
    public void Frame_Borders_Override_The_Graphic_Stroke(string border, double width)
    {
        var shape = Assert.Single(Read("<draw:frame draw:style-name=\"g\" svg:width=\"80pt\" svg:height=\"40pt\"><draw:text-box><text:p>frame</text:p></draw:text-box></draw:frame>",
            $"draw:stroke=\"solid\" svg:stroke-color=\"#ff0000\" fo:border=\"{border}\"").Shapes);
        Assert.Equal(width, shape.OutlineWidth);
        Assert.Equal(width == 0 ? BColor.Empty : new BColor(0x66, 0x19, 0), shape.Outline);
    }

    [Theory]
    [InlineData(ShapeGeometry.Rectangle)]
    [InlineData(ShapeGeometry.Ellipse)]
    [InlineData(ShapeGeometry.Line)]
    [InlineData(ShapeGeometry.LineAscending)]
    public void Geometry_And_Stroke_Width_Survive_Export(ShapeGeometry geometry)
    {
        var source = RichTextDocument.FromPlainText("body").WithShapes([new DocumentShape(0, 10, 20, 80, 40,
            outline: BColor.Black, geometry: geometry, outlineWidth: 2.5)]);
        using var bytes = new MemoryStream(OdtDocumentCodec.WriteToArray(source));
        var restored = Assert.Single(new OdtDocumentCodec().Read(bytes).Document.Shapes);
        Assert.Equal(geometry, restored.Geometry);
        Assert.Equal(2.5, restored.OutlineWidth, 3);
        Assert.Equal(80, restored.Width, 3);
        Assert.Equal(40, restored.Height, 3);
    }
}
