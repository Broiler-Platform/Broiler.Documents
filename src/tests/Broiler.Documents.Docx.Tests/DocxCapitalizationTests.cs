namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// <c>w:caps</c> and <c>w:smallCaps</c>. Word stores the author's casing and
/// draws capitals, so these must survive as an attribute rather than being baked
/// into the text — otherwise an open-and-save rewrites what the author typed.
/// </summary>
public sealed class DocxCapitalizationTests
{
    [Fact]
    public void Reads_Caps_As_An_Attribute_Not_As_Uppercased_Text()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:rPr><w:caps/></w:rPr><w:t>Elene Bartky</w:t></w:r></w:p>");

        RichTextParagraph paragraph = Assert.Single(result.Document.Paragraphs);
        Assert.Equal("Elene Bartky", paragraph.Text);
        Assert.Equal(TextCapitalization.AllCaps, paragraph.StyleAt(0).Capitalization);
    }

    [Fact]
    public void Reads_SmallCaps()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:rPr><w:smallCaps/></w:rPr><w:t>Kontakt</w:t></w:r></w:p>");

        Assert.Equal(
            TextCapitalization.SmallCaps,
            Assert.Single(result.Document.Paragraphs).StyleAt(0).Capitalization);
    }

    [Fact]
    public void Reads_Caps_Declared_By_A_Paragraph_Style()
    {
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            DocxTestPackage.StyledParagraph("Title", "Elene Bartky"),
            DocxTestPackage.Style("Title", "<w:rPr><w:caps/><w:sz w:val=\"96\"/></w:rPr>"));

        RichTextParagraph paragraph = Assert.Single(result.Document.Paragraphs);
        Assert.Equal("Elene Bartky", paragraph.Text);
        Assert.Equal(TextCapitalization.AllCaps, paragraph.StyleAt(0).Capitalization);
        Assert.Equal(48f, paragraph.StyleAt(0).FontSize);
    }

    [Fact]
    public void A_Run_Can_Turn_Off_The_Caps_Its_Style_Applied()
    {
        string paragraph =
            "<w:p><w:pPr><w:pStyle w:val=\"Title\"/></w:pPr>" +
            "<w:r><w:rPr><w:caps w:val=\"0\"/></w:rPr><w:t>quiet</w:t></w:r></w:p>";

        DocumentReadResult result = DocxTestPackage.ReadStyled(
            paragraph,
            DocxTestPackage.Style("Title", "<w:rPr><w:caps/></w:rPr>"));

        Assert.Equal(
            TextCapitalization.None,
            Assert.Single(result.Document.Paragraphs).StyleAt(0).Capitalization);
    }

    /// <summary>Turning one kind off must not disturb the other.</summary>
    [Fact]
    public void Turning_SmallCaps_Off_Leaves_Inherited_AllCaps_Alone()
    {
        string paragraph =
            "<w:p><w:pPr><w:pStyle w:val=\"Title\"/></w:pPr>" +
            "<w:r><w:rPr><w:smallCaps w:val=\"0\"/></w:rPr><w:t>loud</w:t></w:r></w:p>";

        DocumentReadResult result = DocxTestPackage.ReadStyled(
            paragraph,
            DocxTestPackage.Style("Title", "<w:rPr><w:caps/></w:rPr>"));

        Assert.Equal(
            TextCapitalization.AllCaps,
            Assert.Single(result.Document.Paragraphs).StyleAt(0).Capitalization);
    }

    /// <summary>Word draws all caps when a run asks for both.</summary>
    [Fact]
    public void All_Caps_Wins_When_A_Run_Declares_Both()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:rPr><w:smallCaps/><w:caps/></w:rPr><w:t>text</w:t></w:r></w:p>");

        Assert.Equal(
            TextCapitalization.AllCaps,
            Assert.Single(result.Document.Paragraphs).StyleAt(0).Capitalization);
    }

    [Theory]
    [InlineData(TextCapitalization.AllCaps, "caps")]
    [InlineData(TextCapitalization.SmallCaps, "smallCaps")]
    public void Writes_Capitalization_As_A_Run_Property(TextCapitalization capitalization, string expectedElement)
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create("Elene Bartky", InlineStyle.Default with { Capitalization = capitalization }),
        ]);

        byte[] bytes = DocxDocumentCodec.WriteToArray(document);
        string documentXml = ReadDocumentPart(bytes);

        Assert.Contains("<w:" + expectedElement, documentXml, StringComparison.Ordinal);
        Assert.Contains("Elene Bartky", documentXml, StringComparison.Ordinal);
        Assert.DoesNotContain("ELENE BARTKY", documentXml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TextCapitalization.None)]
    [InlineData(TextCapitalization.AllCaps)]
    [InlineData(TextCapitalization.SmallCaps)]
    public void Round_Trips_Capitalization_Without_Rewriting_The_Text(TextCapitalization capitalization)
    {
        RichTextDocument original = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create("Elene Bartky", InlineStyle.Default with { Capitalization = capitalization }),
        ]);

        byte[] bytes = DocxDocumentCodec.WriteToArray(original);
        using var stream = new MemoryStream(bytes, writable: false);
        DocumentReadResult result = new DocxDocumentCodec().Read(stream);

        RichTextParagraph paragraph = Assert.Single(result.Document.Paragraphs);
        Assert.Equal("Elene Bartky", paragraph.Text);
        Assert.Equal(capitalization, paragraph.StyleAt(0).Capitalization);
    }

    private static string ReadDocumentPart(byte[] bytes)
    {
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(bytes), System.IO.Compression.ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
        return reader.ReadToEnd();
    }
}
