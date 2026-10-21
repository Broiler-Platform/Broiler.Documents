using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;

namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// A shaded paragraph is painted as LibreOffice paints it, measured: one box
/// over its lines, from its indent to its column's edge, its space before and
/// after left white - unless the next paragraph is shaded the same, when the
/// space between them is shaded too.
/// </summary>
public sealed class ParagraphShadingTests
{
    /// <summary>US Letter with one-inch margins.</summary>
    private static readonly DocumentPageBox Letter = new(612, 792, 72, 72, 72, 72);

    private static readonly BColor Red = BColor.FromArgb(255, 0, 0);
    private static readonly BColor Blue = BColor.FromArgb(0, 0, 255);

    private static LayoutPage Layout(params RichTextParagraph[] paragraphs)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images)
            .Layout(RichTextDocument.FromParagraphs(paragraphs), Letter).Pages[0];
    }

    private static List<BRenderCommand.FillRect> Fills(LayoutPage page)
    {
        using var images = new ImageStore();
        var list = new BRenderList();
        new DocumentPagePainter(new LayoutSettings(), images).Paint(page, Letter, list, _ => null);
        return [.. list.Commands.OfType<BRenderCommand.FillRect>()];
    }

    private static RichTextParagraph Shaded(string text, BColor color, float before = 0, float after = 0, int indent = 0) =>
        RichTextParagraph.Plain(text).WithParagraphStyle(ParagraphStyle.Default with
        {
            Background = color,
            SpacingBefore = before,
            SpacingAfter = after,
            IndentLevel = indent,
        });

    [Fact]
    public void Covers_The_Lines_Across_The_Column()
    {
        LayoutPage page = Layout(RichTextParagraph.Plain("above"), Shaded("shaded", Red), RichTextParagraph.Plain("below"));

        LayoutLine line = page.Lines[1];
        BRenderCommand.FillRect fill = Assert.Single(Fills(page));
        Assert.Equal(Red, fill.Color);
        Assert.Equal(new BRect(72, line.Top, 468, line.Height), fill.Rect);
    }

    [Fact]
    public void Leaves_The_Space_Before_And_After_White()
    {
        LayoutPage page = Layout(RichTextParagraph.Plain("above"), Shaded("shaded", Red, before: 24, after: 24));

        LayoutLine line = page.Lines[1];
        BRenderCommand.FillRect fill = Assert.Single(Fills(page));
        Assert.Equal(line.Top, fill.Rect.Top, 6);
        Assert.Equal(line.Height, fill.Rect.Height, 6);
    }

    [Fact]
    public void Starts_At_The_Indent()
    {
        LayoutPage page = Layout(Shaded("indented", Red, indent: 2));

        BRenderCommand.FillRect fill = Assert.Single(Fills(page));
        Assert.True(fill.Rect.Left > 72);
        Assert.Equal(540, fill.Rect.Right, 6);
    }

    [Fact]
    public void Joins_The_Next_Paragraph_Shaded_The_Same_Across_The_Space_Between()
    {
        LayoutPage page = Layout(Shaded("first", Blue, after: 12), Shaded("second", Blue, after: 12), RichTextParagraph.Plain("after"));

        BRenderCommand.FillRect fill = Assert.Single(Fills(page));
        Assert.Equal(page.Lines[0].Top, fill.Rect.Top, 6);
        Assert.Equal(page.Lines[1].Top + page.Lines[1].Height, fill.Rect.Bottom, 6);
    }

    [Fact]
    public void A_Different_Colour_Is_A_Box_Of_Its_Own()
    {
        LayoutPage page = Layout(Shaded("first", Blue, after: 12), Shaded("second", Red, after: 12));

        Assert.Equal([Blue, Red], Fills(page).Select(fill => fill.Color));
    }

    [Fact]
    public void An_Unshaded_Paragraph_Paints_Nothing()
    {
        Assert.Empty(Fills(Layout(RichTextParagraph.Plain("plain"))));
    }
}
