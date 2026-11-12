using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// DOCX shapes and pictures of office issue #242: a table placed in a
/// paragraph frame, Word's washout picture watermark, a VML group of text
/// boxes, and inline pictures that are turned or carry an outer shadow.
/// </summary>
public sealed class DocxOfficeIssue242ShapesTests
{
    private const string Vml =
        "xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\"";

    private const string Frame =
        "<w:framePr w:w=\"5103\" w:h=\"1565\" w:hRule=\"exact\" w:wrap=\"around\" w:vAnchor=\"page\" w:hAnchor=\"page\" w:x=\"6345\" w:y=\"2225\"/>";

    private const string Section =
        "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/>" +
        "<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr>";

    private static Dictionary<string, byte[]> Media() =>
        new(StringComparer.Ordinal) { ["word/media/image1.png"] = DocxTestPackage.OnePixelPng };

    private static DocumentReadResult ReadPicture(string runXml) =>
        DocxTestPackage.ReadWithMedia(
            "<w:p>" + runXml + "</w:p>",
            DocxTestPackage.ImageRelationship("rId7", "media/image1.png"),
            Media());

    [Fact]
    public void A_Table_Whose_Cells_Carry_The_Frame_Properties_Goes_Into_The_Frame()
    {
        // fdo60957-2.docx: paragraph F, a one-cell table G and paragraph H all
        // state the same w:framePr, and LibreOffice draws the three in one frame.
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:t>s</w:t></w:r></w:p>" +
            "<w:p><w:pPr>" + Frame + "</w:pPr><w:r><w:t>F</w:t></w:r></w:p>" +
            "<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"5046\"/></w:tblGrid>" +
            "<w:tr><w:tc><w:tcPr><w:tcW w:w=\"5046\" w:type=\"dxa\"/></w:tcPr>" +
            "<w:p><w:pPr>" + Frame + "</w:pPr><w:r><w:t>G</w:t></w:r></w:p></w:tc></w:tr></w:tbl>" +
            "<w:p><w:pPr>" + Frame + "</w:pPr><w:r><w:t>H</w:t></w:r></w:p>" +
            "<w:p><w:r><w:t>E</w:t></w:r></w:p>");

        RichTextDocument document = result.Document;
        Assert.Empty(document.Tables);
        Assert.Equal(["s", "E"], document.Paragraphs.Select(paragraph => paragraph.Text));
        DocumentShape frame = Assert.Single(document.Shapes);
        Assert.Equal(["F", "G", "H"], frame.Paragraphs.Select(paragraph => paragraph.Text));
        DocumentTable table = Assert.Single(frame.Tables);
        Assert.Equal(1, table.ParagraphIndex);
        Assert.Equal(5103 / 20d, frame.Width, 3);
    }

    [Fact]
    public void A_Table_With_No_Frame_Properties_Ends_The_Frame()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:pPr>" + Frame + "</w:pPr><w:r><w:t>F</w:t></w:r></w:p>" +
            DocxTestPackage.Table(["<w:p><w:r><w:t>G</w:t></w:r></w:p>"]) +
            "<w:p><w:r><w:t>E</w:t></w:r></w:p>");

        DocumentShape frame = Assert.Single(result.Document.Shapes);
        Assert.Equal("F", Assert.Single(frame.Paragraphs).Text);
        Assert.Empty(frame.Tables);
        Assert.Single(result.Document.Tables);
    }

    [Fact]
    public void A_Washout_Vml_Picture_Is_Drawn_As_LibreOffice_Draws_A_Watermark()
    {
        // pictureWatermark.docx: Word's picture watermark is its "Washout"
        // recolouring, gain 19661f and black level 22938f.
        InlineImage washed = ReadVmlImage("gain=\"19661f\" blacklevel=\"22938f\"");
        Assert.Equal(0.5, washed.Presentation.Brightness);
        Assert.Equal(-0.7, washed.Presentation.Contrast);

        // Any other gain or black level LibreOffice leaves alone.
        Assert.True(ReadVmlImage("gain=\"32768f\" blacklevel=\"16384f\"").Presentation.IsDefault);
        Assert.True(ReadVmlImage("gain=\"19661f\"").Presentation.IsDefault);
    }

    private static InlineImage ReadVmlImage(string attributes)
    {
        DocumentReadResult result = ReadPicture(
            "<w:r><w:pict><v:shape " + Vml + " id=\"wm\" style=\"position:absolute;margin-left:0;margin-top:0;width:100pt;height:80pt\">" +
            "<v:imagedata r:id=\"rId7\" " + attributes + "/></v:shape></w:pict></w:r>");
        return Assert.IsType<InlineImage>(Assert.Single(result.Document.Shapes).Image);
    }

    [Fact]
    public void A_Vml_Group_Places_Its_Children_By_Its_Coordinates_And_Aligns_By_Them()
    {
        // n751077.docx: coordorigin=",1440" leaves the x origin empty, the
        // children say "centred in the margin" for when they are ungrouped, and
        // the page-wide group is centred by the box its children cover.
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:pict>" +
            "<v:group " + Vml + " style=\"position:absolute;margin-left:0;margin-top:0;width:600pt;height:600pt;" +
            "mso-position-horizontal:center;mso-position-horizontal-relative:page;mso-position-vertical-relative:page\" " +
            "coordorigin=\",1000\" coordsize=\"1000,1000\">" +
            "<v:rect style=\"position:absolute;left:200;top:1000;width:200;height:100;" +
            "mso-position-horizontal:right;mso-position-horizontal-relative:margin\" filled=\"f\" stroked=\"f\">" +
            "<v:textbox><w:txbxContent><w:p><w:r><w:t>A</w:t></w:r></w:p></w:txbxContent></v:textbox></v:rect>" +
            "<v:rect style=\"position:absolute;left:400;top:1500;width:200;height:100\" filled=\"f\" stroked=\"f\">" +
            "<v:textbox><w:txbxContent><w:p><w:r><w:t>B</w:t></w:r></w:p></w:txbxContent></v:textbox></v:rect>" +
            "</v:group></w:pict></w:r></w:p>" + Section);

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "docx.shape.group");
        DocumentShape first = Assert.Single(result.Document.Shapes, shape => shape.Paragraphs.Any(p => p.Text == "A"));
        DocumentShape second = Assert.Single(result.Document.Shapes, shape => shape.Paragraphs.Any(p => p.Text == "B"));
        // 0.6 pt a unit. The children span 200..600, 240 pt, centred on the
        // 612 pt page from 186 pt; each keeps its place in the group.
        Assert.Equal(120, first.Width, 3);
        Assert.Equal(120, second.OffsetX - first.OffsetX, 3);
        Assert.Equal(300, second.OffsetY - first.OffsetY, 3);
        // Offsets across are measured from the text column, 72 pt in.
        Assert.Equal(186 - 72, first.OffsetX, 3);
    }

    [Fact]
    public void A_Turned_Inline_Picture_Takes_LibreOffices_Box_And_Its_Effect_Extent()
    {
        // tdf103001.docx: a picture turned a quarter turn takes the box of its
        // height across and its width down, and the effect extent Word leaves
        // around a turned picture is added to its margins.
        InlineImage image = ReadInline(width: 254000, height: 127000, rotation: "5400000",
            effectExtent: "l=\"0\" t=\"63500\" r=\"0\" b=\"127000\"");

        Assert.Equal(10, image.Width!.Value, 3);
        Assert.Equal(20, image.Height!.Value, 3);
        ImagePresentation presentation = image.Presentation;
        Assert.Equal(90, presentation.Rotation, 3);
        Assert.Equal(20, presentation.UnrotatedWidth!.Value, 3);
        Assert.Equal(10, presentation.UnrotatedHeight!.Value, 3);
        Assert.Equal(new TableCellPadding(0, 5, 0, 10), presentation.EffectExtent);
    }

    [Fact]
    public void A_Picture_Turned_Less_Than_An_Eighth_Keeps_Its_Width_And_Grows_To_Its_Turned_Height()
    {
        InlineImage image = ReadInline(width: 254000, height: 127000, rotation: "1800000");

        Assert.Equal(20, image.Width!.Value, 3);
        Assert.Equal(20 * 0.5 + 10 * Math.Sqrt(3) / 2, image.Height!.Value, 3);
    }

    [Fact]
    public void A_Plain_Inline_Picture_Ignores_Its_Effect_Extent()
    {
        InlineImage image = ReadInline(width: 254000, height: 127000, rotation: null,
            effectExtent: "l=\"0\" t=\"0\" r=\"0\" b=\"127000\"");

        Assert.True(image.Presentation.IsDefault);
        Assert.Equal(20, image.Width!.Value, 3);
    }

    [Fact]
    public void An_Outer_Shadow_Is_Read_With_Its_Scale_Alignment_And_Offset()
    {
        // tdf133473.docx: a black shadow twice the picture's size from its top left.
        InlineImage image = ReadInline(width: 254000, height: 127000, rotation: null,
            effectExtent: "l=\"0\" t=\"0\" r=\"254000\" b=\"127000\"",
            effects: "<a:effectLst><a:outerShdw sx=\"200000\" sy=\"200000\" algn=\"tl\" rotWithShape=\"0\">" +
                "<a:prstClr val=\"black\"/></a:outerShdw></a:effectLst>");

        ImageShadow shadow = Assert.IsType<ImageShadow>(image.Presentation.Shadow);
        Assert.Equal(new BColor(0, 0, 0), shadow.Color);
        Assert.Equal(new BRect(10, 20, 40, 20), shadow.Bounds(new BRect(10, 20, 20, 10)));
        Assert.Equal(new TableCellPadding(0, 0, 20, 10), image.Presentation.EffectExtent);

        // A shadow cast down and to the right, at 40% opacity, centred on the bottom edge by default.
        InlineImage offset = ReadInline(width: 254000, height: 127000, rotation: null,
            effects: "<a:effectLst><a:outerShdw dist=\"127000\" dir=\"5400000\">" +
                "<a:srgbClr val=\"FF0000\"><a:alpha val=\"40000\"/></a:srgbClr></a:outerShdw></a:effectLst>");
        ImageShadow cast = Assert.IsType<ImageShadow>(offset.Presentation.Shadow);
        Assert.Equal(102, cast.Color.A);
        Assert.Equal(0, cast.OffsetX, 6);
        Assert.Equal(10, cast.OffsetY, 6);
    }

    private static InlineImage ReadInline(long width, long height, string? rotation, string effectExtent = "", string effects = "")
    {
        string run =
            "<w:r><w:drawing>" +
            "<wp:inline xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\">" +
            "<wp:extent cx=\"" + width + "\" cy=\"" + height + "\"/>" +
            (effectExtent.Length > 0 ? "<wp:effectExtent " + effectExtent + "/>" : string.Empty) +
            "<wp:docPr id=\"1\" name=\"Picture 1\"/>" +
            "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
            "<a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">" +
            "<pic:pic xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">" +
            "<pic:nvPicPr><pic:cNvPr id=\"1\" name=\"Picture 1\"/><pic:cNvPicPr/></pic:nvPicPr>" +
            "<pic:blipFill><a:blip r:embed=\"rId7\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>" +
            "<pic:spPr><a:xfrm" + (rotation is null ? string.Empty : " rot=\"" + rotation + "\"") + ">" +
            "<a:off x=\"0\" y=\"0\"/><a:ext cx=\"" + width + "\" cy=\"" + height + "\"/></a:xfrm>" +
            "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>" + effects + "</pic:spPr>" +
            "</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r>";
        RichTextParagraph paragraph = Assert.Single(ReadPicture(run).Document.Paragraphs);
        return Assert.IsType<InlineImage>(paragraph.Runs[0].Style.Image);
    }
}
