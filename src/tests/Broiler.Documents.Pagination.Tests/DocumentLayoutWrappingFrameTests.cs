namespace Broiler.Documents.Pagination.Tests;

public sealed class DocumentLayoutWrappingFrameTests
{
    private static RichTextParagraph Paragraph(string text) => RichTextParagraph.Create(text,
        InlineStyle.Default, ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 10 });

    [Fact]
    public void A_Wrapping_Frame_Moves_With_Its_Anchor_When_It_Does_Not_Fit()
    {
        var document = RichTextDocument.FromParagraphs([Paragraph("one\u2028two\u2028three"), Paragraph("after")])
            .WithShapes([new DocumentShape(1, 0, 0, 100, 60, wrap: ShapeWrap.TopAndBottom)]);
        using var images = new ImageStore();
        var pages = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10)).Pages;
        Assert.Equal(2, pages.Count);
        Assert.Empty(pages[0].Shapes);
        Assert.Single(pages[1].Shapes);
        Assert.Equal(70, pages[1].Lines[^1].Top, 3);
    }

    [Fact]
    public void An_Oversized_Frame_Keeps_Its_Page_And_The_Anchor_Text_Continues()
    {
        var document = RichTextDocument.FromParagraphs([Paragraph("before"), Paragraph("after")])
            .WithShapes([new DocumentShape(1, 0, 0, 100, 85, wrap: ShapeWrap.TopAndBottom)]);
        using var images = new ImageStore();
        var pages = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10)).Pages;
        Assert.Equal(3, pages.Count);
        Assert.Empty(pages[0].Shapes);
        Assert.Single(pages[1].Shapes);
        Assert.Empty(pages[2].Shapes);
        Assert.Equal(10, Assert.Single(pages[2].Lines).Top, 3);
    }

    [Fact]
    public void A_Run_Through_Frame_Does_Not_Reserve_Body_Space()
    {
        var document = RichTextDocument.FromParagraphs([Paragraph("before"), Paragraph("after")])
            .WithShapes([new DocumentShape(1, 0, 0, 100, 100)]);
        using var images = new ImageStore();
        var pages = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10)).Pages;
        Assert.Single(pages);
        Assert.Single(pages[0].Shapes);
        Assert.Equal(2, pages[0].Lines.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Text_Box_Tables_Keep_Their_Cell_Grid(bool grows)
    {
        var table = new DocumentTable(0, 2, [new TableRow([
            new TableCell(0, 1, 0), new TableCell(1, 1, 1)])], [40, 60], cellPadding: 0);
        var document = RichTextDocument.FromPlainText("body").WithShapes([
            new DocumentShape(0, 5, 10, 100, grows ? 1 : 80,
                paragraphs: [Paragraph("A"), Paragraph("B")], growsToText: grows, tables: [table])]);
        using var images = new ImageStore();
        var page = Assert.Single(new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 200, 10, 10, 10, 10)).Pages);
        var shape = Assert.Single(page.Shapes);
        Assert.Equal(2, shape.Cells.Count);
        Assert.Equal(2, shape.Lines.Count);
        Assert.Equal(40, shape.Lines[1].Pieces[0].X - shape.Lines[0].Pieces[0].X, 3);
        Assert.Equal(shape.Lines[0].Top, shape.Lines[1].Top, 3);
        Assert.True(shape.Bounds.Height >= 10);
    }
}
