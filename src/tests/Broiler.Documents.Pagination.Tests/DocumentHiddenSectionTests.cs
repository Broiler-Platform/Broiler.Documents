namespace Broiler.Documents.Pagination.Tests;

public sealed class DocumentHiddenSectionTests
{
    [Fact]
    public void A_Hidden_Section_Takes_No_Pages_And_Does_Not_Draw_Its_Objects()
    {
        var paragraphs = new[] { RichTextParagraph.Plain("before") }
            .Concat(Enumerable.Range(0, 20).Select(_ => RichTextParagraph.Create("hidden", InlineStyle.Default,
                ParagraphStyle.Default with { PageBreakBefore = true })))
            .Append(RichTextParagraph.Plain("after"));
        var document = RichTextDocument.FromParagraphs(paragraphs)
            .WithSections([new DocumentSection(1, 20, 1, 0, hidden: true)])
            .WithShapes([new DocumentShape(1, 0, 0, 10, 10, anchorPage: 30)])
            .WithTables([new DocumentTable(1, 20, [new TableRow([new TableCell(1, 20, 0)])])]);
        using var images = new ImageStore();
        var page = Assert.Single(new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10)).Pages);
        Assert.Equal(new[] { "before", "after" }, page.Lines.Select(line => string.Concat(line.Pieces.Select(piece => piece.Text))));
        Assert.Empty(page.Cells);
        Assert.Empty(page.Shapes);
        Assert.Contains("hidden", document.PlainText);
    }

    [Fact]
    public void Hidden_Content_In_One_Cell_Does_Not_Hide_The_Other_Cell()
    {
        var document = RichTextDocument.FromPlainText("hidden\nvisible")
            .WithSections([new DocumentSection(0, 1, 1, 0, hidden: true)])
            .WithTables([new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])])]);
        using var images = new ImageStore();
        var page = Assert.Single(new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10)).Pages);
        Assert.Equal("visible", string.Concat(Assert.Single(page.Lines).Pieces.Select(piece => piece.Text)));
        Assert.Equal(2, page.Cells.Count);
    }
}
