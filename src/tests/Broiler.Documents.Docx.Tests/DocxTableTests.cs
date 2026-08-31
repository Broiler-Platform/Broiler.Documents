namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Tables: the grid, the spans, the borders, and the shading a <c>w:tbl</c>
/// states. All of it used to be dropped — the cells' paragraphs were read in row
/// order and the shape they were in was thrown away with a note saying so, which
/// turned a CV's two-column layout into a single column of alternating headings
/// and answers.
/// </summary>
public sealed class DocxTableTests
{
    /// <summary>A grid of three columns, 90 points each: 1800 twips to the point-and-a-half.</summary>
    private const string Grid =
        "<w:tblGrid>" +
        "<w:gridCol w:w=\"1800\"/><w:gridCol w:w=\"1800\"/><w:gridCol w:w=\"1800\"/>" +
        "</w:tblGrid>";

    private static string Cell(string text, string properties = "") =>
        "<w:tc><w:tcPr>" + properties + "</w:tcPr>" + DocxTestPackage.Paragraph(text) + "</w:tc>";

    private static string Row(params string[] cells) => "<w:tr>" + string.Concat(cells) + "</w:tr>";

    private static string Table(string properties, string grid, params string[] rows) =>
        "<w:tbl><w:tblPr>" + properties + "</w:tblPr>" + grid + string.Concat(rows) + "</w:tbl>";

    private static string SimpleTable() =>
        Table(
            string.Empty,
            Grid,
            Row(Cell("a1"), Cell("b1"), Cell("c1")),
            Row(Cell("a2"), Cell("b2"), Cell("c2")));

    private static RichTextDocument Read(string bodyXml) => DocxTestPackage.ReadBody(bodyXml).Document;

    private static RichTextDocument RoundTrip(RichTextDocument document)
    {
        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(document), writable: false);
        return new DocxDocumentCodec().Read(stream).Document;
    }

    [Fact(Timeout = 600000)]
    public void Reads_A_Table_As_A_Grid_Over_Its_Paragraphs()
    {
        RichTextDocument document = Read(SimpleTable());

        DocumentTable table = Assert.Single(document.Tables);
        Assert.Equal(0, table.ParagraphIndex);
        Assert.Equal(6, table.ParagraphCount);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(3, table.Rows[0].Cells.Count);

        // The text is where it always was: one flat list, in row-major order.
        Assert.Equal("a1\nb1\nc1\na2\nb2\nc2", document.PlainText);
    }

    [Fact(Timeout = 600000)]
    public void Each_Cell_Names_The_Paragraphs_It_Holds()
    {
        DocumentTable table = Assert.Single(Read(SimpleTable()).Tables);

        TableCell first = table.Rows[0].Cells[0];
        Assert.Equal(0, first.ParagraphIndex);
        Assert.Equal(1, first.ParagraphCount);

        TableCell last = table.Rows[1].Cells[2];
        Assert.Equal(5, last.ParagraphIndex);
        Assert.Equal(1, last.ParagraphCount);
    }

    [Fact(Timeout = 600000)]
    public void A_Cell_Holding_Several_Paragraphs_Names_All_Of_Them()
    {
        RichTextDocument document = Read(Table(
            string.Empty,
            Grid,
            Row(
                "<w:tc><w:tcPr/>" +
                DocxTestPackage.Paragraph("one") +
                DocxTestPackage.Paragraph("two") +
                "</w:tc>",
                Cell("right"))));

        TableCell cell = Assert.Single(document.Tables).Rows[0].Cells[0];
        Assert.Equal(0, cell.ParagraphIndex);
        Assert.Equal(2, cell.ParagraphCount);
        Assert.Equal("one\ntwo\nright", document.PlainText);
    }

    [Fact(Timeout = 600000)]
    public void Reads_The_Grid_In_Points()
    {
        DocumentTable table = Assert.Single(Read(SimpleTable()).Tables);

        // Twentieths of a point, which is what the format measures in.
        Assert.Equal(3, table.ColumnWidths.Count);
        Assert.Equal(90, table.ColumnWidths[0], 3);
        Assert.Equal(270, table.TotalWidth, 3);
    }

    [Fact(Timeout = 600000)]
    public void Reads_A_Horizontal_Span_And_The_Column_It_Starts_In()
    {
        RichTextDocument document = Read(Table(
            string.Empty,
            Grid,
            Row(Cell("wide", "<w:gridSpan w:val=\"2\"/>"), Cell("last")),
            Row(Cell("a"), Cell("b"), Cell("c"))));

        TableRow row = Assert.Single(document.Tables).Rows[0];
        Assert.Equal(2, row.Cells[0].ColumnSpan);
        Assert.Equal(0, row.Cells[0].ColumnIndex);

        // The next cell starts after the span, not after the cell.
        Assert.Equal(2, row.Cells[1].ColumnIndex);
        Assert.Equal(1, row.Cells[1].ColumnSpan);
    }

    [Fact(Timeout = 600000)]
    public void Reads_A_Vertical_Merge_As_A_Row_Span()
    {
        RichTextDocument document = Read(Table(
            string.Empty,
            Grid,
            Row(Cell("tall", "<w:vMerge w:val=\"restart\"/>"), Cell("b1")),
            Row(Cell("", "<w:vMerge/>"), Cell("b2")),
            Row(Cell("", "<w:vMerge/>"), Cell("b3"))));

        DocumentTable table = Assert.Single(document.Tables);
        Assert.Equal(3, table.Rows[0].Cells[0].RowSpan);
        Assert.False(table.Rows[0].Cells[0].IsRowSpanContinuation);

        // The cells it covers are still in the grid, so the row keeps its
        // columns; they are drawn by no one.
        Assert.True(table.Rows[1].Cells[0].IsRowSpanContinuation);
        Assert.Equal(1, table.Rows[1].Cells[1].RowSpan);
    }

    [Fact(Timeout = 600000)]
    public void A_Merge_Continuation_With_Nothing_Above_It_Is_Its_Own_Cell()
    {
        RichTextDocument document = Read(Table(
            string.Empty,
            Grid,
            Row(Cell("a1"), Cell("b1")),
            Row(Cell("orphan", "<w:vMerge/>"), Cell("b2"))));

        // The first row's cell never opened a merge, so there is nothing for this
        // one to continue and it is a cell in its own right.
        DocumentTable table = Assert.Single(document.Tables);
        Assert.False(table.Rows[1].Cells[0].IsRowSpanContinuation);
        Assert.Equal(1, table.Rows[0].Cells[0].RowSpan);
    }

    [Fact(Timeout = 600000)]
    public void Reads_Cell_Shading()
    {
        RichTextDocument document = Read(Table(
            string.Empty,
            Grid,
            Row(Cell("shaded", "<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"AECF00\"/>"), Cell("plain"))));

        TableRow row = Assert.Single(document.Tables).Rows[0];
        Assert.Equal(0xAE, row.Cells[0].Shading.R);
        Assert.Equal(0xCF, row.Cells[0].Shading.G);
        Assert.Equal(0x00, row.Cells[0].Shading.B);
        Assert.True(row.Cells[1].Shading.IsEmpty);
    }

    [Fact(Timeout = 600000)]
    public void A_Cell_Inherits_The_Tables_Borders()
    {
        RichTextDocument document = Read(Table(
            "<w:tblBorders>" +
            "<w:top w:val=\"single\" w:sz=\"8\" w:color=\"FF0000\"/>" +
            "<w:left w:val=\"single\" w:sz=\"8\" w:color=\"FF0000\"/>" +
            "<w:bottom w:val=\"single\" w:sz=\"8\" w:color=\"FF0000\"/>" +
            "<w:right w:val=\"single\" w:sz=\"8\" w:color=\"FF0000\"/>" +
            "<w:insideH w:val=\"single\" w:sz=\"4\" w:color=\"FF0000\"/>" +
            "<w:insideV w:val=\"single\" w:sz=\"4\" w:color=\"FF0000\"/>" +
            "</w:tblBorders>",
            Grid,
            Row(Cell("a"), Cell("b"))));

        CellBorders borders = Assert.Single(document.Tables).Rows[0].Cells[0].Borders;

        // w:sz is eighths of a point.
        Assert.True(borders.Top.IsVisible);
        Assert.Equal(1, borders.Top.Width, 3);
        Assert.Equal(0xFF, borders.Top.Color.R);
    }

    [Fact(Timeout = 600000)]
    public void A_Cells_Own_Borders_Win_Over_The_Tables()
    {
        RichTextDocument document = Read(Table(
            "<w:tblBorders><w:top w:val=\"single\" w:sz=\"8\" w:color=\"FF0000\"/></w:tblBorders>",
            Grid,
            Row(Cell("a", "<w:tcBorders><w:top w:val=\"none\"/></w:tcBorders>"), Cell("b"))));

        TableRow row = Assert.Single(document.Tables).Rows[0];
        Assert.False(row.Cells[0].Borders.Top.IsVisible);
        Assert.True(row.Cells[1].Borders.Top.IsVisible);
    }

    [Fact(Timeout = 600000)]
    public void Reads_The_Cell_Padding_The_Table_States()
    {
        DocumentTable table = Assert.Single(Read(Table(
            "<w:tblCellMar><w:left w:w=\"200\" w:type=\"dxa\"/><w:right w:w=\"200\" w:type=\"dxa\"/></w:tblCellMar>",
            Grid,
            Row(Cell("a"), Cell("b")))).Tables);

        Assert.Equal(10, table.CellPadding, 3);
    }

    [Fact(Timeout = 600000)]
    public void A_Table_Stating_No_Margins_Takes_Words_Default()
    {
        Assert.Equal(
            DocumentTable.DefaultCellPadding,
            Assert.Single(Read(SimpleTable()).Tables).CellPadding,
            3);
    }

    [Fact(Timeout = 600000)]
    public void Reads_A_Nested_Table_As_A_Table_Inside_The_Cell()
    {
        string inner = Table(string.Empty, "<w:tblGrid><w:gridCol w:w=\"900\"/></w:tblGrid>", Row(Cell("deep")));
        RichTextDocument document = Read(Table(
            string.Empty,
            Grid,
            Row("<w:tc><w:tcPr/>" + inner + DocxTestPackage.Paragraph("after") + "</w:tc>", Cell("right"))));

        // The body holds the outer table; the nested one belongs to the cell it
        // is in, which is what tells them apart when their ranges cannot.
        DocumentTable outer = Assert.Single(document.Tables);
        Assert.Equal(3, outer.ParagraphCount);

        TableCell cell = outer.Rows[0].Cells[0];
        DocumentTable nested = Assert.Single(cell.Tables);
        Assert.Equal(1, nested.ParagraphCount);
        Assert.Equal(cell.ParagraphIndex, nested.ParagraphIndex);
        Assert.Equal("deep\nafter\nright", document.PlainText);
    }

    [Fact(Timeout = 600000)]
    public void Reports_A_Table_Style_It_Cannot_Apply()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(Table(
            "<w:tblStyle w:val=\"TableGrid\"/>",
            Grid,
            Row(Cell("a"), Cell("b"))));

        Assert.Contains(result.Diagnostics, d => d.Code == "docx.table.style");
    }

    [Fact(Timeout = 600000)]
    public void A_Table_Round_Trips_With_Its_Grid_And_Text()
    {
        RichTextDocument actual = RoundTrip(Read(SimpleTable()));

        DocumentTable table = Assert.Single(actual.Tables);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(3, table.Rows[0].Cells.Count);
        Assert.Equal(90, table.ColumnWidths[0], 3);
        Assert.Equal("a1\nb1\nc1\na2\nb2\nc2", actual.PlainText);
    }

    [Fact(Timeout = 600000)]
    public void Spans_Shading_And_Borders_Round_Trip()
    {
        RichTextDocument source = Read(Table(
            "<w:tblBorders><w:top w:val=\"single\" w:sz=\"8\" w:color=\"123456\"/></w:tblBorders>",
            Grid,
            Row(
                Cell("wide", "<w:gridSpan w:val=\"2\"/><w:shd w:val=\"clear\" w:fill=\"AECF00\"/>"),
                Cell("tall", "<w:vMerge w:val=\"restart\"/>")),
            Row(Cell("a"), Cell("b"), Cell("", "<w:vMerge/>"))));

        DocumentTable table = Assert.Single(RoundTrip(source).Tables);

        Assert.Equal(2, table.Rows[0].Cells[0].ColumnSpan);
        Assert.Equal(0xAE, table.Rows[0].Cells[0].Shading.R);
        Assert.Equal(2, table.Rows[0].Cells[1].RowSpan);
        Assert.True(table.Rows[1].Cells[2].IsRowSpanContinuation);
        Assert.Equal(1, table.Rows[0].Cells[0].Borders.Top.Width, 3);
        Assert.Equal(0x12, table.Rows[0].Cells[0].Borders.Top.Color.R);
    }

    [Fact(Timeout = 600000)]
    public void A_Nested_Table_Round_Trips_Inside_Its_Cell()
    {
        string inner = Table(string.Empty, "<w:tblGrid><w:gridCol w:w=\"900\"/></w:tblGrid>", Row(Cell("deep")));
        RichTextDocument source = Read(Table(
            string.Empty,
            Grid,
            Row("<w:tc><w:tcPr/>" + inner + DocxTestPackage.Paragraph("after") + "</w:tc>", Cell("right"))));

        RichTextDocument actual = RoundTrip(source);

        DocumentTable outer = Assert.Single(actual.Tables);
        Assert.Single(outer.Rows[0].Cells[0].Tables);
        Assert.Equal("deep\nafter\nright", actual.PlainText);
    }

    [Fact(Timeout = 600000)]
    public void A_Table_Written_Into_A_Package_Is_A_Table()
    {
        using var package = new System.IO.Compression.ZipArchive(
            new MemoryStream(DocxDocumentCodec.WriteToArray(Read(SimpleTable())), writable: false),
            System.IO.Compression.ZipArchiveMode.Read);
        using var reader = new StreamReader(package.GetEntry("word/document.xml")!.Open());
        string xml = reader.ReadToEnd();

        Assert.Contains("<w:tbl>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:gridCol", xml, StringComparison.Ordinal);
        Assert.Contains("<w:tc>", xml, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void A_Document_Without_Tables_Writes_None()
    {
        using var package = new System.IO.Compression.ZipArchive(
            new MemoryStream(DocxDocumentCodec.WriteToArray(RichTextDocument.FromPlainText("body")), writable: false),
            System.IO.Compression.ZipArchiveMode.Read);
        using var reader = new StreamReader(package.GetEntry("word/document.xml")!.Open());

        Assert.DoesNotContain("<w:tbl", reader.ReadToEnd(), StringComparison.Ordinal);
    }
}
