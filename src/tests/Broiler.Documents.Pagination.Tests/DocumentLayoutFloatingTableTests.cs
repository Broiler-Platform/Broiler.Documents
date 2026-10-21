namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// A floating table stands where its position puts it, at its own width, out of
/// the text flow; the text runs around it and an inline table moves below it.
/// </summary>
/// <remarks>
/// LibreOffice's <c>floattable-center-shift-down.docx</c> and
/// <c>fdo48718-1.docx</c> each float a table with <c>w:tblpPr</c>. Laid out in
/// the flow at the text's width, the first pushed the inline tables after it a
/// page down and the second broke its page three names early.
/// </remarks>
public sealed class DocumentLayoutFloatingTableTests
{
    /// <summary>US Letter with one-inch margins: a text area 468 by 648 points.</summary>
    private static readonly DocumentPageBox Letter = new(612, 792, 72, 72, 72, 72);

    private static LayoutResult Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, Letter);
    }

    /// <summary>
    /// <paramref name="before"/> body paragraphs, a floating table of
    /// <paramref name="rows"/> one-paragraph rows, <paramref name="width"/> wide,
    /// then <paramref name="after"/> body paragraphs.
    /// </summary>
    private static RichTextDocument Floating(TablePosition position, int rows, double width, int before = 0, int after = 3)
    {
        var paragraphs = new List<RichTextParagraph>();
        for (int i = 0; i < before; i++)
            paragraphs.Add(RichTextParagraph.Plain("Before " + i));

        int start = paragraphs.Count;
        for (int i = 0; i < rows; i++)
            paragraphs.Add(RichTextParagraph.Plain("Cell " + i));

        for (int i = 0; i < after; i++)
            paragraphs.Add(RichTextParagraph.Plain("After " + i));

        var table = new DocumentTable(
            start,
            rows,
            [.. Enumerable.Range(0, rows).Select(r => new TableRow([new TableCell(start + r, 1, 0)]))],
            [width],
            position: position);
        return RichTextDocument.FromParagraphs(paragraphs).WithTables([table]);
    }

    private static IEnumerable<LayoutLine> LinesStarting(LayoutPage page, string prefix, RichTextDocument document) =>
        page.Lines.Where(line => document.Paragraphs[line.ParagraphIndex].Text.StartsWith(prefix, StringComparison.Ordinal));

    [Fact]
    public void A_Table_Anchored_To_The_Page_Stands_At_Its_Offset_And_Takes_No_Room_In_The_Flow()
    {
        var position = new TablePosition(TableAnchor.Page, 300, TableAnchor.Page, 400);
        RichTextDocument document = Floating(position, rows: 2, width: 150);

        LayoutPage page = Assert.Single(Layout(document).Pages);

        // The text after the table starts at the top margin, where the table
        // would have been, and the table's cells stand at its position.
        Assert.Equal(72, LinesStarting(page, "After 0", document).Single().Top, 3);
        Assert.Equal(400, page.Cells.Min(cell => cell.Bounds.Top), 3);
        Assert.All(page.Cells, cell => Assert.Equal(300, cell.Bounds.Left, 3));
        Assert.All(page.Cells, cell => Assert.Equal(150, cell.Bounds.Width, 3));
    }

    [Fact]
    public void A_Floating_Table_Keeps_Its_Own_Width_When_It_Is_Wider_Than_The_Text()
    {
        // Centred on the margins, as fdo48718-1.docx's 504-point table is on a
        // narrower text area.
        var position = new TablePosition(TableAnchor.Margin, 0, TableAnchor.Page, 100, HorizontalAlignment: TableAlignment.Center);
        RichTextDocument document = Floating(position, rows: 1, width: 520);

        LayoutCell cell = Assert.Single(Layout(document).Pages[0].Cells);

        Assert.Equal(520, cell.Bounds.Width, 3);
        Assert.Equal(72 + ((468 - 520) / 2.0), cell.Bounds.Left, 3);
    }

    [Fact]
    public void Text_Beside_A_Narrow_Floating_Table_Runs_Down_The_Other_Side()
    {
        var position = new TablePosition(TableAnchor.Margin, 0, TableAnchor.Margin, 0);
        RichTextDocument document = Floating(position, rows: 6, width: 150);

        LayoutPage page = Layout(document).Pages[0];

        // The table stands at the left of the text area, so the text beside it
        // starts to its right and stays clear of it.
        LayoutLine first = LinesStarting(page, "After 0", document).Single();
        Assert.Equal(72, first.Top, 3);
        Assert.True(page.Cells.All(cell => cell.Bounds.Right <= first.Pieces[0].X + 0.001));
    }

    [Fact]
    public void Text_Goes_Below_A_Floating_Table_That_Leaves_No_Side_To_Write_In()
    {
        var position = new TablePosition(TableAnchor.Margin, 0, TableAnchor.Margin, 0);
        RichTextDocument document = Floating(position, rows: 3, width: 440);

        LayoutPage page = Layout(document).Pages[0];

        double tableBottom = page.Cells.Max(cell => cell.Bounds.Bottom);
        Assert.True(LinesStarting(page, "After 0", document).Single().Top >= tableBottom - 0.001);
    }

    [Fact]
    public void An_Inline_Table_That_Would_Run_Into_A_Floating_One_Moves_Below_It()
    {
        // LibreOffice's testInlineTableShiftDown: an inline table is not drawn
        // over the floating table beside it, but moved down past it.
        var position = new TablePosition(TableAnchor.Margin, 0, TableAnchor.Page, 200);
        var paragraphs = new List<RichTextParagraph>
        {
            RichTextParagraph.Plain("Float 0"),
            RichTextParagraph.Plain("Float 1"),
        };
        for (int i = 0; i < 20; i++)
            paragraphs.Add(RichTextParagraph.Plain("Inline " + i));

        DocumentTable floating = new(0, 2,
            [new TableRow([new TableCell(0, 1, 0)]), new TableRow([new TableCell(1, 1, 0)])], [468.0], position: position);
        DocumentTable inline = new(2, 20,
            [.. Enumerable.Range(0, 20).Select(r => new TableRow([new TableCell(2 + r, 1, 0)]))], [468.0]);
        RichTextDocument document = RichTextDocument.FromParagraphs(paragraphs).WithTables([floating, inline]);

        LayoutPage page = Layout(document).Pages[0];

        double floatBottom = LinesStarting(page, "Float 1", document).Single().Top +
            LinesStarting(page, "Float 1", document).Single().Height;
        Assert.All(LinesStarting(page, "Inline", document), line => Assert.True(line.Top >= floatBottom - 0.001));
    }

    [Fact]
    public void A_Floating_Table_Taller_Than_The_Room_Left_Continues_At_The_Top_Of_The_Next_Page()
    {
        // LibreOffice's TestTdf161348: the frame a floating table is imported as
        // splits across pages.
        var position = new TablePosition(TableAnchor.Margin, 0, TableAnchor.Page, 500);
        RichTextDocument document = Floating(position, rows: 40, width: 200, after: 1);

        LayoutResult result = Layout(document);

        Assert.True(result.Pages.Count >= 2);
        IReadOnlyList<LayoutLine> first = [.. LinesStarting(result.Pages[0], "Cell", document)];
        IReadOnlyList<LayoutLine> second = [.. LinesStarting(result.Pages[1], "Cell", document)];
        Assert.NotEmpty(first);
        Assert.Equal(40, first.Count + second.Count);
        Assert.Equal(500, result.Pages[0].Cells.Min(cell => cell.Bounds.Top), 3);
        Assert.Equal(72, second.Min(line => line.Top), 3);
        Assert.All(first, line => Assert.True(line.Top + line.Height <= 72 + 648 + 0.001));
    }
}
