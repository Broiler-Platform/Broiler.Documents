namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// A table's header rows repeat at the top of every page it continues onto.
/// </summary>
/// <remarks>
/// LibreOffice's <c>tdf142694-1.odt</c> is a one-column table whose header row
/// holds a whole nested table; LibreOffice draws it again above the row that
/// moves to the next page. Nothing in the layout read
/// <see cref="TableRow.IsHeader"/>, so the second page started with the row.
/// </remarks>
public sealed class DocumentLayoutHeaderRowTests
{
    /// <summary>US Letter with one-inch margins: a text area 648 points tall.</summary>
    private static readonly DocumentPageBox Letter = new(612, 792, 72, 72, 72, 72);

    private static LayoutResult Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, Letter);
    }

    /// <summary>
    /// A table of <paramref name="headers"/> header rows and <paramref name="rows"/>
    /// body rows, one paragraph each, with <paramref name="linesPerRow"/> lines in
    /// every body row.
    /// </summary>
    private static RichTextDocument Table(int headers, int rows, int linesPerRow = 1, bool everyRowAHeader = false)
    {
        var paragraphs = new List<RichTextParagraph>();
        var tableRows = new List<TableRow>();
        for (int h = 0; h < headers; h++)
        {
            tableRows.Add(new TableRow([new TableCell(paragraphs.Count, 1, 0)], isHeader: true));
            paragraphs.Add(RichTextParagraph.Plain("Heading " + h));
        }

        for (int r = 0; r < rows; r++)
        {
            int start = paragraphs.Count;
            for (int line = 0; line < linesPerRow; line++)
                paragraphs.Add(RichTextParagraph.Plain("Row " + r + "." + line));

            tableRows.Add(new TableRow([new TableCell(start, linesPerRow, 0)], isHeader: everyRowAHeader));
        }

        int count = paragraphs.Count;
        paragraphs.Add(RichTextParagraph.Plain("After"));
        return RichTextDocument.FromParagraphs(paragraphs)
            .WithTables([new DocumentTable(0, count, tableRows, [300.0])]);
    }

    private static IEnumerable<LayoutLine> LinesStarting(LayoutPage page, string prefix, RichTextDocument document) =>
        page.Lines.Where(line => document.Paragraphs[line.ParagraphIndex].Text.StartsWith(prefix, StringComparison.Ordinal));

    [Fact]
    public void The_Header_Rows_Start_Every_Page_The_Table_Continues_Onto()
    {
        RichTextDocument document = Table(headers: 2, rows: 120);

        LayoutResult result = Layout(document);

        Assert.True(result.Pages.Count >= 3);
        foreach (LayoutPage page in result.Pages.Where(page => LinesStarting(page, "Row", document).Any()))
        {
            IReadOnlyList<LayoutLine> headings = [.. LinesStarting(page, "Heading", document)];
            Assert.Equal(2, headings.Count);
            Assert.Equal(72, headings[0].Top, 3);
            Assert.True(headings[1].Top < LinesStarting(page, "Row", document).Min(line => line.Top));
        }
    }

    [Fact]
    public void Every_Body_Row_Is_Laid_Out_Once()
    {
        RichTextDocument document = Table(headers: 1, rows: 120);

        LayoutResult result = Layout(document);

        Assert.Equal(120, result.Pages.Sum(page => LinesStarting(page, "Row", document).Count()));
    }

    [Fact]
    public void A_Row_Split_Across_A_Page_Continues_Under_The_Repeated_Header()
    {
        RichTextDocument document = Table(headers: 1, rows: 1, linesPerRow: 80);

        LayoutResult result = Layout(document);

        Assert.True(result.Pages.Count >= 2);
        LayoutLine heading = LinesStarting(result.Pages[1], "Heading", document).Single();
        Assert.Equal(72, heading.Top, 3);
        Assert.True(LinesStarting(result.Pages[1], "Row", document).Min(line => line.Top) >= heading.Top + heading.Height - 0.001);
        Assert.Equal(80, result.Pages.Sum(page => LinesStarting(page, "Row", document).Count()));
    }

    [Fact]
    public void A_Table_Of_Nothing_But_Header_Rows_Repeats_Nothing()
    {
        RichTextDocument document = Table(headers: 0, rows: 120, everyRowAHeader: true);

        LayoutResult result = Layout(document);

        Assert.Equal(120, result.Pages.Sum(page => LinesStarting(page, "Row", document).Count()));
    }

    [Fact]
    public void A_Table_With_No_Header_Rows_Repeats_Nothing()
    {
        RichTextDocument document = Table(headers: 0, rows: 120);

        LayoutResult result = Layout(document);

        Assert.All(result.Pages.Skip(1), page => Assert.Empty(LinesStarting(page, "Heading", document)));
        Assert.Equal(120, result.Pages.Sum(page => LinesStarting(page, "Row", document).Count()));
    }

    [Fact]
    public void Repeated_Header_Is_Omitted_When_Unsplittable_Row_Cannot_Fit_Under_It()
    {
        // Content height of Letter is 792 - 72 - 72 = 648 pt.
        // Header is 200 pt tall.
        // Row 0 is 600 pt tall and unsplittable.
        // On page 1: header is placed. Row 0 cannot fit (200 + 600 > 648) -> breaks to page 2.
        // On page 2: row 0 cannot fit beneath repeated header (200 + 600 > 648).
        // Since row 0 cannot split, the repeated header is omitted on page 2 so row 0 gets the full page.
        // Row 1 (50 pt) cannot fit in the remaining space on page 2 (648 - 600 = 48 < 50) -> breaks to page 3.
        // On page 3: row 1 fits easily under the repeated header (200 + 50 <= 648), so the header repeats normally.
        var paragraphs = new List<RichTextParagraph>
        {
            RichTextParagraph.Plain("Heading"),
            RichTextParagraph.Plain("Tall unsplittable row"),
            RichTextParagraph.Plain("Small row"),
            RichTextParagraph.Plain("After"),
        };
        var tableRows = new List<TableRow>
        {
            new([new TableCell(0, 1, 0)], minHeight: 200, isHeader: true),
            new([new TableCell(1, 1, 0)], minHeight: 600, canSplit: false),
            new([new TableCell(2, 1, 0)], minHeight: 50, canSplit: false),
        };
        RichTextDocument document = RichTextDocument.FromParagraphs(paragraphs)
            .WithTables([new DocumentTable(0, 3, tableRows, [300.0])]);

        LayoutResult result = Layout(document);

        Assert.True(result.Pages.Count >= 3);
        // Page 1 has heading
        Assert.Single(LinesStarting(result.Pages[0], "Heading", document));
        // Page 2 omits repeated heading
        Assert.Empty(LinesStarting(result.Pages[1], "Heading", document));
        Assert.Single(LinesStarting(result.Pages[1], "Tall unsplittable", document));
        // Page 3 has repeated heading
        Assert.Single(LinesStarting(result.Pages[2], "Heading", document));
        Assert.Single(LinesStarting(result.Pages[2], "Small row", document));
    }
}
