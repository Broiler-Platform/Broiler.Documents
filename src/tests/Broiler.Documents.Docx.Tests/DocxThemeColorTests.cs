using Broiler.Graphics.Color;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Covers DrawingML colours a shape names through the theme rather than
/// stating: <c>a:schemeClr</c> and the transforms under it. Read as
/// <c>a:srgbClr</c> only, a template's themed box had no fill and was dropped
/// with its paint. The expected values are what LibreOffice draws for each.
/// </summary>
public sealed class DocxThemeColorTests
{
    private const string Theme =
        "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"test\">" +
        "<a:themeElements><a:clrScheme name=\"test\">" +
        "<a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1>" +
        "<a:lt1><a:sysClr val=\"window\" lastClr=\"FFFFFF\"/></a:lt1>" +
        "<a:dk2><a:srgbClr val=\"1F497D\"/></a:dk2>" +
        "<a:lt2><a:srgbClr val=\"EEECE1\"/></a:lt2>" +
        "<a:accent1><a:srgbClr val=\"4F81BD\"/></a:accent1>" +
        "<a:accent2><a:srgbClr val=\"C0504D\"/></a:accent2>" +
        "</a:clrScheme>" +
        "<a:fontScheme name=\"test\"><a:majorFont><a:latin typeface=\"Cambria\"/></a:majorFont>" +
        "<a:minorFont><a:latin typeface=\"Calibri\"/></a:minorFont></a:fontScheme>" +
        "</a:themeElements></a:theme>";

    internal static string ShapeRun(string fillXml, string lineXml = "<a:ln><a:noFill/></a:ln>") =>
        "<w:r><w:drawing>" +
        "<wp:anchor xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\">" +
        "<wp:positionH relativeFrom=\"column\"><wp:posOffset>0</wp:posOffset></wp:positionH>" +
        "<wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>0</wp:posOffset></wp:positionV>" +
        "<wp:extent cx=\"1270000\" cy=\"1270000\"/>" +
        "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
        "<a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
        "<wps:wsp xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
        "<wps:spPr><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>" + fillXml + lineXml + "</wps:spPr>" +
        "</wps:wsp></a:graphicData></a:graphic></wp:anchor>" +
        "</w:drawing></w:r>";

    private static DocumentReadResult ReadShape(string fillXml, string? theme = Theme, string lineXml = "<a:ln><a:noFill/></a:ln>") =>
        DocxTestPackage.ReadStyled("<w:p>" + ShapeRun(fillXml, lineXml) + "</w:p>", string.Empty, theme);

    private static BColor SolidColor(string colorXml)
    {
        DocumentShape shape = Assert.Single(ReadShape("<a:solidFill>" + colorXml + "</a:solidFill>").Document.Shapes);
        Assert.NotNull(shape.Fill);
        Assert.False(shape.Fill!.IsGradient);
        return shape.Fill.Start;
    }

    private static string Hex(BColor color) => $"{color.R:X2}{color.G:X2}{color.B:X2}";

    [Theory]
    [InlineData("<a:schemeClr val=\"accent1\"/>", "4F81BD")]
    [InlineData("<a:schemeClr val=\"accent2\"/>", "C0504D")]
    [InlineData("<a:schemeClr val=\"tx1\"/>", "000000")]
    [InlineData("<a:schemeClr val=\"bg1\"/>", "FFFFFF")]
    [InlineData("<a:schemeClr val=\"tx2\"/>", "1F497D")]
    [InlineData("<a:schemeClr val=\"bg2\"/>", "EEECE1")]
    [InlineData("<a:sysClr val=\"windowText\" lastClr=\"123456\"/>", "123456")]
    public void Resolves_A_Scheme_Colour_Through_The_Theme(string colorXml, string expected)
    {
        Assert.Equal(expected, Hex(SolidColor(colorXml)));
    }

    // Each value is what LibreOffice 26.2 drew for the same box on this
    // project's measuring host: shade and tint in linear RGB at a gamma of 2.3,
    // lumMod and lumOff on the HSL luminance.
    [Theory]
    [InlineData("<a:schemeClr val=\"accent1\"><a:shade val=\"50000\"/></a:schemeClr>", "3A5F8B")]
    [InlineData("<a:schemeClr val=\"accent1\"><a:tint val=\"50000\"/></a:schemeClr>", "C2CCE1")]
    [InlineData("<a:schemeClr val=\"accent1\"><a:lumMod val=\"75000\"/></a:schemeClr>", "376092")]
    [InlineData("<a:schemeClr val=\"accent1\"><a:lumMod val=\"60000\"/><a:lumOff val=\"40000\"/></a:schemeClr>", "95B3D7")]
    [InlineData("<a:srgbClr val=\"4F81BD\"><a:shade val=\"50000\"/></a:srgbClr>", "3A5F8B")]
    public void Applies_A_Colours_Transforms_As_LibreOffice_Does(string colorXml, string expected)
    {
        BColor color = SolidColor(colorXml);

        // A channel either side is rounding, not a different rule.
        int[] want = [Convert.ToInt32(expected[..2], 16), Convert.ToInt32(expected[2..4], 16), Convert.ToInt32(expected[4..], 16)];
        Assert.InRange(color.R, want[0] - 1, want[0] + 1);
        Assert.InRange(color.G, want[1] - 1, want[1] + 1);
        Assert.InRange(color.B, want[2] - 1, want[2] + 1);
    }

    [Fact]
    public void Alpha_Is_The_Colours_Opacity()
    {
        BColor color = SolidColor("<a:schemeClr val=\"accent1\"><a:alpha val=\"0\"/></a:schemeClr>");

        Assert.Equal("4F81BD", Hex(color));
        Assert.Equal(0, color.A);
        Assert.Equal(255, SolidColor("<a:schemeClr val=\"accent1\"/>").A);
    }

    [Fact]
    public void A_Gradient_Of_Scheme_Colours_Is_Read()
    {
        DocumentShape shape = Assert.Single(ReadShape(
            "<a:gradFill><a:gsLst>" +
            "<a:gs pos=\"0\"><a:schemeClr val=\"accent1\"><a:alpha val=\"0\"/></a:schemeClr></a:gs>" +
            "<a:gs pos=\"100000\"><a:schemeClr val=\"accent2\"/></a:gs>" +
            "</a:gsLst><a:lin ang=\"5400000\"/></a:gradFill>").Document.Shapes);

        Assert.True(shape.Fill!.IsGradient);
        Assert.Equal(0, shape.Fill.Start.A);
        Assert.Equal("C0504D", Hex(shape.Fill.End));
        Assert.Equal(90, shape.Fill.AngleDegrees, 3);
    }

    [Fact]
    public void An_Outline_In_A_Scheme_Colour_Is_Read()
    {
        DocumentShape shape = Assert.Single(ReadShape(
            "<a:solidFill><a:srgbClr val=\"FFFFFF\"/></a:solidFill>",
            lineXml: "<a:ln><a:solidFill><a:schemeClr val=\"accent2\"/></a:solidFill></a:ln>").Document.Shapes);

        Assert.Equal("C0504D", Hex(shape.Outline));
    }

    [Fact]
    public void A_Colours_Opacity_Survives_A_Round_Trip()
    {
        RichTextDocument source = ReadShape(
            "<a:gradFill><a:gsLst>" +
            "<a:gs pos=\"0\"><a:schemeClr val=\"accent1\"><a:alpha val=\"0\"/></a:schemeClr></a:gs>" +
            "<a:gs pos=\"100000\"><a:schemeClr val=\"accent1\"/></a:gs>" +
            "</a:gsLst><a:lin ang=\"5400000\"/></a:gradFill>").Document;

        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(source), writable: false);
        ShapeFill fill = Assert.Single(new DocxDocumentCodec().Read(stream).Document.Shapes).Fill!;

        Assert.Equal(0, fill.Start.A);
        Assert.Equal(255, fill.End.A);
        Assert.Equal("4F81BD", Hex(fill.Start));
    }

    [Fact]
    public void A_Scheme_Colour_With_No_Theme_Is_No_Fill()
    {
        // Nothing to resolve it through: the shape holds no paint, as before.
        Assert.Empty(ReadShape("<a:solidFill><a:schemeClr val=\"accent1\"/></a:solidFill>", theme: null).Document.Shapes);
    }
}
