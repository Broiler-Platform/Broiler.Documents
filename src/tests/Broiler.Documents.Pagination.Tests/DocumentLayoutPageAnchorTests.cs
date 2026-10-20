using Broiler.Graphics.Color;

namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// A shape anchored to a page by number stands on that page, and the layout makes
/// the pages it needs to have one.
/// </summary>
public sealed class DocumentLayoutPageAnchorTests
{
    /// <summary>US Letter with one-inch margins.</summary>
    private static readonly DocumentPageBox Letter = new(612, 792, 72, 72, 72, 72);

    private static readonly ShapeFill Red = ShapeFill.Solid(BColor.FromArgb(0xFF, 0xCC, 0, 0));

    private static LayoutResult Layout(RichTextDocument document, DocumentPageBox? box = null, int maxPages = 200)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings { MaxPages = maxPages }, images).Layout(document, box ?? Letter);
    }

    private static RichTextDocument Body(int lines, params DocumentShape[] shapes) =>
        RichTextDocument.FromParagraphs(Enumerable.Range(0, lines).Select(i => RichTextParagraph.Plain("Line " + i)))
            .WithShapes(shapes);

    [Fact]
    public void A_Shape_On_A_Page_The_Text_Never_Reaches_Gets_That_Page()
    {
        LayoutResult result = Layout(Body(3, new DocumentShape(0, 36, 100, 50, 20, Red, anchorPage: 4)));

        Assert.Equal(4, result.Pages.Count);
        Assert.Empty(result.Pages[1].Lines);
        Assert.Empty(result.Pages[2].Lines);
        LayoutShape shape = Assert.Single(result.Pages[3].Shapes);

        // Across from the text column, down from the page's top edge.
        Assert.Equal(72 + 36, shape.Bounds.Left, 3);
        Assert.Equal(100, shape.Bounds.Top, 3);
        Assert.Empty(result.Pages[0].Shapes);
    }

    [Fact]
    public void A_Shape_On_The_First_Page_Is_Not_Drawn_Beside_Its_Paragraph()
    {
        // The paragraph it was read beside is on page two; the shape is not.
        int perPage = Layout(Body(400)).Pages[0].Lines.Count;
        LayoutResult result = Layout(Body(perPage + 5, new DocumentShape(perPage + 2, 0, 10, 50, 20, Red, anchorPage: 1)));

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(10, Assert.Single(result.Pages[0].Shapes).Bounds.Top, 3);
        Assert.Empty(result.Pages[1].Shapes);
    }

    [Fact]
    public void A_Paragraph_Shape_Is_Not_Drawn_Again_On_The_Pages_Made_For_A_Page_Anchor()
    {
        LayoutResult result = Layout(Body(
            3,
            new DocumentShape(1, 0, 0, 50, 20, Red),
            new DocumentShape(0, 0, 0, 50, 20, Red, anchorPage: 3)));

        Assert.Single(result.Pages[0].Shapes);
        Assert.Empty(result.Pages[1].Shapes);
        Assert.Single(result.Pages[2].Shapes);
    }

    [Fact]
    public void A_Wrapping_Shape_On_A_Page_Pushes_Only_That_Pages_Text()
    {
        // A band across the whole column, one inch below the top margin: text on
        // its page goes below it, and text on the page before does not move.
        var band = new DocumentShape(0, -10, 72 + 72, 500, 100, Red, wrap: ShapeWrap.TopAndBottom, anchorPage: 2);
        int perPage = Layout(Body(400)).Pages[0].Lines.Count;
        LayoutResult plain = Layout(Body(perPage + 20));
        LayoutResult wrapped = Layout(Body(perPage + 20, band));

        Assert.Equal(perPage, wrapped.Pages[0].Lines.Count);
        LayoutLine first = wrapped.Pages[1].Lines[0];
        LayoutLine below = wrapped.Pages[1].Lines.First(line => line.Top >= 144);
        Assert.Equal(plain.Pages[1].Lines[0].Top, first.Top, 3);
        Assert.True(below.Top >= 144 + 100, $"line at {below.Top} should clear the band ending at 244");
        Assert.DoesNotContain(wrapped.Pages[1].Lines, line => line.Top + line.Height > 144 && line.Top < 244);
    }

    [Fact]
    public void The_Pages_It_Makes_Stop_At_The_Page_Limit()
    {
        LayoutResult result = Layout(Body(1, new DocumentShape(0, 0, 0, 50, 20, Red, anchorPage: 50)), maxPages: 5);

        Assert.Equal(5, result.Pages.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void A_Continuous_Render_Says_What_It_Left_Out()
    {
        var continuous = new DocumentPageBox(612, 792, 72, 72, 72, 72, continuous: true);

        LayoutResult result = Layout(Body(3, new DocumentShape(0, 0, 0, 50, 20, Red, anchorPage: 3)), continuous);

        Assert.Single(result.Pages);
        Assert.Empty(result.Pages[0].Shapes);
        Assert.Contains(result.Notes, note => note.Contains("anchored to a page", StringComparison.Ordinal));
    }
}
