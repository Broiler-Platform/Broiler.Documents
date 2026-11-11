namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Issue #238: the DOCX readings behind its layout differences - how the space
/// between paragraphs adds up, positional tabs, and the width of a Word frame
/// that states none.
/// </summary>
public sealed class DocxIssue238Tests
{
    private static DocumentReadResult Read(string body, Dictionary<string, string>? parts = null)
    {
        using var stream = new MemoryStream(DocxTestPackage.FromBody(body, parts ?? []), writable: false);
        return new DocxDocumentCodec().Read(stream);
    }

    private static Dictionary<string, string> Settings(string compatXml) => new(StringComparer.Ordinal)
    {
        ["word/settings.xml"] =
            "<w:settings " + DocxTestPackage.BodyNamespaceDeclarations + "><w:compat>" + compatXml + "</w:compat></w:settings>",
    };

    [Fact]
    public void Paragraph_Spacing_Collapses_Unless_The_Settings_Turn_HTML_Auto_Spacing_Off()
    {
        // writerfilter sets Writer's AddParaTableSpacing from
        // w:doNotUseHTMLParagraphAutoSpacing, so a Word document without it
        // takes the larger of the space below and the space above
        // (tdf164905.docx).
        Assert.True(Read(DocxTestPackage.Paragraph("a")).Document.StyleDefaults.CollapsesParagraphSpacing);
        Assert.True(Read(DocxTestPackage.Paragraph("a"), Settings("")).Document.StyleDefaults.CollapsesParagraphSpacing);
        Assert.False(Read(DocxTestPackage.Paragraph("a"), Settings("<w:doNotUseHTMLParagraphAutoSpacing/>"))
            .Document.StyleDefaults.CollapsesParagraphSpacing);
        Assert.True(Read(DocxTestPackage.Paragraph("a"), Settings("<w:doNotUseHTMLParagraphAutoSpacing w:val=\"0\"/>"))
            .Document.StyleDefaults.CollapsesParagraphSpacing);
    }

    [Theory]
    [InlineData("center", "left\tmiddle")]
    [InlineData("right", "left\tmiddle")]
    [InlineData("left", "left\u2028middle")]
    public void A_Positional_Tab_Is_A_Tab_And_A_Left_One_After_Text_Breaks_The_Line(string alignment, string expected)
    {
        // tdf164905.docx's header spreads three placeholders over the header
        // style's centre and right tab stops with w:ptab; writerfilter reads a
        // positional tab as a tab, and one aligned left after text as a line
        // break (DomainMapper_Impl::HandlePTab).
        DocumentReadResult result = Read(
            "<w:p><w:r><w:t>left</w:t></w:r>" +
            "<w:r><w:ptab w:relativeTo=\"margin\" w:alignment=\"" + alignment + "\" w:leader=\"none\"/></w:r>" +
            "<w:r><w:t>middle</w:t></w:r></w:p>");

        Assert.Equal(expected, Assert.Single(result.Document.Paragraphs).Text);
    }

    [Fact]
    public void A_Leading_Left_Positional_Tab_Is_A_Tab()
    {
        DocumentReadResult result = Read(
            "<w:p><w:r><w:ptab w:relativeTo=\"margin\" w:alignment=\"left\" w:leader=\"none\"/><w:t>text</w:t></w:r></w:p>");

        Assert.Equal("\ttext", Assert.Single(result.Document.Paragraphs).Text);
    }

    private const string Section =
        "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/>" +
        "<w:pgMar w:top=\"1417\" w:right=\"1417\" w:bottom=\"1417\" w:left=\"1417\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/>" +
        "<w:cols w:num=\"3\" w:space=\"708\"/></w:sectPr>";

    private static string FramedParagraph(string text, string framePr = "w:hAnchor=\"page\" w:x=\"4569\" w:y=\"340\"") =>
        "<w:p><w:pPr><w:framePr w:hSpace=\"142\" w:wrap=\"around\" w:vAnchor=\"text\" " + framePr + "/></w:pPr>" +
        (text.Length == 0 ? "" : "<w:r><w:t>" + text + "</w:t></w:r>") + "</w:p>";

    [Fact]
    public void A_Frame_Of_One_Paragraph_Without_A_Width_Narrows_To_Its_Text_Within_Its_Column()
    {
        // tdf135035.docx: three frames with no w:w in a three-column section.
        // Writer fits a lone paragraph's frame to its text and lets it be no
        // wider than the column: the long one wraps at the column's width.
        DocumentReadResult result = Read(FramedParagraph("Aaaa") + DocxTestPackage.Paragraph("body") + Section);

        DocumentShape frame = Assert.Single(result.Document.Shapes);
        Assert.True(frame.ShrinksWidthToText);
        double textWidth = (11906 - 2 * 1417) / 20d;
        Assert.Equal((textWidth - 2 * 708 / 20d) / 3, frame.Width, 3);
    }

    [Fact]
    public void A_Frame_Of_Several_Paragraphs_Without_A_Width_Takes_Its_Column()
    {
        DocumentReadResult result = Read(
            FramedParagraph("") + FramedParagraph("") + DocxTestPackage.Paragraph("body") + Section);

        DocumentShape frame = Assert.Single(result.Document.Shapes);
        Assert.False(frame.ShrinksWidthToText);
        double textWidth = (11906 - 2 * 1417) / 20d;
        Assert.Equal((textWidth - 2 * 708 / 20d) / 3, frame.Width, 3);
    }

    [Fact]
    public void A_Frame_With_A_Width_Or_Aligned_Across_Keeps_Its_Width()
    {
        DocumentReadResult sized = Read(
            FramedParagraph("text", "w:w=\"2000\" w:hAnchor=\"page\" w:x=\"4569\" w:y=\"340\"") +
            DocxTestPackage.Paragraph("body") + Section);
        DocumentShape fixedFrame = Assert.Single(sized.Document.Shapes);
        Assert.False(fixedFrame.ShrinksWidthToText);
        Assert.Equal(100, fixedFrame.Width, 3);

        DocumentReadResult centred = Read(
            FramedParagraph("text", "w:hAnchor=\"page\" w:xAlign=\"center\" w:y=\"340\"") +
            DocxTestPackage.Paragraph("body") + Section);
        Assert.False(Assert.Single(centred.Document.Shapes).ShrinksWidthToText);
    }

    [Fact]
    public void A_Section_Or_Own_Page_Break_Keeps_The_Space_Above_And_A_Run_Break_Does_Not()
    {
        // tdf170003_bottomSpacing.docx: LibreOffice keeps the 50 pt above the
        // paragraph a section opens a page with, and above one with
        // w:pageBreakBefore, and drops it after a w:br page break.
        DocumentReadResult section = Read(
            DocxTestPackage.Paragraph("one") +
            "<w:p><w:pPr><w:sectPr/></w:pPr><w:r><w:t>two</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:spacing w:before=\"1000\"/></w:pPr><w:r><w:t>three</w:t></w:r></w:p>");
        ParagraphStyle opened = section.Document.Paragraphs.Single(p => p.Text == "three").Style;
        Assert.True(opened.PageBreakBefore);
        Assert.True(opened.PageBreakKeepsSpaceAbove);

        DocumentReadResult own = Read(
            DocxTestPackage.Paragraph("one") +
            "<w:p><w:pPr><w:pageBreakBefore/></w:pPr><w:r><w:t>two</w:t></w:r></w:p>");
        Assert.True(own.Document.Paragraphs[1].Style.PageBreakKeepsSpaceAbove);

        DocumentReadResult run = Read(
            "<w:p><w:r><w:t>one</w:t></w:r><w:r><w:br w:type=\"page\"/></w:r></w:p>" +
            DocxTestPackage.Paragraph("two"));
        ParagraphStyle broken = run.Document.Paragraphs.Single(p => p.Text == "two").Style;
        Assert.True(broken.PageBreakBefore);
        Assert.False(broken.PageBreakKeepsSpaceAbove);
    }
}
