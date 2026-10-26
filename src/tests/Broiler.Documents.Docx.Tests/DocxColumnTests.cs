using System.IO.Compression;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Text columns: the <c>w:cols</c> a section divides its page into, and the
/// column break, which is a page break wherever there is no next column.
/// </summary>
/// <remarks>
/// Both came from LibreOffice's <c>sw/qa/extras</c> tree. <c>fdo43573-2-min.docx</c>
/// is a two-column landscape document that drew as twenty-seven pages of one
/// wide column where LibreOffice draws fifteen; <c>FDO77715.docx</c> opens each
/// of its blocks with a column break in a one-column section, and every block ran
/// on from the last.
/// </remarks>
public sealed class DocxColumnTests
{
    private const string LineBreak = "\u2028";

    private const string Page =
        "<w:pgSz w:w=\"12240\" w:h=\"15840\"/>" +
        "<w:pgMar w:left=\"1440\" w:right=\"1440\" w:top=\"1440\" w:bottom=\"1440\"/>";

    private static string Section(string cols = "") => "<w:sectPr>" + Page + cols + "</w:sectPr>";

    /// <summary>A paragraph that ends a section, carrying that section's properties.</summary>
    private static string SectionEnd(string text, string cols) =>
        "<w:p><w:pPr>" + Section(cols) + "</w:pPr><w:r><w:t>" + text + "</w:t></w:r></w:p>";

    private static string ColumnBreakFirst(string text) =>
        "<w:p><w:r><w:br w:type=\"column\"/></w:r><w:r><w:t>" + text + "</w:t></w:r></w:p>";

    private static DocumentReadResult Read(string body) => DocxTestPackage.ReadBody(body);

    private static PageGeometry GeometryOf(DocumentReadResult result) =>
        Assert.IsType<PageGeometry>(result.Document.PageGeometry);

    private static bool Reports(DocumentReadResult result, string code) =>
        result.Diagnostics.Any(diagnostic => diagnostic.Code == code);

    [Fact]
    public void Reads_Equal_Columns_And_The_Gap_Between_Them()
    {
        PageGeometry geometry = GeometryOf(Read(
            DocxTestPackage.Paragraph("body") + Section("<w:cols w:num=\"3\" w:space=\"360\"/>")));

        Assert.Equal(3, geometry.ColumnCount);
        Assert.Equal(18, geometry.ColumnGap, 3);

        // Letter with one-inch margins leaves 468 points, and two gaps of 18.
        Assert.Equal(144, geometry.ColumnWidth, 3);
    }

    [Fact]
    public void An_Absent_Gap_Is_Half_An_Inch()
    {
        PageGeometry geometry = GeometryOf(Read(
            DocxTestPackage.Paragraph("body") + Section("<w:cols w:num=\"2\"/>")));

        Assert.Equal(2, geometry.ColumnCount);
        Assert.Equal(36, geometry.ColumnGap, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<w:cols/>")]
    [InlineData("<w:cols w:space=\"708\"/>")]
    [InlineData("<w:cols w:num=\"1\" w:space=\"708\"/>")]
    [InlineData("<w:cols w:num=\"0\"/>")]
    [InlineData("<w:cols w:num=\"-3\"/>")]
    public void A_Section_That_Is_Not_Divided_Has_One_Column(string cols)
    {
        PageGeometry geometry = GeometryOf(Read(DocxTestPackage.Paragraph("body") + Section(cols)));

        Assert.Equal(1, geometry.ColumnCount);
        Assert.False(geometry.HasColumns);
    }

    [Fact]
    public void Listed_Columns_Are_Read_As_Their_Count_And_Mean_Gap()
    {
        // How fdo43573-2-min.docx states its two columns: listed, one twip apart.
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("body") +
            Section(
                "<w:cols w:num=\"2\" w:space=\"708\" w:equalWidth=\"0\">" +
                "<w:col w:w=\"4000\" w:space=\"720\"/><w:col w:w=\"4001\" w:space=\"0\"/>" +
                "</w:cols>"));

        PageGeometry geometry = GeometryOf(result);
        Assert.Equal(2, geometry.ColumnCount);
        Assert.Equal(36, geometry.ColumnGap, 3);
        Assert.False(Reports(result, "docx.columns.unequal"));
    }

    [Fact]
    public void Columns_Of_Unequal_Width_Are_Read_As_Equal_And_Reported()
    {
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("body") +
            Section(
                "<w:cols w:equalWidth=\"0\">" +
                "<w:col w:w=\"2000\" w:space=\"720\"/><w:col w:w=\"6000\"/>" +
                "</w:cols>"));

        Assert.Equal(2, GeometryOf(result).ColumnCount);
        Assert.True(Reports(result, "docx.columns.unequal"));
    }

    [Fact]
    public void Columns_That_Leave_No_Room_Keep_The_Page_And_Drop_The_Columns()
    {
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("body") + Section("<w:cols w:num=\"2\" w:space=\"20000\"/>"));

        PageGeometry geometry = GeometryOf(result);
        Assert.Equal(612, geometry.Width, 3);
        Assert.Equal(1, geometry.ColumnCount);
        Assert.True(Reports(result, "docx.columns.geometry"));
    }

    [Fact]
    public void The_Columns_Are_The_Ones_Most_Of_The_Text_Is_Written_In()
    {
        // A two-column body closed by a continuous break before one empty
        // paragraph in a section of one column: the last section's columns are
        // the columns of nothing.
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("one") +
            DocxTestPackage.Paragraph("two") +
            SectionEnd("three", "<w:cols w:num=\"2\" w:space=\"720\"/>") +
            "<w:p/>" +
            Section("<w:cols w:space=\"0\"/>"));

        Assert.Equal(2, GeometryOf(result).ColumnCount);
        Assert.True(Reports(result, "docx.section.columns"));
    }

    [Fact]
    public void A_Tie_Goes_To_The_Later_Section()
    {
        DocumentReadResult result = Read(
            SectionEnd("first", "<w:cols w:num=\"3\"/>") +
            DocxTestPackage.Paragraph("second") +
            Section("<w:cols w:num=\"2\"/>"));

        Assert.Equal(2, GeometryOf(result).ColumnCount);
    }

    [Fact]
    public void Paragraphs_In_A_Table_Count_Toward_Their_Section()
    {
        DocumentReadResult result = Read(
            DocxTestPackage.Table(
                [[DocxTestPackage.Paragraph("a"), DocxTestPackage.Paragraph("b")],
                 [DocxTestPackage.Paragraph("c"), DocxTestPackage.Paragraph("d")]]) +
            SectionEnd("end", "<w:cols w:num=\"2\"/>") +
            DocxTestPackage.Paragraph("x") +
            DocxTestPackage.Paragraph("y") +
            Section());

        Assert.Equal(2, GeometryOf(result).ColumnCount);
    }

    [Fact]
    public void A_Column_Break_In_A_Single_Column_Starts_The_Paragraph_It_Opens_On_A_New_Page()
    {
        // ECMA-376's ST_BrType: where the section is not divided into columns, a
        // column break restarts the text on the next page.
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("index") + ColumnBreakFirst("block") + Section());

        Assert.Equal("block", result.Document.Paragraphs[1].Text);
        Assert.True(result.Document.Paragraphs[1].Style.PageBreakBefore);
        Assert.False(Reports(result, "docx.pagebreak.split"));
    }

    [Fact]
    public void A_Column_Break_At_The_End_Of_A_Paragraph_Starts_The_Next_Page()
    {
        DocumentReadResult result = Read(
            "<w:p><w:r><w:t>one</w:t><w:br w:type=\"column\"/></w:r></w:p>" +
            DocxTestPackage.Paragraph("two"));

        Assert.Equal("one", result.Document.Paragraphs[0].Text);
        Assert.True(result.Document.Paragraphs[1].Style.PageBreakBefore);
    }

    [Fact]
    public void A_Column_Break_Inside_A_Paragraph_Of_One_Column_Splits_It_Like_A_Page_Break()
    {
        DocumentReadResult result = Read(
            "<w:p><w:r><w:t>one</w:t><w:br w:type=\"column\"/><w:t>two</w:t></w:r></w:p>" +
            DocxTestPackage.Paragraph("next"));

        Assert.Equal(new[] { "one", "two", "next" }, result.Document.Paragraphs.Select(p => p.Text));
        Assert.True(result.Document.Paragraphs[1].Style.PageBreakBefore);
        Assert.False(result.Document.Paragraphs[2].Style.PageBreakBefore);
        Assert.False(Reports(result, "docx.pagebreak.split"));
    }

    [Fact]
    public void A_Column_Break_In_A_Section_Of_Columns_Starts_The_Next_Column()
    {
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("one") + ColumnBreakFirst("two") +
            Section("<w:cols w:num=\"2\"/>"));

        ParagraphStyle style = result.Document.Paragraphs[1].Style;
        Assert.True(style.ColumnBreakBefore);
        Assert.False(style.PageBreakBefore);
        Assert.Equal("two", result.Document.Paragraphs[1].Text);
    }

    [Fact]
    public void A_Column_Break_At_The_End_Of_A_Paragraph_In_Columns_Starts_The_Next()
    {
        DocumentReadResult result = Read(
            "<w:p><w:r><w:t>one</w:t><w:br w:type=\"column\"/></w:r></w:p>" +
            DocxTestPackage.Paragraph("two") +
            Section("<w:cols w:num=\"2\"/>"));

        Assert.Equal("one", result.Document.Paragraphs[0].Text);
        Assert.True(result.Document.Paragraphs[1].Style.ColumnBreakBefore);
    }

    [Fact]
    public void A_Column_Break_Inside_A_Paragraph_Preserves_The_Column_Boundary()
    {
        DocumentReadResult result = Read(
            "<w:p><w:r><w:t>one</w:t><w:br w:type=\"column\"/><w:t>two</w:t></w:r></w:p>" +
            Section("<w:cols w:num=\"2\"/>"));

        Assert.Equal(new[] { "one", "two" }, result.Document.Paragraphs.Select(p => p.Text));
        Assert.True(result.Document.Paragraphs[1].Style.ColumnBreakBefore);
        Assert.False(result.Document.Paragraphs[1].Style.PageBreakBefore);
        Assert.False(Reports(result, "docx.break.column"));
    }

    [Fact]
    public void A_Column_Break_Is_Read_Against_The_Section_It_Is_In()
    {
        // The first section has two columns and its break moves to the second
        // column; the second has one, and its break goes to the next page.
        DocumentReadResult result = Read(
            ColumnBreakFirst("in columns") +
            SectionEnd("end of columns", "<w:cols w:num=\"2\"/>") +
            ColumnBreakFirst("on its own page") +
            Section("<w:type w:val=\"continuous\"/>"));

        RichTextParagraph inColumns = result.Document.Paragraphs[0];
        RichTextParagraph ownPage = result.Document.Paragraphs[2];
        Assert.True(inColumns.Style.ColumnBreakBefore);
        Assert.False(inColumns.Style.PageBreakBefore);
        Assert.True(ownPage.Style.PageBreakBefore);
        Assert.Equal("on its own page", ownPage.Text);
    }

    [Fact]
    public void The_Paragraph_That_Ends_A_Section_Is_Read_In_That_Section()
    {
        // Its w:sectPr describes the section it closes, so a break in it is read
        // against those columns, not the next section's.
        DocumentReadResult result = Read(
            "<w:p><w:pPr>" + Section("<w:cols w:num=\"2\"/>") + "</w:pPr>" +
            "<w:r><w:br w:type=\"column\"/><w:t>last of the columns</w:t></w:r></w:p>" +
            DocxTestPackage.Paragraph("after") +
            Section("<w:type w:val=\"continuous\"/>"));

        Assert.True(result.Document.Paragraphs[0].Style.ColumnBreakBefore);
        Assert.False(result.Document.Paragraphs[0].Style.PageBreakBefore);
        Assert.False(result.Document.Paragraphs[1].Style.PageBreakBefore);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("<w:type w:val=\"nextPage\"/>", true)]
    [InlineData("<w:type w:val=\"continuous\"/>", false)]
    [InlineData("<w:type w:val=\"nextColumn\"/>", false)]
    public void A_Section_That_Starts_A_Page_Starts_One(string type, bool startsPage)
    {
        // w:type belongs to the section it starts, and its default is nextPage.
        DocumentReadResult result = Read(
            SectionEnd("first section", string.Empty) +
            DocxTestPackage.Paragraph("second section") +
            Section(type));

        Assert.False(result.Document.Paragraphs[0].Style.PageBreakBefore);
        Assert.Equal(startsPage, result.Document.Paragraphs[1].Style.PageBreakBefore);
    }

    [Fact]
    public void A_Section_On_An_Odd_Page_Starts_A_Page_And_Says_What_It_Did_Not_Make()
    {
        DocumentReadResult result = Read(
            SectionEnd("first section", string.Empty) +
            DocxTestPackage.Paragraph("second section") +
            Section("<w:type w:val=\"oddPage\"/>"));

        Assert.True(result.Document.Paragraphs[1].Style.PageBreakBefore);
        Assert.True(Reports(result, "docx.section.parity"));
    }

    [Fact]
    public void A_Section_Break_And_A_Page_Break_At_The_Same_Place_Are_One_Boundary()
    {
        DocumentReadResult result = Read(
            "<w:p><w:pPr>" + Section(string.Empty) + "</w:pPr>" +
            "<w:r><w:t>first</w:t><w:br w:type=\"page\"/></w:r></w:p>" +
            DocxTestPackage.Paragraph("second") +
            Section());

        Assert.True(result.Document.Paragraphs[1].Style.PageBreakBefore);
        Assert.False(Reports(result, "docx.pagebreak.repeated"));
    }

    [Fact]
    public void Sections_With_Their_Own_Columns_Are_Kept_As_Sections()
    {
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("title") +
            SectionEnd("abstract", string.Empty) +
            DocxTestPackage.Table([[DocxTestPackage.Paragraph("a"), DocxTestPackage.Paragraph("b")]]) +
            DocxTestPackage.Paragraph("body one") +
            SectionEnd("body two", "<w:type w:val=\"continuous\"/><w:cols w:num=\"2\" w:space=\"360\"/>") +
            DocxTestPackage.Paragraph("end") +
            Section("<w:type w:val=\"continuous\"/>"));

        Assert.Collection(
            result.Document.Sections,
            first =>
            {
                Assert.Equal(0, first.ParagraphIndex);
                Assert.Equal(2, first.ParagraphCount);
                Assert.Equal(1, first.ColumnCount);
            },
            second =>
            {
                // The table's cell paragraphs are the section's too.
                Assert.Equal(2, second.ParagraphIndex);
                Assert.Equal(4, second.ParagraphCount);
                Assert.Equal(2, second.ColumnCount);
                Assert.Equal(18, second.ColumnGap, 3);
            },
            third =>
            {
                Assert.Equal(6, third.ParagraphIndex);
                Assert.Equal(1, third.ParagraphCount);
                Assert.Equal(1, third.ColumnCount);
            });
    }

    [Fact]
    public void Sections_That_Agree_On_Their_Columns_Leave_Them_To_The_Page()
    {
        DocumentReadResult result = Read(
            SectionEnd("first", "<w:cols w:num=\"2\"/>") +
            DocxTestPackage.Paragraph("second") +
            Section("<w:cols w:num=\"2\"/>"));

        Assert.Empty(result.Document.Sections);
        Assert.Equal(2, GeometryOf(result).ColumnCount);
    }

    [Fact]
    public void A_Page_Break_That_Opens_A_Paragraph_Starts_That_Paragraph_On_The_New_Page()
    {
        // Nothing of the paragraph is before the break, so nothing is split:
        // the paragraph is what the page starts with.
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("before") +
            "<w:p><w:r><w:br w:type=\"page\"/><w:t>after</w:t></w:r></w:p>");

        Assert.Equal("after", result.Document.Paragraphs[1].Text);
        Assert.True(result.Document.Paragraphs[1].Style.PageBreakBefore);
        Assert.False(Reports(result, "docx.pagebreak.split"));
    }

    [Fact]
    public void A_Break_That_Opens_A_Paragraph_Already_Starting_A_Page_Is_One_Boundary()
    {
        DocumentReadResult result = Read(
            "<w:p><w:r><w:t>before</w:t><w:br w:type=\"page\"/></w:r></w:p>" +
            "<w:p><w:r><w:br w:type=\"page\"/><w:t>after</w:t></w:r></w:p>");

        Assert.Equal("after", result.Document.Paragraphs[1].Text);
        Assert.True(result.Document.Paragraphs[1].Style.PageBreakBefore);
        Assert.True(Reports(result, "docx.pagebreak.repeated"));
    }

    [Fact]
    public void A_Column_Break_In_A_Header_Stays_A_Line_Break()
    {
        // A header is not the body's flow and has no columns to move between.
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/_rels/document.xml.rels"] =
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rH\" " +
                "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" " +
                "Target=\"header1.xml\"/></Relationships>",
            ["word/header1.xml"] =
                "<w:hdr " + DocxTestPackage.BodyNamespaceDeclarations + ">" +
                "<w:p><w:r><w:t>a</w:t><w:br w:type=\"column\"/><w:t>b</w:t></w:r></w:p></w:hdr>",
        };

        using var stream = new MemoryStream(
            DocxTestPackage.FromBody(
                DocxTestPackage.Paragraph("body") +
                "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"default\"/>" + Page + "</w:sectPr>",
                parts),
            writable: false);
        DocumentReadResult result = new DocxDocumentCodec().Read(stream);

        RichTextParagraph header = Assert.Single(result.Document.RunningContent.Header(PageSelection.Default));
        Assert.Equal("a" + LineBreak + "b", header.Text);
        Assert.False(Reports(result, "docx.pagebreak.split"));
    }

    [Fact]
    public void Columns_Survive_A_Round_Trip()
    {
        RichTextDocument source = Read(
            DocxTestPackage.Paragraph("body") + Section("<w:cols w:num=\"3\" w:space=\"360\"/>")).Document;

        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(source), writable: false);
        PageGeometry geometry = Assert.IsType<PageGeometry>(new DocxDocumentCodec().Read(stream).Document.PageGeometry);

        Assert.Equal(3, geometry.ColumnCount);
        Assert.Equal(18, geometry.ColumnGap, 3);
    }

    [Fact]
    public void Writes_The_Columns_Between_The_Margins_And_The_Title_Page_Flag()
    {
        // CT_SectPr is a sequence: pgSz, pgMar, cols, then titlePg.
        RichTextDocument source = Read(
            DocxTestPackage.Paragraph("body") + Section("<w:cols w:num=\"2\" w:space=\"720\"/>")).Document;
        source = source.WithRunningContent(source.RunningContent.WithDifferentFirstPage(true));

        byte[] bytes = DocxDocumentCodec.WriteToArray(source);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
        string xml = reader.ReadToEnd();

        int margins = xml.IndexOf("<w:pgMar", StringComparison.Ordinal);
        int cols = xml.IndexOf("<w:cols", StringComparison.Ordinal);
        int titlePage = xml.IndexOf("<w:titlePg", StringComparison.Ordinal);
        Assert.True(margins >= 0 && cols > margins && titlePage > cols, xml);
        Assert.Contains("w:num=\"2\"", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Single_Column_Writes_No_Columns()
    {
        byte[] bytes = DocxDocumentCodec.WriteToArray(Read(DocxTestPackage.Paragraph("body") + Section()).Document);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());

        Assert.DoesNotContain("w:cols", reader.ReadToEnd(), StringComparison.Ordinal);
    }

    private static RichTextDocument RoundTrip(RichTextDocument document)
    {
        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(document), writable: false);
        return new DocxDocumentCodec().Read(stream).Document;
    }

    private static RichTextParagraph Breaking(string text) =>
        RichTextParagraph.Create(text, InlineStyle.Default, ParagraphStyle.Default with { ColumnBreakBefore = true });

    [Fact]
    public void A_Column_Break_Round_Trips_In_A_Page_Of_Columns()
    {
        RichTextDocument source = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("one"), Breaking("two")])
            .WithPageGeometry(new PageGeometry(612, 792, 72, 72, 72, 72, ColumnCount: 2, ColumnGap: 36));

        RichTextParagraph two = RoundTrip(source).Paragraphs[1];

        Assert.Equal("two", two.Text);
        Assert.True(two.Style.ColumnBreakBefore);
        Assert.False(two.Style.PageBreakBefore);
    }

    [Fact]
    public void A_Column_Break_On_A_Page_Of_One_Column_Comes_Back_As_The_Page_Break_It_Is()
    {
        RichTextDocument source = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("one"), Breaking("two")])
            .WithPageGeometry(new PageGeometry(612, 792, 72, 72, 72, 72));

        Assert.True(RoundTrip(source).Paragraphs[1].Style.PageBreakBefore);
    }

    [Fact]
    public void Sections_Round_Trip_With_Their_Columns()
    {
        RichTextDocument source = RichTextDocument.FromParagraphs(
            Enumerable.Range(0, 6).Select(i => RichTextParagraph.Plain("p" + i)).ToList())
            .WithPageGeometry(new PageGeometry(612, 792, 72, 72, 72, 72))
            .WithSections(
            [
                new DocumentSection(0, 2, 1, 0),
                new DocumentSection(2, 3, 3, 18),
                new DocumentSection(5, 1, 1, 0),
            ]);

        RichTextDocument read = RoundTrip(source);

        Assert.Equal(6, read.ParagraphCount);
        Assert.Collection(
            read.Sections,
            first => Assert.Equal((0, 2, 1), (first.ParagraphIndex, first.ParagraphCount, first.ColumnCount)),
            second =>
            {
                Assert.Equal((2, 3, 3), (second.ParagraphIndex, second.ParagraphCount, second.ColumnCount));
                Assert.Equal(18, second.ColumnGap, 3);
            },
            third => Assert.Equal((5, 1, 1), (third.ParagraphIndex, third.ParagraphCount, third.ColumnCount)));

        // The breaks between them are continuous: no section starts a page.
        Assert.All(read.Paragraphs, paragraph => Assert.False(paragraph.Style.PageBreakBefore));
    }

    [Fact]
    public void A_Section_Ending_In_A_Table_Is_Closed_After_It()
    {
        var table = new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])]);
        RichTextDocument source = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Plain("a"), RichTextParagraph.Plain("b"), RichTextParagraph.Plain("after")])
            .WithTables([table])
            .WithSections([new DocumentSection(0, 2, 2, 18), new DocumentSection(2, 1, 1, 0)]);

        RichTextDocument read = RoundTrip(source);

        // Word's own shape: an empty paragraph after the table holds the
        // section's properties, so the section gains it.
        Assert.Equal("a\nb\n\nafter", read.PlainText);
        Assert.Equal(2, read.Sections[0].ColumnCount);
        Assert.Equal(3, read.Sections[0].ParagraphCount);
    }

    [Fact]
    public void An_Empty_Paragraph_Holding_Only_A_Section_Mark_Is_Not_A_Paragraph()
    {
        // tdf170602_exportContext.docx: a paragraph ends on a page break, and
        // the next-page section mark sits in an empty Heading 1 after it.
        // LibreOffice makes no paragraph of it, and the next chapter starts the
        // page the break and the section both ask for; read as a paragraph it
        // drew a page of its own.
        DocumentReadResult result = Read(
            "<w:p><w:r><w:t>contents</w:t></w:r><w:r><w:br w:type=\"page\"/></w:r></w:p>" +
            "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/>" + Section("<w:cols w:num=\"2\"/>") + "</w:pPr></w:p>" +
            DocxTestPackage.Paragraph("chapter") +
            Section());

        RichTextDocument document = result.Document;
        Assert.Equal("contents\nchapter", document.PlainText);
        Assert.True(document.Paragraphs[1].Style.PageBreakBefore);

        // The section it closed ends at the paragraph before it, its columns kept.
        Assert.Equal(2, document.Sections.Count);
        Assert.Equal(1, document.Sections[0].ParagraphCount);
        Assert.Equal(2, document.Sections[0].ColumnCount);
        Assert.Equal(1, document.Sections[1].ParagraphIndex);
    }

    [Fact]
    public void A_Section_Mark_That_Is_All_Its_Section_Holds_Stays()
    {
        // Dropping it would drop the section; LibreOffice keeps it too.
        DocumentReadResult result = Read(
            "<w:p><w:pPr>" + Section("<w:cols w:num=\"2\"/>") + "</w:pPr></w:p>" +
            DocxTestPackage.Paragraph("body") +
            Section());

        Assert.Equal(2, result.Document.ParagraphCount);
        Assert.Equal(2, result.Document.Sections[0].ColumnCount);
    }

    [Fact]
    public void A_Section_With_Its_Own_Margins_Keeps_Them()
    {
        // The middle section is two inches in from either side; the others, and
        // so the document, are an inch in.
        const string narrow =
            "<w:sectPr><w:type w:val=\"continuous\"/><w:pgSz w:w=\"12240\" w:h=\"15840\"/>" +
            "<w:pgMar w:left=\"2880\" w:right=\"2880\" w:top=\"1440\" w:bottom=\"1440\"/></w:sectPr>";
        DocumentReadResult result = Read(
            SectionEnd("first", string.Empty) +
            "<w:p><w:pPr>" + narrow + "</w:pPr><w:r><w:t>second</w:t></w:r></w:p>" +
            DocxTestPackage.Paragraph("third") +
            Section("<w:type w:val=\"continuous\"/>"));

        Assert.Equal(72, GeometryOf(result).MarginLeft, 3);
        Assert.Collection(
            result.Document.Sections,
            first => Assert.Null(first.Page),
            second =>
            {
                PageGeometry page = Assert.IsType<PageGeometry>(second.Page);
                Assert.Equal((144.0, 144.0), (page.MarginLeft, page.MarginRight));
                Assert.Equal(1, second.ColumnCount);
            },
            third => Assert.Null(third.Page));
    }

    [Fact]
    public void A_Section_Page_Round_Trips()
    {
        PageGeometry letter = new(612, 792, 72, 72, 72, 72);
        PageGeometry narrow = new(612, 792, 144, 144, 90, 72);
        RichTextDocument source = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Plain("a"), RichTextParagraph.Plain("b"), RichTextParagraph.Plain("c")])
            .WithPageGeometry(letter)
            .WithSections(
            [
                new DocumentSection(0, 1, 1, 0),
                new DocumentSection(1, 1, 2, 18, narrow),
                new DocumentSection(2, 1, 1, 0),
            ]);

        RichTextDocument read = RoundTrip(source);

        Assert.Equal(72, read.PageGeometry!.MarginLeft, 3);
        PageGeometry page = Assert.IsType<PageGeometry>(read.Sections[1].Page);
        Assert.Equal((144.0, 144.0, 90.0), (page.MarginLeft, page.MarginRight, page.MarginTop));
        Assert.Null(read.Sections[0].Page);
        Assert.Null(read.Sections[2].Page);
    }
}
