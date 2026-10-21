namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Sections and column breaks: a stretch of the body with columns of its own
/// starts below the text above it, shares its lines out evenly between its
/// columns where the next section follows on the same page, and a column break
/// moves to the next column, or to the next page where there is none.
/// </summary>
/// <remarks>
/// <c>tdf123163-1.docx</c> runs one-column and two-column sections into each
/// other on the same pages; read with one set of columns for the whole page it
/// came out at twelve pages where LibreOffice draws sixteen.
/// </remarks>
public sealed class DocumentLayoutSectionTests
{
    /// <summary>US Letter with one-inch margins: a text area 468 points wide and 648 tall.</summary>
    private static readonly DocumentPageBox Letter = new(612, 792, 72, 72, 72, 72);

    /// <summary>Where the second of two columns 36 points apart starts.</summary>
    private const double SecondColumn = 72 + 216 + 36;

    private static RichTextParagraph Line(string text, ParagraphStyle? style = null) =>
        RichTextParagraph.Create(text, InlineStyle.Default, style ?? ParagraphStyle.Default);

    private static IEnumerable<RichTextParagraph> Lines(string prefix, int count) =>
        Enumerable.Range(1, count).Select(i => Line(prefix + " " + i));

    private static LayoutResult Layout(RichTextDocument document, DocumentPageBox? box = null)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, box ?? Letter);
    }

    private static double LeftOf(LayoutLine line) => line.Pieces[0].X;

    private static int PerColumn() =>
        Layout(RichTextDocument.FromParagraphs(Lines("Line", 400))).Pages[0].Lines.Count;

    /// <summary>One paragraph, ten in two columns, then one: three sections on one page.</summary>
    private static RichTextDocument Sandwich(ParagraphStyle? after = null) =>
        RichTextDocument.FromParagraphs(
            [Line("above"), .. Lines("in", 10), Line("below", after)])
            .WithSections(
            [
                new DocumentSection(0, 1, 1, 0),
                new DocumentSection(1, 10, 2, 36),
                new DocumentSection(11, 1, 1, 0),
            ]);

    [Fact]
    public void A_Section_Starts_Below_The_Text_Above_It_In_Both_Its_Columns()
    {
        LayoutPage page = Assert.Single(Layout(Sandwich()).Pages);

        LayoutLine above = page.Lines.Single(line => line.ParagraphIndex == 0);
        List<LayoutLine> section = [.. page.Lines.Where(line => line.ParagraphIndex is >= 1 and <= 10)];
        LayoutLine firstOfSecond = section.First(line => LeftOf(line) > 72);

        Assert.Equal(above.Top + above.Height, section[0].Top, 3);
        Assert.Equal(section[0].Top, firstOfSecond.Top, 3);
    }

    [Fact]
    public void A_Section_The_Next_Follows_On_The_Same_Page_Shares_Its_Lines_Between_Its_Columns()
    {
        LayoutPage page = Assert.Single(Layout(Sandwich()).Pages);

        List<LayoutLine> section = [.. page.Lines.Where(line => line.ParagraphIndex is >= 1 and <= 10)];
        Assert.Equal(5, section.Count(line => LeftOf(line) == 72));
        Assert.Equal(5, section.Count(line => LeftOf(line) == SecondColumn));

        // The text after it starts below the deeper of the two columns, across
        // the page again.
        LayoutLine below = page.Lines.Single(line => line.ParagraphIndex == 11);
        Assert.Equal(section.Max(line => line.Top + line.Height), below.Top, 3);
        Assert.Equal(72, LeftOf(below), 3);
    }

    [Fact]
    public void A_Section_That_Ran_Into_Its_Second_Column_Is_Balanced_Over_Both()
    {
        // Laid out in turn, the first column is full and the second holds four
        // lines; balanced, each holds half, and the text after them starts below
        // the deeper - not below the full column it was before.
        int perColumn = PerColumn();
        int count = perColumn + 4;
        RichTextDocument document = RichTextDocument.FromParagraphs([.. Lines("in", count), Line("below")])
            .WithSections([new DocumentSection(0, count, 2, 36), new DocumentSection(count, 1, 1, 0)]);

        LayoutPage page = Assert.Single(Layout(document).Pages);

        List<LayoutLine> section = [.. page.Lines.Where(line => line.ParagraphIndex < count)];
        int first = section.Count(line => LeftOf(line) == 72);
        Assert.InRange(first, count / 2, (count / 2) + 1);
        LayoutLine below = page.Lines.Single(line => line.ParagraphIndex == count);
        Assert.Equal(section.Max(line => line.Top + line.Height), below.Top, 3);
    }

    [Fact]
    public void A_Section_Followed_By_A_New_Page_Fills_Its_First_Column_First()
    {
        LayoutResult result = Layout(Sandwich(ParagraphStyle.Default with { PageBreakBefore = true }));

        Assert.Equal(2, result.Pages.Count);
        Assert.All(
            result.Pages[0].Lines.Where(line => line.ParagraphIndex is >= 1 and <= 10),
            line => Assert.Equal(72, LeftOf(line), 3));
        Assert.Equal(11, Assert.Single(result.Pages[1].Lines).ParagraphIndex);
    }

    [Fact]
    public void The_Last_Section_Of_The_Document_Fills_Its_First_Column_First()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs([Line("above"), .. Lines("in", 10)])
            .WithSections([new DocumentSection(0, 1, 1, 0), new DocumentSection(1, 10, 2, 36)]);

        LayoutPage page = Assert.Single(Layout(document).Pages);

        Assert.All(page.Lines, line => Assert.Equal(72, LeftOf(line), 3));
    }

    [Fact]
    public void Each_Section_Wraps_To_The_Width_Of_Its_Own_Columns()
    {
        string words = string.Join(' ', Enumerable.Repeat("word", 60));
        RichTextDocument document = RichTextDocument.FromParagraphs([Line(words), Line(words), Line("end")])
            .WithSections(
            [
                new DocumentSection(0, 1, 1, 0),
                new DocumentSection(1, 1, 2, 36),
                new DocumentSection(2, 1, 1, 0),
            ]);

        LayoutPage page = Assert.Single(Layout(document).Pages);

        int wide = page.Lines.Count(line => line.ParagraphIndex == 0);
        int narrow = page.Lines.Count(line => line.ParagraphIndex == 1);
        Assert.True(narrow > wide * 2 - 1, $"{narrow} lines in a column of 216 points against {wide} in 468");
    }

    [Fact]
    public void A_Section_Too_Low_On_The_Page_For_A_Line_Starts_On_The_Next()
    {
        int perColumn = PerColumn();
        RichTextDocument document = RichTextDocument.FromParagraphs([.. Lines("above", perColumn), .. Lines("in", 4)])
            .WithSections([new DocumentSection(0, perColumn, 1, 0), new DocumentSection(perColumn, 4, 2, 36)]);

        LayoutResult result = Layout(document);

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(perColumn, result.Pages[0].Lines.Count);
        Assert.Equal(4, result.Pages[1].Lines.Count);
        Assert.Equal(72, LeftOf(result.Pages[1].Lines[0]), 3);
        Assert.Equal(72, result.Pages[1].Lines[0].Top, 3);
    }

    [Fact]
    public void A_Section_That_Holds_A_Table_Shares_Its_Rows_Out_Whole()
    {
        // Six one-line rows and nothing else: balanced, three to a column, each
        // row's box going with its text.
        var paragraphs = new List<RichTextParagraph> { Line("above") };
        paragraphs.AddRange(Lines("cell", 6));
        paragraphs.Add(Line("below"));
        var table = new DocumentTable(
            1,
            6,
            [.. Enumerable.Range(1, 6).Select(i => new TableRow([new TableCell(i, 1, 0)]))],
            [100.0]);
        RichTextDocument document = RichTextDocument.FromParagraphs(paragraphs)
            .WithTables([table])
            .WithSections(
            [
                new DocumentSection(0, 1, 1, 0),
                new DocumentSection(1, 6, 2, 36),
                new DocumentSection(7, 1, 1, 0),
            ]);

        LayoutPage page = Assert.Single(Layout(document).Pages);

        Assert.Equal(3, page.Cells.Count(cell => cell.Bounds.Left < SecondColumn));
        Assert.Equal(3, page.Cells.Count(cell => cell.Bounds.Left >= SecondColumn));
        foreach (LayoutCell cell in page.Cells)
        {
            Assert.Single(page.Lines, line =>
                line.ParagraphIndex is >= 1 and <= 6 &&
                line.Top >= cell.Bounds.Top && line.Top < cell.Bounds.Bottom &&
                LeftOf(line) > cell.Bounds.Left && LeftOf(line) < cell.Bounds.Right);
        }

        LayoutLine below = page.Lines.Single(line => line.ParagraphIndex == 7);
        Assert.Equal(page.Cells.Max(cell => cell.Bounds.Bottom), below.Top, 3);
    }

    [Fact]
    public void A_Section_That_Starts_A_Page_Lays_Its_Text_Out_Between_Its_Own_Margins()
    {
        // The document's page is Letter with inch margins; the second section
        // starts a page and states two inches either side.
        PageGeometry letter = new(612, 792, 72, 72, 72, 72);
        PageGeometry narrow = new(612, 792, 144, 144, 72, 72);
        string words = string.Join(' ', Enumerable.Repeat("word", 60));
        RichTextDocument document = RichTextDocument.FromParagraphs(
                [Line(words), Line(words, ParagraphStyle.Default with { PageBreakBefore = true })])
            .WithPageGeometry(letter)
            .WithSections([new DocumentSection(0, 1, 1, 0), new DocumentSection(1, 1, 1, 0, narrow)]);

        LayoutResult result = Layout(document);

        Assert.Equal(2, result.Pages.Count);
        Assert.All(result.Pages[1].Lines, line => Assert.True(LeftOf(line) >= 144));
        Assert.All(result.Pages[1].Lines, line =>
            Assert.True(line.Pieces[^1].X + line.Pieces[^1].Width <= 612 - 144 + 0.5));
        Assert.True(result.Pages[1].Lines.Count > result.Pages[0].Lines.Count);
    }

    [Fact]
    public void A_Section_That_Follows_On_A_Page_Keeps_The_Pages_Margins()
    {
        // LibreOffice makes a section a continuous break opens a text section
        // inside the page style of the section that started the page, and
        // gives it columns but not margins of its own (writerfilter's
        // SectionPropertyMap::CloseSectionGroup). Word sets it between its
        // own; the comparison here is LibreOffice's.
        PageGeometry letter = new(612, 792, 72, 72, 72, 72);
        PageGeometry narrow = new(612, 792, 144, 144, 72, 72);
        string words = string.Join(' ', Enumerable.Repeat("word", 60));
        RichTextDocument document = RichTextDocument.FromParagraphs([Line(words), Line(words), Line(words)])
            .WithPageGeometry(letter)
            .WithSections(
            [
                new DocumentSection(0, 1, 1, 0),
                new DocumentSection(1, 1, 1, 0, narrow),
                new DocumentSection(2, 1, 1, 0),
            ]);

        LayoutPage page = Assert.Single(Layout(document).Pages);

        Assert.All(page.Lines, line => Assert.Equal(72, LeftOf(line), 3));
        Assert.Equal(
            page.Lines.Count(line => line.ParagraphIndex == 0), page.Lines.Count(line => line.ParagraphIndex == 1));
    }

    [Fact]
    public void A_Section_That_Starts_A_Page_Sets_Its_Top_Margin()
    {
        PageGeometry letter = new(612, 792, 72, 72, 72, 72);
        PageGeometry low = new(612, 792, 72, 72, 216, 72);
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [Line("first"), Line("second", ParagraphStyle.Default with { PageBreakBefore = true }), Line("third")])
            .WithPageGeometry(letter)
            .WithSections([new DocumentSection(0, 1, 1, 0), new DocumentSection(1, 2, 1, 0, low)]);

        LayoutResult result = Layout(document);

        Assert.Equal(72, result.Pages[0].Lines[0].Top, 3);
        Assert.Equal(216, result.Pages[1].Lines[0].Top, 3);
    }

    [Fact]
    public void A_Section_On_Other_Paper_Keeps_The_Margins_Of_The_Page_Asked_For()
    {
        // No page geometry: the box is not the document's paper, so a section's
        // margins, measured on that paper, are not applied - and a note says so.
        PageGeometry narrow = new(612, 792, 144, 144, 72, 72);
        RichTextDocument document = RichTextDocument.FromParagraphs([Line("one"), Line("two")])
            .WithSections([new DocumentSection(0, 1, 1, 0), new DocumentSection(1, 1, 1, 0, narrow)]);

        LayoutResult result = Layout(document);

        Assert.All(result.Pages[0].Lines, line => Assert.Equal(72, LeftOf(line), 3));
        Assert.Contains(result.Notes, note => note.Contains("own paper", StringComparison.Ordinal));
    }

    [Fact]
    public void A_Column_Break_Moves_To_The_Top_Of_The_Next_Column_Of_Its_Section()
    {
        ParagraphStyle columnBreak = ParagraphStyle.Default with { ColumnBreakBefore = true };
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [Line("above"), Line("one"), Line("two"), Line("three", columnBreak)])
            .WithSections([new DocumentSection(0, 1, 1, 0), new DocumentSection(1, 3, 2, 36)]);

        LayoutPage page = Assert.Single(Layout(document).Pages);

        LayoutLine one = page.Lines.Single(line => line.ParagraphIndex == 1);
        LayoutLine three = page.Lines.Single(line => line.ParagraphIndex == 3);
        Assert.Equal(SecondColumn, LeftOf(three), 3);
        Assert.Equal(one.Top, three.Top, 3);
    }

    [Fact]
    public void A_Column_Break_On_A_Page_Of_One_Column_Starts_A_Page()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [Line("one"), Line("two", ParagraphStyle.Default with { ColumnBreakBefore = true })]);

        LayoutResult result = Layout(document);

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(1, Assert.Single(result.Pages[1].Lines).ParagraphIndex);
    }

    [Fact]
    public void A_Column_Break_In_The_Last_Column_Starts_A_Page()
    {
        ParagraphStyle columnBreak = ParagraphStyle.Default with { ColumnBreakBefore = true };
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [Line("one"), Line("two", columnBreak), Line("three", columnBreak)])
            .WithPageGeometry(new PageGeometry(612, 792, 72, 72, 72, 72, ColumnCount: 2, ColumnGap: 36));

        LayoutResult result = Layout(document);

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(SecondColumn, LeftOf(result.Pages[0].Lines[1]), 3);
        LayoutLine three = Assert.Single(result.Pages[1].Lines);
        Assert.Equal(72, LeftOf(three), 3);
    }

    [Fact]
    public void A_Continuous_Render_Ignores_A_Column_Break_And_Says_So()
    {
        var continuous = new DocumentPageBox(612, 792, 72, 72, 72, 72, continuous: true);
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [Line("one"), Line("two", ParagraphStyle.Default with { ColumnBreakBefore = true })]);

        LayoutResult result = Layout(document, continuous);

        Assert.Single(result.Pages);
        Assert.Contains(result.Notes, note => note.Contains("ignored 1 page break", StringComparison.Ordinal));
    }

    [Fact]
    public void A_Document_Without_Sections_Lays_Out_As_Before()
    {
        RichTextDocument plain = RichTextDocument.FromParagraphs(Lines("Line", 120));
        RichTextDocument one = plain.WithSections([new DocumentSection(0, 120, 1, 0)]);

        LayoutResult before = Layout(plain);
        LayoutResult after = Layout(one);

        Assert.Equal(before.Pages.Count, after.Pages.Count);
        Assert.Equal(
            before.Pages.SelectMany(page => page.Lines).Select(line => (line.Top, LeftOf(line))),
            after.Pages.SelectMany(page => page.Lines).Select(line => (line.Top, LeftOf(line))));
    }
}
