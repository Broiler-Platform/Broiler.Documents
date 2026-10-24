using Broiler.Graphics.Color;

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
}
