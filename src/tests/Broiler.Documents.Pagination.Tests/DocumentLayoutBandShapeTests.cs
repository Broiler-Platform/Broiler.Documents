using Broiler.Graphics.Color;

namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Office issue 249: a header or footer shape placed against its paragraph
/// hangs from where the band lays that paragraph out (fdo80895.docx's oval
/// above a one-line footer that sits on its distance from the page's foot).
/// </summary>
public sealed class DocumentLayoutBandShapeTests
{
    private static LayoutPage Layout(RichTextDocument document, DocumentPageBox page)
    {
        using var images = new ImageStore();
        return Assert.Single(new DocumentLayout(new LayoutSettings(), images).Layout(document, page).Pages);
    }

    private static readonly DocumentPageBox Page = new(300, 400, 72, 20, 72, 20);

    private static RichTextDocument WithFooterShape(DocumentShape shape) =>
        RichTextDocument.FromParagraphs([RichTextParagraph.Plain("body")])
            .WithPageGeometry(new PageGeometry(300, 400, 72, 20, 72, 20, 20, 20))
            .WithRunningContent(RunningContent.Empty.WithFooter(
                PageSelection.Default, [RichTextParagraph.Plain("footer")], [shape]));

    [Fact]
    public void A_Footer_Shape_Hangs_From_Its_Paragraph_As_The_Band_Lays_It_Out()
    {
        // The reader's estimate puts the paragraph at the bottom margin (328);
        // the footer's one line ends 20 points above the foot, much lower.
        var shape = new DocumentShape(0, 10, 328 - 8, 40, 20, fill: ShapeFill.Solid(BColor.Red))
        {
            OffsetFromBandParagraph = -8,
        };

        LayoutPage page = Layout(WithFooterShape(shape), Page);

        LayoutLine footer = Assert.Single(page.Lines, line => line.ParagraphIndex == -1);
        LayoutShape drawn = Assert.Single(page.Shapes);
        Assert.Equal(footer.Top - 8, drawn.Bounds.Top, 3);
        Assert.True(footer.Top > 340, $"footer line at {footer.Top}");
    }

    [Fact]
    public void A_Footer_Shape_With_No_Paragraph_Offset_Keeps_Its_Stated_Top()
    {
        var shape = new DocumentShape(0, 10, 300, 40, 20, fill: ShapeFill.Solid(BColor.Red));

        LayoutPage page = Layout(WithFooterShape(shape), Page);

        Assert.Equal(300, Assert.Single(page.Shapes).Bounds.Top, 3);
    }
}
