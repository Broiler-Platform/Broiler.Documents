namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Issue #240, DOCX layout cluster: how writerfilter reads a run's page or
/// column break that opens a paragraph (tdf#153964), and body paragraph borders.
/// </summary>
public sealed class DocxIssue240DocxLayoutTests
{
    private static DocumentReadResult Read(string body, int? compatibilityMode = null)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (compatibilityMode is int mode)
        {
            parts["word/settings.xml"] =
                "<w:settings " + DocxTestPackage.BodyNamespaceDeclarations + "><w:compat>" +
                "<w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"" + mode + "\"/>" +
                "</w:compat></w:settings>";
        }

        using var stream = new MemoryStream(DocxTestPackage.FromBody(body, parts), writable: false);
        return new DocxDocumentCodec().Read(stream);
    }

    private const string TwoColumns =
        "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/>" +
        "<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/>" +
        "<w:cols w:num=\"2\" w:space=\"708\"/></w:sectPr>";

    private const string Spaced = "<w:pPr><w:spacing w:before=\"1200\"/><w:ind w:firstLine=\"1134\"/></w:pPr>";

    private static string Paragraph(string runs, string properties = Spaced) => "<w:p>" + properties + runs + "</w:p>";

    private static string Text(string text) => "<w:r><w:t>" + text + "</w:t></w:r>";

    private static string Break(string type) => "<w:r><w:br w:type=\"" + type + "\"/></w:r>";

    [Theory]
    [InlineData(14, true)]
    [InlineData(15, false)]
    public void A_Page_Break_Opening_A_Paragraph_Keeps_Its_Space_Above_Below_Mode_15(int mode, bool keeps)
    {
        // tdf153964_firstIndentAfterBreak14.docx: "How is first line indent
        // affected by a page break?" starts page three 60 points down.
        DocumentReadResult result = Read(
            Paragraph(Text("one")) + Paragraph(Break("page") + Text("two")), mode);

        ParagraphStyle style = result.Document.Paragraphs[1].Style;
        Assert.Equal("two", result.Document.Paragraphs[1].Text);
        Assert.True(style.PageBreakBefore);
        Assert.Equal(keeps, style.PageBreakKeepsSpaceAbove);
        Assert.Equal(60, style.SpacingBefore, 3);
    }

    [Theory]
    [InlineData(14, 60)]
    [InlineData(15, 0)]
    public void A_Column_Break_Opening_A_Paragraph_Keeps_Its_Space_Above_Below_Mode_15(int mode, double above)
    {
        // tdf153964_topMarginAfterBreak14.docx's "column break2" keeps its 60
        // points; the mode 15 twin's has none.
        DocumentReadResult result = Read(
            Paragraph(Text("one")) + Paragraph(Break("column") + Text("two")) + TwoColumns, mode);

        ParagraphStyle style = result.Document.Paragraphs[1].Style;
        Assert.True(style.ColumnBreakBefore);
        Assert.Equal(above, style.SpacingBefore, 3);
    }

    [Fact]
    public void A_Column_Break_Opening_The_Document_Leaves_An_Empty_Paragraph_Before_It()
    {
        // tdf153964_firstIndentAfterBreak14.docx: page one holds the empty
        // first paragraph; its text keeps the first-line indent and the space
        // above.
        DocumentReadResult result = Read(Paragraph(Break("column") + Text("text")) + TwoColumns, 11);

        Assert.Equal(2, result.Document.Paragraphs.Count);
        Assert.Equal("", result.Document.Paragraphs[0].Text);
        Assert.False(result.Document.Paragraphs[0].Style.ColumnBreakBefore);
        ParagraphStyle style = result.Document.Paragraphs[1].Style;
        Assert.True(style.ColumnBreakBefore);
        Assert.Equal(60, style.SpacingBefore, 3);
        Assert.Equal(56.7, style.FirstLineIndentIn, 1);
    }

    [Fact]
    public void A_Paragraph_Of_Only_A_Column_Break_Takes_It_Itself()
    {
        // tdf153964_topMarginAfterBreak14.docx: the lone column break puts its
        // own empty paragraph in the next column.
        DocumentReadResult result = Read(
            Paragraph(Text("one")) + Paragraph(Break("column")) + Paragraph(Text("two")) + TwoColumns, 14);

        Assert.Equal(3, result.Document.Paragraphs.Count);
        Assert.True(result.Document.Paragraphs[1].Style.ColumnBreakBefore);
        Assert.Equal(60, result.Document.Paragraphs[1].Style.SpacingBefore, 3);
        Assert.False(result.Document.Paragraphs[2].Style.ColumnBreakBefore);
    }

    [Fact]
    public void A_Paragraph_Of_Only_A_Page_Break_Passes_It_To_The_Next()
    {
        // "the w:br was actually at the end of the previous w:p": the paragraph
        // after it starts the page, without its space above.
        DocumentReadResult result = Read(
            Paragraph(Text("one")) + Paragraph(Break("page")) + Paragraph(Text("two")), 14);

        Assert.False(result.Document.Paragraphs[1].Style.PageBreakBefore);
        Assert.True(result.Document.Paragraphs[2].Style.PageBreakBefore);
        Assert.False(result.Document.Paragraphs[2].Style.PageBreakKeepsSpaceAbove);
    }

    [Fact]
    public void A_Page_Break_After_A_Lone_Column_Break_Passes_On()
    {
        // The last paragraph of tdf153964_topMarginAfterBreak14.docx holds a
        // column break and a page break; LibreOffice gives it neither.
        DocumentReadResult result = Read(
            Paragraph(Text("one")) + Paragraph(Break("column") + Break("page")) + Paragraph(Text("two")) + TwoColumns, 14);

        Assert.False(result.Document.Paragraphs[1].Style.ColumnBreakBefore);
        Assert.False(result.Document.Paragraphs[1].Style.PageBreakBefore);
        Assert.True(result.Document.Paragraphs[2].Style.PageBreakBefore);
    }

    [Fact]
    public void Body_Paragraph_Borders_Are_Read_And_Laid_Out()
    {
        DocumentReadResult result = Read(Paragraph(Text("boxed"),
            "<w:pPr><w:pBdr><w:top w:val=\"single\" w:sz=\"24\" w:space=\"1\" w:color=\"auto\"/>" +
            "<w:bottom w:val=\"single\" w:sz=\"24\" w:space=\"1\" w:color=\"auto\"/></w:pBdr></w:pPr>"));

        ParagraphBorder border = Assert.IsType<ParagraphBorder>(result.Document.Paragraphs[0].Style.Border);
        Assert.Equal(3, border.Edges.Top.Width, 3);
        Assert.Equal(1, border.Spacing.Top, 3);
        Assert.True(result.Document.StyleDefaults.LaysOutBodyParagraphBorders);
    }
}
