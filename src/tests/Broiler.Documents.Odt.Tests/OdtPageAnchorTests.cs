using Broiler.Graphics.Color;

namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Drawings anchored to a page by number: <c>text:anchor-type="page"</c> with
/// <c>text:anchor-page-number</c>.
/// </summary>
/// <remarks>
/// LibreOffice's <c>tdf135056.odt</c> anchors six frames to pages 1, 4, 5 and 17,
/// and LibreOffice makes seventeen pages to stand the last one on. Read against
/// the paragraph they sat beside, they all landed on the first pages, and nothing
/// asked for a seventeenth.
/// </remarks>
public sealed class OdtPageAnchorTests
{
    /// <summary>US Letter with one-inch margins.</summary>
    private const string Styles =
        "<office:automatic-styles>" +
        "<style:page-layout style:name=\"pm1\">" +
        "<style:page-layout-properties fo:page-width=\"8.5in\" fo:page-height=\"11in\" " +
        "fo:margin-left=\"1in\" fo:margin-right=\"1in\" fo:margin-top=\"1in\" fo:margin-bottom=\"1in\"/>" +
        "</style:page-layout>" +
        "</office:automatic-styles>" +
        "<office:master-styles>" +
        "<style:master-page style:name=\"Standard\" style:page-layout-name=\"pm1\"/>" +
        "</office:master-styles>";

    private static string GraphicStyle(string name, string properties) =>
        "<style:style style:name=\"" + name + "\" style:family=\"graphic\">" +
        "<style:graphic-properties draw:fill=\"solid\" draw:fill-color=\"#cc0000\" " + properties + "/>" +
        "</style:style>";

    private static string Frame(string anchor, string box, string style = "fr1", string textBoxAttributes = "") =>
        "<draw:frame draw:style-name=\"" + style + "\" " + anchor + " " + box + ">" +
        "<draw:text-box " + textBoxAttributes + "><text:p>boxed</text:p></draw:text-box></draw:frame>";

    private const string OnPageThree = "text:anchor-type=\"page\" text:anchor-page-number=\"3\"";

    private const string Box = "svg:x=\"2in\" svg:y=\"3in\" svg:width=\"2in\" svg:height=\"1in\"";

    private static DocumentReadResult Read(string bodyXml, string graphicStyles)
    {
        using var stream = new MemoryStream(
            OdtTestPackage.FromBody(bodyXml, graphicStyles, Styles),
            writable: false);
        return new OdtDocumentCodec().Read(stream);
    }

    private static RichTextDocument RoundTrip(RichTextDocument document)
    {
        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(document), writable: false);
        return new OdtDocumentCodec().Read(stream).Document;
    }

    [Fact]
    public void A_Frame_Anchored_To_A_Page_Stands_On_That_Page()
    {
        DocumentReadResult result = Read(
            Frame(OnPageThree, Box) + OdtTestPackage.Paragraph("body"),
            GraphicStyle("fr1", "style:horizontal-rel=\"page\" style:vertical-rel=\"page\""));

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.Equal(3, shape.AnchorPage);
        Assert.True(shape.IsPageAnchored);

        // Two inches from the page's left edge is one inch into the text column;
        // three inches down is three inches from the page's top edge.
        Assert.Equal(72, shape.OffsetX, 3);
        Assert.Equal(216, shape.OffsetY, 3);
        Assert.Equal("body", Assert.Single(result.Document.Paragraphs).Text);
    }

    [Fact]
    public void Offsets_Measured_In_The_Content_Area_Are_Taken_Back_To_The_Page()
    {
        DocumentShape shape = Assert.Single(Read(
            Frame(OnPageThree, Box) + OdtTestPackage.Paragraph("body"),
            GraphicStyle("fr1", "style:horizontal-rel=\"page-content\" style:vertical-rel=\"page-content\""))
            .Document.Shapes);

        Assert.Equal(144, shape.OffsetX, 3);
        Assert.Equal(288, shape.OffsetY, 3);
    }

    [Fact]
    public void A_Centred_Frame_Is_Placed_In_The_Middle_Of_Its_Area()
    {
        DocumentShape shape = Assert.Single(Read(
            Frame(OnPageThree, Box) + OdtTestPackage.Paragraph("body"),
            GraphicStyle(
                "fr1",
                "style:horizontal-pos=\"center\" style:horizontal-rel=\"page\" " +
                "style:vertical-pos=\"bottom\" style:vertical-rel=\"page-content\""))
            .Document.Shapes);

        // A two-inch box in the middle of an 8.5-inch page starts 3.25 inches in,
        // 2.25 inches into the column; its foot sits on the bottom margin.
        Assert.Equal(162, shape.OffsetX, 3);
        Assert.Equal(792 - 72 - 72, shape.OffsetY, 3);
    }

    [Fact]
    public void A_Style_That_Says_Nothing_Measures_From_The_Page()
    {
        DocumentShape shape = Assert.Single(Read(
            Frame(OnPageThree, Box) + OdtTestPackage.Paragraph("body"),
            GraphicStyle("fr1", string.Empty)).Document.Shapes);

        Assert.Equal(72, shape.OffsetX, 3);
        Assert.Equal(216, shape.OffsetY, 3);
    }

    [Theory]
    [InlineData("text:anchor-type=\"paragraph\"")]
    [InlineData("text:anchor-type=\"page\"")]
    [InlineData("text:anchor-type=\"page\" text:anchor-page-number=\"0\"")]
    [InlineData("text:anchor-type=\"page\" text:anchor-page-number=\"three\"")]
    public void A_Frame_With_No_Page_Number_Stays_With_Its_Paragraph(string anchor)
    {
        DocumentShape shape = Assert.Single(Read(
            Frame(anchor, Box) + OdtTestPackage.Paragraph("body"),
            GraphicStyle("fr1", "style:horizontal-rel=\"page\" style:vertical-rel=\"page\""))
            .Document.Shapes);

        Assert.False(shape.IsPageAnchored);
        Assert.Equal(144, shape.OffsetX, 3);
        Assert.Equal(216, shape.OffsetY, 3);
    }

    [Fact]
    public void A_Text_Box_Sized_By_Its_Minimum_Width_Is_A_Frame()
    {
        // How the page-17 frame of tdf135056 states its width.
        DocumentReadResult result = Read(
            Frame(OnPageThree, "svg:x=\"1in\" svg:y=\"1in\" svg:height=\"2in\"", textBoxAttributes: "fo:min-width=\"3in\"") +
            OdtTestPackage.Paragraph("body"),
            GraphicStyle("fr1", "style:horizontal-rel=\"page\" style:vertical-rel=\"page\""));

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.Equal(216, shape.Width, 3);
        Assert.Equal("boxed", Assert.Single(shape.Paragraphs).Text);
        Assert.Equal("body", Assert.Single(result.Document.Paragraphs).Text);
    }

    [Fact]
    public void A_Text_Box_Sized_Only_By_Its_Minimum_Height_Stays_In_The_Body()
    {
        // Its height is what its text needs, which is not known here; a box of
        // the minimum would clip the text it was made to hold.
        DocumentReadResult result = Read(
            Frame(OnPageThree, "svg:x=\"1in\" svg:y=\"1in\" svg:width=\"3in\"", textBoxAttributes: "fo:min-height=\"1in\"") +
            OdtTestPackage.Paragraph("body"),
            GraphicStyle("fr1", string.Empty));

        Assert.Empty(result.Document.Shapes);
        Assert.Contains(result.Document.Paragraphs, paragraph => paragraph.Text == "boxed");
    }

    [Fact]
    public void A_Page_Anchored_Shape_Round_Trips_With_Its_Page_And_Place()
    {
        var shape = new DocumentShape(
            0, 36, 500, 100, 50, ShapeFill.Solid(BColor.FromArgb(0xFF, 0x20, 0x40, 0x60)), anchorPage: 4);
        RichTextDocument source = RichTextDocument.FromPlainText("body")
            .WithPageGeometry(new PageGeometry(612, 792, 72, 72, 72, 72))
            .WithShapes([shape]);

        DocumentShape read = Assert.Single(RoundTrip(source).Shapes);

        Assert.Equal(4, read.AnchorPage);
        Assert.Equal(36, read.OffsetX, 2);
        Assert.Equal(500, read.OffsetY, 2);
        Assert.Equal(100, read.Width, 2);
    }

    [Fact]
    public void A_Page_Anchored_Shape_Is_Written_In_The_Body_And_Not_In_A_Paragraph()
    {
        var shape = new DocumentShape(0, 0, 0, 100, 50, ShapeFill.Solid(BColor.Black), anchorPage: 2);
        byte[] odt = OdtDocumentCodec.WriteToArray(RichTextDocument.FromPlainText("body").WithShapes([shape]));

        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(odt));
        using var reader = new StreamReader(archive.GetEntry("content.xml")!.Open());
        System.Xml.Linq.XDocument content = System.Xml.Linq.XDocument.Parse(reader.ReadToEnd());

        System.Xml.Linq.XNamespace office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
        System.Xml.Linq.XNamespace text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
        System.Xml.Linq.XElement body = content.Descendants(office + "text").Single();
        System.Xml.Linq.XElement drawing = body.Elements().First();

        Assert.Equal("page", (string?)drawing.Attribute(text + "anchor-type"));
        Assert.Equal("2", (string?)drawing.Attribute(text + "anchor-page-number"));
    }
}
