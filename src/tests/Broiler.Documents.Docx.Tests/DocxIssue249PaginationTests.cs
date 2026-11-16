namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Issue #249, DOCX page counts: how the reader carries a page break that ends
/// a paragraph when the document asks for <c>w:splitPgBreakAndParaMark</c>.
/// </summary>
public sealed class DocxIssue249PaginationTests
{
    private const string PageBreak = "<w:r><w:br w:type=\"page\"/></w:r>";

    private static DocumentReadResult Read(string body, string compatXml)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/settings.xml"] =
                "<w:settings " + DocxTestPackage.BodyNamespaceDeclarations + "><w:compat>" + compatXml + "</w:compat></w:settings>",
        };
        using var stream = new MemoryStream(DocxTestPackage.FromBody(body, parts), writable: false);
        return new DocxDocumentCodec().Read(stream);
    }

    [Fact]
    public void A_Break_Ending_A_Paragraph_Is_Split_From_Its_Mark()
    {
        // LibreOffice 24.2 reads "B<break>" as "B" and an empty paragraph that
        // starts the next page, which then holds the empty line above "C".
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("A") +
            "<w:p><w:pPr><w:spacing w:before=\"400\" w:after=\"400\"/></w:pPr><w:r><w:t>B</w:t></w:r>" + PageBreak + "</w:p>" +
            DocxTestPackage.Paragraph("C"),
            "<w:splitPgBreakAndParaMark/>");

        RichTextParagraph[] paragraphs = [.. result.Document.Paragraphs];
        Assert.Equal(new[] { "A", "B", string.Empty, "C" }, paragraphs.Select(p => p.Text));
        Assert.False(paragraphs[1].Style.PageBreakBefore);
        Assert.True(paragraphs[2].Style.PageBreakBefore);
        Assert.Equal(0, paragraphs[2].Style.SpacingBefore);
        Assert.Equal(20, paragraphs[2].Style.SpacingAfter, 3);
        Assert.False(paragraphs[3].Style.PageBreakBefore);
    }

    [Fact]
    public void An_Empty_Paragraph_Holding_Only_The_Break_Moves_To_The_New_Page()
    {
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("A") + "<w:p>" + PageBreak + "</w:p>",
            "<w:splitPgBreakAndParaMark/>");

        RichTextParagraph[] paragraphs = [.. result.Document.Paragraphs];
        Assert.Equal(2, paragraphs.Length);
        Assert.True(paragraphs[1].Style.PageBreakBefore);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "docx.pagebreak.trailing");
    }

    [Fact]
    public void A_Lone_Break_In_The_First_Paragraph_Leaves_It_And_Opens_A_Page()
    {
        // tdf123636_newlinePageBreak.docx: two paragraphs, two pages.
        DocumentReadResult result = Read("<w:p>" + PageBreak + "</w:p>", "<w:splitPgBreakAndParaMark/>");

        RichTextParagraph[] paragraphs = [.. result.Document.Paragraphs];
        Assert.Equal(2, paragraphs.Length);
        Assert.False(paragraphs[0].Style.PageBreakBefore);
        Assert.True(paragraphs[1].Style.PageBreakBefore);
    }

    [Theory]
    [InlineData("<w:cols w:num=\"2\" w:space=\"720\"/>", false)]
    [InlineData("<w:cols w:space=\"720\"/>", true)]
    public void A_Column_Break_Ending_A_Section_Of_Columns_Starts_Nothing(string endingColumns, bool startsPage)
    {
        // tdf103975_notPageBreakE.docx: the last paragraph of a two-column
        // section ends on a column break, and the next, continuous section of
        // one column goes on right below it in LibreOffice 24.2. Out of a
        // section of one column the break is still a page break, and with
        // w:splitPgBreakAndParaMark its paragraph's mark goes to the new page
        // as an empty paragraph: soffice 24.2.7.2 sets "B" one empty line
        // down page 2.
        const string continuous = "<w:type w:val=\"continuous\"/><w:pgSz w:w=\"11906\" w:h=\"16838\"/>";
        DocumentReadResult result = Read(
            "<w:p><w:pPr><w:sectPr>" + continuous + endingColumns + "</w:sectPr></w:pPr>" +
            "<w:r><w:t>A</w:t></w:r><w:r><w:br w:type=\"column\"/></w:r></w:p>" +
            DocxTestPackage.Paragraph("B") +
            "<w:sectPr>" + continuous + "<w:cols w:space=\"720\"/></w:sectPr>",
            "<w:splitPgBreakAndParaMark/>");

        RichTextParagraph[] paragraphs = [.. result.Document.Paragraphs];
        Assert.Equal(startsPage ? new[] { "A", "", "B" } : new[] { "A", "B" }, paragraphs.Select(p => p.Text));
        Assert.False(paragraphs[1].Style.ColumnBreakBefore);
        Assert.Equal(startsPage, paragraphs[1].Style.PageBreakBefore);
        Assert.False(paragraphs[^1].Style.PageBreakBefore && startsPage);
    }

    [Theory]
    [InlineData("continuous")]
    [InlineData("nextPage")]
    public void A_Section_Mark_With_Text_Before_A_Page_Break_Is_Split_Too(string sectionType)
    {
        // soffice 24.2.7.2 with w:splitPgBreakAndParaMark: a section ending
        // "x" and a page break, then "next", sets "next" one empty line down
        // page 2 in both kinds of section, as without the section, and on two
        // pages. A section mark holding nothing but the break is not split.
        DocumentReadResult result = Read(
            "<w:p><w:pPr><w:sectPr><w:type w:val=\"" + sectionType + "\"/></w:sectPr></w:pPr><w:r><w:t>x</w:t></w:r>" + PageBreak + "</w:p>" +
            DocxTestPackage.Paragraph("next") +
            "<w:sectPr><w:type w:val=\"continuous\"/></w:sectPr>",
            "<w:splitPgBreakAndParaMark/>");

        RichTextParagraph[] paragraphs = [.. result.Document.Paragraphs];
        Assert.Equal(new[] { "x", "", "next" }, paragraphs.Select(p => p.Text));
        Assert.True(paragraphs[1].Style.PageBreakBefore);
        Assert.False(paragraphs[2].Style.PageBreakBefore);
    }

    [Fact]
    public void A_Style_Based_On_Nothing_Takes_The_Document_Defaults_Not_The_Default_Style()
    {
        // tdf108350_noFontdefaults.docx: "Normal" states 32 point Calibri,
        // w:rPrDefault states nothing, and "NoParent" has no w:basedOn.
        // LibreOffice sets "NoParent" in 10 point Times New Roman.
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            DocxTestPackage.Paragraph("normal") +
            "<w:p><w:pPr><w:pStyle w:val=\"NoParent\"/></w:pPr><w:r><w:t>detached</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:pStyle w:val=\"Child\"/></w:pPr><w:r><w:t>child</w:t></w:r></w:p>",
            DocxTestPackage.DocumentDefaults(string.Empty) +
            DocxTestPackage.Style("Normal", "<w:rPr><w:rFonts w:ascii=\"Calibri\"/><w:sz w:val=\"64\"/></w:rPr>", isDefault: true) +
            DocxTestPackage.Style("NoParent", "<w:rPr/>") +
            DocxTestPackage.Style("Child", "<w:rPr/>", basedOn: "Normal"));

        RichTextDocument document = result.Document;
        Assert.Equal(32f, document.StyleDefaults.FontSizePoints);
        Assert.Equal("Calibri", document.StyleDefaults.FontFamily);

        InlineStyle detached = document.Paragraphs[1].Runs[0].Style;
        Assert.Equal(10f, detached.FontSize);
        Assert.Equal("Times New Roman", detached.FontFamily);

        // A style based on the default style inherits it as before.
        InlineStyle child = document.Paragraphs[2].Runs[0].Style;
        Assert.Equal(32f, child.FontSize);
        Assert.Equal("Calibri", child.FontFamily);
    }

    [Fact]
    public void A_Docx_Lets_Proportional_Line_Spacing_Run_Past_The_Bottom_Of_A_Page()
    {
        // LibreOffice 24.2 needs room only for the text of the last line on a
        // DOCX page (calendar3.docx); see DocumentLayoutIssue249PaginationTests.
        Assert.True(DocxTestPackage.ReadBody(DocxTestPackage.Paragraph("a")).Document.StyleDefaults.ProportionalSpacingRunsPastBottom);
    }

    [Fact]
    public void Without_The_Setting_A_Break_Ending_The_Document_Is_Still_Dropped()
    {
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("A") + "<w:p><w:r><w:t>B</w:t></w:r>" + PageBreak + "</w:p>",
            string.Empty);

        Assert.Equal(2, result.Document.ParagraphCount);
        Assert.All(result.Document.Paragraphs, paragraph => Assert.False(paragraph.Style.PageBreakBefore));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.pagebreak.trailing");
    }

    [Fact]
    public void A_Compatibility_Mode_In_A_Second_Compat_Element_Counts()
    {
        // tdf142404_tabSpacing.docx: LibreOffice's own flags in one w:compat,
        // Word's compatibilityMode 15 in another; tabs then do not go over the margin.
        const string settings =
            "<w:compat><w:doNotExpandShiftReturn/></w:compat>" +
            "<w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/></w:compat>";
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/settings.xml"] = "<w:settings " + DocxTestPackage.BodyNamespaceDeclarations + ">" + settings + "</w:settings>",
        };
        using var stream = new MemoryStream(DocxTestPackage.FromBody(DocxTestPackage.Paragraph("a"), parts), writable: false);
        DocumentStyleDefaults defaults = new DocxDocumentCodec().Read(stream).Document.StyleDefaults;

        Assert.False(defaults.TabsOverMargin);
        Assert.True(defaults.ShrinksJustifiedLines);
        Assert.False(defaults.IgnoresBlanksInLineHeight);
    }

    [Theory]
    [InlineData("14", true)]
    [InlineData("15", false)]
    public void Spaces_Leave_The_Line_Height_Alone_Only_Up_To_Word_2010(string mode, bool ignores)
    {
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("a"),
            "<w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"" + mode + "\"/>");

        Assert.Equal(ignores, result.Document.StyleDefaults.IgnoresBlanksInLineHeight);
    }

    [Fact]
    public void The_Last_Paragraph_Style_Marked_Default_Is_The_Default()
    {
        // defaultStyle.docx marks "Normal" and then "Title" default; LibreOffice
        // gives its unstyled paragraphs "Title".
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            DocxTestPackage.Paragraph("text"),
            DocxTestPackage.Style("Normal", "<w:rPr><w:sz w:val=\"24\"/></w:rPr>", isDefault: true) +
            DocxTestPackage.Style("Title", "<w:rPr><w:sz w:val=\"56\"/></w:rPr>", isDefault: true));

        Assert.Equal(28f, result.Document.Paragraphs[0].StyleAt(0).FontSize ?? result.Document.StyleDefaults.FontSizePoints);
    }
}
