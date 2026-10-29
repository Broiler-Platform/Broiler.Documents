namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// The text columns a page layout divides its page into: <c>style:columns</c>
/// on <c>style:page-layout-properties</c>.
/// </summary>
/// <remarks>
/// LibreOffice's <c>many-paragraphs-per-page-in-6-columns.odt</c> is a landscape
/// page of six columns. Read as one wide column, its twelve pages came out as
/// forty.
/// </remarks>
public sealed class OdtColumnTests
{
    private static string Styles(string columnsXml) =>
        "<office:automatic-styles>" +
        "<style:page-layout style:name=\"pm1\">" +
        "<style:page-layout-properties fo:page-width=\"8.5in\" fo:page-height=\"11in\" " +
        "fo:margin-left=\"1in\" fo:margin-right=\"1in\" fo:margin-top=\"1in\" fo:margin-bottom=\"1in\">" +
        columnsXml +
        "</style:page-layout-properties>" +
        "</style:page-layout>" +
        "</office:automatic-styles>" +
        "<office:master-styles>" +
        "<style:master-page style:name=\"Standard\" style:page-layout-name=\"pm1\"/>" +
        "</office:master-styles>";

    private static DocumentReadResult Read(string columnsXml) =>
        OdtTestPackage.ReadStyled(OdtTestPackage.Paragraph("body"), Styles(columnsXml));

    private static PageGeometry PageOf(DocumentReadResult result) =>
        Assert.IsType<PageGeometry>(result.Document.PageGeometry);

    private static bool Reports(DocumentReadResult result, string code) =>
        result.Diagnostics.Any(diagnostic => diagnostic.Code == code);

    private static RichTextDocument RoundTrip(RichTextDocument document)
    {
        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(document), writable: false);
        return new OdtDocumentCodec().Read(stream).Document;
    }

    [Fact]
    public void Reads_The_Column_Count_And_Gap()
    {
        PageGeometry page = PageOf(Read("<style:columns fo:column-count=\"3\" fo:column-gap=\"0.25in\"/>"));

        Assert.Equal(3, page.ColumnCount);
        Assert.Equal(18, page.ColumnGap, 3);
        Assert.Equal(144, page.ColumnWidth, 3);
    }

    [Fact]
    public void Reads_The_Gap_From_Listed_Columns_When_The_Attribute_Is_Absent()
    {
        // LibreOffice lists every column, stating each gap as the end indent of
        // one column and the start indent of the next.
        PageGeometry page = PageOf(Read(
            "<style:columns fo:column-count=\"2\">" +
            "<style:column style:rel-width=\"4999*\" fo:start-indent=\"0in\" fo:end-indent=\"0.125in\"/>" +
            "<style:column style:rel-width=\"5001*\" fo:start-indent=\"0.125in\" fo:end-indent=\"0in\"/>" +
            "</style:columns>"));

        Assert.Equal(2, page.ColumnCount);
        Assert.Equal(18, page.ColumnGap, 3);
    }

    [Fact]
    public void Reads_The_Six_Column_Page_LibreOffice_Writes()
    {
        DocumentReadResult result = Read(
            "<style:columns fo:column-count=\"6\" fo:column-gap=\"0.078in\">" +
            "<style:column-sep style:width=\"0.0035in\" style:color=\"#000000\" style:height=\"100%\" style:style=\"solid\"/>" +
            "<style:column style:rel-width=\"10922*\" fo:start-indent=\"0in\" fo:end-indent=\"0.039in\"/>" +
            "<style:column style:rel-width=\"10922*\" fo:start-indent=\"0.039in\" fo:end-indent=\"0.039in\"/>" +
            "<style:column style:rel-width=\"10922*\" fo:start-indent=\"0.039in\" fo:end-indent=\"0.039in\"/>" +
            "<style:column style:rel-width=\"10922*\" fo:start-indent=\"0.039in\" fo:end-indent=\"0.039in\"/>" +
            "<style:column style:rel-width=\"10922*\" fo:start-indent=\"0.039in\" fo:end-indent=\"0.039in\"/>" +
            "<style:column style:rel-width=\"10925*\" fo:start-indent=\"0.039in\" fo:end-indent=\"0in\"/>" +
            "</style:columns>");

        Assert.Equal(6, PageOf(result).ColumnCount);
        Assert.Equal(5.616, PageOf(result).ColumnGap, 3);

        // 10922 against 10925 is how a producer shares a width out in whole
        // units, not a request for unequal columns.
        Assert.False(Reports(result, "odt.columns.unequal"));
    }

    [Fact]
    public void Columns_Of_Unequal_Width_Are_Read_As_Equal_And_Reported()
    {
        DocumentReadResult result = Read(
            "<style:columns fo:column-count=\"2\" fo:column-gap=\"0.25in\">" +
            "<style:column style:rel-width=\"3000*\"/><style:column style:rel-width=\"7000*\"/>" +
            "</style:columns>");

        Assert.Equal(2, PageOf(result).ColumnCount);
        Assert.True(Reports(result, "odt.columns.unequal"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<style:columns fo:column-count=\"1\"/>")]
    [InlineData("<style:columns fo:column-count=\"0\"/>")]
    [InlineData("<style:columns fo:column-count=\"two\"/>")]
    [InlineData("<style:columns/>")]
    public void A_Page_That_Is_Not_Divided_Has_One_Column(string columnsXml)
    {
        Assert.Equal(1, PageOf(Read(columnsXml)).ColumnCount);
    }

    [Fact]
    public void Columns_That_Leave_No_Room_Keep_The_Page_And_Drop_The_Columns()
    {
        DocumentReadResult result = Read("<style:columns fo:column-count=\"2\" fo:column-gap=\"7in\"/>");

        PageGeometry page = PageOf(result);
        Assert.Equal(612, page.Width, 3);
        Assert.Equal(1, page.ColumnCount);
        Assert.True(Reports(result, "odt.columns.geometry"));
    }

    [Fact]
    public void Columns_Survive_A_Round_Trip()
    {
        RichTextDocument source = RichTextDocument.FromPlainText("body")
            .WithPageGeometry(new PageGeometry(612, 792, 72, 72, 72, 72, ColumnCount: 3, ColumnGap: 18));

        PageGeometry page = Assert.IsType<PageGeometry>(RoundTrip(source).PageGeometry);

        Assert.Equal(3, page.ColumnCount);
        Assert.Equal(18, page.ColumnGap, 2);
    }

    [Fact]
    public void A_Single_Column_Writes_No_Columns()
    {
        RichTextDocument source = RichTextDocument.FromPlainText("body")
            .WithPageGeometry(new PageGeometry(612, 792, 72, 72, 72, 72));

        PageGeometry page = Assert.IsType<PageGeometry>(RoundTrip(source).PageGeometry);

        Assert.Equal(1, page.ColumnCount);
    }

    private static RichTextParagraph Breaking(string text) =>
        RichTextParagraph.Create(text, InlineStyle.Default, ParagraphStyle.Default with { ColumnBreakBefore = true });

    [Fact]
    public void A_Column_Break_Round_Trips()
    {
        RichTextDocument source = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("one"), Breaking("two")]);

        RichTextParagraph two = RoundTrip(source).Paragraphs[1];

        Assert.True(two.Style.ColumnBreakBefore);
        Assert.False(two.Style.PageBreakBefore);
    }

    [Fact]
    public void A_Section_With_Columns_Is_A_Section()
    {
        string sectionStyle =
            "<style:style style:name=\"Sect1\" style:family=\"section\">" +
            "<style:section-properties><style:columns fo:column-count=\"3\" fo:column-gap=\"0.25in\"/>" +
            "</style:section-properties></style:style>";
        string body =
            OdtTestPackage.Paragraph("before") +
            "<text:section text:style-name=\"Sect1\" text:name=\"Section1\">" +
            OdtTestPackage.Paragraph("in one") + OdtTestPackage.Paragraph("in two") +
            "</text:section>" +
            OdtTestPackage.Paragraph("after");

        DocumentReadResult result = OdtTestPackage.ReadBody(body, sectionStyle);

        DocumentSection section = Assert.Single(result.Document.Sections);
        Assert.Equal(1, section.ParagraphIndex);
        Assert.Equal(2, section.ParagraphCount);
        Assert.Equal(3, section.ColumnCount);
        Assert.Equal(18, section.ColumnGap, 3);
    }

    [Fact]
    public void A_Section_Without_Columns_Is_Only_Its_Paragraphs()
    {
        string body =
            "<text:section text:name=\"Plain\">" + OdtTestPackage.Paragraph("inside") + "</text:section>";

        DocumentReadResult result = OdtTestPackage.ReadBody(body);

        Assert.Empty(result.Document.Sections);
        Assert.Equal("inside", Assert.Single(result.Document.Paragraphs).Text);
    }

    [Fact]
    public void An_Outer_Section_With_Columns_Holds_The_Inner_One()
    {
        string styles =
            "<style:style style:name=\"Two\" style:family=\"section\">" +
            "<style:section-properties><style:columns fo:column-count=\"2\" fo:column-gap=\"0.25in\"/>" +
            "</style:section-properties></style:style>" +
            "<style:style style:name=\"Three\" style:family=\"section\">" +
            "<style:section-properties><style:columns fo:column-count=\"3\" fo:column-gap=\"0.25in\"/>" +
            "</style:section-properties></style:style>";
        string body =
            "<text:section text:style-name=\"Two\" text:name=\"Outer\">" +
            OdtTestPackage.Paragraph("a") +
            "<text:section text:style-name=\"Three\" text:name=\"Inner\">" + OdtTestPackage.Paragraph("b") + "</text:section>" +
            OdtTestPackage.Paragraph("c") +
            "</text:section>";

        DocumentSection section = Assert.Single(OdtTestPackage.ReadBody(body, styles).Document.Sections);

        Assert.Equal(0, section.ParagraphIndex);
        Assert.Equal(3, section.ParagraphCount);
        Assert.Equal(2, section.ColumnCount);
    }

    [Fact]
    public void Sections_Round_Trip()
    {
        RichTextDocument source = RichTextDocument.FromParagraphs(
            Enumerable.Range(0, 5).Select(i => RichTextParagraph.Plain("p" + i)).ToList())
            .WithSections([new DocumentSection(1, 2, 2, 18), new DocumentSection(3, 1, 4, 9)]);

        RichTextDocument read = RoundTrip(source);

        Assert.Equal("p0\np1\np2\np3\np4", read.PlainText);
        Assert.Collection(
            read.Sections,
            first => Assert.Equal((1, 2, 2), (first.ParagraphIndex, first.ParagraphCount, first.ColumnCount)),
            second => Assert.Equal((3, 1, 4), (second.ParagraphIndex, second.ParagraphCount, second.ColumnCount)));
        Assert.Equal(18, read.Sections[0].ColumnGap, 2);
    }

    [Fact]
    public void A_One_Column_Section_On_A_Page_Of_Columns_Comes_Back_In_One_Column()
    {
        // ODF lays a section's columns out inside the page's, where the model
        // lets them stand in for the page's. So the page is written with one
        // column, and the page's columns go on the stretch no section holds.
        RichTextDocument source = RichTextDocument.FromParagraphs(
            Enumerable.Range(0, 4).Select(i => RichTextParagraph.Plain("p" + i)).ToList())
            .WithPageGeometry(new PageGeometry(612, 792, 72, 72, 72, 72, ColumnCount: 2, ColumnGap: 18))
            .WithSections([new DocumentSection(0, 1, 1, 0), new DocumentSection(1, 2, 3, 9)]);

        RichTextDocument read = RoundTrip(source);

        Assert.Equal(1, read.PageGeometry!.ColumnCount);
        Assert.Null(DocumentSection.Containing(read.Sections, 0));
        Assert.Equal(3, DocumentSection.Containing(read.Sections, 1)!.ColumnCount);
        DocumentSection last = DocumentSection.Containing(read.Sections, 3)!;
        Assert.Equal((2, 18.0), (last.ColumnCount, Math.Round(last.ColumnGap, 2)));
    }

    [Fact]
    public void Section_Margins_Round_Trip()
    {
        PageGeometry letter = new(612, 792, 72, 72, 72, 72);
        PageGeometry indented = new(612, 792, 108, 108, 72, 72);
        RichTextDocument source = RichTextDocument.FromParagraphs(
            Enumerable.Range(0, 3).Select(i => RichTextParagraph.Plain("p" + i)).ToList())
            .WithPageGeometry(letter)
            .WithSections(
            [
                new DocumentSection(1, 1, 2, 18, page: indented),
            ]);

        using var stream = new MemoryStream();
        DocumentWriteResult writeResult = new OdtDocumentCodec().Write(source, stream);
        Assert.DoesNotContain(writeResult.Diagnostics, d => d.Code == "odt.section.page");

        stream.Position = 0;
        RichTextDocument read = new OdtDocumentCodec().Read(stream).Document;

        // ODF states a section's margins as indents within the page's text
        // area, and they come back as that: half an inch either side of the
        // document's inch margins.
        Assert.Single(read.Sections);
        Assert.Equal(36, read.Sections[0].LeftIndent, 2);
        Assert.Equal(36, read.Sections[0].RightIndent, 2);
    }

    [Fact]
    public void Section_Indents_Round_Trip()
    {
        RichTextDocument source = RichTextDocument.FromParagraphs(
            Enumerable.Range(0, 3).Select(i => RichTextParagraph.Plain("p" + i)).ToList())
            .WithSections([new DocumentSection(1, 1, 1, 0, leftIndent: 18, rightIndent: 241)]);

        RichTextDocument read = RoundTrip(source);

        DocumentSection section = Assert.Single(read.Sections);
        Assert.Equal((1, 1), (section.ParagraphIndex, section.ParagraphCount));
        Assert.Equal(18, section.LeftIndent, 2);
        Assert.Equal(241, section.RightIndent, 2);
        Assert.Null(section.Page);
    }

    [Fact]
    public void Section_With_DontBalanceColumns_Round_Trips()
    {
        RichTextDocument source = RichTextDocument.FromParagraphs(
            Enumerable.Range(0, 4).Select(i => RichTextParagraph.Plain("p" + i)).ToList())
            .WithSections([new DocumentSection(1, 2, 2, 18, dontBalanceColumns: true)]);

        RichTextDocument read = RoundTrip(source);

        Assert.Single(read.Sections);
        Assert.True(read.Sections[0].DontBalanceColumns);
        Assert.Equal(2, read.Sections[0].ColumnCount);
    }

    [Fact]
    public void One_Column_Section_With_Margins_Is_Preserved()
    {
        PageGeometry letter = new(612, 792, 72, 72, 72, 72);
        PageGeometry indented = new(612, 792, 108, 108, 72, 72);
        RichTextDocument source = RichTextDocument.FromParagraphs(
            Enumerable.Range(0, 3).Select(i => RichTextParagraph.Plain("p" + i)).ToList())
            .WithPageGeometry(letter)
            .WithSections(
            [
                new DocumentSection(1, 1, 1, 0, page: indented),
            ]);

        RichTextDocument read = RoundTrip(source);

        Assert.Single(read.Sections);
        Assert.Equal(1, read.Sections[0].ColumnCount);
        Assert.Equal(36, read.Sections[0].LeftIndent, 2);
    }

    [Fact]
    public void Section_With_Unsupported_Paper_Size_Emits_Diagnostic()
    {
        PageGeometry letter = new(612, 792, 72, 72, 72, 72);
        PageGeometry customPaper = new(800, 1000, 72, 72, 72, 72);
        RichTextDocument source = RichTextDocument.FromParagraphs(
            Enumerable.Range(0, 3).Select(i => RichTextParagraph.Plain("p" + i)).ToList())
            .WithPageGeometry(letter)
            .WithSections(
            [
                new DocumentSection(1, 1, 1, 0, page: customPaper),
            ]);

        using var stream = new MemoryStream();
        DocumentWriteResult writeResult = new OdtDocumentCodec().Write(source, stream);
        Assert.Contains(writeResult.Diagnostics, d => d.Code == "odt.section.page");
    }
}
