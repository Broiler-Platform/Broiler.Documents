namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// A table row that does not fit on its page breaks across it, cell by cell,
/// unless it says it may not.
/// </summary>
/// <remarks>
/// LibreOffice's <c>tdf134965.odt</c> is a two-column table of comments, some of
/// whose rows are longer than a page. Placed whole, such a row ran off the foot of
/// its page and the pages its continuation fills were never made: 23 pages where
/// LibreOffice draws 29.
/// </remarks>
public sealed class DocumentLayoutRowSplitTests
{
    /// <summary>US Letter with one-inch margins: a text area 648 points tall.</summary>
    private static readonly DocumentPageBox Letter = new(612, 792, 72, 72, 72, 72);

    private static LayoutResult Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, Letter);
    }

    /// <summary>
    /// <paramref name="before"/> body paragraphs, then a one-row table of two cells
    /// holding <paramref name="left"/> and <paramref name="right"/> paragraphs.
    /// </summary>
    private static RichTextDocument TableAfter(int before, int left, int right, bool canSplit = true, int nestedRows = 0)
    {
        var paragraphs = new List<RichTextParagraph>();
        for (int i = 0; i < before; i++)
            paragraphs.Add(RichTextParagraph.Plain("Body " + i));

        int start = paragraphs.Count;
        for (int i = 0; i < left; i++)
            paragraphs.Add(RichTextParagraph.Plain("Left " + i));
        for (int i = 0; i < right; i++)
            paragraphs.Add(RichTextParagraph.Plain("Right " + i));

        paragraphs.Add(RichTextParagraph.Plain("After the table"));

        IReadOnlyList<DocumentTable>? nested = null;
        if (nestedRows > 0)
        {
            nested =
            [
                new DocumentTable(start, nestedRows,
                    [.. Enumerable.Range(0, nestedRows).Select(r => new TableRow([new TableCell(start + r, 1, 0)]))]),
            ];
        }

        var row = new TableRow(
            [new TableCell(start, left, 0, tables: nested), new TableCell(start + left, right, 1)],
            canSplit: canSplit);
        var table = new DocumentTable(start, left + right, [row], [200.0, 200.0]);
        return RichTextDocument.FromParagraphs(paragraphs).WithTables([table]);
    }

    /// <summary>How many plain lines one page holds.</summary>
    private static int PerPage() =>
        Layout(RichTextDocument.FromParagraphs(Enumerable.Range(0, 400).Select(i => RichTextParagraph.Plain("Line " + i))))
            .Pages[0].Lines.Count;

    private static IEnumerable<LayoutLine> LinesStarting(LayoutPage page, string prefix, RichTextDocument document) =>
        page.Lines.Where(line => document.Paragraphs[line.ParagraphIndex].Text.StartsWith(prefix, StringComparison.Ordinal));

    [Fact]
    public void A_Row_Taller_Than_The_Page_Continues_On_The_Next()
    {
        int perPage = PerPage();
        RichTextDocument document = TableAfter(0, perPage + 10, 3);

        LayoutResult result = Layout(document);

        Assert.True(result.Pages.Count >= 2);
        Assert.Equal(perPage, LinesStarting(result.Pages[0], "Left", document).Count());
        Assert.Equal(10, LinesStarting(result.Pages[1], "Left", document).Count());

        // The continuation starts at the top of its page, and nothing runs off the
        // foot of the first.
        Assert.Equal(72, LinesStarting(result.Pages[1], "Left", document).First().Top, 3);
        Assert.All(result.Pages[0].Lines, line => Assert.True(line.Top + line.Height <= 72 + 648 + 0.001));
    }

    [Fact]
    public void A_Row_That_Does_Not_Fit_Below_The_Body_Breaks_Where_The_Page_Ends()
    {
        int perPage = PerPage();
        RichTextDocument document = TableAfter(perPage - 4, 10, 2);

        LayoutResult result = Layout(document);

        // Four lines of the left cell fit under the body; the short right cell
        // fits whole on the first page and has nothing to carry.
        Assert.Equal(4, LinesStarting(result.Pages[0], "Left", document).Count());
        Assert.Equal(2, LinesStarting(result.Pages[0], "Right", document).Count());
        Assert.Equal(6, LinesStarting(result.Pages[1], "Left", document).Count());
        Assert.Empty(LinesStarting(result.Pages[1], "Right", document));
    }

    [Fact]
    public void Both_Parts_Draw_Their_Cell_Boxes()
    {
        int perPage = PerPage();
        RichTextDocument document = TableAfter(perPage - 4, 10, 2);

        LayoutResult result = Layout(document);

        // Each part draws both cells: the first down to the foot of the page, the
        // second from the top of the next.
        Assert.Equal(2, result.Pages[0].Cells.Count);
        Assert.All(result.Pages[0].Cells, cell => Assert.Equal(72 + 648, cell.Bounds.Bottom, 3));
        Assert.Equal(2, result.Pages[1].Cells.Count);
        Assert.All(result.Pages[1].Cells, cell => Assert.Equal(72, cell.Bounds.Top, 3));
    }

    [Fact]
    public void A_Row_That_May_Not_Split_Moves_To_The_Next_Page_Whole()
    {
        int perPage = PerPage();
        RichTextDocument document = TableAfter(perPage - 4, 10, 2, canSplit: false);

        LayoutResult result = Layout(document);

        Assert.Empty(LinesStarting(result.Pages[0], "Left", document));
        Assert.Equal(10, LinesStarting(result.Pages[1], "Left", document).Count());
    }

    [Fact]
    public void A_Row_That_May_Not_Split_And_Cannot_Fit_A_Page_Is_Placed_Whole()
    {
        // Waiting for room it will never have would stop the layout.
        int perPage = PerPage();
        RichTextDocument document = TableAfter(0, perPage + 10, 1, canSplit: false);

        LayoutResult result = Layout(document);

        Assert.Equal(perPage + 10, LinesStarting(result.Pages[0], "Left", document).Count());
    }

    [Fact]
    public void A_Row_Holding_A_Nested_Table_Stays_Whole()
    {
        int perPage = PerPage();
        RichTextDocument document = TableAfter(perPage - 4, 10, 2, nestedRows: 2);

        LayoutResult result = Layout(document);

        Assert.Empty(LinesStarting(result.Pages[0], "Left", document));
        Assert.Equal(10, LinesStarting(result.Pages[1], "Left", document).Count());
    }

    [Fact]
    public void A_Row_That_Fits_Is_Not_Split()
    {
        RichTextDocument document = TableAfter(3, 4, 4);

        LayoutResult result = Layout(document);

        Assert.Single(result.Pages);
        Assert.Equal(2, result.Pages[0].Cells.Count);
    }
}
