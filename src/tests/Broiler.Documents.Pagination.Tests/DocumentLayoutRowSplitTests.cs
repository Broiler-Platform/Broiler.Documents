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
    [Fact]
    public void Minimum_row_height_sets_the_next_rows_position()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
                [RichTextParagraph.Plain("first"), RichTextParagraph.Plain("second")])
            .WithTables([new DocumentTable(0, 2,
                [new TableRow([new TableCell(0, 1, 0)], minHeight: 180),
                 new TableRow([new TableCell(1, 1, 0)])], [200], cellPadding: 0)]);

        LayoutPage page = Assert.Single(Layout(document).Pages);

        Assert.Equal(180, page.Cells[0].Bounds.Height, 3);
        Assert.Equal(252, page.Lines.Single(line => line.ParagraphIndex == 1).Top, 3);
    }

    [Fact]
    public void A_rows_minimum_moves_it_to_a_fresh_page_when_only_its_text_would_fit()
    {
        RichTextParagraph[] paragraphs = [RichTextParagraph.Plain("space"), RichTextParagraph.Plain("tall row")];
        RichTextDocument document = RichTextDocument.FromParagraphs(paragraphs)
            .WithTables([new DocumentTable(0, 2,
                [new TableRow([new TableCell(0, 1, 0)], minHeight: 400),
                 new TableRow([new TableCell(1, 1, 0)], minHeight: 400)], [200], cellPadding: 0)]);

        LayoutResult result = Layout(document);

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(1, Assert.Single(result.Pages[1].Lines).ParagraphIndex);
        Assert.Equal(400, Assert.Single(result.Pages[1].Cells).Bounds.Height, 3);
    }

    [Fact]
    public void Content_taller_than_a_rows_minimum_still_grows()
    {
        RichTextDocument document = RichTextDocument.FromPlainText(string.Join("\n", Enumerable.Repeat("line", 12)))
            .WithTables([new DocumentTable(0, 12,
                [new TableRow([new TableCell(0, 12, 0)], minHeight: 30)], [200], cellPadding: 0)]);

        LayoutPage page = Assert.Single(Layout(document).Pages);
        Assert.True(page.Cells[0].Bounds.Height > 30);
        Assert.Equal(12, page.Lines.Count);
    }

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

    /// <summary>
    /// LibreOffice's <c>fdo48718-1.docx</c> is a one-row table whose one cell
    /// holds the whole report, a table of 76 rows among it. Kept whole, the row
    /// left page one blank and ran off the foot of page two.
    /// </summary>
    [Fact]
    public void A_Row_Holding_A_Nested_Table_Breaks_Between_Its_Rows()
    {
        int perPage = PerPage();
        RichTextDocument document = TableAfter(perPage - 4, 10, 2, nestedRows: 10);

        LayoutResult result = Layout(document);

        Assert.Equal(4, LinesStarting(result.Pages[0], "Left", document).Count());
        Assert.Equal(6, LinesStarting(result.Pages[1], "Left", document).Count());

        // Each nested row's box goes with its line: the outer two cells and four
        // nested ones above the cut, the outer two and six below it.
        Assert.Equal(2 + 4, result.Pages[0].Cells.Count);
        Assert.Equal(2 + 6, result.Pages[1].Cells.Count);
        Assert.All(result.Pages[0].Cells, cell => Assert.True(cell.Bounds.Bottom <= 72 + 648 + 0.001));
        Assert.Equal(72, result.Pages[1].Cells.Min(cell => cell.Bounds.Top), 3);
    }

    [Fact]
    public void A_Row_Holding_A_Nested_Table_Taller_Than_The_Page_Starts_On_The_First()
    {
        int perPage = PerPage();
        RichTextDocument document = TableAfter(0, perPage + 10, 1, nestedRows: perPage + 10);

        LayoutResult result = Layout(document);

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(perPage, LinesStarting(result.Pages[0], "Left", document).Count());
        Assert.Equal(10, LinesStarting(result.Pages[1], "Left", document).Count());
        Assert.All(result.Pages[0].Lines, line => Assert.True(line.Top + line.Height <= 72 + 648 + 0.001));
    }

    [Fact]
    public void A_Nested_Row_That_Forbids_Splitting_Is_Not_Cut_Through()
    {
        // Two nested rows of three lines each under a body that leaves room for
        // four: the first row fits, and the second goes to the next page whole.
        int perPage = PerPage();
        var paragraphs = new List<RichTextParagraph>();
        for (int i = 0; i < perPage - 4; i++)
            paragraphs.Add(RichTextParagraph.Plain("Body " + i));

        int start = paragraphs.Count;
        for (int i = 0; i < 6; i++)
            paragraphs.Add(RichTextParagraph.Plain((i < 3 ? "First " : "Second ") + i));

        paragraphs.Add(RichTextParagraph.Plain("After the table"));

        var nested = new DocumentTable(start, 6,
            [new TableRow([new TableCell(start, 3, 0)], canSplit: false),
             new TableRow([new TableCell(start + 3, 3, 0)], canSplit: false)]);
        var outer = new DocumentTable(start, 6,
            [new TableRow([new TableCell(start, 6, 0, tables: [nested])])], [400.0]);
        RichTextDocument document = RichTextDocument.FromParagraphs(paragraphs).WithTables([outer]);

        LayoutResult result = Layout(document);

        Assert.Equal(3, LinesStarting(result.Pages[0], "First", document).Count());
        Assert.Empty(LinesStarting(result.Pages[0], "Second", document));
        Assert.Equal(3, LinesStarting(result.Pages[1], "Second", document).Count());
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
