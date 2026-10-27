using Broiler.Graphics.Color;

namespace Broiler.Documents.Pagination.Tests;

public sealed class DocumentLayoutCellShapeTests
{
    [Theory]
    [InlineData(TextAlignment.Left, 92)]
    [InlineData(TextAlignment.Center, 120)]
    [InlineData(TextAlignment.Right, 148)]
    public void A_Cell_Object_Uses_The_Padded_Cell_Width_For_Horizontal_Alignment(TextAlignment alignment, double left)
    {
        var paragraph = Para("").WithParagraphStyle(Para("").Style with { LeftIndent = 11 });
        var document = RichTextDocument.FromParagraphs([Para("left"), paragraph])
            .WithTables([new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0),
                new TableCell(1, 1, 1, padding: new TableCellPadding(2, 0, 2, 0))])], [80, 80], cellPadding: 0)])
            .WithShapes([new DocumentShape(1, 500, 0, 20, 10, fill: ShapeFill.Solid(BColor.Red),
                offsetXFromCell: true, cellHorizontalAlignment: alignment)]);

        var shape = Assert.Single(Assert.Single(Layout(document).Pages).Shapes);
        Assert.Equal(left, shape.Bounds.Left, 3);
        Assert.Equal(20, shape.Bounds.Width, 3);
    }

    private static RichTextParagraph Para(string text) => RichTextParagraph.Create(text, InlineStyle.Default,
        ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 10 });

    private static LayoutResult Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10));
    }

    [Theory]
    [InlineData(ShapeWrap.TopAndBottom, 70)]
    [InlineData(ShapeWrap.Square, 70)]
    [InlineData(ShapeWrap.None, 20)]
    public void A_Wrapping_Cell_Object_Leaves_Room_For_Its_Caption(ShapeWrap wrap, double height)
    {
        var document = RichTextDocument.FromParagraphs([Para(""), Para("caption")])
            .WithTables([new DocumentTable(0, 2, [new TableRow([new TableCell(0, 2, 0)])], [100], cellPadding: 0)])
            .WithShapes([new DocumentShape(0, 0, 5, 40, 55, fill: ShapeFill.Solid(BColor.Red),
                wrap: wrap, offsetXFromCell: true)]);

        var page = Assert.Single(Layout(document).Pages);
        Assert.Equal(height, Assert.Single(page.Cells).Bounds.Height, 3);
        Assert.Equal(height, page.Lines.Single(line => line.ParagraphIndex == 1).Top, 3);
    }

    [Fact]
    public void A_Cell_Object_Moves_With_Its_Anchor_And_Stands_In_Its_Own_Column()
    {
        var document = RichTextDocument.FromParagraphs([Para(string.Join("\u2028", Enumerable.Repeat("body", 5))),
                Para("left"), Para(""), Para("caption")])
            .WithTables([new DocumentTable(1, 3, [new TableRow([new TableCell(1, 1, 0), new TableCell(2, 2, 1)])],
                [80, 80], cellPadding: 0)])
            .WithShapes([new DocumentShape(2, 3, 0, 40, 50, fill: ShapeFill.Solid(BColor.Red),
                wrap: ShapeWrap.TopAndBottom, offsetXFromCell: true)]);

        var result = Layout(document);
        Assert.Equal(2, result.Pages.Count);
        Assert.Empty(result.Pages[0].Shapes);
        var shape = Assert.Single(result.Pages[1].Shapes);
        Assert.Equal(93, shape.Bounds.Left, 3);
        Assert.Equal(10, shape.Bounds.Top, 3);
        Assert.Equal(60, result.Pages[1].Lines.Single(line => line.ParagraphIndex == 3).Top, 3);
    }

    [Fact]
    public void A_Page_Relative_Object_Does_Not_Stretch_Its_Anchoring_Cell()
    {
        var document = RichTextDocument.FromParagraphs([Para("")])
            .WithTables([new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0)])], [100], cellPadding: 0)])
            .WithShapes([new DocumentShape(0, 0, 0, 40, 60, fill: ShapeFill.Solid(BColor.Red),
                wrap: ShapeWrap.TopAndBottom, offsetFromPage: true, offsetXFromCell: true)]);

        Assert.Equal(10, Assert.Single(Assert.Single(Layout(document).Pages).Cells).Bounds.Height, 3);
    }
}
