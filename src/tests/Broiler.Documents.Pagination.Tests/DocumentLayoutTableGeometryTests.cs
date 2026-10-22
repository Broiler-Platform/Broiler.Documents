namespace Broiler.Documents.Pagination.Tests;

public sealed class DocumentLayoutTableGeometryTests
{
    private static RichTextParagraph Para(string text) => RichTextParagraph.Create(text, InlineStyle.Default,
        ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 10 });
    private static LayoutResult Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, new DocumentPageBox(200, 100, 10, 10, 10, 10));
    }
    private static RichTextDocument Document(double? preferred = null, double? percent = null,
        TableAlignment alignment = TableAlignment.Start, double indent = 0) =>
        RichTextDocument.FromParagraphs([Para("left"), Para("right")]).WithTables([
            new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])],
                [40, 60], cellPadding: 0, preferredWidth: preferred, widthPercent: percent,
                alignment: alignment, leftIndent: indent)]);

    [Theory]
    [InlineData(50, 20, 30)]
    [InlineData(150, 60, 90)]
    public void Preferred_width_scales_the_grid_in_both_directions(double width, double first, double second)
    {
        LayoutPage page = Assert.Single(Layout(Document(preferred: width)).Pages);
        Assert.Equal(first, page.Cells[0].Bounds.Width, 3);
        Assert.Equal(second, page.Cells[1].Bounds.Width, 3);
        Assert.Equal(10 + first, page.Lines[1].Pieces[0].X, 3);
    }

    [Fact]
    public void Percentage_width_uses_the_containing_text_area()
    {
        LayoutPage page = Assert.Single(Layout(Document(percent: 50)).Pages);
        Assert.Equal(90, page.Cells.Sum(cell => cell.Bounds.Width), 3);
    }

    [Fact]
    public void Unspecified_columns_share_only_the_space_left_inside_the_preferred_width()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs([Para("left"), Para("right")]).WithTables([
            new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])],
                [40, 0], preferredWidth: 100)]);
        LayoutPage page = Assert.Single(Layout(document).Pages);
        Assert.Equal(40, page.Cells[0].Bounds.Width, 3);
        Assert.Equal(60, page.Cells[1].Bounds.Width, 3);
    }

    [Theory]
    [InlineData(TableAlignment.Start, 10)]
    [InlineData(TableAlignment.Center, 50)]
    [InlineData(TableAlignment.End, 90)]
    public void A_narrow_table_aligns_inside_its_containing_area(TableAlignment alignment, double left)
    {
        LayoutPage page = Assert.Single(Layout(Document(alignment: alignment)).Pages);
        Assert.Equal(left, page.Cells[0].Bounds.Left, 3);
        Assert.Equal(left, page.Lines[0].Pieces[0].X, 3);
    }

    [Fact]
    public void Indentation_moves_the_table_and_its_contents()
    {
        LayoutPage page = Assert.Single(Layout(Document(indent: 18)).Pages);
        Assert.Equal(28, page.Cells[0].Bounds.Left, 3);
        Assert.Equal(28, page.Lines[0].Pieces[0].X, 3);
    }

    [Theory]
    [InlineData(CellVerticalAlignment.Top, 17)]
    [InlineData(CellVerticalAlignment.Center, 32)]
    [InlineData(CellVerticalAlignment.Bottom, 47)]
    public void Vertical_alignment_uses_spare_row_height_after_padding(CellVerticalAlignment alignment, double top)
    {
        var cell = new TableCell(0, 1, 0, padding: new TableCellPadding(0, 7, 0, 13), verticalAlignment: alignment);
        RichTextDocument document = RichTextDocument.FromParagraphs([Para("cell")])
            .WithTables([new DocumentTable(0, 1, [new TableRow([cell], minHeight: 60)], [180])]);
        LayoutPage page = Assert.Single(Layout(document).Pages);
        Assert.Equal(top, page.Lines[0].Top, 3);
        Assert.Equal(60, page.Cells[0].Bounds.Height, 3);
    }

    [Fact]
    public void Merged_cell_alignment_uses_the_combined_height_of_its_rows()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs([Para("span"), Para("other")]).WithTables([
            new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0, rowSpan: 2,
                verticalAlignment: CellVerticalAlignment.Center)], minHeight: 20),
                new TableRow([new TableCell(1, 1, 1)], minHeight: 40)], [90, 90])]);
        LayoutPage page = Assert.Single(Layout(document).Pages);
        Assert.Equal(35, page.Lines.Single(line => line.ParagraphIndex == 0).Top, 3);
    }

    [Fact]
    public void Repeated_headers_keep_the_same_preferred_width_and_alignment()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs([Para("head"), Para("a"), Para("b"), Para("c")])
            .WithTables([new DocumentTable(0, 4, [new TableRow([new TableCell(0, 1, 0)], isHeader: true),
                new TableRow([new TableCell(1, 1, 0)], minHeight: 40),
                new TableRow([new TableCell(2, 1, 0)], minHeight: 40),
                new TableRow([new TableCell(3, 1, 0)], minHeight: 40)], [180],
                preferredWidth: 80, alignment: TableAlignment.Center)]);
        LayoutResult result = Layout(document);
        Assert.True(result.Pages.Count > 1);
        Assert.All(result.Pages.SelectMany(page => page.Cells), cell =>
        {
            Assert.Equal(60, cell.Bounds.Left, 3);
            Assert.Equal(80, cell.Bounds.Width, 3);
        });
    }
}
