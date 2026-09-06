using System.IO.Compression;
using System.Xml.Linq;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Covers the page break, which WordprocessingML spells two ways for one thing.
/// <c>w:pageBreakBefore</c> is a paragraph property and says what the model
/// says; <c>w:br w:type="page"</c> is a run child and is what Word writes when
/// a user presses Ctrl+Enter, so a reader that knows only the property reads a
/// two-page letter as one page. Both are read here and the property is what is
/// written back.
/// </summary>
public sealed class DocxPageBreakTests
{
    private static readonly XNamespace W =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>The line separator a <c>w:br</c> that is not a page break becomes.</summary>
    private const string LineBreak = "\u2028";

    [Fact(Timeout = 600000)]
    public void Reads_The_Paragraph_Property()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:pPr><w:pageBreakBefore/></w:pPr><w:r><w:t>a new page</w:t></w:r></w:p>");

        Assert.True(Assert.Single(result.Document.Paragraphs).Style.PageBreakBefore);
    }

    [Fact(Timeout = 600000)]
    public void Reads_A_Page_Break_A_Paragraph_Style_States()
    {
        // Where a template actually keeps it: the heading style starts the
        // chapter's page, and no chapter heading in the body says anything.
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            DocxTestPackage.StyledParagraph("ChapterTitle", "Chapter Two"),
            DocxTestPackage.Style("ChapterTitle", "<w:pPr><w:pageBreakBefore/></w:pPr>"));

        Assert.True(Assert.Single(result.Document.Paragraphs).Style.PageBreakBefore);
    }

    // The on/off rule, which is the whole reason this is not a presence test:
    // the one heading that must not start a page is spelled by turning the
    // style's break off in the paragraph's own w:pPr.

    [Theory(Timeout = 600000)]
    [InlineData("<w:pageBreakBefore w:val=\"false\"/>", false)]
    [InlineData("<w:pageBreakBefore w:val=\"0\"/>", false)]
    [InlineData("<w:pageBreakBefore w:val=\"off\"/>", false)]
    [InlineData("<w:pageBreakBefore w:val=\"true\"/>", true)]
    [InlineData("<w:pageBreakBefore w:val=\"1\"/>", true)]
    [InlineData("<w:pageBreakBefore/>", true)]
    public void An_Explicit_Value_Overrides_The_Style_Chain(string pageBreakXml, bool expected)
    {
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            "<w:p><w:pPr><w:pStyle w:val=\"ChapterTitle\"/>" + pageBreakXml + "</w:pPr>" +
            "<w:r><w:t>Chapter Two</w:t></w:r></w:p>",
            DocxTestPackage.Style("ChapterTitle", "<w:pPr><w:pageBreakBefore/></w:pPr>"));

        Assert.Equal(expected, Assert.Single(result.Document.Paragraphs).Style.PageBreakBefore);
    }

    [Fact(Timeout = 600000)]
    public void A_Document_That_States_No_Break_Starts_No_Page()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(
            DocxTestPackage.Paragraph("first") + DocxTestPackage.Paragraph("second"));

        Assert.All(result.Document.Paragraphs, paragraph => Assert.False(paragraph.Style.PageBreakBefore));
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Code.StartsWith("docx.pagebreak", StringComparison.Ordinal));
    }

    [Fact(Timeout = 600000)]
    public void A_Break_Run_Starts_The_Following_Paragraph_And_Not_Its_Own()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:t>page one</w:t></w:r><w:r><w:br w:type=\"page\"/></w:r></w:p>" +
            DocxTestPackage.Paragraph("page two"));

        Assert.Equal(2, result.Document.ParagraphCount);
        Assert.False(result.Document.Paragraphs[0].Style.PageBreakBefore);
        Assert.True(result.Document.Paragraphs[1].Style.PageBreakBefore);

        // And it leaves nothing behind in the text it was written into.
        Assert.Equal("page one", result.Document.Paragraphs[0].Text);
    }

    [Fact(Timeout = 600000)]
    public void A_Break_Run_In_A_Paragraph_Of_Its_Own_Still_Starts_The_Next_One()
    {
        // How Word writes Ctrl+Enter on an empty line: the break is the whole
        // of a paragraph, and the paragraph after it opens the page.
        DocumentReadResult result = DocxTestPackage.ReadBody(
            DocxTestPackage.Paragraph("page one") +
            "<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>" +
            DocxTestPackage.Paragraph("page two"));

        Assert.Equal(3, result.Document.ParagraphCount);
        Assert.False(result.Document.Paragraphs[1].Style.PageBreakBefore);
        Assert.True(result.Document.Paragraphs[2].Style.PageBreakBefore);
    }

    [Theory(Timeout = 600000)]
    [InlineData("<w:br/>")]
    [InlineData("<w:br w:type=\"textWrapping\"/>")]
    [InlineData("<w:br w:type=\"column\"/>")]
    public void A_Break_That_Names_No_Page_Is_Still_A_Line_Break(string breakXml)
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:t>one</w:t>" + breakXml + "<w:t>two</w:t></w:r></w:p>" +
            DocxTestPackage.Paragraph("next"));

        Assert.Equal("one" + LineBreak + "two", result.Document.Paragraphs[0].Text);
        Assert.False(result.Document.Paragraphs[1].Style.PageBreakBefore);
    }

    [Fact(Timeout = 600000)]
    public void A_Break_In_The_Last_Paragraph_Is_Dropped_And_Reported()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(
            DocxTestPackage.Paragraph("first") +
            "<w:p><w:r><w:t>last</w:t></w:r><w:r><w:br w:type=\"page\"/></w:r></w:p>");

        Assert.All(result.Document.Paragraphs, paragraph => Assert.False(paragraph.Style.PageBreakBefore));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.pagebreak.trailing");
    }

    [Fact(Timeout = 600000)]
    public void A_Break_With_Text_After_It_Becomes_A_Line_Break_And_Says_So()
    {
        // Word splits the paragraph across the boundary. One flag on one
        // paragraph cannot say that, and carrying the break to the paragraph
        // after would leave "after" on the page it does not belong to.
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:t>before</w:t><w:br w:type=\"page\"/><w:t>after</w:t></w:r></w:p>" +
            DocxTestPackage.Paragraph("next"));

        Assert.Equal("before" + LineBreak + "after", result.Document.Paragraphs[0].Text);
        Assert.False(result.Document.Paragraphs[1].Style.PageBreakBefore);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.pagebreak.split");
    }

    [Fact(Timeout = 600000)]
    public void A_Break_With_Text_After_It_Does_Not_Consume_The_Next_Break()
    {
        // The demoted break must not leave the paragraph latched: the second
        // break here is trailing and is the one the next paragraph starts on.
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:t>before</w:t><w:br w:type=\"page\"/><w:t>after</w:t>" +
            "<w:br w:type=\"page\"/></w:r></w:p>" +
            DocxTestPackage.Paragraph("next"));

        Assert.Equal("before" + LineBreak + "after", result.Document.Paragraphs[0].Text);
        Assert.True(result.Document.Paragraphs[1].Style.PageBreakBefore);
    }

    [Fact(Timeout = 600000)]
    public void Two_Breaks_With_Nothing_Between_Them_Report_The_Blank_Page_They_Lose()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:t>one</w:t><w:br w:type=\"page\"/><w:br w:type=\"page\"/></w:r></w:p>" +
            DocxTestPackage.Paragraph("two"));

        Assert.True(result.Document.Paragraphs[1].Style.PageBreakBefore);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.pagebreak.repeated");
    }

    [Fact(Timeout = 600000)]
    public void Both_Spellings_Of_One_Boundary_Are_One_Break()
    {
        // A file that states the break in the run before and in the property
        // after means one page boundary, not two.
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:t>page one</w:t></w:r><w:r><w:br w:type=\"page\"/></w:r></w:p>" +
            "<w:p><w:pPr><w:pageBreakBefore/></w:pPr><w:r><w:t>page two</w:t></w:r></w:p>");

        Assert.Equal(2, result.Document.ParagraphCount);
        Assert.True(result.Document.Paragraphs[1].Style.PageBreakBefore);
    }

    [Fact(Timeout = 600000)]
    public void A_Break_Run_Does_Not_Escape_The_Table_Cell_It_Is_In()
    {
        // Every cell's paragraphs are in the one flat list, so the paragraph
        // after a cell's last one is the next cell's or the body's past the
        // table. Word splits the row instead, and a row is placed whole here,
        // so the break is dropped and reported rather than starting a page a
        // whole table later than the document asked for.
        DocumentReadResult result = DocxTestPackage.ReadBody(
            DocxTestPackage.Table(
                [["<w:p><w:r><w:t>cell</w:t></w:r><w:r><w:br w:type=\"page\"/></w:r></w:p>"]]) +
            DocxTestPackage.Paragraph("after the table"));

        Assert.All(result.Document.Paragraphs, paragraph => Assert.False(paragraph.Style.PageBreakBefore));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.pagebreak.table");
    }

    [Fact(Timeout = 600000)]
    public void Writes_The_Property_Ahead_Of_Every_Other_Paragraph_Property()
    {
        // CT_PPr is a sequence and Word refuses a file whose pPr children are
        // out of order, so this asserts the position and not just the presence.
        // The other four properties are written so there is an order to get
        // wrong.
        byte[] package = DocxDocumentCodec.WriteToArray(RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create(
                "Chapter Two",
                InlineStyle.Default,
                new ParagraphStyle
                {
                    PageBreakBefore = true,
                    ListKind = ListKind.Numbered,
                    IndentLevel = 1,
                    LineSpacing = 1.5f,
                    SpacingBefore = 6f,
                    Alignment = TextAlignment.Center,
                }),
        ]));

        XElement properties = ParagraphProperties(package, 0);

        Assert.Equal(W + "pageBreakBefore", properties.Elements().First().Name);

        // Bare, with no w:val: OOXML reads an on/off property with no value as
        // on, which is how this codec writes w:b and w:strike too.
        Assert.Empty(properties.Element(W + "pageBreakBefore")!.Attributes());
    }

    [Fact(Timeout = 600000)]
    public void Writes_Nothing_For_A_Paragraph_That_Starts_No_Page()
    {
        string documentXml = DocumentXml(
            DocxDocumentCodec.WriteToArray(RichTextDocument.FromPlainText("ordinary")));

        Assert.DoesNotContain("pageBreakBefore", documentXml, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void Round_Trips_Through_The_Package()
    {
        RichTextDocument expected = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create("page one", InlineStyle.Default, ParagraphStyle.Default),
            RichTextParagraph.Create(
                "page two",
                InlineStyle.Default,
                ParagraphStyle.Default with { PageBreakBefore = true }),
        ]);

        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(expected), writable: false);
        DocumentAssert.Equivalent(expected, new DocxDocumentCodec().Read(stream).Document);
    }

    [Fact(Timeout = 600000)]
    public void A_Break_Read_From_A_Run_Is_Written_Back_As_The_Property()
    {
        // The two spellings converge on write. What a Ctrl+Enter document
        // survives as is a w:pageBreakBefore on the paragraph that starts the
        // page - the same break, stated where the model holds it.
        DocumentReadResult read = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:t>page one</w:t></w:r><w:r><w:br w:type=\"page\"/></w:r></w:p>" +
            DocxTestPackage.Paragraph("page two"));

        byte[] package = DocxDocumentCodec.WriteToArray(read.Document);

        Assert.Equal(W + "pageBreakBefore", ParagraphProperties(package, 1).Elements().First().Name);
        Assert.DoesNotContain("w:type=\"page\"", DocumentXml(package), StringComparison.Ordinal);

        using var stream = new MemoryStream(package, writable: false);
        RichTextDocument reread = new DocxDocumentCodec().Read(stream).Document;

        Assert.False(reread.Paragraphs[0].Style.PageBreakBefore);
        Assert.True(reread.Paragraphs[1].Style.PageBreakBefore);
    }

    /// <summary>The <c>w:pPr</c> of one paragraph of a written package.</summary>
    private static XElement ParagraphProperties(byte[] package, int paragraphIndex)
    {
        XElement paragraph = XDocument.Parse(DocumentXml(package))
            .Descendants(W + "p")
            .ElementAt(paragraphIndex);

        return paragraph.Element(W + "pPr") ??
            throw new InvalidOperationException("The paragraph carries no w:pPr.");
    }

    private static string DocumentXml(byte[] package)
    {
        using var archive = new ZipArchive(new MemoryStream(package, writable: false), ZipArchiveMode.Read);
        using Stream stream = archive.GetEntry("word/document.xml")!.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
