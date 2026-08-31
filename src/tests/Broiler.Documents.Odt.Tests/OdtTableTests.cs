namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Tables in ODT: the grid, the spans, the borders, and the background a
/// <c>table:table</c> states. All of it used to be dropped - the cells'
/// paragraphs were read in row order and the shape they were in was thrown away
/// with a note saying so.
/// </summary>
public sealed class OdtTableTests
{
    private static string Styles() =>
        "<style:style style:name=\"co1\" style:family=\"table-column\">" +
        "<style:table-column-properties style:column-width=\"90pt\"/></style:style>" +
        "<style:style style:name=\"ce1\" style:family=\"table-cell\">" +
        "<style:table-cell-properties fo:background-color=\"#aecf00\" " +
        "fo:border=\"0.5pt solid #333333\"/></style:style>";

    private static string Columns(int count) =>
        string.Concat(Enumerable.Repeat("<table:table-column table:style-name=\"co1\"/>", count));

    private static string Cell(string text, string attributes = "") =>
        "<table:table-cell " + attributes + ">" + OdtTestPackage.Paragraph(text) + "</table:table-cell>";

    private static string Row(params string[] cells) =>
        "<table:table-row>" + string.Concat(cells) + "</table:table-row>";

    private static string Table(string columns, params string[] rows) =>
        "<table:table table:name=\"T\">" + columns + string.Concat(rows) + "</table:table>";

    private static string SimpleTable() =>
        Table(Columns(2), Row(Cell("a1"), Cell("b1")), Row(Cell("a2"), Cell("b2")));

    private static RichTextDocument Read(string bodyXml) =>
        OdtTestPackage.ReadStyled(bodyXml, Styles()).Document;

    private static RichTextDocument RoundTrip(RichTextDocument document)
    {
        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(document), writable: false);
        return new OdtDocumentCodec().Read(stream).Document;
    }

    [Fact]
    public void Reads_A_Table_As_A_Grid_Over_Its_Paragraphs()
    {
        RichTextDocument document = Read(SimpleTable());

        DocumentTable table = Assert.Single(document.Tables);
        Assert.Equal(0, table.ParagraphIndex);
        Assert.Equal(4, table.ParagraphCount);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("a1\nb1\na2\nb2", document.PlainText);
    }

    [Fact]
    public void Reads_The_Column_Widths_From_The_Column_Styles()
    {
        DocumentTable table = Assert.Single(Read(SimpleTable()).Tables);

        Assert.Equal(2, table.ColumnWidths.Count);
        Assert.Equal(90, table.ColumnWidths[0], 3);
    }

    [Fact]
    public void Repeats_A_Column_As_Many_Times_As_It_Says()
    {
        DocumentTable table = Assert.Single(Read(Table(
            "<table:table-column table:style-name=\"co1\" table:number-columns-repeated=\"3\"/>",
            Row(Cell("a"), Cell("b"), Cell("c")))).Tables);

        Assert.Equal(3, table.ColumnWidths.Count);
        Assert.Equal(270, table.TotalWidth, 3);
    }

    [Fact]
    public void Reads_A_Column_Span_And_Passes_Over_The_Cells_It_Covers()
    {
        RichTextDocument document = Read(Table(
            Columns(3),
            Row(
                Cell("wide", "table:number-columns-spanned=\"2\""),
                "<table:covered-table-cell/>",
                Cell("last")),
            Row(Cell("a"), Cell("b"), Cell("c"))));

        TableRow row = Assert.Single(document.Tables).Rows[0];

        // The covered cell is not a cell: it holds nothing and is drawn by
        // nobody, so only the column it occupies is taken from it.
        Assert.Equal(2, row.Cells.Count);
        Assert.Equal(2, row.Cells[0].ColumnSpan);
        Assert.Equal(2, row.Cells[1].ColumnIndex);
        Assert.Equal("wide\nlast\na\nb\nc", document.PlainText);
    }

    [Fact]
    public void Reads_A_Row_Span_From_The_Cell_That_Opens_It()
    {
        RichTextDocument document = Read(Table(
            Columns(2),
            Row(Cell("tall", "table:number-rows-spanned=\"2\""), Cell("b1")),
            Row("<table:covered-table-cell/>", Cell("b2"))));

        DocumentTable table = Assert.Single(document.Tables);
        Assert.Equal(2, table.Rows[0].Cells[0].RowSpan);
        Assert.Equal(1, Assert.Single(table.Rows[1].Cells).ColumnIndex);
    }

    [Fact]
    public void Reads_A_Cells_Background_And_Borders()
    {
        RichTextDocument document = Read(Table(
            Columns(2),
            Row(Cell("shaded", "table:style-name=\"ce1\""), Cell("plain"))));

        TableRow row = Assert.Single(document.Tables).Rows[0];
        Assert.Equal(0xAE, row.Cells[0].Shading.R);
        Assert.Equal(0xCF, row.Cells[0].Shading.G);
        Assert.Equal(0.5, row.Cells[0].Borders.Top.Width, 3);
        Assert.Equal(0x33, row.Cells[0].Borders.Left.Color.R);
        Assert.True(row.Cells[1].Shading.IsEmpty);
        Assert.False(row.Cells[1].Borders.IsVisible);
    }

    [Fact]
    public void Marks_The_Rows_In_A_Header_Group()
    {
        RichTextDocument document = Read(
            "<table:table table:name=\"T\">" + Columns(1) +
            "<table:table-header-rows>" + Row(Cell("head")) + "</table:table-header-rows>" +
            "<table:table-rows>" + Row(Cell("body")) + "</table:table-rows>" +
            "</table:table>");

        DocumentTable table = Assert.Single(document.Tables);
        Assert.True(table.Rows[0].IsHeader);
        Assert.False(table.Rows[1].IsHeader);
    }

    [Fact]
    public void Reads_A_Nested_Table_As_A_Table_Inside_The_Cell()
    {
        string inner = Table(Columns(1), Row(Cell("deep")));
        RichTextDocument document = Read(Table(
            Columns(2),
            Row("<table:table-cell>" + inner + OdtTestPackage.Paragraph("after") + "</table:table-cell>", Cell("right"))));

        DocumentTable outer = Assert.Single(document.Tables);
        DocumentTable nested = Assert.Single(outer.Rows[0].Cells[0].Tables);
        Assert.Equal(1, nested.ParagraphCount);
        Assert.Equal("deep\nafter\nright", document.PlainText);
    }

    [Fact]
    public void A_Table_Round_Trips_With_Its_Grid_And_Text()
    {
        RichTextDocument actual = RoundTrip(Read(SimpleTable()));

        DocumentTable table = Assert.Single(actual.Tables);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(2, table.Rows[0].Cells.Count);
        Assert.Equal(90, table.ColumnWidths[0], 3);
        Assert.Equal("a1\nb1\na2\nb2", actual.PlainText);
    }

    [Fact]
    public void Spans_Background_And_Borders_Round_Trip()
    {
        RichTextDocument source = Read(Table(
            Columns(3),
            Row(
                Cell("wide", "table:number-columns-spanned=\"2\" table:style-name=\"ce1\""),
                "<table:covered-table-cell/>",
                Cell("tall", "table:number-rows-spanned=\"2\"")),
            Row(Cell("a"), Cell("b"), "<table:covered-table-cell/>")));

        DocumentTable table = Assert.Single(RoundTrip(source).Tables);

        Assert.Equal(2, table.Rows[0].Cells[0].ColumnSpan);
        Assert.Equal(0xAE, table.Rows[0].Cells[0].Shading.R);
        Assert.Equal(0.5, table.Rows[0].Cells[0].Borders.Bottom.Width, 3);
        Assert.Equal(2, table.Rows[0].Cells[1].RowSpan);
    }

    [Fact]
    public void A_Written_Table_Is_A_Table()
    {
        using var archive = new System.IO.Compression.ZipArchive(
            new MemoryStream(OdtDocumentCodec.WriteToArray(Read(SimpleTable()))));
        using var reader = new StreamReader(archive.GetEntry("content.xml")!.Open());
        string xml = reader.ReadToEnd();

        Assert.Contains("<table:table ", xml, StringComparison.Ordinal);
        Assert.Contains("<table:table-column", xml, StringComparison.Ordinal);
        Assert.Contains("<table:table-cell", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Document_Without_Tables_Writes_None()
    {
        using var archive = new System.IO.Compression.ZipArchive(
            new MemoryStream(OdtDocumentCodec.WriteToArray(RichTextDocument.FromPlainText("body"))));
        using var reader = new StreamReader(archive.GetEntry("content.xml")!.Open());

        Assert.DoesNotContain("<table:table", reader.ReadToEnd(), StringComparison.Ordinal);
    }
}
