using System.Xml.Linq;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Docx.Tests;

public sealed class DocxDrawingColorOperationTests
{
    private const string Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static bool TryColor(string xml, out BColor color) => DocxDrawingColor.TryRead(
        XElement.Parse("<a:solidFill xmlns:a='" + Drawing + "'>" + xml + "</a:solidFill>"), DocxTheme.Empty, out color);
    private static BColor Color(string xml)
    {
        Assert.True(TryColor(xml, out var color)); return color;
    }
    private static BColor Rgb(string hex, string transforms) => Color("<a:srgbClr val='" + hex + "'>" + transforms + "</a:srgbClr>");
    private static void Near(string hex, BColor actual, int tolerance = 1)
    {
        int[] values = [Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex.Substring(2, 2), 16), Convert.ToInt32(hex[4..], 16)];
        Assert.InRange(actual.R, values[0] - tolerance, values[0] + tolerance);
        Assert.InRange(actual.G, values[1] - tolerance, values[1] + tolerance);
        Assert.InRange(actual.B, values[2] - tolerance, values[2] + tolerance);
    }

    [Theory]
    [InlineData("100000", "0", "0", "FF0000")]
    [InlineData("0", "100000", "0", "00FF00")]
    [InlineData("0", "0", "100000", "0000FF")]
    [InlineData("50000", "50000", "50000", "BDBDBD")]
    [InlineData("0", "0", "0", "000000")]
    [InlineData("100000", "100000", "100000", "FFFFFF")]
    public void ScRgb_Components_Are_Linear_Light_Percentages(string red, string green, string blue, string expected) =>
        Near(expected, Color("<a:scrgbClr r='" + red + "' g='" + green + "' b='" + blue + "'/>"));

    [Theory]
    [InlineData("0", "100000", "50000", "FF0000")]
    [InlineData("7200000", "100000", "50000", "00FF00")]
    [InlineData("14400000", "100000", "50000", "0000FF")]
    [InlineData("21600000", "100000", "50000", "FF0000")]
    [InlineData("12000000", "0", "50000", "808080")]
    public void Hsl_Base_Colors_Use_Angle_Units_And_Percentages(string hue, string saturation, string luminance, string expected) =>
        Near(expected, Color("<a:hslClr hue='" + hue + "' sat='" + saturation + "' lum='" + luminance + "'/>"));

    [Theory]
    [InlineData("aliceBlue", "F0F8FF")]
    [InlineData("orange", "FFA500")]
    [InlineData("gold", "FFD700")]
    [InlineData("dkGoldenrod", "B8860B")]
    [InlineData("ltSkyBlue", "87CEFA")]
    [InlineData("medVioletRed", "C71585")]
    [InlineData("ltGoldenrodYellow", "FAFAD2")]
    [InlineData("rebeccaPurple", null)]
    [InlineData("DarkBlue", null)]
    [InlineData("transparent", null)]
    public void DrawingMl_Preset_Names_Use_Their_Own_Palette(string name, string? expected)
    {
        bool read = TryColor("<a:prstClr val='" + name + "'/>", out var color);
        Assert.Equal(expected is not null, read); if (expected is not null) Near(expected, color, 0);
    }

    [Theory]
    [InlineData("<a:alpha val='50000'/><a:alphaMod val='50000'/>", 64)]
    [InlineData("<a:alpha val='50000'/><a:alphaOff val='-25000'/>", 64)]
    [InlineData("<a:alpha val='25000'/><a:alphaOff val='50000'/>", 191)]
    [InlineData("<a:alphaMod val='0'/>", 0)]
    [InlineData("<a:alphaOff val='-100000'/>", 0)]
    [InlineData("<a:alpha val='50000'/><a:alphaMod val='300000'/>", 255)]
    public void Alpha_Arithmetic_Uses_Current_Opacity_And_Clamps(string transforms, int expected)
    {
        var color = Rgb("4F81BD", transforms); Assert.Equal(expected, color.A); Near("4F81BD", color, 0);
    }

    [Theory]
    [InlineData("FF0000", "<a:hue val='3600000'/>", "FFFF00")]
    [InlineData("FF0000", "<a:hueOff val='7200000'/>", "00FF00")]
    [InlineData("FF0000", "<a:hueOff val='-3600000'/>", "FF0000")]
    [InlineData("0000FF", "<a:hueOff val='10800000'/>", "FF0000")]
    [InlineData("00FF00", "<a:hueMod val='50000'/>", "FFFF00")]
    [InlineData("00FF00", "<a:hueMod val='0'/>", "FF0000")]
    [InlineData("FF0000", "<a:sat val='50000'/>", "BF4040")]
    [InlineData("FF0000", "<a:satOff val='-50000'/>", "BF4040")]
    [InlineData("808080", "<a:satOff val='100000'/>", "FF0101")]
    [InlineData("FF0000", "<a:lum val='25000'/>", "800000")]
    [InlineData("FF0000", "<a:comp/>", "00FFFF")]
    [InlineData("FF0000", "<a:inv/>", "00FFFF")]
    [InlineData("808080", "<a:inv/>", "E7E7E7")]
    [InlineData("FF0000", "<a:gray/>", "383838")]
    [InlineData("00FF00", "<a:gray/>", "B7B7B7")]
    [InlineData("0000FF", "<a:gray/>", "0F0F0F")]
    [InlineData("808080", "<a:gamma/>", "BDBDBD")]
    [InlineData("808080", "<a:invGamma/>", "343434")]
    public void Transforms_Modify_Their_Stated_Color_Component(string source, string transforms, string expected) =>
        Near(expected, Rgb(source, transforms));

    [Fact]
    public void Hue_Arithmetic_Clamps_At_The_Native_Angle_Boundaries()
    {
        Near("FF0000", Rgb("00FF00", "<a:hueMod val='400000'/>"));
        Near("FF0000", Rgb("0000FF", "<a:hueOff val='10800000'/>"));
        Near("FF0000", Rgb("FF0000", "<a:hueOff val='-3600000'/>"));
    }

    [Fact]
    public void Adjacent_Hsl_Transforms_Retain_Achromatic_Hue_Until_Saturation_Returns()
    {
        Near("00FF00", Rgb("FF0000", "<a:sat val='0'/><a:hue val='7200000'/><a:sat val='100000'/>"));
        Near("00FF00", Color("<a:hslClr hue='7200000' sat='0' lum='50000'><a:sat val='100000'/></a:hslClr>"));
    }

    [Fact]
    public void Transform_Order_Is_Observable_And_Black_Luminance_Stays_Achromatic()
    {
        Near("BF4040", Rgb("FF0000", "<a:sat val='100000'/><a:satOff val='-50000'/>"));
        Near("FF0000", Rgb("FF0000", "<a:satOff val='-50000'/><a:sat val='100000'/>"));
        Near("808080", Rgb("FF0000", "<a:lum val='0'/><a:lum val='50000'/>"));
    }

    [Theory]
    [InlineData("<a:comp/><a:comp/>")]
    [InlineData("<a:inv/><a:inv/>")]
    [InlineData("<a:gamma/><a:invGamma/>")]
    [InlineData("<a:invGamma/><a:gamma/>")]
    public void Paired_Reversible_Operations_Restore_Color(string transforms) => Near("4F81BD", Rgb("4F81BD", transforms));

    [Theory]
    [InlineData("<a:scrgbClr r='100000' g='0'/>")]
    [InlineData("<a:scrgbClr r='NaN' g='0' b='0'/>")]
    [InlineData("<a:hslClr hue='0' sat='100000'/>")]
    [InlineData("<a:hslClr hue='x' sat='100000' lum='50000'/>")]
    public void Missing_And_Malformed_Base_Components_Do_Not_Invent_Color(string xml) => Assert.False(TryColor(xml, out _));

    [Fact]
    public void Malformed_And_Foreign_Transforms_Do_Not_Change_Existing_Color()
    {
        Near("4F81BD", Rgb("4F81BD", "<a:hue/><a:sat val='NaN'/><x:inv xmlns:x='urn:foreign'/><a:unknown val='100000'/>"), 0);
    }

    [Fact]
    public void NonRgb_Theme_Slots_Reach_Word_Text_Colors_And_Shape_Fills()
    {
        string theme = "<a:theme xmlns:a='" + Drawing + "'><a:themeElements><a:clrScheme name='test'>" +
            "<a:accent1><a:hslClr hue='7200000' sat='100000' lum='50000'/></a:accent1>" +
            "<a:accent2><a:scrgbClr r='0' g='0' b='100000'/></a:accent2>" +
            "<a:accent3><a:prstClr val='orange'/></a:accent3></a:clrScheme></a:themeElements></a:theme>";
        string body = "<w:p><w:r><w:rPr><w:color w:themeColor='accent1'/></w:rPr><w:t>green</w:t></w:r>" +
            DocxThemeColorTests.ShapeRun("<a:solidFill><a:schemeClr val='accent2'/></a:solidFill>",
                "<a:ln><a:solidFill><a:schemeClr val='accent3'/></a:solidFill></a:ln>") + "</w:p>";
        var document = DocxTestPackage.ReadStyled(body, "", theme).Document;
        Near("00FF00", document.Paragraphs[0].StyleAt(0).Foreground, 0);
        var shape = Assert.Single(document.Shapes); Near("0000FF", shape.Fill!.Start, 0); Near("FFA500", shape.Outline, 0);
    }

    [Fact]
    public void Gradient_Stops_Outline_And_Export_Share_The_Color_Operations()
    {
        string run = DocxThemeColorTests.ShapeRun("<a:gradFill><a:gsLst>" +
            "<a:gs pos='0'><a:hslClr hue='0' sat='100000' lum='50000'><a:alphaMod val='50000'/></a:hslClr></a:gs>" +
            "<a:gs pos='100000'><a:scrgbClr r='0' g='0' b='100000'/></a:gs></a:gsLst><a:lin ang='0'/></a:gradFill>",
            "<a:ln><a:solidFill><a:prstClr val='orange'><a:gray/></a:prstClr></a:solidFill></a:ln>");
        var source = DocxTestPackage.ReadStyled("<w:p>" + run + "</w:p>", "").Document;
        var shape = Assert.Single(source.Shapes); Near("FF0000", shape.Fill!.Start); Assert.Equal(128, shape.Fill.Start.A);
        Near("0000FF", shape.Fill.End); Near("AEAEAE", shape.Outline);
        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(source));
        var restored = Assert.Single(new DocxDocumentCodec().Read(stream).Document.Shapes);
        Assert.Equal(shape.Fill, restored.Fill); Assert.Equal(shape.Outline, restored.Outline);
    }
}
