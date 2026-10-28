namespace Broiler.Documents.Pagination.Tests;

public sealed class DocumentLayoutAutoTableTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(80.0)]
    public void Auto_Table_Allocates_More_Space_To_Long_Content(double? percent)
    {
        var document = RichTextDocument.FromParagraphs([
            RichTextParagraph.Create("a longer cell value", InlineStyle.Default),
            RichTextParagraph.Create("3", InlineStyle.Default)])
            .WithTables([new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])],
                autoFit: true, widthPercent: percent)]);
        using var images = new ImageStore();
        var page = Assert.Single(new DocumentLayout(new LayoutSettings(), images)
            .Layout(document, new DocumentPageBox(600, 800, 20, 20, 20, 20)).Pages);
        Assert.Equal(2, page.Cells.Count);
        Assert.True(page.Cells[0].Bounds.Width > page.Cells[1].Bounds.Width + 20);
        double total = page.Cells.Sum(cell => cell.Bounds.Width);
        if (percent is double stated)
            Assert.Equal(560 * stated / 100, total, 5);
        else
            Assert.True(total < 560);
    }

    [Fact]
    public void Auto_Table_Intermediate_Regime_Absorbs_Compression_In_Flexible_Column()
    {
        // Col 0: single short number (rigid: min == max)
        // Col 1: long sentence with several words (flexible: min < max)
        var document = RichTextDocument.FromParagraphs([
            RichTextParagraph.Create("123", InlineStyle.Default),
            RichTextParagraph.Create("This is a long sentence with multiple words for wrapping", InlineStyle.Default)])
            .WithTables([new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])],
                autoFit: true, preferredWidth: 200)]);

        using var images = new ImageStore();
        var page = Assert.Single(new DocumentLayout(new LayoutSettings(), images)
            .Layout(document, new DocumentPageBox(600, 800, 20, 20, 20, 20)).Pages);

        Assert.Equal(2, page.Cells.Count);
        double total = page.Cells.Sum(c => c.Bounds.Width);
        Assert.Equal(200, total, 3);
        // The rigid column should be sized close to its natural preferred width (padding + text ~ 30-40pt)
        Assert.True(page.Cells[0].Bounds.Width < 60);
        // Flexible column absorbs the remaining space
        Assert.True(page.Cells[1].Bounds.Width > 140);
    }

    [Fact]
    public void Auto_Table_Underflow_Expands_Columns_Proportionally()
    {
        // 560 pt available width with widthPercent: 100.
        // Narrow column vs wide column. Narrow column should expand proportionally, not equally.
        var document = RichTextDocument.FromParagraphs([
            RichTextParagraph.Create("Short", InlineStyle.Default),
            RichTextParagraph.Create("A much longer multi-word sentence providing a wider base preferred extent", InlineStyle.Default)])
            .WithTables([new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])],
                autoFit: true, widthPercent: 100)]);

        using var images = new ImageStore();
        var page = Assert.Single(new DocumentLayout(new LayoutSettings(), images)
            .Layout(document, new DocumentPageBox(600, 800, 20, 20, 20, 20)).Pages);

        Assert.Equal(2, page.Cells.Count);
        double total = page.Cells.Sum(c => c.Bounds.Width);
        Assert.Equal(560, total, 4);
        // Wide column should have significantly more than half the table width
        Assert.True(page.Cells[1].Bounds.Width > page.Cells[0].Bounds.Width * 3);
    }

    [Fact]
    public void Auto_Table_Spanning_Cell_Distributes_Deficit_To_Columns()
    {
        // Row 0: ColSpan 2 with long title
        // Row 1: Col 0 ("A"), Col 1 ("B")
        var document = RichTextDocument.FromParagraphs([
            RichTextParagraph.Create("A Very Long Title That Spans Two Columns Easily", InlineStyle.Default),
            RichTextParagraph.Create("A", InlineStyle.Default),
            RichTextParagraph.Create("B", InlineStyle.Default)])
            .WithTables([new DocumentTable(0, 3, [
                new TableRow([new TableCell(0, 1, 0, columnSpan: 2)]),
                new TableRow([new TableCell(1, 1, 0), new TableCell(2, 1, 1)])
            ], autoFit: true)]);

        using var images = new ImageStore();
        var page = Assert.Single(new DocumentLayout(new LayoutSettings(), images)
            .Layout(document, new DocumentPageBox(600, 800, 20, 20, 20, 20)).Pages);

        // 3 cells: row 0 span cell, row 1 col 0, row 1 col 1
        Assert.Equal(3, page.Cells.Count);
        double spanWidth = page.Cells[0].Bounds.Width;
        double col0Width = page.Cells[1].Bounds.Width;
        double col1Width = page.Cells[2].Bounds.Width;

        // Spanning cell width should equal the sum of the two columns
        Assert.Equal(spanWidth, col0Width + col1Width, 4);
        // The total table width should accommodate the long title
        Assert.True(spanWidth > 150);
    }

    [Fact]
    public void Auto_Table_Cell_With_Nested_Table_Allocates_Required_Width()
    {
        // Outer table has 2 columns:
        // Col 0 holds a nested table with 2 columns of 50pt each (100pt total)
        // Col 1 holds a short string "Side"
        var nestedTable = new DocumentTable(0, 2, [
            new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])
        ], columnWidths: [50, 50]);

        var document = RichTextDocument.FromParagraphs([
            RichTextParagraph.Create("Inner 1", InlineStyle.Default),
            RichTextParagraph.Create("Inner 2", InlineStyle.Default),
            RichTextParagraph.Create("Side text", InlineStyle.Default)])
            .WithTables([
                new DocumentTable(0, 3, [
                    new TableRow([
                        new TableCell(0, 2, 0, tables: [nestedTable]),
                        new TableCell(2, 1, 1)
                    ])
                ], autoFit: true)
            ]);

        using var images = new ImageStore();
        var page = Assert.Single(new DocumentLayout(new LayoutSettings(), images)
            .Layout(document, new DocumentPageBox(600, 800, 20, 20, 20, 20)).Pages);

        // Outer cell at left = 20.
        // It must be wide enough to contain the 100pt nested table + padding.
        var outerCol0 = page.Cells.First(c => Math.Abs(c.Bounds.Left - 20) < 0.1);
        Assert.True(outerCol0.Bounds.Width >= 100);
    }

    [Fact]
    public void Auto_Table_Honors_Column_Width_Hints()
    {
        // An auto table with an explicit column width hint of 200 on column 0
        var document = RichTextDocument.FromParagraphs([
            RichTextParagraph.Create("Small", InlineStyle.Default),
            RichTextParagraph.Create("Also small", InlineStyle.Default)])
            .WithTables([new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])],
                columnWidths: [200, 0], autoFit: true)]);

        using var images = new ImageStore();
        var page = Assert.Single(new DocumentLayout(new LayoutSettings(), images)
            .Layout(document, new DocumentPageBox(600, 800, 20, 20, 20, 20)).Pages);

        Assert.Equal(2, page.Cells.Count);
        // Column 0 should be at least ~200 pt due to the explicit hint
        Assert.True(page.Cells[0].Bounds.Width >= 199);
    }

    [Fact]
    public void Auto_Table_Aligns_Content_Hugging_Table()
    {
        // Auto table without explicit width hugs content (< 560 available).
        // Centered table should have positive left offset.
        var document = RichTextDocument.FromParagraphs([
            RichTextParagraph.Create("A", InlineStyle.Default),
            RichTextParagraph.Create("B", InlineStyle.Default)])
            .WithTables([new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])],
                autoFit: true, alignment: TableAlignment.Center)]);

        using var images = new ImageStore();
        var page = Assert.Single(new DocumentLayout(new LayoutSettings(), images)
            .Layout(document, new DocumentPageBox(600, 800, 20, 20, 20, 20)).Pages);

        Assert.Equal(2, page.Cells.Count);
        double tableLeft = page.Cells[0].Bounds.Left;
        // Page margin is 20. If centered, tableLeft should be substantially > 20.
        Assert.True(tableLeft > 100);
    }
}
