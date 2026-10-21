using Broiler.Graphics.Color;

namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// A box sized by its text, and a box with no breadth. A frame that leaves its
/// height to its lines - RTF's <c>\absh0</c>, ODF's <c>fo:min-height</c> - was
/// drawn at the height it stated, which clipped its text; a line was a box of
/// nought and was not drawn at all.
/// </summary>
public sealed class DocumentLayoutShapeSizeTests
{
    private static readonly DocumentPageBox Letter = new(612, 792, 72, 72, 72, 72);

    private static LayoutResult Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, Letter);
    }

    private static RichTextDocument WithShape(DocumentShape shape) =>
        RichTextDocument.FromPlainText("body").WithShapes([shape]);

    private static RichTextParagraph[] Lines(int count) =>
        [.. Enumerable.Range(0, count).Select(i => RichTextParagraph.Plain("Line " + i))];

    [Fact]
    public void A_Box_That_Grows_To_Its_Text_Draws_All_Of_It()
    {
        LayoutShape shape = Assert.Single(Layout(WithShape(new DocumentShape(
            0, 0, 100, 200, 0, paragraphs: Lines(4), offsetFromPage: true, growsToText: true))).Pages[0].Shapes);

        Assert.Equal(4, shape.Lines.Count);
        Assert.True(shape.Bounds.Height >= shape.Lines.Sum(line => line.Height) - 0.01);
        Assert.Equal(100, shape.Bounds.Top, 3);
    }

    [Fact]
    public void A_Box_That_Grows_Keeps_Its_Stated_Height_As_The_Least()
    {
        LayoutShape shape = Assert.Single(Layout(WithShape(new DocumentShape(
            0, 0, 100, 200, 300, paragraphs: Lines(1), growsToText: true))).Pages[0].Shapes);

        Assert.Equal(300, shape.Bounds.Height, 3);
    }

    [Fact]
    public void A_Box_Of_Fixed_Height_Still_Clips_Its_Text()
    {
        LayoutShape grown = Assert.Single(Layout(WithShape(new DocumentShape(
            0, 0, 100, 200, 0, paragraphs: Lines(4), growsToText: true))).Pages[0].Shapes);
        LayoutShape fixedBox = Assert.Single(Layout(WithShape(new DocumentShape(
            0, 0, 100, 200, grown.Lines[0].Height * 2, paragraphs: Lines(4)))).Pages[0].Shapes);

        Assert.Equal(2, fixedBox.Lines.Count);
    }

    [Fact]
    public void Text_Wraps_Around_The_Height_A_Box_Grew_To()
    {
        // A box at the column's left edge, two hundred points wide, that its
        // text makes taller than the one line of body text beside its top.
        RichTextDocument document = RichTextDocument.FromParagraphs(Lines(30)).WithShapes(
        [
            new DocumentShape(0, 0, 0, 200, 1, paragraphs: Lines(6), wrap: ShapeWrap.Square,
                wrapSide: WrapSide.Right, growsToText: true),
        ]);

        LayoutPage page = Layout(document).Pages[0];
        LayoutShape shape = Assert.Single(page.Shapes);
        LayoutLine below = page.Lines.First(line => line.Top >= shape.Bounds.Bottom);
        LayoutLine beside = page.Lines.Last(line => line.Top < shape.Bounds.Bottom - 1);

        Assert.True(beside.Pieces[0].X >= shape.Bounds.Right - 0.01);
        Assert.Equal(Letter.ContentLeftPoints, below.Pieces[0].X, 3);
    }

    [Theory]
    [InlineData(0, 120)]
    [InlineData(120, 0)]
    public void A_Box_With_No_Breadth_And_An_Outline_Is_Drawn_As_A_Rule(double width, double height)
    {
        LayoutShape rule = Assert.Single(Layout(WithShape(new DocumentShape(
            0, 10, 20, width, height, outline: BColor.Black))).Pages[0].Shapes);

        Assert.Equal(width, rule.Bounds.Width, 3);
        Assert.Equal(height, rule.Bounds.Height, 3);
    }

    [Fact]
    public void A_Box_With_No_Breadth_And_No_Outline_Draws_Nothing()
    {
        Assert.Empty(Layout(WithShape(new DocumentShape(0, 10, 20, 0, 120))).Pages[0].Shapes);
    }
}
