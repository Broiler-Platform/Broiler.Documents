namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// ISO/IEC 29500 Strict packages: the same WordprocessingML under the
/// <c>http://purl.oclc.org/ooxml/</c> namespaces, with lengths written with a
/// unit. The reader only knew the Transitional names, found no <c>w:body</c>,
/// and drew a blank page where LibreOffice draws the document.
/// </summary>
public sealed class DocxStrictTests
{
    /// <summary>
    /// Rewrites the Transitional names a test writes into their Strict
    /// spellings, so each test can be read as ordinary WordprocessingML.
    /// </summary>
    private static string Strict(string xml) => xml
        .Replace("http://schemas.openxmlformats.org/wordprocessingml/2006/main",
            "http://purl.oclc.org/ooxml/wordprocessingml/main", StringComparison.Ordinal)
        .Replace("http://schemas.openxmlformats.org/officeDocument/2006/relationships",
            "http://purl.oclc.org/ooxml/officeDocument/relationships", StringComparison.Ordinal)
        .Replace("http://schemas.openxmlformats.org/drawingml/2006/main",
            "http://purl.oclc.org/ooxml/drawingml/main", StringComparison.Ordinal)
        .Replace("http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing",
            "http://purl.oclc.org/ooxml/drawingml/wordprocessingDrawing", StringComparison.Ordinal)
        .Replace("http://schemas.openxmlformats.org/drawingml/2006/picture",
            "http://purl.oclc.org/ooxml/drawingml/picture", StringComparison.Ordinal);

    private static DocumentReadResult ReadStrict(
        string bodyXml,
        string stylesInnerXml = "",
        string relationshipsInnerXml = "",
        IReadOnlyDictionary<string, byte[]>? media = null)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_rels/.rels"] = Strict(
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" " +
                "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" " +
                "Target=\"word/document.xml\"/>" +
                "</Relationships>"),
            ["word/document.xml"] = Strict(
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<w:document " + DocxTestPackage.BodyNamespaceDeclarations + ">" +
                "<w:body>" + bodyXml + "</w:body>" +
                "</w:document>"),
            ["word/_rels/document.xml.rels"] = Strict(
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rIdStyles\" " +
                "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" " +
                "Target=\"strict-styles.xml\"/>" +
                relationshipsInnerXml +
                "</Relationships>"),
            ["word/strict-styles.xml"] = Strict(
                "<w:styles " + DocxTestPackage.BodyNamespaceDeclarations + ">" + stylesInnerXml + "</w:styles>"),
        };

        using var stream = new MemoryStream(DocxTestPackage.FromBody(string.Empty, parts, media), writable: false);
        return new DocxDocumentCodec().Read(stream);
    }

    [Fact]
    public void The_Body_Is_Read_Under_The_Strict_Namespace()
    {
        DocumentReadResult result = ReadStrict(
            "<w:p><w:r><w:t xml:space=\"preserve\">plain </w:t></w:r>" +
            "<w:r><w:rPr><w:b/></w:rPr><w:t>bold</w:t></w:r></w:p>" +
            DocxTestPackage.Paragraph("second"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "docx.document.body");
        Assert.Equal(["plain bold", "second"], result.Document.Paragraphs.Select(p => p.Text));
        Assert.True(result.Document.Paragraphs[0].StyleAt(6).Bold);
    }

    [Fact]
    public void The_Read_Says_It_Was_Strict()
    {
        DocumentReadResult strict = ReadStrict(DocxTestPackage.Paragraph("x"));
        DocumentReadResult transitional = DocxTestPackage.ReadBody(DocxTestPackage.Paragraph("x"));

        DocumentDiagnostic diagnostic = Assert.Single(strict.Diagnostics, d => d.Code == "docx.strict");
        Assert.Equal(DocumentDiagnosticSeverity.Info, diagnostic.Severity);
        Assert.DoesNotContain(transitional.Diagnostics, d => d.Code == "docx.strict");
    }

    [Fact]
    public void Styles_Are_Found_Through_A_Strict_Relationship_Type()
    {
        // The styles part has a name nothing would find by convention, so only
        // the relationship can lead to it.
        DocumentReadResult result = ReadStrict(
            DocxTestPackage.StyledParagraph("Big", "big"),
            DocxTestPackage.Style("Big", "<w:rPr><w:sz w:val=\"40\"/></w:rPr>"));

        Assert.Equal(20, result.Document.Paragraphs[0].StyleAt(0).FontSize);
    }

    [Fact]
    public void A_Picture_Is_Found_Through_Strict_DrawingML_And_A_Strict_Relationship()
    {
        DocumentReadResult result = ReadStrict(
            "<w:p>" + DocxTestPackage.DrawingRun("rIdImage", 914400, 457200) + "</w:p>",
            relationshipsInnerXml: Strict(DocxTestPackage.ImageRelationship("rIdImage", "media/one.png")),
            media: new Dictionary<string, byte[]> { ["word/media/one.png"] = DocxTestPackage.OnePixelPng });

        InlineImage image = Assert.IsType<InlineImage>(result.Document.Paragraphs[0].StyleAt(0).Image);
        Assert.Equal(72, image.Width);
        Assert.Equal(DocxTestPackage.OnePixelPng, image.Data.ToArray());
    }

    [Fact]
    public void Lengths_With_Units_Are_Read_Where_Twips_Were_Expected()
    {
        DocumentReadResult result = ReadStrict(
            "<w:p><w:pPr><w:spacing w:before=\"18pt\" w:after=\"6pt\" w:line=\"12.95pt\" w:lineRule=\"auto\"/>" +
            "<w:ind w:start=\"36pt\"/></w:pPr>" +
            "<w:r><w:rPr><w:sz w:val=\"11pt\"/></w:rPr><w:t>x</w:t></w:r></w:p>" +
            "<w:sectPr><w:pgSz w:w=\"612pt\" w:h=\"11in\"/>" +
            "<w:pgMar w:top=\"2.54cm\" w:right=\"25.4mm\" w:bottom=\"1in\" w:left=\"6pc\" " +
            "w:header=\"36pt\" w:footer=\"36pt\" w:gutter=\"0pt\"/></w:sectPr>");

        RichTextParagraph paragraph = result.Document.Paragraphs[0];
        Assert.Equal(18, paragraph.Style.SpacingBefore, 2);
        Assert.Equal(6, paragraph.Style.SpacingAfter, 2);
        Assert.Equal(259 / 240f, paragraph.Style.LineSpacing, 3);
        Assert.Equal(2, paragraph.Style.IndentLevel);
        Assert.Equal(11, paragraph.StyleAt(0).FontSize);

        PageGeometry page = Assert.IsType<PageGeometry>(result.Document.PageGeometry);
        Assert.Equal(612, page.Width, 1);
        Assert.Equal(792, page.Height, 1);
        Assert.Equal(72, page.MarginTop, 1);
        Assert.Equal(72, page.MarginRight, 1);
        Assert.Equal(72, page.MarginBottom, 1);
        Assert.Equal(72, page.MarginLeft, 1);
    }

    [Theory]
    [InlineData("360", 360)]
    [InlineData("18pt", 360)]
    [InlineData("-14.15pt", -283)]
    [InlineData("1in", 1440)]
    [InlineData("2.54cm", 1440)]
    [InlineData("25.4mm", 1440)]
    [InlineData("1pc", 240)]
    [InlineData("1pi", 240)]
    [InlineData(" 6pt ", 120)]
    public void A_Twips_Measure_Is_Read_Bare_Or_With_A_Unit(string value, int expected)
    {
        Assert.True(DocxValueReader.TryReadTwips(new System.Xml.Linq.XAttribute("v", value), out int twips));
        Assert.Equal(expected, twips);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pt")]
    [InlineData("12px")]
    [InlineData("1e3pt")]
    [InlineData("twelve")]
    [InlineData("99999999999pt")]
    public void A_Measure_That_Is_Not_One_Is_Refused(string value)
    {
        Assert.False(DocxValueReader.TryReadTwips(new System.Xml.Linq.XAttribute("v", value), out int twips));
        Assert.Equal(0, twips);
    }

    [Theory]
    [InlineData("http://purl.oclc.org/ooxml/wordprocessingml/main",
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main")]
    [InlineData("http://purl.oclc.org/ooxml/officeDocument/relationships",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships")]
    [InlineData("http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument")]
    [InlineData("http://purl.oclc.org/ooxml/officeDocument/relationships/extendedProperties",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties")]
    [InlineData("http://purl.oclc.org/ooxml/officeDocument/extendedProperties",
        "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties")]
    [InlineData("http://purl.oclc.org/ooxml/drawingml/wordprocessingDrawing",
        "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing")]
    [InlineData("http://schemas.openxmlformats.org/wordprocessingml/2006/main",
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main")]
    [InlineData("http://purl.oclc.org/ooxml/", "http://purl.oclc.org/ooxml/")]
    public void Strict_Names_Map_To_Transitional_Ones_By_Rule(string strict, string transitional)
    {
        Assert.Equal(transitional, DocxStrict.ToTransitional(strict));
    }
}
