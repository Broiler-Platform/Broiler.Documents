using Broiler.Documents.Model.Fonts;

namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Header shapes of office issue #249 (docx-images-shapes): a shape that
/// hangs from its header paragraph (<see cref="DocumentShape.OffsetFromBandParagraph"/>)
/// is measured from where that paragraph lands, not from the page's top.
/// </summary>
/// <remarks>
/// LibreOffice 24.2 draws <c>Cool15691_headerTextWrapsAroundImage.docx</c>'s
/// picture, at nought from its header paragraph, 35.4 points down the page, the
/// header distance; broilerdoc drew it from the top of the page.
/// </remarks>
public sealed class DocumentLayoutIssue249ImagesShapesTests
{
    private static LayoutPage Layout(DocumentShape shape, params string[] header)
    {
        PageGeometry page = new(300, 300, 20, 20, 60, 20, HeaderDistance: 30);
        RunningContent running = RunningContent.Empty.WithHeader(PageSelection.Default,
            [.. header.Select(RichTextParagraph.Plain)], [shape]);
        RichTextDocument document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("body")])
            .WithPageGeometry(page)
            .WithRunningContent(running);
        using var images = new ImageStore();
        return Assert.Single(new DocumentLayout(new LayoutSettings(), images)
            .Layout(document, new DocumentPageBox(300, 300, 60, 20, 20, 20)).Pages);
    }

    private static DocumentShape Box(int paragraph, double offsetY, bool fromParagraph) =>
        new(paragraph, 0, offsetY, 40, 10, ShapeFill.Solid(Broiler.Graphics.Color.BColor.Black))
        {
            OffsetFromBandParagraph = fromParagraph,
        };

    [Fact]
    public void A_Header_Shape_Placed_From_Its_Paragraph_Starts_At_The_Header_Distance()
    {
        LayoutPage page = Layout(Box(0, 4, fromParagraph: true), "header");

        LayoutLine first = page.Lines.First(line => line.ParagraphIndex == -1);
        Assert.Equal(30, first.Top, 3);
        Assert.Equal(34, Assert.Single(page.Shapes).Bounds.Top, 3);
    }

    [Fact]
    public void A_Header_Shape_Follows_The_Paragraph_It_Hangs_From()
    {
        LayoutPage page = Layout(Box(1, 0, fromParagraph: true), "one", "two");

        LayoutLine second = page.Lines.First(line => line.ParagraphIndex == -2);
        Assert.True(second.Top > 30);
        Assert.Equal(second.Top, Assert.Single(page.Shapes).Bounds.Top, 3);
    }

    [Fact]
    public void A_Bold_Run_In_An_Unmapped_Family_Takes_The_Host_Bold_Face()
    {
        // The renderer draws it with its fallback's bold face; the service
        // measured it with the regular one (fdo77887.docx).
        string regular = Path.GetTempFileName();
        string bold = Path.GetTempFileName();
        try
        {
            using var service = new DocumentFontService(regular, bold);

            Assert.Equal(bold, service.ResolveWithOutcome("Noto Sans", bold: true).Face!.SourcePath);
            Assert.Equal(regular, service.ResolveWithOutcome("Noto Sans").Face!.SourcePath);
            Assert.Equal(FontResolutionOutcome.HostFallback, service.ResolveWithOutcome("Noto Sans", bold: true).Outcome);

            using var regularOnly = new DocumentFontService(regular);
            Assert.Equal(regular, regularOnly.ResolveWithOutcome("Noto Sans", bold: true).Face!.SourcePath);
        }
        finally
        {
            File.Delete(regular);
            File.Delete(bold);
        }
    }

    [Fact]
    public void A_Header_Shape_Placed_On_The_Page_Keeps_Its_Offset_From_The_Page_Top()
    {
        LayoutPage page = Layout(Box(0, 4, fromParagraph: false), "header");

        Assert.Equal(4, Assert.Single(page.Shapes).Bounds.Top, 3);
    }
}
