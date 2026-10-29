using Broiler.Graphics.Color;
using Broiler.Graphics.RenderList;

namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Lines a wrapping shape leaves no room beside, as LibreOffice lays them out
/// in <c>tdf123163-1.docx</c>: a row of exact height moves down by whole rows,
/// the paragraph's shading fills the rows it skipped, and an empty paragraph
/// its own backdrop pushes off the page stays on it.
/// </summary>
public sealed class DocumentLayoutBlockedLineTests
{
    /// <summary>200 by 100 points, ten-point margins: a column 180 wide, 80 tall.</summary>
    private static readonly DocumentPageBox Page = new(200, 100, 10, 10, 10, 10);

    private static readonly BColor Grey = BColor.FromArgb(191, 197, 199);

    private static ParagraphStyle Exact(float height) =>
        ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = height };

    private static IReadOnlyList<LayoutPage> Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, Page).Pages;
    }

    /// <summary>A shape across the whole column, from the paragraph's top down.</summary>
    private static DocumentShape Across(int paragraph, double height) =>
        new(paragraph, 0, 0, 180, height, wrap: ShapeWrap.Square);

    [Fact]
    public void A_Blocked_Row_Of_Exact_Height_Moves_Down_By_Whole_Rows()
    {
        // The shape ends 15 points down: past one ten-point row, half into the
        // next. The text starts on the third row, not at the shape's foot.
        var document = RichTextDocument.FromParagraphs([RichTextParagraph.Create("title", InlineStyle.Default, Exact(10))])
            .WithShapes([Across(0, 15)]);

        LayoutLine line = Assert.Single(Assert.Single(Layout(document)).Lines);
        Assert.Equal(30, line.Top, 3);
        Assert.Equal(20, line.LeadingSkip, 3);
    }

    [Fact]
    public void A_Row_Of_Proportional_Height_Still_Starts_At_The_Shapes_Foot()
    {
        var document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("title")])
            .WithShapes([Across(0, 15)]);

        LayoutLine line = Assert.Single(Assert.Single(Layout(document)).Lines);
        Assert.Equal(25, line.Top, 3);
    }

    [Fact]
    public void The_Shading_Fills_The_Rows_The_Line_Skipped()
    {
        var shaded = RichTextParagraph.Create("title", InlineStyle.Default, Exact(10) with { Background = Grey });
        LayoutPage page = Assert.Single(Layout(RichTextDocument.FromParagraphs([shaded]).WithShapes([Across(0, 15)])));

        using var images = new ImageStore();
        var list = new BRenderList();
        new DocumentPagePainter(new LayoutSettings(), images).Paint(page, Page, list, _ => null);
        BRenderCommand.FillRect fill = Assert.Single(list.Commands.OfType<BRenderCommand.FillRect>(), f => f.Color == Grey);
        Assert.Equal(10, fill.Rect.Y, 3);
        Assert.Equal(30, fill.Rect.Height, 3);
    }

    [Fact]
    public void An_Empty_Paragraph_Its_Own_Backdrop_Pushes_Off_The_Page_Stays_On_It()
    {
        // A backdrop taller than the column, then a section on the next page.
        var next = RichTextParagraph.Plain("next").WithParagraphStyle(ParagraphStyle.Default with { PageBreakBefore = true });
        var document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain(string.Empty), next])
            .WithShapes([Across(0, 85)]);

        IReadOnlyList<LayoutPage> pages = Layout(document);
        Assert.Equal(2, pages.Count);
        Assert.Single(pages[0].Shapes);
        Assert.Equal("next", Assert.Single(pages[1].Lines).Pieces[0].Text);
    }

    [Fact]
    public void An_Empty_Paragraph_Under_A_Top_And_Bottom_Frame_Still_Moves_On()
    {
        // tdf138039.odt: a page-tall picture anchored on an empty paragraph,
        // keeping text above and below it. LibreOffice moves the empty line
        // onto a page of its own.
        var next = RichTextParagraph.Plain("next").WithParagraphStyle(ParagraphStyle.Default with { PageBreakBefore = true });
        var document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain(string.Empty), next])
            .WithShapes([new DocumentShape(0, 0, 0, 180, 85, wrap: ShapeWrap.TopAndBottom)]);

        Assert.Equal(3, Layout(document).Count);
    }

    [Fact]
    public void A_Paragraph_With_Text_Still_Moves_On()
    {
        var document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("text")])
            .WithShapes([Across(0, 85)]);

        IReadOnlyList<LayoutPage> pages = Layout(document);
        Assert.Equal(2, pages.Count);
        Assert.Empty(pages[0].Lines);
    }
}
