using System.IO.Compression;
using System.Xml.Linq;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Covers the paragraph mark's formatting, <c>w:pPr/w:rPr</c>. An empty
/// paragraph has no run, and the mark is the only thing in the file saying how
/// tall its line is: LibreOffice's <c>tdf168017.docx</c> opens with 36 blank
/// lines whose marks are 10-point Arial Narrow under a 12-point Times New Roman
/// default, and read without the mark they pushed the page's text onto the next.
/// </summary>
public sealed class DocxParagraphMarkTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private const string NarrowMark =
        "<w:rPr><w:rFonts w:ascii=\"Arial Narrow\" w:hAnsi=\"Arial Narrow\"/><w:sz w:val=\"20\"/></w:rPr>";

    [Fact]
    public void Reads_The_Mark_Of_An_Empty_Paragraph()
    {
        RichTextDocument document = DocxTestPackage.ReadBody(
            "<w:p><w:pPr>" + NarrowMark + "</w:pPr></w:p>").Document;

        RichTextParagraph paragraph = Assert.Single(document.Paragraphs);
        Assert.Equal(0, paragraph.Length);
        Assert.Equal("Arial Narrow", paragraph.MarkStyle.FontFamily);
        Assert.Equal(10f, paragraph.MarkStyle.FontSize);
    }

    [Fact]
    public void A_Mark_Applies_Over_The_Paragraph_Style_And_Not_To_The_Runs()
    {
        // ECMA-376 puts w:pPr/w:rPr on the mark and nowhere else: the run beside
        // it keeps what the style gave it.
        RichTextDocument document = DocxTestPackage.ReadStyled(
            "<w:p><w:pPr><w:pStyle w:val=\"Body\"/>" + NarrowMark + "</w:pPr>" +
            "<w:r><w:t>text</w:t></w:r></w:p>",
            DocxTestPackage.Style("Body", "<w:rPr><w:rFonts w:ascii=\"Georgia\"/><w:b/><w:sz w:val=\"24\"/></w:rPr>")).Document;

        RichTextParagraph paragraph = Assert.Single(document.Paragraphs);
        Assert.Equal(new InlineStyle { FontFamily = "Arial Narrow", FontSize = 10f, Bold = true }, paragraph.MarkStyle);
        Assert.Equal("Georgia", paragraph.Runs[0].Style.FontFamily);
        Assert.Equal(12f, paragraph.Runs[0].Style.FontSize);
    }

    [Fact]
    public void A_Paragraph_That_States_No_Mark_Has_The_Formatting_A_Run_Would_Inherit()
    {
        RichTextDocument document = DocxTestPackage.ReadStyled(
            "<w:p><w:pPr><w:pStyle w:val=\"Body\"/></w:pPr></w:p>",
            DocxTestPackage.Style("Body", "<w:rPr><w:sz w:val=\"16\"/></w:rPr>")).Document;

        Assert.Equal(8f, Assert.Single(document.Paragraphs).MarkStyle.FontSize);
    }

    [Fact]
    public void Writes_The_Mark_And_Reads_It_Back()
    {
        var mark = new InlineStyle { FontFamily = "Arial Narrow", FontSize = 10f };
        RichTextDocument source = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Plain("text"), RichTextParagraph.Empty.WithMarkStyle(mark)]);

        byte[] package = DocxDocumentCodec.WriteToArray(source);
        using var stream = new MemoryStream(package, writable: false);
        RichTextDocument read = new DocxDocumentCodec().Read(stream).Document;

        Assert.Equal(mark, read.Paragraphs[1].MarkStyle);
        Assert.Null(Paragraphs(package)[0].Element(W + "pPr"));
    }

    private static List<XElement> Paragraphs(byte[] package)
    {
        using var archive = new ZipArchive(new MemoryStream(package, writable: false), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
        return [.. XDocument.Parse(reader.ReadToEnd()).Descendants(W + "p")];
    }
}
