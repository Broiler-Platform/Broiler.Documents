using Broiler.Graphics.Color;
using System.IO.Compression;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// VML shapes in a <c>w:pict</c>: how Word wrote a text box before 2010, and
/// how some producers still write one. They were read as nothing, and the text
/// in them was lost with the box.
/// </summary>
public sealed class DocxVmlShapeTests
{
    private const string Vml =
        "xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:w10=\"urn:schemas-microsoft-com:office:word\"";

    /// <summary>A4 with one-inch margins.</summary>
    private const string Section =
        "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/>" +
        "<w:pgMar w:left=\"1440\" w:right=\"1440\" w:top=\"1440\" w:bottom=\"1440\"/></w:sectPr>";

    private static DocumentReadResult Read(string shapeXml) =>
        DocxTestPackage.ReadBody(
            "<w:p><w:r><w:pict " + Vml + ">" + shapeXml + "</w:pict></w:r><w:r><w:t>body</w:t></w:r></w:p>" +
            Section);

    private static string TextBox(string text) =>
        "<v:textbox><w:txbxContent><w:p><w:r><w:t>" + text + "</w:t></w:r></w:p></w:txbxContent></v:textbox>";

    [Fact]
    public void Every_Sibling_Shape_In_A_Group_Is_Read()
    {
        // FDO78590.docx: a logo of ninety-odd v:shape siblings drew only the last.
        string Shape(int left) =>
            $"<v:shape coordsize=\"10,10\" style=\"position:absolute;left:{left};top:0;width:10;height:10\" " +
            "fillcolor=\"red\" stroked=\"f\" path=\"m,l10,,10,10xe\"/>";
        var document = Read("<v:group coordsize=\"30,10\" style=\"width:30pt;height:10pt\">" +
            Shape(0) + Shape(10) + Shape(20) + "</v:group>").Document;

        Assert.Equal(3, document.Shapes.Count);
    }

    [Fact]
    public void A_Shape_In_A_Framed_Paragraph_Is_Kept_And_Placed_From_The_Frame()
    {
        var document = DocxTestPackage.ReadBody(
            "<w:p><w:pPr><w:framePr w:w=\"2000\" w:h=\"1000\" w:hRule=\"exact\" w:hAnchor=\"page\" " +
            "w:vAnchor=\"page\" w:x=\"2000\" w:y=\"1000\"/></w:pPr><w:r><w:pict " + Vml + ">" +
            "<v:rect style=\"width:20pt;height:10pt\" fillcolor=\"red\"/></w:pict></w:r></w:p>" + Section).Document;

        Assert.Equal(2, document.Shapes.Count);
        DocumentShape inner = document.Shapes[1];
        Assert.Equal(20, inner.Width, 3);
        Assert.Equal(document.Shapes[0].OffsetX, inner.OffsetX, 3);
        Assert.Equal(50, inner.OffsetY, 3);
        Assert.True(inner.OffsetFromPage);
    }

    [Fact]
    public void An_Inline_Group_Keeps_Every_Child_And_Starts_At_Their_Bounds()
    {
        // fdo46361: a drawing canvas of three rectangles. Same-named children
        // replaced each other, and the canvas's empty lead space was kept where
        // LibreOffice draws the group shrunk to its children.
        var shapes = Read("""
            <v:group style="width:450pt;height:657pt;mso-position-horizontal-relative:char" coordsize="57150,83439">
              <v:rect style="position:absolute;left:43434;top:43434;width:11430;height:16002" fillcolor="#396"/>
              <v:rect style="position:absolute;left:44577;top:45720;width:9144;height:5715"/>
              <v:rect style="position:absolute;left:44577;top:52578;width:9144;height:5715"/>
            </v:group>
            """).Document.Shapes;

        Assert.Equal(3, shapes.Count);
        Assert.Equal(0, shapes[0].OffsetX, 3);
        Assert.Equal(0, shapes[0].OffsetY, 3);
        Assert.Equal(90, shapes[0].Width, 3);
        Assert.Equal(9, shapes[1].OffsetX, 3);
    }

    [Fact]
    public void A_Grouped_Polygon_Preserves_Transparent_Space_And_Group_Alignment()
    {
        var shape = Assert.Single(Read("""
            <v:group coordsize="100,100" style="width:100pt;height:100pt;mso-position-horizontal:center;mso-position-horizontal-relative:page">
              <v:shape coordsize="100,100" style="width:100;height:100" fillcolor="red" stroked="f" path="m,l100,,100,100xe"/>
            </v:group>
            """).Document.Shapes);

        Assert.Equal((11906 / 20d - 100) / 2 - 72, shape.OffsetX, 3);
        var image = Assert.IsType<InlineImage>(shape.Image);
        Assert.Equal("image/png", image.ContentType);
        byte[] png = image.Data.ToArray();
        int dataLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(33, 4));
        using var data = new MemoryStream(png, 41, dataLength);
        using var compressor = new ZLibStream(data, CompressionMode.Decompress);
        var pixels = new byte[200 * 801];
        compressor.ReadExactly(pixels);
        Assert.Equal(255, pixels[20 * 801 + 4 * 180 + 4]);
        Assert.Equal(0, pixels[180 * 801 + 4 * 20 + 4]);
    }

    [Theory]
    [InlineData("t", true)]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("f", false)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    public void Vml_Textbox_Autofit_Keeps_Its_Explicit_Growth_Setting(string value, bool grows)
    {
        var document = Read("<v:rect style='width:80pt;height:10pt'>" +
            TextBox("first line").Replace("<v:textbox>", $"<v:textbox style='mso-fit-shape-to-text:{value}'>") + "</v:rect>").Document;
        Assert.Equal(grows, Assert.Single(document.Shapes).GrowsToText);
    }

    [Fact]
    public void A_Text_Box_Is_A_Shape_With_Its_Text_Fill_And_Outline()
    {
        // tdf126533_axialAngle.docx's box, with its gradient.
        DocumentReadResult result = Read(
            "<v:rect style=\"position:absolute;left:0;margin-left:19.5pt;margin-top:-34.5pt;width:204pt;" +
            "height:155.8pt;z-index:251658240\" fillcolor=\"green\" strokeweight=\"0\">" +
            "<v:fill color2=\"red\" angle=\"-150\" type=\"gradient\"/>" + TextBox("Textbox") + "</v:rect>");

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.Equal("Textbox", Assert.Single(shape.Paragraphs).Text);
        Assert.Equal(19.5, shape.OffsetX, 3);
        Assert.Equal(-34.5, shape.OffsetY, 3);
        Assert.Equal(204, shape.Width, 3);
        Assert.Equal(155.8, shape.Height, 3);
        Assert.False(shape.OffsetFromPage);
        Assert.False(shape.BehindText);
        Assert.Equal(new ShapeFill(BColor.FromArgb(0, 128, 0), BColor.FromArgb(255, 0, 0), 240), shape.Fill);
        Assert.Equal(BColor.FromArgb(0, 0, 0), shape.Outline);
        Assert.Equal("body", result.Document.PlainText);
    }

    [Fact]
    public void Negative_Vml_Levels_Keep_Backgrounds_Behind_Other_Shapes()
    {
        DocumentShape At(int level) => Assert.Single(Read(
            $"<v:rect fillcolor='red' style='width:10pt;height:10pt;z-index:{level}'/>").Document.Shapes);
        var levels = new[] { int.MinValue, -251658240, -998, -997, -1 };
        var shapes = levels.Select(At).ToArray();
        Assert.All(shapes, shape => Assert.True(shape.BehindText));
        for (int i = 1; i < shapes.Length; i++)
            Assert.True(shapes[i - 1].ZOrder < shapes[i].ZOrder);
        Assert.Equal(0, shapes[0].ZOrder);
        Assert.Equal(int.MaxValue, shapes[^1].ZOrder);
        Assert.False(At(0).BehindText);
        Assert.Equal(123, At(123).ZOrder);
    }

    [Fact]
    public void A_Backdrop_Measured_From_The_Page_Stands_On_The_Page()
    {
        // tdf123163-1.docx: a full-page panel behind the text, at nought from
        // the page's top-left corner.
        DocumentShape shape = Assert.Single(Read(
            "<v:shape fillcolor=\"#EAE9E4\" stroked=\"f\" style=\"position:absolute;width:573.1pt;height:772.1pt;" +
            "z-index:-251658240;margin-left:0pt;margin-top:0pt;mso-position-horizontal-relative:page;" +
            "mso-position-vertical-relative:page\"><v:textbox><w:txbxContent/></v:textbox></v:shape>")
            .Document.Shapes);

        Assert.True(shape.OffsetFromPage);
        Assert.Equal(0, shape.OffsetY, 3);
        Assert.Equal(-72, shape.OffsetX, 3);
        Assert.True(shape.BehindText);
        Assert.True(shape.Outline.IsEmpty);
        Assert.Equal(ShapeFill.Solid(BColor.FromArgb(0xEA, 0xE9, 0xE4)), shape.Fill);
    }

    [Fact]
    public void A_Shape_Measured_From_The_Margin_Is_Measured_From_The_Page_Through_It()
    {
        DocumentShape shape = Assert.Single(Read(
            "<v:rect fillcolor=\"red\" style=\"position:absolute;width:10pt;height:10pt;margin-top:5pt;" +
            "mso-position-vertical-relative:margin\"/>").Document.Shapes);

        Assert.True(shape.OffsetFromPage);
        Assert.Equal(72 + 5, shape.OffsetY, 3);
    }

    [Fact]
    public void An_Unfilled_Empty_Shape_Keeps_Its_Default_Outline()
    {
        var shape = Assert.Single(Read(
            "<v:rect filled=\"f\" strokeweight=\"2pt\" style=\"position:absolute;width:10pt;height:10pt\"/>").Document.Shapes);
        Assert.Null(shape.Fill);
        Assert.Equal(BColor.Black, shape.Outline);
        Assert.Equal(2, shape.OutlineWidth);
    }

    [Fact]
    public void A_Vml_Picture_Is_Left_To_The_Picture_Path()
    {
        DocumentReadResult result = Read(
            "<v:shape style=\"width:10pt;height:10pt\"><v:imagedata r:id=\"rMissing\"/></v:shape>");

        Assert.Empty(result.Document.Shapes);
    }

    [Fact]
    public void A_Wrap_Is_Read_From_W10_Wrap()
    {
        DocumentShape shape = Assert.Single(Read(
            "<v:rect fillcolor=\"red\" style=\"position:absolute;width:10pt;height:10pt\">" +
            "<w10:wrap type=\"square\"/></v:rect>").Document.Shapes);

        Assert.Equal(ShapeWrap.Square, shape.Wrap);
    }

    private static DocumentReadResult ReadWithImage(string shapeXml) =>
        DocxTestPackage.ReadWithMedia(
            "<w:p><w:r><w:pict " + Vml + ">" + shapeXml + "</w:pict></w:r><w:r><w:t>body</w:t></w:r></w:p>" +
            Section,
            DocxTestPackage.ImageRelationship("rImg", "media/image1.png"),
            new Dictionary<string, byte[]> { ["word/media/image1.png"] = DocxTestPackage.OnePixelPng });

    [Fact]
    public void An_Absolute_Positioned_Vml_Picture_Is_Read_As_A_Floating_Shape_With_Image()
    {
        DocumentReadResult result = ReadWithImage(
            "<v:shape style=\"position:absolute;left:0;margin-left:10pt;margin-top:20pt;width:100pt;height:50pt\">" +
            "<v:imagedata r:id=\"rImg\"/></v:shape>");

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.NotNull(shape.Image);
        Assert.True(shape.HasImage);
        Assert.Equal(10, shape.OffsetX, 3);
        Assert.Equal(20, shape.OffsetY, 3);
        Assert.Equal(100, shape.Width, 3);
        Assert.Equal(50, shape.Height, 3);
        Assert.Equal("body", result.Document.PlainText);
    }

    [Fact]
    public void An_Extreme_Negative_Top_Margin_Bypasses_Wrapping()
    {
        // tdf#78902: when marginTop < -1000 pt, wrap must be None to prevent text
        // wrapping around an out-of-page graphic.
        DocumentReadResult result = ReadWithImage(
            "<v:shape style=\"position:absolute;left:0;margin-left:10pt;margin-top:-1006.4pt;width:100pt;height:50pt\">" +
            "<w10:wrap type=\"through\"/><v:imagedata r:id=\"rImg\"/></v:shape>");

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.Equal(ShapeWrap.None, shape.Wrap);
    }

    [Fact]
    public void An_Inline_WordArt_Shape_Draws_Its_String()
    {
        DocumentReadResult result = Read(
            "<v:shapetype id='t172'><v:textpath on='t'/></v:shapetype>" +
            "<v:shape type='#t172' style='width:60pt;height:91.5pt' fillcolor='black'>" +
            "<v:textpath style='font-family:\"Arial Narrow\";font-size:12pt' string='Watch LIVE&#xA;ask'/></v:shape>");

        RichTextParagraph paragraph = Assert.Single(result.Document.Paragraphs);
        Assert.StartsWith("Watch LIVE\u2028ask", paragraph.Text);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.shape.wordart");
    }

    [Fact]
    public void A_WordArt_Shape_In_A_Text_Box_Inherits_Its_Type_And_Draws_Its_String()
    {
        // fdo78300: the text box's content is read detached from the document,
        // so the v:shapetype that turns the text path on was not found.
        DocumentReadResult result = Read(
            "<v:shape style='position:absolute;margin-left:30pt;width:175pt;height:40pt' stroked='f'>" +
            "<v:textbox><w:txbxContent><w:p><w:r><w:pict>" +
            "<v:shapetype id='t136' coordsize='21600,21600'><v:textpath on='t' fitshape='t'/></v:shapetype>" +
            "<v:shape type='#t136' style='width:150pt;height:19.5pt' fillcolor='#9400ed'>" +
            "<v:textpath style='font-family:\"Arial Black\"' string='Homer was sleeping'/></v:shape>" +
            "</w:pict></w:r></w:p></w:txbxContent></v:textbox></v:shape>");

        DocumentShape box = Assert.Single(result.Document.Shapes);
        Assert.Contains(box.Paragraphs, p => p.Text.Contains("Homer was sleeping"));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "docx.shape.geometry");
    }
}
