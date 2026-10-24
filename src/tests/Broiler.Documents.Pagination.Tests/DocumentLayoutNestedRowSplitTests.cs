namespace Broiler.Documents.Pagination.Tests;

public sealed class DocumentLayoutNestedRowSplitTests
{
    [Fact]
    public void A_Nested_Row_Can_Continue_Inside_Its_Outer_Row()
    {
        var result = Layout(Nested());
        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(14, result.Pages.Sum(page => page.Lines.Count));
        Assert.All(result.Pages.SelectMany(page => page.Lines), line => Assert.True(line.Top + line.Height <= 90.001));
        Assert.All(result.Pages, page => Assert.Equal(2, page.Cells.Count));
        Assert.Equal(Enumerable.Range(0, 14), result.Pages.SelectMany(page => page.Lines).Select(line => line.ParagraphIndex));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Either_Rows_Explicit_Keep_Prevents_A_Nested_Cut(bool outerSplit, bool innerSplit)
    {
        var page = Assert.Single(Layout(Nested(outerSplit, innerSplit)).Pages);
        Assert.Equal(14, page.Lines.Count);
    }

    [Fact]
    public void Different_Cell_Line_Heights_Never_Cut_Through_A_Line()
    {
        var paragraphs = Enumerable.Range(0, 24).Select(i => Para(i, i < 14 ? 10 : 15)).ToArray();
        var inner = new DocumentTable(0, 24, [new TableRow([new TableCell(0, 14, 0), new TableCell(14, 10, 1)])],
            [90, 90], cellPadding: 0);
        var document = RichTextDocument.FromParagraphs(paragraphs).WithTables([
            new DocumentTable(0, 24, [new TableRow([new TableCell(0, 24, 0, tables: [inner])])], [180], cellPadding: 0)]);
        var result = Layout(document);
        Assert.True(result.Pages.Count > 1);
        Assert.Equal(Enumerable.Range(0, 24), result.Pages.SelectMany(page => page.Lines)
            .Select(line => line.ParagraphIndex).OrderBy(index => index));
        Assert.All(result.Pages.SelectMany(page => page.Lines), line => Assert.True(line.Top + line.Height <= 90.001));
    }

    private static RichTextDocument Nested(bool outerSplit = true, bool innerSplit = true)
    {
        var paragraphs = Enumerable.Range(0, 14).Select(i => Para(i, 10));
        var inner = new DocumentTable(0, 14, [new TableRow([new TableCell(0, 14, 0)], canSplit: innerSplit)],
            [180], cellPadding: 0);
        return RichTextDocument.FromParagraphs(paragraphs).WithTables([
            new DocumentTable(0, 14, [new TableRow([new TableCell(0, 14, 0, tables: [inner])], canSplit: outerSplit)],
                [180], cellPadding: 0)]);
    }

    private static RichTextParagraph Para(int i, float height) => RichTextParagraph.Create("line " + i, InlineStyle.Default,
        ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = height });

    private static LayoutResult Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, new DocumentPageBox(200, 100, 10, 10, 10, 10));
    }
}
