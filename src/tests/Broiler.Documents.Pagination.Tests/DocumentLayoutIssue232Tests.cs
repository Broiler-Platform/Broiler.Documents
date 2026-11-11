namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Office conformance issue 232: a footer laid out with its table
/// (nestedTableInFooter.odt), and a line beside two frames of different
/// heights (tdf135056.odt).
/// </summary>
public sealed class DocumentLayoutIssue232Tests
{
    private static IReadOnlyList<LayoutPage> Layout(RichTextDocument document, DocumentPageBox page)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, page).Pages;
    }

    [Fact]
    public void A_Footer_Table_Draws_Its_Cells()
    {
        var page = new DocumentPageBox(200, 200, 20, 20, 20, 40);
        var table = new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0)])]);
        RichTextParagraph[] footer = [RichTextParagraph.Plain("cell")];
        var document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("body")])
            .WithPageGeometry(new PageGeometry(200, 200, 20, 20, 20, 40, 10, 10))
            .WithRunningContent(RunningContent.Empty.WithFooter(PageSelection.Default, footer, tables: [table]));

        LayoutPage laid = Assert.Single(Layout(document, page));

        LayoutCell cell = Assert.Single(laid.Cells, c => c.Bounds.Top > 160);
        Assert.Contains(laid.Lines, line => line.ParagraphIndex < 0 && line.Top >= cell.Bounds.Top - 0.01);
    }

    [Fact]
    public void A_Line_Blocked_Beside_Two_Frames_Moves_Below_The_Shorter_One()
    {
        // tdf135056.odt: a short frame on the left and a page-high one on the
        // right leave a strip too narrow to write in. The line starts below
        // the short frame, beside the tall one, not below both.
        var page = new DocumentPageBox(400, 400, 20, 20, 20, 20);
        DocumentShape shortFrame = new(0, 0, 0, 150, 50, wrap: ShapeWrap.Square) { MinimumWrapRoom = 56.7 };
        DocumentShape tallFrame = new(0, 160, 0, 200, 340, wrap: ShapeWrap.Square) { MinimumWrapRoom = 56.7 };
        var document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain(string.Empty), RichTextParagraph.Plain("after")])
            .WithShapes([shortFrame, tallFrame]);

        IReadOnlyList<LayoutPage> pages = Layout(document, page);

        LayoutPage first = Assert.Single(pages);
        LayoutLine after = Assert.Single(first.Lines, line => line.ParagraphIndex == 1);
        Assert.True(after.Top < 360, $"line at {after.Top}");
    }
}
