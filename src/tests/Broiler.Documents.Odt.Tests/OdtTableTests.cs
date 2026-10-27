namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Tables in ODT: the grid, the spans, the borders, and the background a
/// <c>table:table</c> states. All of it used to be dropped - the cells'
/// paragraphs were read in row order and the shape they were in was thrown away
/// with a note saying so.
/// </summary>
public sealed class OdtTableTests
{
    [Fact]
    public void Large_Relative_Weights_And_Repeated_Columns_Remain_Finite()
    {
        string styles = "<style:style style:name=\"relative\" style:family=\"table-column\">" +
            "<style:table-column-properties style:rel-column-width=\"1e308*\"/></style:style>";
        string body = Table("<table:table-column table:style-name=\"relative\" table:number-columns-repeated=\"3\"/>",
            Row(Cell("one"), Cell("two"), Cell("three")));
        var table = Assert.Single(OdtTestPackage.ReadStyled(body, styles).Document.Tables);
        Assert.Equal(100, table.WidthPercent);
        Assert.Equal(3, table.ColumnWidths.Count);
        Assert.All(table.ColumnWidths, width => Assert.Equal(100d / 3, width, 6));
    }

    [Theory]
    [InlineData("NaN*")]
    [InlineData("Infinity*")]
    [InlineData("0*")]
    [InlineData("-2*")]
    [InlineData("2")]
    public void Invalid_Relative_Weights_Keep_The_Unspecified_Grid_Fallback(string weight)
    {
        string styles = "<style:style style:name=\"relative\" style:family=\"table-column\">" +
            "<style:table-column-properties style:rel-column-width=\"" + weight + "\"/></style:style>";
        var table = Assert.Single(OdtTestPackage.ReadStyled(Table(
            "<table:table-column table:style-name=\"relative\"/>", Row(Cell("body"))), styles).Document.Tables);
        Assert.Equal(0, Assert.Single(table.ColumnWidths));
        Assert.Null(table.WidthPercent);
    }

    [Theory]
    [InlineData("", 100d, 25, 75)]
    [InlineData("style:width=\"240pt\"", null, 60, 180)]
    public void Relative_Column_Widths_Keep_Their_Proportions(string tableProperties, double? percentage, double first, double second)
    {
        string styles = "<style:style style:name=\"table\" style:family=\"table\"><style:table-properties " +
            tableProperties + "/></style:style>" +
            "<style:style style:name=\"small\" style:family=\"table-column\"><style:table-column-properties style:rel-column-width=\"1*\"/></style:style>" +
            "<style:style style:name=\"large\" style:family=\"table-column\"><style:table-column-properties style:rel-column-width=\"3*\"/></style:style>";
        string body = "<table:table table:style-name=\"table\"><table:table-column table:style-name=\"small\"/>" +
            "<table:table-column table:style-name=\"large\"/>" + Row(Cell("one"), Cell("two")) + "</table:table>";
        var document = OdtTestPackage.ReadStyled(body, styles).Document;
        var table = Assert.Single(document.Tables);
        Assert.Equal(first, table.ColumnWidths[0], 3);
        Assert.Equal(second, table.ColumnWidths[1], 3);
        Assert.Equal(percentage, table.WidthPercent);
        var restored = Assert.Single(RoundTrip(document).Tables);
        Assert.Equal(3, restored.ColumnWidths[1] / restored.ColumnWidths[0], 3);
        Assert.Equal(percentage, restored.WidthPercent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_Explicitly_Disabled_Cell_Edge_Overrides_The_Uniform_Border(bool inherited)
    {
        string styles = "<style:style style:name=\"base\" style:family=\"table-cell\">" +
            "<style:table-cell-properties fo:border=\"1pt solid #ff0000\"/></style:style>" +
            "<style:style style:name=\"cell\" style:family=\"table-cell\"" +
            (inherited ? " style:parent-style-name=\"base\"" : "") + ">" +
            "<style:table-cell-properties " + (inherited ? "" : "fo:border=\"1pt solid #ff0000\" ") +
            "fo:border-right=\"none\"/></style:style>";
        var cell = Assert.Single(Assert.Single(Assert.Single(OdtTestPackage.ReadStyled(
            Table(Columns(1), Row(Cell("body", "table:style-name=\"cell\""))), Styles() + styles).Document.Tables).Rows).Cells);
        Assert.Equal(1, cell.Borders.Left.Width, 3);
        Assert.Equal(0, cell.Borders.Right.Width, 3);
        Assert.Equal(1, cell.Borders.Top.Width, 3);
    }

    [Fact]
    public void A_Childs_Uniform_Border_Overrides_The_Parents_Specific_Edges()
    {
        string styles = "<style:style style:name=\"base\" style:family=\"table-cell\">" +
            "<style:table-cell-properties fo:border-left=\"2pt solid #ff0000\"/></style:style>" +
            "<style:style style:name=\"cell\" style:family=\"table-cell\" style:parent-style-name=\"base\">" +
            "<style:table-cell-properties fo:border=\"none\" fo:border-bottom=\"1pt solid #000000\"/></style:style>";
        var cell = Assert.Single(Assert.Single(Assert.Single(OdtTestPackage.ReadStyled(
            Table(Columns(1), Row(Cell("body", "table:style-name=\"cell\""))), Styles() + styles).Document.Tables).Rows).Cells);
        Assert.Equal(0, cell.Borders.Left.Width, 3);
        Assert.Equal(0, cell.Borders.Right.Width, 3);
        Assert.Equal(1, cell.Borders.Bottom.Width, 3);
    }

    [Theory]
    [InlineData(false, 90, 30, false)]
    [InlineData(true, 120, 40, true)]
    public void Row_And_Column_Styles_Inherit_Their_Parents(bool overrides, double width, double height, bool canSplit)
    {
        string styles = "<style:style style:name=\"baseColumn\" style:family=\"table-column\">" +
            "<style:table-column-properties style:column-width=\"90pt\"/></style:style>" +
            "<style:style style:name=\"co1\" style:family=\"table-column\" style:parent-style-name=\"baseColumn\">" +
            (overrides ? "<style:table-column-properties style:column-width=\"120pt\"/>" : "") + "</style:style>" +
            "<style:style style:name=\"baseRow\" style:family=\"table-row\">" +
            "<style:table-row-properties style:min-row-height=\"30pt\" fo:keep-together=\"always\"/></style:style>" +
            "<style:style style:name=\"ro1\" style:family=\"table-row\" style:parent-style-name=\"baseRow\">" +
            (overrides ? "<style:table-row-properties style:min-row-height=\"40pt\" fo:keep-together=\"auto\"/>" : "") + "</style:style>";
        var table = Assert.Single(OdtTestPackage.ReadStyled(Table(Columns(1), StyledRow("ro1", Cell("body"))), styles).Document.Tables);
        Assert.Equal(width, Assert.Single(table.ColumnWidths), 3);
        var row = Assert.Single(table.Rows);
        Assert.Equal(height, row.MinHeight, 3);
        Assert.Equal(canSplit, row.CanSplit);
    }

    [Theory]
    [InlineData("min-row-height", "row-height", true)]
    [InlineData("row-height", "min-row-height", false)]
    public void A_Child_Rows_Height_Replaces_The_Parents_Height_Rule(string parentRule, string childRule, bool exact)
    {
        string styles = "<style:style style:name=\"base\" style:family=\"table-row\">" +
            "<style:table-row-properties style:" + parentRule + "=\"30pt\"/></style:style>" +
            "<style:style style:name=\"child\" style:family=\"table-row\" style:parent-style-name=\"base\">" +
            "<style:table-row-properties style:" + childRule + "=\"40pt\"/></style:style>";
        var row = Assert.Single(Assert.Single(OdtTestPackage.ReadStyled(
            Table(Columns(1), StyledRow("child", Cell("body"))), Styles() + styles).Document.Tables).Rows);
        Assert.Equal(40, row.MinHeight, 3);
        Assert.Equal(exact, row.ExactHeight);
    }

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

    private static string StyledRow(string styleName, params string[] cells) =>
        "<table:table-row table:style-name=\"" + styleName + "\">" +
        string.Concat(cells) + "</table:table-row>";

    /// <summary>The base styles plus one table-row style stating <paramref name="properties"/>.</summary>
    private static string StylesWithRow(string properties) =>
        Styles() +
        "<style:style style:name=\"ro1\" style:family=\"table-row\">" +
        "<style:table-row-properties " + properties + "/></style:style>";

    private static RichTextDocument ReadWithRowStyle(string properties, string bodyXml) =>
        OdtTestPackage.ReadStyled(bodyXml, StylesWithRow(properties)).Document;

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

    [Fact]
    public void Reads_The_Minimum_Height_A_Row_States()
    {
        RichTextDocument document = ReadWithRowStyle(
            "style:min-row-height=\"72pt\"",
            Table(Columns(2), StyledRow("ro1", Cell("a1"), Cell("b1")), Row(Cell("a2"), Cell("b2"))));

        DocumentTable table = Assert.Single(document.Tables);
        Assert.Equal(72, table.Rows[0].MinHeight, 3);
        Assert.Equal(0, table.Rows[1].MinHeight);
    }

    [Fact]
    public void A_Fixed_Row_Height_Remains_Fixed_And_Round_Trips()
    {
        // Fixed height is separate from the editable content it may clip.
        RichTextDocument document = ReadWithRowStyle(
            "style:row-height=\"1in\"",
            Table(Columns(2), StyledRow("ro1", Cell("a1"), Cell("b1")), Row(Cell("a2"), Cell("b2"))));

        Assert.Equal(72, Assert.Single(document.Tables).Rows[0].MinHeight, 3);
        Assert.True(document.Tables[0].Rows[0].ExactHeight);
        Assert.True(RoundTrip(document).Tables[0].Rows[0].ExactHeight);
    }

    [Theory]
    [InlineData("style:min-row-height=\"120pt\" style:row-height=\"90pt\"", 90, true)]
    [InlineData("style:row-height=\"90pt\" style:min-row-height=\"120pt\"", 120, false)]
    public void Conflicting_Height_Choices_Follow_LibreOffice_Attribute_Order(string properties, double height, bool exact)
    {
        RichTextDocument document = ReadWithRowStyle(
            properties,
            Table(Columns(2), StyledRow("ro1", Cell("a1"), Cell("b1"))));

        Assert.Equal(height, Assert.Single(document.Tables).Rows[0].MinHeight, 3);
        Assert.Equal(exact, document.Tables[0].Rows[0].ExactHeight);
    }

    [Fact]
    public void An_Optimal_Height_Row_Asks_For_Nothing()
    {
        // Optimal is the height the content needs, which is what a row without a
        // floor already does.
        RichTextDocument document = ReadWithRowStyle(
            "style:use-optimal-row-height=\"true\" style:row-height=\"90pt\"",
            Table(Columns(2), StyledRow("ro1", Cell("a1"), Cell("b1"))));

        Assert.Equal(0, Assert.Single(document.Tables).Rows[0].MinHeight);
    }

    [Fact]
    public void A_Row_Height_Round_Trips()
    {
        RichTextDocument source = ReadWithRowStyle(
            "style:min-row-height=\"72pt\"",
            Table(Columns(2), StyledRow("ro1", Cell("a1"), Cell("b1")), Row(Cell("a2"), Cell("b2"))));

        DocumentTable table = Assert.Single(RoundTrip(source).Tables);
        Assert.Equal(72, table.Rows[0].MinHeight, 3);
        Assert.Equal(0, table.Rows[1].MinHeight);
    }

    [Fact]
    public void Two_Writes_Of_A_Document_With_Row_Heights_Are_Still_Byte_Identical()
    {
        // The row styles are minted on first use and cached by their formatted
        // height, so a second write must reuse the same names in the same order.
        // Determinism is a property this codec states, and a new style cache is
        // exactly the shape of thing that quietly breaks it.
        RichTextDocument source = ReadWithRowStyle(
            "style:min-row-height=\"72pt\"",
            Table(
                Columns(2),
                StyledRow("ro1", Cell("a1"), Cell("b1")),
                Row(Cell("a2"), Cell("b2")),
                StyledRow("ro1", Cell("a3"), Cell("b3"))));

        Assert.Equal(
            OdtDocumentCodec.WriteToArray(source),
            OdtDocumentCodec.WriteToArray(source));
    }

    [Theory]
    [InlineData("fo:keep-together=\"always\"", false)]
    [InlineData("fo:keep-together=\"auto\"", true)]
    [InlineData("style:min-row-height=\"20pt\"", true)]
    public void A_Row_May_Break_Across_A_Page_Unless_It_Keeps_Together(string properties, bool canSplit)
    {
        RichTextDocument document = ReadWithRowStyle(
            properties,
            Table(Columns(2), StyledRow("ro1", Cell("a1"), Cell("b1")), Row(Cell("a2"), Cell("b2"))));

        DocumentTable table = Assert.Single(document.Tables);
        Assert.Equal(canSplit, table.Rows[0].CanSplit);
        Assert.True(table.Rows[1].CanSplit);
    }

    [Fact]
    public void A_Row_That_Keeps_Together_Round_Trips()
    {
        RichTextDocument source = ReadWithRowStyle(
            "fo:keep-together=\"always\" style:min-row-height=\"30pt\"",
            Table(Columns(2), StyledRow("ro1", Cell("a1"), Cell("b1")), Row(Cell("a2"), Cell("b2"))));

        DocumentTable table = Assert.Single(RoundTrip(source).Tables);
        Assert.False(table.Rows[0].CanSplit);
        Assert.Equal(30, table.Rows[0].MinHeight, 3);
        Assert.True(table.Rows[1].CanSplit);
    }
}
