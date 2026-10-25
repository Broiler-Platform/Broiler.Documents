using System.Text;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfShapePropertyTests
{
    private static string Property(string name, long value) => "{\\sp{\\sn " + name + "}{\\sv " + value + "}}";
    private static string Shape(string properties = "", string controls = "", string text = "") =>
        "{\\shp{\\*\\shpinst\\shpleft200\\shptop200\\shpright2200\\shpbottom2200" + controls +
        Property("fillColor", 255) + properties + (text.Length == 0 ? "" : "{\\shptxt " + text + "\\par}") + "}}";
    private static DocumentReadResult Read(string shape) => RtfReader.Read(Encoding.ASCII.GetBytes(
        "{\\rtf1\\ansi\\margl1440 " + shape + "body\\par}"));
    private static DocumentShape One(string properties = "", string controls = "", string text = "") =>
        Assert.Single(Read(Shape(properties, controls, text)).Document.Shapes);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Explicit_Fill_Disabling_Wins_Over_The_Colour_In_Either_Order(bool flagFirst)
    {
        string fill = Property("fFilled", 0), color = Property("fillColor", 65280);
        Assert.Null(One(flagFirst ? fill + color : color + fill).Fill);
    }

    [Theory]
    [InlineData(0, 0, ShapeGeometry.Line)]
    [InlineData(1, 0, ShapeGeometry.LineAscending)]
    [InlineData(0, 1, ShapeGeometry.LineAscending)]
    [InlineData(1, 1, ShapeGeometry.Line)]
    public void Horizontal_And_Vertical_Line_Flips_Compose(int horizontal, int vertical, ShapeGeometry expected)
    {
        Assert.Equal(expected, One(Property("shapeType", 20) + Property("fFlipH", horizontal) + Property("fFlipV", vertical)).Geometry);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, -62)]
    [InlineData(2, 10)]
    public void Horizontal_Frame_Properties_Use_The_Same_Origin_As_Control_Words(int frame, double x) =>
        Assert.Equal(x, One(Property("posrelh", frame)).OffsetX, 3);

    [Fact]
    public void Four_Text_Inset_Dimensions_Are_Kept_Independently()
    {
        var shape = One(Property("dxTextLeft", 127000) + Property("dyTextTop", 25400) +
            Property("dxTextRight", 38100) + Property("dyTextBottom", 50800), text: "text");
        Assert.Equal(new TableCellPadding(10, 2, 3, 4), shape.TextPadding);
        Assert.Equal(87, shape.TextWidth);
    }

    [Theory]
    [InlineData(0, CellVerticalAlignment.Top)]
    [InlineData(1, CellVerticalAlignment.Center)]
    [InlineData(2, CellVerticalAlignment.Bottom)]
    [InlineData(4, CellVerticalAlignment.Center)]
    public void Shape_Text_Uses_Its_Vertical_Anchor(int anchor, CellVerticalAlignment expected) =>
        Assert.Equal(expected, One(Property("anchorText", anchor), text: "text").TextVerticalAlignment);

    [Fact]
    public void Fill_Opacity_Is_Order_Independent_And_Gradient_Ends_Have_Independent_Alpha()
    {
        var shape = One(Property("fillOpacity", 32768) + Property("fillColor", 65280) +
            Property("fillBackOpacity", 16384) + Property("fillBackColor", 16711680) + Property("fillType", 1));
        Assert.Equal(128, shape.Fill!.Start.A);
        Assert.Equal(64, shape.Fill.End.A);
        Assert.Equal(255, shape.Fill.Start.G);
        Assert.Equal(255, shape.Fill.End.B);
    }

    [Fact]
    public void Outline_Opacity_Does_Not_Alter_Its_Colour_Or_Width()
    {
        var shape = One(Property("lineOpacity", 32768) + Property("lineColor", 65280) + Property("lineWidth", 25400));
        Assert.Equal(128, shape.Outline.A);
        Assert.Equal(255, shape.Outline.G);
        Assert.Equal(2, shape.OutlineWidth);
    }

    [Fact]
    public void A_Shape_With_No_Fill_Properties_Uses_The_Native_White_Default()
    {
        string shape = Shape().Replace(Property("fillColor", 255), "", StringComparison.Ordinal);
        Assert.Equal(BColor.White, Assert.Single(Read(shape).Document.Shapes).Fill!.Start);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("\\shpfblwtxt0", false)]
    [InlineData("\\shpfblwtxt1", true)]
    public void Behind_Text_Is_Independent_Of_Shape_Stacking(string controls, bool behind)
    {
        var shape = One(controls: controls + "\\shpz7");
        Assert.Equal(behind, shape.BehindText);
        Assert.Equal(7, shape.ZOrder);
    }

    [Theory]
    [InlineData(1, ShapeWrap.TopAndBottom)]
    [InlineData(2, ShapeWrap.Square)]
    [InlineData(3, ShapeWrap.None)]
    public void Shape_Wrapping_Retains_Mode_Side_And_Clearance(int value, ShapeWrap expected)
    {
        var shape = One(Property("dxWrapDistRight", 63500), "\\shpwr" + value + "\\shpwrk2");
        Assert.Equal(expected, shape.Wrap);
        Assert.Equal(WrapSide.Right, shape.WrapSide);
        Assert.Equal(5, shape.WrapDistance);
    }

    [Fact]
    public void Supported_Paint_Text_And_Wrapping_Properties_Survive_Export()
    {
        var source = new DocumentShape(0, 10, 10, 100, 80,
            ShapeFill.Solid(BColor.FromArgb(128, 255, 0, 0)), BColor.FromArgb(64, 0, 255, 0),
            [RichTextParagraph.Plain("text")], behindText: false, wrap: ShapeWrap.Square,
            wrapSide: WrapSide.Right, wrapDistance: 5, textPadding: new(10, 2, 3, 4),
            textVerticalAlignment: CellVerticalAlignment.Bottom);
        var document = RichTextDocument.FromPlainText("body").WithShapes([source]);
        var restored = Assert.Single(RtfReader.Read(RtfWriter.WriteToArray(document)).Document.Shapes);
        Assert.Equal(source.Fill, restored.Fill);
        Assert.Equal(source.Outline, restored.Outline);
        Assert.Equal(source.TextPadding, restored.TextPadding);
        Assert.Equal(source.TextVerticalAlignment, restored.TextVerticalAlignment);
        Assert.Equal(source.BehindText, restored.BehindText);
        Assert.Equal(source.Wrap, restored.Wrap);
        Assert.Equal(source.WrapSide, restored.WrapSide);
        Assert.Equal(source.WrapDistance, restored.WrapDistance);
        Assert.Equal("text", Assert.Single(restored.Paragraphs).Text);
    }
}
