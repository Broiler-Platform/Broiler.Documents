namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Shapes a document keeps off the printed page (<see cref="DocumentShape.Printable"/>):
/// laid out, and kept clear of by the text, but drawn only for a host that asks.
/// </summary>
/// <remarks>
/// <c>fdo114306_2.odt</c> is a letterhead template whose crest and heading frame
/// say <c>style:print-content="false"</c>; LibreOffice prints the page without
/// them, the title as far down the page as if they were there.
/// </remarks>
public sealed class DocumentLayoutPrintableShapeTests
{
    private static readonly DocumentPageBox Box = new(300, 300, 20, 20, 20, 20);

    private static RichTextDocument Document(bool printable) =>
        RichTextDocument.FromParagraphs([RichTextParagraph.Plain("body")])
            .WithShapes([new DocumentShape(0, 0, 0, 260, 50, ShapeFill.Solid(Broiler.Graphics.Color.BColor.Black),
                paragraphs: [RichTextParagraph.Plain("heading")], wrap: ShapeWrap.TopAndBottom, printable: printable)]);

    private static LayoutPage Layout(RichTextDocument document, bool drawNonPrinting = false)
    {
        using var images = new ImageStore();
        return Assert.Single(new DocumentLayout(new LayoutSettings { DrawNonPrintingShapes = drawNonPrinting }, images)
            .Layout(document, Box).Pages);
    }

    [Fact]
    public void A_Non_Printing_Shape_Is_Not_Drawn_But_The_Text_Keeps_Clear_Of_It()
    {
        LayoutPage printed = Layout(Document(printable: true));
        LayoutPage hidden = Layout(Document(printable: false));

        Assert.Single(printed.Shapes);
        Assert.Empty(hidden.Shapes);
        Assert.Equal(printed.Lines.Single(line => line.ParagraphIndex == 0).Top,
            hidden.Lines.Single(line => line.ParagraphIndex == 0).Top, 3);
        Assert.True(hidden.Lines.Single(line => line.ParagraphIndex == 0).Top >= 70);
    }

    [Fact]
    public void A_Host_That_Shows_Non_Printing_Shapes_Draws_Them()
    {
        LayoutPage page = Layout(Document(printable: false), drawNonPrinting: true);

        Assert.Single(page.Shapes);
    }

    [Fact]
    public void A_Non_Printing_Shape_In_A_Header_Is_Not_Drawn()
    {
        var shape = new DocumentShape(0, 0, 5, 100, 10, ShapeFill.Solid(Broiler.Graphics.Color.BColor.Black), printable: false);
        RichTextDocument document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("body")])
            .WithRunningContent(RunningContent.Empty.WithHeader(PageSelection.Default, null, [shape]));

        Assert.Empty(Layout(document).Shapes);
        Assert.Single(Layout(document, drawNonPrinting: true).Shapes);
    }
}
