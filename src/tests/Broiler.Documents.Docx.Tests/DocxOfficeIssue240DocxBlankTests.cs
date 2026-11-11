using Broiler.Graphics.Color;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Office issue #240, DOCX pages one side drew blank: a drawing canvas read
/// from its VML fallback, WordArt stretched to its box and outlined, and VML
/// angles in fixed-point degrees.
/// </summary>
public sealed class DocxOfficeIssue240DocxBlankTests
{
    private const string Vml =
        "xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\"";

    private const string Canvas =
        "xmlns:wpc=\"http://schemas.microsoft.com/office/word/2010/wordprocessingCanvas\" " +
        "xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\" " +
        "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"";

    /// <summary>
    /// A canvas holding one text box, with a VML fallback of two boxes, as
    /// groupshape-fontname.docx writes its diagram.
    /// </summary>
    private static string CanvasWithFallback(string requires) =>
        "<w:p><w:r><mc:AlternateContent " + Canvas + " " + Vml + ">" +
        "<mc:Choice Requires=\"" + requires + "\"><w:drawing><wp:inline><wp:extent cx=\"2540000\" cy=\"1270000\"/>" +
        "<a:graphic><a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingCanvas\"><wpc:wpc>" +
        "<wps:wsp><wps:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1270000\" cy=\"254000\"/></a:xfrm>" +
        "<a:prstGeom prst=\"rect\"/></wps:spPr>" +
        "<wps:txbx><w:txbxContent><w:p><w:r><w:t>AVANT</w:t></w:r></w:p></w:txbxContent></wps:txbx><wps:bodyPr/></wps:wsp>" +
        "</wpc:wpc></a:graphicData></a:graphic></wp:inline></w:drawing></mc:Choice>" +
        "<mc:Fallback><w:pict><v:group style=\"width:200pt;height:100pt\" coordsize=\"2000,1000\">" +
        "<v:rect style=\"position:absolute;left:0;top:0;width:1000;height:500\"/>" +
        "<v:rect style=\"position:absolute;left:1000;top:500;width:1000;height:500\"/>" +
        "</v:group></w:pict></mc:Fallback></mc:AlternateContent></w:r></w:p>";

    [Fact]
    public void A_Drawing_Canvas_Is_Read_From_Its_Vml_Fallback()
    {
        // groupshape-fontname.docx: only the canvas's first text box, "AVANT
        // L'OPERATION", was read, as a line of text; the diagram was lost.
        RichTextDocument document = DocxTestPackage.ReadBody(CanvasWithFallback("wpc")).Document;

        Assert.Equal(2, document.Shapes.Count);
        Assert.DoesNotContain("AVANT", document.PlainText);
        // LibreOffice reads a grouped box as a drawing shape, not a text frame,
        // so its text is not what a text export of an empty body lists.
        Assert.All(document.Shapes, shape => Assert.True(shape.IsCustomShape));
    }

    [Fact]
    public void A_Choice_That_Does_Not_Require_The_Canvas_Is_Still_Read()
    {
        RichTextDocument document = DocxTestPackage.ReadBody(CanvasWithFallback("wps")).Document;

        Assert.DoesNotContain(document.Shapes, shape => shape.Width == 100 && shape.Height == 50);
    }

    private static DocumentShape ReadWordArt(string shapeAttributes, string shapeType = "#_x0000_t136") =>
        Assert.Single(DocxTestPackage.ReadBody(
            "<w:p><w:r><w:pict " + Vml + ">" +
            "<v:shapetype id=\"_x0000_t136\" coordsize=\"21600,21600\" o:spt=\"136\" path=\"m@7,l@8,m@5,21600l@6,21600e\">" +
            "<v:path textpathok=\"t\"/><v:textpath on=\"t\" fitshape=\"t\"/></v:shapetype>" +
            "<v:shape type=\"" + shapeType + "\" style=\"position:absolute;margin-left:0;margin-top:0;width:412.4pt;height:247.45pt;z-index:-1\" " +
            shapeAttributes + ">" +
            "<v:textpath style=\"font-family:&quot;Arial Black&quot;;font-size:1pt\" string=\"DRAFT\"/>" +
            "</v:shape></w:pict></w:r></w:p>").Document.Shapes);

    [Fact]
    public void WordArt_With_Vmls_Default_Fill_And_Stroke_Is_White_Glyphs_Outlined_In_Black()
    {
        // tdf114308.docx: the "DRAFT" watermark states no colours. VML's white
        // fill painted the glyphs on a white page, so nothing showed; LibreOffice
        // draws the black stroke round each letter.
        DocumentShape shape = ReadWordArt("");

        Assert.True(shape.StretchesTextToBox);
        Assert.Equal(BColor.Black, shape.TextOutline);
        Assert.Equal(0.75, shape.TextOutlineWidth, 3);
        Assert.Equal(new BColor(255, 255, 255), Assert.Single(Assert.Single(shape.Paragraphs).Runs).Style.Foreground);
        Assert.True(shape.Outline.IsEmpty, "the box itself is not stroked");
    }

    [Fact]
    public void An_Unstroked_Watermark_Is_Stretched_But_Not_Outlined()
    {
        // textWatermark.docx, watermark-font.docx: the glyphs fill the box, as
        // LibreOffice's Fontwork stretches them.
        DocumentShape shape = ReadWordArt("fillcolor=\"silver\" stroked=\"f\"");

        Assert.True(shape.StretchesTextToBox);
        Assert.True(shape.TextOutline.IsEmpty);
        Assert.Equal(BColor.FromArgb(192, 192, 192), Assert.Single(Assert.Single(shape.Paragraphs).Runs).Style.Foreground);
    }

    [Fact]
    public void A_Text_Path_On_A_Custom_Shape_Is_Not_Stretched()
    {
        // tdf138739.docx, drawinglayer-pic-pos.docx: LibreOffice's own Fontwork
        // written back as o:spt="100", which LibreOffice 24.2 reads as a plain
        // shape.
        DocumentShape shape = ReadWordArt("o:spt=\"100\" fillcolor=\"#06c\"");

        Assert.False(shape.StretchesTextToBox);
        Assert.True(shape.TextOutline.IsEmpty);
    }

    [Theory]
    [InlineData("-2041671fd", -2041671 / 65536d)] // tdf168017.docx
    [InlineData("-31.15", -31.15)]
    [InlineData("315", 315)]
    [InlineData("", 0)]
    public void A_Vml_Angle_May_Be_In_Fixed_Point_Degrees(string rotation, double degrees)
    {
        DocumentShape shape = Assert.Single(DocxTestPackage.ReadBody(
            "<w:p><w:r><w:pict " + Vml + ">" +
            "<v:rect style=\"position:absolute;margin-left:119.55pt;margin-top:21.8pt;width:154.7pt;height:97.7pt;rotation:" + rotation + "\"/>" +
            "</w:pict></w:r></w:p>").Document.Shapes);

        Assert.Equal(degrees % 360 * Math.PI / 180, shape.RotationRadians, 6);
        Assert.False(shape.IsCustomShape, "an ungrouped box stays a frame");
    }

    [Theory]
    // FDO74774.docx's chart frame: a rounded rectangle with a rectangular hole.
    [InlineData("m,8c,4,4,,8,l7480,v5,,8,4,8,8l7488,4104v,5,-3,8,-8,8l8,4112v-4,,-8,-3,-8,-8l,8xm16,4104r-8,-8l7480,4096r-8,8l7472,8r8,8l8,16,16,8r,4096xe", true)]
    [InlineData("m,l7488,c7488,2000,7488,2000,7488,4112xe", true)]
    [InlineData("m,c4,4xe", false)] // a curve needs six numbers per segment
    [InlineData("m,qb7488,4112xe", false)]
    public void A_Vml_Freeform_Draws_Cubic_Curves(string path, bool drawn)
    {
        var shape = new System.Xml.Linq.XElement("shape", new System.Xml.Linq.XAttribute("coordsize", "7488,4112"));

        byte[]? png = DocxVmlPath.Render(shape, path, 300, 165, new BColor(127, 127, 127), new BColor(127, 127, 127), 0.75);

        Assert.Equal(drawn, png is not null);
    }
}
