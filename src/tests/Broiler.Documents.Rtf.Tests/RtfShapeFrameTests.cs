using System.Text;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// What a shape's <c>\shptop</c> is measured from. <c>\shpbypage</c> and
/// <c>\shpbymargin</c> were not read, so a shape placed from the top of the page
/// was drawn that far below its paragraph instead.
/// </summary>
public sealed class RtfShapeFrameTests
{
    private static DocumentShape ReadShape(string frame, string properties = "") =>
        Assert.Single(RtfReader.Read(Encoding.ASCII.GetBytes(
            "{\\rtf1\\ansi\\margt1440 " +
            "{\\shp{\\*\\shpinst\\shpleft0\\shptop400\\shpright2000\\shpbottom1400" + frame +
            "{\\sp{\\sn fillColor}{\\sv 255}}" + properties + "}}" +
            "body\\par}")).Document.Shapes);

    [Fact]
    public void A_Shape_By_Paragraph_Is_Measured_From_Its_Paragraph()
    {
        DocumentShape shape = ReadShape("\\shpbypara");

        Assert.False(shape.OffsetFromPage);
        Assert.Equal(20, shape.OffsetY, 3);
    }

    [Fact]
    public void A_Shape_By_Page_Is_Measured_From_The_Page_Top()
    {
        DocumentShape shape = ReadShape("\\shpbypage");

        Assert.True(shape.OffsetFromPage);
        Assert.Equal(20, shape.OffsetY, 3);
    }

    [Fact]
    public void A_Shape_By_Margin_Is_Measured_From_The_Top_Margin()
    {
        DocumentShape shape = ReadShape("\\shpbymargin");

        Assert.True(shape.OffsetFromPage);
        Assert.Equal(72 + 20, shape.OffsetY, 3);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public void The_Posrelv_Property_Says_The_Same(int frame, bool fromPage)
    {
        DocumentShape shape = ReadShape(string.Empty, "{\\sp{\\sn posrelv}{\\sv " + frame + "}}");

        Assert.Equal(fromPage, shape.OffsetFromPage);
    }

    [Fact]
    public void A_Page_Relative_Shape_Survives_A_Round_Trip()
    {
        RichTextDocument source = RichTextDocument.FromPlainText("body").WithShapes(
            [new DocumentShape(0, 10, 30, 100, 50, ShapeFill.Solid(BColor.FromArgb(255, 0, 0)), offsetFromPage: true)]);

        DocumentShape shape = Assert.Single(RtfReader.Read(RtfWriter.WriteToArray(source)).Document.Shapes);

        Assert.True(shape.OffsetFromPage);
        Assert.Equal(30, shape.OffsetY, 3);
    }
}
