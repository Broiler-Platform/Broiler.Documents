using Broiler.Graphics.Color;

namespace Broiler.Documents.Pagination.Tests;

public sealed class DocumentLayoutSectionBackgroundTests
{
    [Fact]
    public void A_Section_Paints_The_Text_Area_Across_Pages_And_Stops_At_Its_End()
    {
        var paragraph = RichTextParagraph.Create(string.Join('\u2028', Enumerable.Repeat("line", 12)),
            InlineStyle.Default, ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 10 });
        var document = RichTextDocument.FromParagraphs([paragraph, RichTextParagraph.Plain("after")])
            .WithSections([new DocumentSection(0, 1, 1, 0, background: BColor.FromArgb(0x81, 0xd4, 0x1a))]);
        using var images = new ImageStore();
        var pages = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10)).Pages;
        Assert.Equal(2, pages.Count);
        var first = Assert.Single(pages[0].Shapes);
        Assert.Equal(10, first.Bounds.Y, 3);
        Assert.Equal(80, first.Bounds.Height, 3);
        Assert.Equal(180, first.Bounds.Width, 3);
        var second = Assert.Single(pages[1].Shapes);
        Assert.Equal(40, second.Bounds.Height, 3);
        Assert.True(second.Bounds.Bottom <= pages[1].Lines[^1].Top);
    }
}
