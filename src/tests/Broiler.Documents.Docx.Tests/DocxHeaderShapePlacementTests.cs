namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Where a header's shapes stand, and which of them are drawn (office issue 249).
/// </summary>
public sealed class DocxHeaderShapePlacementTests
{
    private const string Rels =
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rH\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" " +
        "Target=\"header1.xml\"/></Relationships>";

    // A header 708 twips (35.4 points) from the top of the page.
    private const string Section =
        "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"default\"/>" +
        "<w:pgSz w:w=\"11906\" w:h=\"16838\"/>" +
        "<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/>" +
        "</w:sectPr>";

    private static string Box(string from, long offset, string docPrAttributes = "") =>
        "<w:r><w:drawing>" +
        "<wp:anchor xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" behindDoc=\"0\" relativeHeight=\"3\">" +
        "<wp:positionH relativeFrom=\"column\"><wp:posOffset>0</wp:posOffset></wp:positionH>" +
        "<wp:positionV relativeFrom=\"" + from + "\"><wp:posOffset>" + offset + "</wp:posOffset></wp:positionV>" +
        "<wp:extent cx=\"914400\" cy=\"228600\"/><wp:wrapNone/>" +
        "<wp:docPr id=\"1\" name=\"Rectangle 1\"" + docPrAttributes + "/>" +
        "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
        "<a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
        "<wps:wsp xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
        "<wps:spPr><a:solidFill><a:srgbClr val=\"2A8EBF\"/></a:solidFill></wps:spPr>" +
        "</wps:wsp></a:graphicData></a:graphic></wp:anchor>" +
        "</w:drawing></w:r>";

    private static DocumentShape ReadHeaderShape(string headerBlockXml)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/_rels/document.xml.rels"] = Rels,
            ["word/header1.xml"] = "<w:hdr " + DocxTestPackage.BodyNamespaceDeclarations + ">" + headerBlockXml + "</w:hdr>",
        };
        using var stream = new MemoryStream(
            DocxTestPackage.FromBody(DocxTestPackage.Paragraph("body") + Section, parts), writable: false);
        RichTextDocument document = new DocxDocumentCodec().Read(stream).Document;
        return Assert.Single(document.RunningContent.HeaderShapes(PageSelection.Default));
    }

    [Fact]
    public void A_Shape_Placed_From_A_Header_Paragraph_Is_Measured_From_The_Header()
    {
        // fdo83057.docx: a page number box 922020 EMU (72.6 points) below its
        // header paragraph, which starts at the header distance; LibreOffice
        // draws it 108 points down the page.
        DocumentShape shape = ReadHeaderShape("<w:p>" + Box("paragraph", 922020) + "</w:p>");

        Assert.Equal(35.4 + 72.6, shape.OffsetY, 1);
    }

    [Fact]
    public void A_Shape_Placed_From_The_Page_Keeps_Its_Offset()
    {
        DocumentShape shape = ReadHeaderShape("<w:p>" + Box("page", 922020) + "</w:p>");

        Assert.Equal(72.6, shape.OffsetY, 1);
    }

    [Fact]
    public void A_Shape_In_A_Header_Table_Cell_Keeps_Its_Offset()
    {
        // tdf105688.docx: the cell's paragraph is where the table puts it, which
        // the reader does not know; the header distance would only add to that.
        DocumentShape shape = ReadHeaderShape(DocxTestPackage.Table(["<w:p>" + Box("paragraph", 102870) + "</w:p>"]));

        Assert.Equal(8.1, shape.OffsetY, 1);
    }

    [Fact]
    public void A_Shape_Placed_From_Its_Header_Paragraph_Keeps_That_Offset_For_The_Layout()
    {
        DocumentShape shape = ReadHeaderShape("<w:p>" + Box("paragraph", 922020) + "</w:p>");

        Assert.Equal(72.6, shape.OffsetFromBandParagraph!.Value, 1);
    }

    [Fact]
    public void A_Shape_Placed_From_The_Page_Or_In_A_Cell_Has_No_Paragraph_Offset()
    {
        Assert.Null(ReadHeaderShape("<w:p>" + Box("page", 922020) + "</w:p>").OffsetFromBandParagraph);
        Assert.Null(ReadHeaderShape(DocxTestPackage.Table(["<w:p>" + Box("paragraph", 102870) + "</w:p>"])).OffsetFromBandParagraph);
    }

    [Theory]
    [InlineData(" hidden=\"1\"", false)]
    [InlineData(" hidden=\"0\"", true)]
    [InlineData("", true)]
    public void A_Hidden_Drawing_Is_Laid_Out_But_Not_Drawn(string docPrAttributes, bool printable)
    {
        // tdf156484.docx and tdf119201.docx: LibreOffice imports the shape
        // invisible.
        DocumentShape shape = ReadHeaderShape("<w:p>" + Box("paragraph", 0, docPrAttributes) + "</w:p>");

        Assert.Equal(printable, shape.Printable);
    }
}
