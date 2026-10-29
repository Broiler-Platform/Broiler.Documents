namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// A header or footer band of fixed height (<see cref="PageGeometry.FixedHeaderHeight"/>):
/// the body starts where the margin puts it however much the band holds, and
/// what overruns the band is cut at its edge rather than drawn over the body.
/// </summary>
public sealed class DocumentLayoutFixedBandTests
{
    private static RichTextDocument Document(bool fixedHeader)
    {
        // A 30-point outer margin, a 20-point band, and a header three lines tall.
        PageGeometry page = new(300, 300, 20, 20, 50, 20, HeaderDistance: 30, FixedHeaderHeight: fixedHeader);
        RunningContent running = RunningContent.Empty.WithHeader(PageSelection.Default,
            [RichTextParagraph.Plain("one"), RichTextParagraph.Plain("two"), RichTextParagraph.Plain("three")]);
        return RichTextDocument.FromParagraphs([RichTextParagraph.Plain("body")])
            .WithPageGeometry(page)
            .WithRunningContent(running);
    }

    private static LayoutPage Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return Assert.Single(new DocumentLayout(new LayoutSettings(), images)
            .Layout(document, new DocumentPageBox(300, 300, 50, 20, 20, 20)).Pages);
    }

    [Fact]
    public void A_Fixed_Header_Leaves_The_Body_Where_The_Margin_Puts_It()
    {
        LayoutPage grown = Layout(Document(fixedHeader: false));
        LayoutPage held = Layout(Document(fixedHeader: true));

        double Body(LayoutPage page) => page.Lines.Single(line => line.ParagraphIndex == 0).Top;
        Assert.True(Body(grown) > 50);
        Assert.Equal(50, Body(held), 3);
    }

    [Fact]
    public void A_Fixed_Header_Is_Cut_At_Its_Band()
    {
        LayoutPage page = Layout(Document(fixedHeader: true));

        LayoutLine[] header = [.. page.Lines.Where(line => line.ParagraphIndex < 0)];
        Assert.NotEmpty(header);
        Assert.True(header.Length < 3);
        Assert.All(header, line => Assert.True(line.Top >= 30 - 0.01 && line.Top + line.Height <= 50 + 0.01));
    }
}
