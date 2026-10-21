using System.Text;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// A shape that is only its line: the ruled grid of <c>tdf133437.rtf</c>, drawn
/// as 285 lines and one unfilled rectangle. A shape with no fill and no text was
/// dropped, and so was one with no breadth, so the statement lost its grid. Word
/// draws a shape's line black unless the shape says otherwise, and so does
/// LibreOffice's import.
/// </summary>
public sealed class RtfShapeRuleTests
{
    private static RtfResult Read(string shape, string page = "\\margl1440") =>
        new(RtfReader.Read(Encoding.ASCII.GetBytes(
            "{\\rtf1\\ansi" + page + "{\\shp{\\*\\shpinst" + shape + "}}body\\par}")));

    [Fact]
    public void A_Vertical_Line_Is_A_Rule_Drawn_Black()
    {
        DocumentShape rule = Assert.Single(Read(
            "\\shpleft1840\\shptop2720\\shpright1840\\shpbottom3280{\\sp{\\sn shapeType}{\\sv 20}}").Shapes);

        Assert.True(rule.IsRule);
        Assert.Equal(0, rule.Width, 3);
        Assert.Equal(28, rule.Height, 3);
        Assert.Equal(BColor.Black, rule.Outline);
    }

    [Fact]
    public void A_Line_Drawn_Backwards_Is_The_Same_Rule()
    {
        DocumentShape rule = Assert.Single(Read(
            "\\shpleft4000\\shptop2720\\shpright2000\\shpbottom2720{\\sp{\\sn shapeType}{\\sv 20}}").Shapes);

        Assert.Equal(100, rule.OffsetX, 3);
        Assert.Equal(100, rule.Width, 3);
        Assert.Equal(0, rule.Height, 3);
    }

    [Fact]
    public void A_Slanted_Line_Is_Not_Read_And_Is_Reported()
    {
        RtfResult result = Read("\\shpleft0\\shptop0\\shpright2000\\shpbottom2000{\\sp{\\sn shapeType}{\\sv 20}}");

        Assert.Empty(result.Shapes);
        Assert.Contains(result.Read.Diagnostics, d => d.Code == "rtf.shape.line");
    }

    [Fact]
    public void An_Unfilled_Rectangle_Keeps_Its_Default_Black_Outline()
    {
        DocumentShape box = Assert.Single(Read(
            "\\shpleft840\\shptop2720\\shpright11220\\shpbottom3280" +
            "{\\sp{\\sn shapeType}{\\sv 1}}{\\sp{\\sn fFilled}{\\sv 0}}").Shapes);

        Assert.Null(box.Fill);
        Assert.Equal(BColor.Black, box.Outline);
        Assert.False(box.IsRule);
    }

    [Fact]
    public void A_Shape_With_No_Line_No_Fill_And_No_Text_Is_Not_Kept()
    {
        Assert.Empty(Read(
            "\\shpleft0\\shptop0\\shpright2000\\shpbottom2000" +
            "{\\sp{\\sn fFilled}{\\sv 0}}{\\sp{\\sn fLine}{\\sv 0}}").Shapes);
    }

    [Fact]
    public void A_Picture_Frame_Is_Not_Kept_For_Its_Default_Line()
    {
        Assert.Empty(Read(
            "\\shpleft0\\shptop0\\shpright2000\\shpbottom2000{\\sp{\\sn shapeType}{\\sv 75}}").Shapes);
    }

    [Fact]
    public void A_Filled_Shape_With_No_Line_Colour_Is_Outlined_Black()
    {
        DocumentShape box = Assert.Single(Read(
            "\\shpleft0\\shptop0\\shpright2000\\shpbottom2000{\\sp{\\sn fillColor}{\\sv 255}}").Shapes);

        Assert.Equal(BColor.Black, box.Outline);
    }

    [Fact]
    public void Shpbxpage_Measures_The_Left_Edge_From_The_Page()
    {
        DocumentShape rule = Assert.Single(Read(
            "\\shpbxpage\\shpleft2440\\shptop0\\shpright2440\\shpbottom400{\\sp{\\sn shapeType}{\\sv 20}}").Shapes);

        // 122 points from the page, 72 of them margin.
        Assert.Equal(50, rule.OffsetX, 3);
    }

    [Fact]
    public void FFitShapeToText_Makes_The_Box_Grow_To_Its_Text()
    {
        DocumentShape box = Assert.Single(Read(
            "\\shpleft0\\shptop0\\shpright2000\\shpbottom400{\\sp{\\sn fFitShapeToText}{\\sv 1}}" +
            "{\\shptxt text\\par}").Shapes);

        Assert.True(box.GrowsToText);
    }

    [Fact]
    public void A_Rule_Comes_Back_From_The_Writer()
    {
        RichTextDocument source = RichTextDocument.FromPlainText("body").WithShapes(
            [new DocumentShape(0, 10, 30, 0, 50, outline: BColor.Black)]);

        DocumentShape rule = Assert.Single(RtfReader.Read(RtfWriter.WriteToArray(source)).Document.Shapes);

        Assert.True(rule.IsRule);
        Assert.Equal(50, rule.Height, 3);
    }

    private sealed record RtfResult(DocumentReadResult Read)
    {
        public IReadOnlyList<DocumentShape> Shapes => Read.Document.Shapes;
    }
}
