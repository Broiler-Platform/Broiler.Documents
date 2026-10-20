namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Text columns: a page whose text area is divided into several columns is
/// filled one column after the other before the next page starts.
/// </summary>
/// <remarks>
/// Laid out as one wide column, LibreOffice's six-column
/// <c>many-paragraphs-per-page-in-6-columns.odt</c> came out at forty pages where
/// LibreOffice draws twelve, and the two-column <c>fdo43573-2-min.docx</c> at
/// twenty-seven against fifteen.
/// </remarks>
public sealed class DocumentLayoutColumnTests
{
    /// <summary>US Letter with one-inch margins: a text area 468 points wide and 648 tall.</summary>
    private static readonly DocumentPageBox Letter = new(612, 792, 72, 72, 72, 72);

    private static PageGeometry LetterIn(int columns, double gap) =>
        new(612, 792, 72, 72, 72, 72, ColumnCount: columns, ColumnGap: gap);

    private static RichTextDocument Lines(int count, PageGeometry? geometry, ParagraphStyle? style = null)
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(Enumerable.Range(1, count).Select(i =>
            RichTextParagraph.Create("Line " + i, InlineStyle.Default, style ?? ParagraphStyle.Default)));
        return geometry is null ? document : document.WithPageGeometry(geometry);
    }

    private static LayoutResult Layout(RichTextDocument document, DocumentPageBox? box = null)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, box ?? Letter);
    }

    private static double LeftOf(LayoutLine line) => line.Pieces[0].X;

    [Fact]
    public void Text_Fills_The_First_Column_Then_The_Second_Before_The_Next_Page()
    {
        int perPage = Layout(Lines(400, geometry: null)).Pages[0].Lines.Count;

        LayoutResult result = Layout(Lines(perPage * 2, LetterIn(2, 36)));

        LayoutPage page = Assert.Single(result.Pages);
        Assert.Equal(perPage * 2, page.Lines.Count);

        // 468 points less one gap of 36, shared by two: each column is 216 wide,
        // and the second starts one column and one gap to the right of the first.
        Assert.All(page.Lines.Take(perPage), line => Assert.Equal(72, LeftOf(line), 3));
        Assert.All(page.Lines.Skip(perPage), line => Assert.Equal(72 + 216 + 36, LeftOf(line), 3));

        // The second column starts back at the top of the page.
        Assert.Equal(page.Lines[0].Top, page.Lines[perPage].Top, 3);
    }

    [Fact]
    public void Six_Columns_Hold_Six_Times_A_Column_Of_Text()
    {
        int perColumn = Layout(Lines(400, geometry: null)).Pages[0].Lines.Count;

        LayoutResult result = Layout(Lines((perColumn * 6) + 1, LetterIn(6, 6)));

        Assert.Equal(2, result.Pages.Count);
        Assert.Single(result.Pages[1].Lines);
        Assert.Equal(72, LeftOf(result.Pages[1].Lines[0]), 3);
    }

    [Fact]
    public void A_Line_Wraps_To_The_Width_Of_Its_Column()
    {
        string words = string.Join(' ', Enumerable.Repeat("word", 120));
        RichTextDocument wide = RichTextDocument.FromParagraphs([RichTextParagraph.Plain(words)]);

        int oneColumn = Layout(wide).Pages[0].Lines.Count;
        int twoColumns = Layout(wide.WithPageGeometry(LetterIn(2, 36))).Pages.Sum(page => page.Lines.Count);

        Assert.True(twoColumns > oneColumn * 2, $"{twoColumns} lines in two columns against {oneColumn} in one");
    }

    [Fact]
    public void A_Paragraph_That_Runs_Out_Of_Room_Continues_In_The_Next_Column()
    {
        int perColumn = Layout(Lines(400, geometry: null)).Pages[0].Lines.Count;
        string words = string.Join(' ', Enumerable.Repeat("word", 40));
        var paragraphs = Enumerable.Range(1, perColumn - 1)
            .Select(i => RichTextParagraph.Plain("Line " + i))
            .Append(RichTextParagraph.Plain(words))
            .ToList();

        LayoutPage page = Layout(RichTextDocument.FromParagraphs(paragraphs).WithPageGeometry(LetterIn(2, 36))).Pages[0];

        List<LayoutLine> last = [.. page.Lines.Where(line => line.ParagraphIndex == perColumn - 1)];
        Assert.True(last.Count > 1);
        Assert.Equal(72, LeftOf(last[0]), 3);
        Assert.Equal(72 + 216 + 36, LeftOf(last[^1]), 3);
    }

    [Fact]
    public void Space_Before_Does_Not_Open_A_Column()
    {
        int perColumn = Layout(Lines(400, geometry: null)).Pages[0].Lines.Count;
        ParagraphStyle spaced = ParagraphStyle.Default with { SpacingBefore = 6 };

        LayoutPage page = Layout(Lines(perColumn, LetterIn(2, 36), spaced)).Pages[0];

        // The paragraph that opens the second column starts at the top margin,
        // as the one that opens the page does.
        LayoutLine second = page.Lines.First(line => LeftOf(line) > 72);
        Assert.Equal(page.Lines[0].Top, second.Top, 3);
    }

    [Fact]
    public void A_Page_Break_Goes_To_The_First_Column_Of_The_Next_Page()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Plain("first"),
            RichTextParagraph.Create("second", InlineStyle.Default, ParagraphStyle.Default with { PageBreakBefore = true }),
        ]).WithPageGeometry(LetterIn(2, 36));

        LayoutResult result = Layout(document);

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(72, LeftOf(Assert.Single(result.Pages[1].Lines)), 3);
    }

    [Fact]
    public void A_Table_Row_Lands_In_The_Column_It_Fits_In()
    {
        int perColumn = Layout(Lines(400, geometry: null)).Pages[0].Lines.Count;
        var paragraphs = Enumerable.Range(1, perColumn)
            .Select(i => RichTextParagraph.Plain("Line " + i))
            .Append(RichTextParagraph.Plain("cell"))
            .ToList();
        var table = new DocumentTable(
            perColumn,
            1,
            [new TableRow([new TableCell(perColumn, 1, 0)])],
            [100.0]);

        LayoutPage page = Layout(RichTextDocument.FromParagraphs(paragraphs)
            .WithTables([table])
            .WithPageGeometry(LetterIn(2, 36))).Pages[0];

        LayoutCell cell = Assert.Single(page.Cells);
        Assert.Equal(72 + 216 + 36, cell.Bounds.Left, 3);
        Assert.Equal(page.Lines[0].Top, cell.Bounds.Top, 3);
    }

    [Fact]
    public void A_Continuous_Render_Lays_The_Columns_Out_As_One_And_Says_So()
    {
        var continuous = new DocumentPageBox(612, 792, 72, 72, 72, 72, continuous: true);

        LayoutResult result = Layout(Lines(3, LetterIn(2, 36)), continuous);

        Assert.All(result.Pages[0].Lines, line => Assert.Equal(72, LeftOf(line), 3));
        Assert.Contains(result.Notes, note => note.Contains("text columns", StringComparison.Ordinal));
    }

    [Fact]
    public void Columns_Too_Narrow_For_A_Word_Are_Laid_Out_As_One()
    {
        // A render on paper the document did not ask for shares out what that
        // paper leaves; here that is less than a word's width per column.
        var narrow = new DocumentPageBox(200, 792, 72, 72, 72, 72);

        LayoutResult result = Layout(Lines(3, LetterIn(6, 6)), narrow);

        Assert.Single(result.Pages);
        Assert.All(result.Pages[0].Lines, line => Assert.Equal(72, LeftOf(line), 3));
        Assert.Contains(result.Notes, note => note.Contains("narrower", StringComparison.Ordinal));
    }

    [Fact]
    public void A_Single_Column_Lays_Out_As_Before()
    {
        LayoutResult plain = Layout(Lines(120, geometry: null));
        LayoutResult one = Layout(Lines(120, LetterIn(1, 0)));

        Assert.Equal(plain.Pages.Count, one.Pages.Count);
        Assert.Equal(
            plain.Pages.SelectMany(page => page.Lines).Select(line => (line.Top, LeftOf(line))),
            one.Pages.SelectMany(page => page.Lines).Select(line => (line.Top, LeftOf(line))));
    }
}
