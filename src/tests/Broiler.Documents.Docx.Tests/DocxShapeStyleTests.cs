using Broiler.Graphics.Color;

namespace Broiler.Documents.Docx.Tests;

public sealed class DocxShapeStyleTests
{
    private const string Theme = "<a:theme xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main' name='test'><a:themeElements>" +
        "<a:clrScheme name='test'><a:accent1><a:srgbClr val='4F81BD'/></a:accent1></a:clrScheme>" +
        "<a:fmtScheme name='test'><a:fillStyleLst>" +
        "<a:solidFill><a:schemeClr val='phClr'/></a:solidFill>" +
        "<a:gradFill><a:gsLst><a:gs pos='0'><a:schemeClr val='phClr'/></a:gs><a:gs pos='100000'><a:srgbClr val='FFFFFF'/></a:gs></a:gsLst><a:lin ang='5400000'/></a:gradFill>" +
        "<a:solidFill><a:schemeClr val='phClr'><a:lumMod val='75000'/></a:schemeClr></a:solidFill>" +
        "</a:fillStyleLst><a:lnStyleLst><a:ln w='25400'><a:solidFill><a:schemeClr val='phClr'/></a:solidFill><a:prstDash val='solid'/></a:ln></a:lnStyleLst>" +
        "<a:bgFillStyleLst><a:solidFill><a:srgbClr val='00FF00'/></a:solidFill></a:bgFillStyleLst></a:fmtScheme></a:themeElements></a:theme>";
    private static readonly BColor Blue = BColor.FromArgb(79, 129, 189), Red = BColor.FromArgb(255, 0, 0);
    private static DocumentShape Read(string reference = "<a:schemeClr val='accent1'/>", string fillIndex = "1", string lineIndex = "1", string directFill = "", string directLine = "", bool theme = true)
    {
        string style = "<wps:style><a:fillRef idx='" + fillIndex + "'>" + reference + "</a:fillRef><a:lnRef idx='" + lineIndex + "'>" + reference + "</a:lnRef></wps:style>";
        string run = DocxThemeColorTests.ShapeRun(directFill, directLine).Replace("</wps:wsp>", style +
            "<wps:txbx><w:txbxContent><w:p><w:r><w:t>retained</w:t></w:r></w:p></w:txbxContent></wps:txbx></wps:wsp>");
        return Assert.Single(DocxTestPackage.ReadStyled("<w:p>" + run + "</w:p>", "", theme ? Theme : null).Document.Shapes);
    }

    [Fact]
    public void Fill_And_Outline_Resolve_Their_Separate_Format_Matrix_Entries()
    {
        var shape = Read(); Assert.Equal(Blue, shape.Fill!.Start); Assert.Equal(Blue, shape.Outline); Assert.Equal(2, shape.OutlineWidth);
    }

    [Fact]
    public void A_Referenced_Gradient_Retains_Stops_Angle_And_Placeholder_Color()
    {
        var fill = Read(fillIndex: "2").Fill!;
        Assert.True(fill.IsGradient); Assert.Equal(Blue, fill.Start); Assert.Equal(BColor.White, fill.End); Assert.Equal(90, fill.AngleDegrees);
    }

    [Fact]
    public void Matrix_Transforms_Apply_After_The_Reference_Color()
    {
        var color = Read(fillIndex: "3").Fill!.Start;
        Assert.InRange(color.R, 54, 56); Assert.InRange(color.G, 95, 97); Assert.InRange(color.B, 145, 147);
    }

    [Fact]
    public void Reference_Transforms_Are_Resolved_Before_Substitution()
    {
        var color = Read("<a:schemeClr val='accent1'><a:alpha val='50000'/></a:schemeClr>").Fill!.Start;
        Assert.Equal(128, color.A); Assert.Equal(Blue.R, color.R);
    }

    [Fact]
    public void Background_Fills_Use_Their_Separate_Thousand_Offset()
    {
        Assert.Equal(BColor.FromArgb(0, 255, 0), Read(fillIndex: "1001").Fill!.Start);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("4")]
    [InlineData("1000")]
    [InlineData("2147483647")]
    [InlineData("invalid")]
    public void Missing_Or_Invalid_Entries_Do_Not_Invent_Paint(string index)
    {
        var shape = Read(fillIndex: index, lineIndex: index); Assert.Null(shape.Fill); Assert.True(shape.Outline.IsEmpty);
    }

    [Fact]
    public void Direct_NoFill_Cancels_Both_Theme_Paints()
    {
        var shape = Read(directFill: "<a:noFill/>", directLine: "<a:ln><a:noFill/></a:ln>");
        Assert.Null(shape.Fill); Assert.True(shape.Outline.IsEmpty);
    }

    [Fact]
    public void Direct_Colors_And_Pen_Width_Override_Their_Theme_Values()
    {
        var shape = Read(directFill: "<a:solidFill><a:srgbClr val='FF0000'/></a:solidFill>",
            directLine: "<a:ln w='38100'><a:solidFill><a:srgbClr val='FF0000'/></a:solidFill></a:ln>");
        Assert.Equal(Red, shape.Fill!.Start); Assert.Equal(Red, shape.Outline); Assert.Equal(3, shape.OutlineWidth);
    }

    [Fact]
    public void A_Direct_Line_Width_Still_Inherits_The_Referenced_Color()
    {
        var shape = Read(directLine: "<a:ln w='12700'/>"); Assert.Equal(1, shape.OutlineWidth); Assert.Equal(Blue, shape.Outline);
    }

    [Fact]
    public void A_Direct_Line_Color_Still_Inherits_The_Referenced_Width()
    {
        var shape = Read(directLine: "<a:ln><a:solidFill><a:srgbClr val='FF0000'/></a:solidFill></a:ln>");
        Assert.Equal(2, shape.OutlineWidth); Assert.Equal(Red, shape.Outline);
    }

    [Fact]
    public void Missing_Themes_Keep_Text_And_Explicit_Paint()
    {
        var shape = Read(theme: false, directFill: "<a:solidFill><a:srgbClr val='FF0000'/></a:solidFill>");
        Assert.Equal(Red, shape.Fill!.Start); Assert.True(shape.Outline.IsEmpty); Assert.Equal("retained", shape.Paragraphs[0].Text);
    }
    [Fact]
    public void Resolved_Matrix_Paint_Survives_Export_Without_Live_Theme_Bindings()
    {
        var original = Read(fillIndex: "2");
        var source = RichTextDocument.FromPlainText("body").WithShapes([original]);
        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(source));
        var restored = Assert.Single(new DocxDocumentCodec().Read(stream).Document.Shapes);
        Assert.Equal(original.Fill, restored.Fill); Assert.Equal(original.Outline, restored.Outline);
        Assert.Equal(original.OutlineWidth, restored.OutlineWidth);
    }

}
