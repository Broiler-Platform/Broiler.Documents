using Broiler.Graphics.Color;

namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Office conformance issue 240, ODT pictures: a picture anchored in a table
/// cell that leaves its paragraph's line no room beside it moves the line below
/// it, and the cell grows to hold the line there (tdf132911.odt, tdf135061.odt).
/// </summary>
public sealed class DocumentLayoutIssue240OdtImagesTests
{
    private static RichTextParagraph Para(string text) => RichTextParagraph.Create(text, InlineStyle.Default,
        ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 10 });

    private static LayoutResult Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 300, 10, 10, 10, 10));
    }

    private static RichTextDocument CellWithPicture(double offsetX, double width, double minimumRoom) =>
        RichTextDocument.FromParagraphs([Para(""), Para("next")])
            .WithTables([new DocumentTable(0, 2, [new TableRow([new TableCell(0, 2, 0)])], [100], cellPadding: 0)])
            .WithShapes([new DocumentShape(0, offsetX, 2, width, 50, fill: ShapeFill.Solid(BColor.Red),
                wrap: ShapeWrap.Square, wrapSide: WrapSide.Largest, offsetXFromCell: true)
            {
                MinimumWrapRoom = minimumRoom,
            }]);

    [Fact]
    public void A_Dynamic_Wrap_Picture_Leaving_Too_Little_Room_Moves_Its_Line_Below_It()
    {
        // 20 pt to the left and 10 pt to the right: less than the 2 cm Writer
        // needs beside a dynamically wrapped object.
        LayoutPage page = Assert.Single(Layout(CellWithPicture(20, 70, 2 * 72 / 2.54)).Pages);

        LayoutShape shape = Assert.Single(page.Shapes);
        Assert.Equal(12, shape.Bounds.Top, 3);
        Assert.Equal(62, page.Lines.Single(line => line.ParagraphIndex == 0).Top, 3);
        Assert.Equal(72, page.Lines.Single(line => line.ParagraphIndex == 1).Top, 3);
        Assert.Equal(72, Assert.Single(page.Cells).Bounds.Height, 3);
    }

    [Fact]
    public void A_Picture_With_Room_Beside_It_Leaves_Its_Line_In_Place()
    {
        LayoutPage page = Assert.Single(Layout(CellWithPicture(0, 30, 2 * 72 / 2.54)).Pages);

        Assert.Equal(10, page.Lines.Single(line => line.ParagraphIndex == 0).Top, 3);
        Assert.Equal(62, page.Lines.Single(line => line.ParagraphIndex == 1).Top, 3);
        Assert.Equal(12, Assert.Single(page.Shapes).Bounds.Top, 3);
    }

    [Fact]
    public void Stacked_Pictures_Move_The_Line_Past_Both()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs([Para("")])
            .WithTables([new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0)])], [100], cellPadding: 0)])
            .WithShapes([
                new DocumentShape(0, 0, 0, 90, 40, fill: ShapeFill.Solid(BColor.Red), wrap: ShapeWrap.TopAndBottom,
                    offsetXFromCell: true),
                new DocumentShape(0, 0, 45, 90, 40, fill: ShapeFill.Solid(BColor.Red), wrap: ShapeWrap.TopAndBottom,
                    offsetXFromCell: true)]);

        LayoutPage page = Assert.Single(Layout(document).Pages);

        // The line does not fit in the 5 pt gap between them.
        Assert.Equal(95, Assert.Single(page.Lines).Top, 3);
        Assert.Equal(95, Assert.Single(page.Cells).Bounds.Height, 3);
        Assert.Equal([10.0, 55.0], page.Shapes.Select(shape => shape.Bounds.Top).Order());
    }
}
