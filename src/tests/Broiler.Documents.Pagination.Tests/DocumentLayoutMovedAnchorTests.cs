using Broiler.Graphics.Color;

namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// A shape anchored to a paragraph goes where the paragraph goes: when the
/// paragraph's first line does not fit and it moves to the next page, the shape
/// is drawn there, from the paragraph's new top, and not on the page it left.
/// </summary>
/// <remarks>
/// <c>tdf123351_UnderlineGroupSapeText.docx</c>: an empty paragraph at the foot
/// of the page holds a group shape; LibreOffice draws the group at the top of the
/// second page, where the paragraph went.
/// </remarks>
public sealed class DocumentLayoutMovedAnchorTests
{
    private static readonly ShapeFill Red = ShapeFill.Solid(BColor.FromArgb(0xFF, 0xCC, 0, 0));

    private static ParagraphStyle Style => ParagraphStyle.Default with
    { LineHeightRule = LineHeightRule.Exact, LineHeight = 10 };

    private static RichTextParagraph Para(int lines, ParagraphStyle? style = null) =>
        RichTextParagraph.Create(string.Join("\u2028", Enumerable.Repeat("x", lines)), InlineStyle.Default, style ?? Style);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_shape_moves_with_the_paragraph_that_moved_to_the_next_page(bool orphanControl)
    {
        ParagraphStyle anchorStyle = orphanControl ? Style with { OrphanLines = 2, WidowLines = 2 } : Style;
        var document = RichTextDocument.FromParagraphs([Para(8), Para(2, anchorStyle)])
            .WithShapes([new DocumentShape(1, 20, 5, 40, 30, Red, behindText: false)]);

        using var images = new ImageStore();
        LayoutResult result = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10));

        Assert.Equal(2, result.Pages.Count);
        Assert.Empty(result.Pages[0].Shapes);
        LayoutShape shape = Assert.Single(result.Pages[1].Shapes);
        Assert.Equal(10 + 5, shape.Bounds.Top, 3);
    }
}
