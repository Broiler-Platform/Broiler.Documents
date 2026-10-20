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
        // ECMA-376 §17.18.3: where the section is not divided into columns, a
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

        Assert.Equal("one" + LineBreak + "two", result.Document.Paragraphs[0].Text);
        Assert.False(result.Document.Paragraphs[1].Style.PageBreakBefore);
        Assert.True(Reports(result, "docx.pagebreak.split"));
    }

    [Fact]
    public void A_Column_Break_In_A_Section_Of_Columns_Is_Not_A_Page_Break()
    {
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("one") + ColumnBreakFirst("two") +
            Section("<w:cols w:num=\"2\"/>"));

        Assert.False(result.Document.Paragraphs[1].Style.PageBreakBefore);
        Assert.Equal(LineBreak + "two", result.Document.Paragraphs[1].Text);
        Assert.True(Reports(result, "docx.break.column"));
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
            Section());

        RichTextParagraph inColumns = result.Document.Paragraphs[0];
        RichTextParagraph ownPage = result.Document.Paragraphs[2];
        Assert.False(inColumns.Style.PageBreakBefore);
        Assert.Equal(LineBreak + "in columns", inColumns.Text);
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
            Section());

        Assert.False(result.Document.Paragraphs[0].Style.PageBreakBefore);
        Assert.False(result.Document.Paragraphs[1].Style.PageBreakBefore);
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
}
