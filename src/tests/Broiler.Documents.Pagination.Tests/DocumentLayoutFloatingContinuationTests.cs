namespace Broiler.Documents.Pagination.Tests;

public sealed class DocumentLayoutFloatingContinuationTests
{
    private static RichTextParagraph Text(string text) => RichTextParagraph.Create(text, InlineStyle.Default,
        ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 10 });
    private static LayoutResult Layout(RichTextDocument document, double height = 100)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, new DocumentPageBox(200, height, 10, 10, 10, 10));
    }

    [Theory]
    [InlineData(50d, null, 90d)]
    [InlineData(null, 140d, 140d)]
    public void Preferred_Floating_Widths_Resize_All_Grid_Columns(double? percent, double? preferred, double width)
    {
        var table = new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])], [10, 30],
            cellPadding: 0, position: new TablePosition(TableAnchor.Margin, 0, TableAnchor.Margin, 0, HorizontalAlignment: TableAlignment.Center),
            preferredWidth: preferred, widthPercent: percent);
        var cells = Assert.Single(Layout(RichTextDocument.FromParagraphs([Text("a"), Text("b")]).WithTables([table])).Pages).Cells;
        Assert.Equal(width / 4, cells[0].Bounds.Width, 3);
        Assert.Equal(width * 3 / 4, cells[1].Bounds.Width, 3);
        Assert.Equal(10 + (180 - width) / 2, cells[0].Bounds.Left, 3);
        Assert.Equal(cells[0].Bounds.Right, cells[1].Bounds.Left, 3);
    }

    [Fact]
    public void Percentage_Floating_Width_Uses_The_Page_Text_Extents_In_A_Multicolumn_Section()
    {
        var page = new PageGeometry(200, 100, 10, 10, 10, 10, ColumnCount: 2, ColumnGap: 10);
        var table = new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0)])], [10], cellPadding: 0,
            position: new TablePosition(TableAnchor.Text, 0, TableAnchor.Text, 0), widthPercent: 50);
        var document = RichTextDocument.FromParagraphs([Text("a")]).WithPageGeometry(page).WithTables([table]);
        Assert.Equal(90, Assert.Single(Layout(document).Pages[0].Cells).Bounds.Width, 3);
    }

    [Fact]
    public void A_First_Whole_Row_Moves_To_A_Fresh_Page_When_It_Fits_There()
    {
        var table = new DocumentTable(3, 1, [new TableRow([new TableCell(3, 1, 0)], minHeight: 30, canSplit: false)], [100], cellPadding: 0,
            position: new TablePosition(TableAnchor.Text, 0, TableAnchor.Text, 0));
        var pages = Layout(RichTextDocument.FromParagraphs([Text("before1"), Text("before2"), Text("before3"), Text("cell")]).WithTables([table]), 70).Pages;
        Assert.Equal(2, pages.Count);
        Assert.Empty(pages[0].Cells);
        Assert.Equal(10, Assert.Single(pages[1].Cells).Bounds.Top, 3);
    }

    [Theory]
    [InlineData(false, true, 50)]
    [InlineData(true, false, 50)]
    [InlineData(true, true, 20)]
    public void Either_Tables_Nonoverlap_Rule_Is_Respected(bool firstOverlap, bool secondOverlap, double secondTop)
    {
        DocumentTable Table(int index, bool overlap) => new(index, 1, [new TableRow([new TableCell(index, 1, 0)], minHeight: 30)], [100], cellPadding: 0,
            position: new TablePosition(TableAnchor.Page, 20, TableAnchor.Page, 20, AllowOverlap: overlap));
        var page = Assert.Single(Layout(RichTextDocument.FromParagraphs([Text("a"), Text("b")])
            .WithTables([Table(0, firstOverlap), Table(1, secondOverlap)]), 150).Pages);
        Assert.Equal(20, page.Cells[0].Bounds.Top, 3);
        Assert.Equal(secondTop, page.Cells[1].Bounds.Top, 3);
    }

    [Fact]
    public void Two_Overflowing_Floats_Both_Retain_Their_Continuation()
    {
        DocumentTable Table(int start, double left) => new(start, 5,
            [.. Enumerable.Range(start, 5).Select(index => new TableRow([new TableCell(index, 1, 0)], minHeight: 20))], [60], cellPadding: 0,
            position: new TablePosition(TableAnchor.Page, left, TableAnchor.Page, 10));
        var document = RichTextDocument.FromParagraphs(Enumerable.Range(0, 10).Select(index => Text("cell" + index)))
            .WithTables([Table(0, 10), Table(5, 110)]);
        var pages = Layout(document, 80).Pages;
        Assert.Equal(2, pages.Count);
        var text = pages.SelectMany(page => page.Lines).SelectMany(line => line.Pieces).Select(piece => piece.Text).ToArray();
        foreach (int index in Enumerable.Range(0, 10)) Assert.Single(text, value => value == "cell" + index);
        Assert.Equal(4, pages[1].Cells.Count);
    }

    [Fact]
    public void Floating_Headers_Repeat_Without_Drifting_To_A_Later_Position()
    {
        var rows = Enumerable.Range(0, 6).Select(index => new TableRow([new TableCell(index, 1, 0)], isHeader: index == 0,
            minHeight: index == 0 ? 10 : 20, canSplit: false)).ToArray();
        var table = new DocumentTable(0, 6, rows, [100], cellPadding: 0, position: new TablePosition(TableAnchor.Margin, 0, TableAnchor.Margin, 0));
        var document = RichTextDocument.FromParagraphs(Enumerable.Range(0, 6).Select(index => Text(index == 0 ? "header" : "body" + index))).WithTables([table]);
        var pages = Layout(document, 70).Pages;
        Assert.Equal(3, pages.Count);
        Assert.All(pages, page => Assert.Equal(10, Assert.Single(page.Lines, line => line.ParagraphIndex == 0).Top, 3));
        Assert.All(pages, page => Assert.Equal("header", page.Lines.First().Pieces.Single().Text));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Repeating_A_Numbered_Header_Does_Not_Consume_Body_List_Counters(bool floating)
    {
        var list = new ListDefinition([new ListLevel { Template = "%1." }]);
        RichTextParagraph Item(string text) => Text(text).WithParagraphStyle(Text(text).Style with { List = new ListReference(list, 0), ListKind = ListKind.Numbered });
        var rows = Enumerable.Range(0, 6).Select(index => new TableRow([new TableCell(index, 1, 0)], isHeader: index == 0,
            minHeight: index == 0 ? 10 : 20, canSplit: false)).ToArray();
        var table = new DocumentTable(0, 6, rows, [180], cellPadding: 0,
            position: floating ? new TablePosition(TableAnchor.Margin, 0, TableAnchor.Margin, 0) : null);
        var document = RichTextDocument.FromParagraphs([Item("header"), .. Enumerable.Range(1, 5).Select(index => Text("body" + index)), Item("tail")]).WithTables([table]);
        var lines = Layout(document, 70).Pages.SelectMany(page => page.Lines).ToArray();
        Assert.All(lines.Where(line => line.ParagraphIndex == 0), line => Assert.Equal("1.", line.Pieces[0].Text));
        Assert.Equal("2.", Assert.Single(lines, line => line.ParagraphIndex == 6).Pieces[0].Text);
    }

    [Fact]
    public void Floating_Continuation_Keeps_Its_Sections_Paper_Before_The_Next_Section_Starts()
    {
        var landscape = new PageGeometry(200, 70, 10, 10, 10, 10);
        var portrait = new PageGeometry(100, 200, 10, 10, 10, 10);
        var table = new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0)], minHeight: 30, canSplit: false),
            new TableRow([new TableCell(1, 1, 0)], minHeight: 30, canSplit: false)], [100], cellPadding: 0,
            position: new TablePosition(TableAnchor.Margin, 0, TableAnchor.Margin, 0));
        var document = RichTextDocument.FromParagraphs([Text("first"), Text("second"), Text("new section")
            .WithParagraphStyle(Text("new section").Style with { PageBreakBefore = true })]).WithTables([table]).WithPageGeometry(portrait)
            .WithSections([new DocumentSection(0, 2, 1, 0, page: landscape), new DocumentSection(2, 1, 1, 0, page: portrait)]);
        using var images = new ImageStore();
        var pages = new DocumentLayout(new LayoutSettings(), images).Layout(document, new DocumentPageBox(100, 200, 10, 10, 10, 10)).Pages;
        Assert.Equal(new[] { 200d, 200, 100 }, pages.Select(page => page.WidthPoints));
        Assert.Equal(new[] { 70d, 70, 200 }, pages.Select(page => page.HeightPoints));
        Assert.Contains(pages[1].Lines.SelectMany(line => line.Pieces), piece => piece.Text == "second");
        Assert.Empty(pages[2].Cells);
    }

    [Fact]
    public void An_Oversized_Repeated_Header_Does_Not_Prevent_Body_Progress()
    {
        var table = new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0)], isHeader: true, minHeight: 45, canSplit: false),
            new TableRow([new TableCell(1, 1, 0)], minHeight: 30, canSplit: false)], [100], cellPadding: 0,
            position: new TablePosition(TableAnchor.Margin, 0, TableAnchor.Margin, 0));
        var pages = Layout(RichTextDocument.FromParagraphs([Text("header"), Text("body")]).WithTables([table]), 70).Pages;
        Assert.Equal(2, pages.Count);
        Assert.Contains(pages[1].Lines.SelectMany(line => line.Pieces), piece => piece.Text == "body");
    }
}
