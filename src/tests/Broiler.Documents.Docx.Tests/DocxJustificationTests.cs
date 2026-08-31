using System.IO.Compression;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Covers justification, which DOCX spells <c>w:jc w:val="both"</c>. The reader
/// used to map every value it did not know to the paragraph's inherited
/// alignment, so a justified paragraph opened ragged-right and nothing said the
/// setting had been dropped.
/// </summary>
public sealed class DocxJustificationTests
{
    [Fact(Timeout = 600000)]
    public void Reads_Jc_Both_As_Justified()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:pPr><w:jc w:val=\"both\"/></w:pPr><w:r><w:t>flush</w:t></w:r></w:p>");

        Assert.Equal(
            TextAlignment.Justify,
            Assert.Single(result.Document.Paragraphs).Style.Alignment);
    }

    [Fact(Timeout = 600000)]
    public void Reads_Justification_Declared_By_A_Paragraph_Style()
    {
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            DocxTestPackage.StyledParagraph("BodyText", "flush"),
            DocxTestPackage.Style("BodyText", "<w:pPr><w:jc w:val=\"both\"/></w:pPr>"));

        Assert.Equal(
            TextAlignment.Justify,
            Assert.Single(result.Document.Paragraphs).Style.Alignment);
    }

    [Fact(Timeout = 600000)]
    public void Writes_Justification_Back_As_Jc_Both()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create(
                "flush",
                InlineStyle.Default,
                ParagraphStyle.Default with { Alignment = TextAlignment.Justify })]);

        byte[] bytes = DocxDocumentCodec.WriteToArray(document);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var entry = new StreamReader(archive.GetEntry("word/document.xml")!.Open());

        Assert.Contains("w:val=\"both\"", entry.ReadToEnd());
    }

    [Fact(Timeout = 600000)]
    public void A_Justified_Paragraph_Round_Trips_Through_Docx()
    {
        RichTextDocument expected = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create(
                "flush",
                InlineStyle.Default,
                ParagraphStyle.Default with { Alignment = TextAlignment.Justify })]);

        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(expected), writable: false);
        RichTextDocument actual = new DocxDocumentCodec().Read(stream).Document;

        Assert.Equal(
            TextAlignment.Justify,
            Assert.Single(actual.Paragraphs).Style.Alignment);
    }
}
