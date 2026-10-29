namespace Broiler.Documents.Pagination.Tests;

public sealed class DocumentLayoutTableFlowTests
{
    [Fact]
    public void First_cell_paragraph_spacing_is_inside_the_row_and_adds_to_its_height()
    {
        RichTextParagraph cell = Para(1).WithParagraphStyle(Para(1).Style with { SpacingBefore = 7, SpacingAfter = 5 });
        RichTextDocument document = RichTextDocument.FromParagraphs([cell, Para(1)])
            .WithTables([new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0,
                padding: new TableCellPadding(0, 3, 0, 2))])], [180])]);
        LayoutPage page = Assert.Single(Layout(document).Pages);
        Assert.Equal(20, page.Lines[0].Top, 3);
        Assert.Equal(27, page.Cells[0].Bounds.Height, 3);
        Assert.Equal(37, page.Lines[1].Top, 3);
    }

    [Fact]
    public void A_table_page_break_moves_the_grid_as_a_whole()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs([Para(1), Para(2)])
            .WithTables([new DocumentTable(1, 1, [new TableRow([new TableCell(1, 1, 0)])], [180], pageBreakBefore: true)]);
        LayoutResult result = Layout(document);
        Assert.Equal(2, result.Pages.Count);
        Assert.Empty(result.Pages[0].Cells);
        Assert.Single(result.Pages[1].Cells);
        Assert.Equal(10, result.Pages[1].Lines[0].Top, 3);
    }

    [Fact]
    public void A_table_column_break_moves_to_the_next_column()
    {
        var document = RichTextDocument.FromParagraphs([Para(1), Para(1)])
            .WithTables([new DocumentTable(1, 1, [new TableRow([new TableCell(1, 1, 0)])], [80], columnBreakBefore: true)])
            .WithPageGeometry(new PageGeometry(200, 100, 10, 10, 10, 10, ColumnCount: 2, ColumnGap: 20));
        LayoutPage page = Assert.Single(Layout(document).Pages);
        Assert.Equal(110, page.Cells[0].Bounds.Left, 3);
        Assert.Equal(10, page.Cells[0].Bounds.Top, 3);
    }

    [Fact]
    public void A_break_on_an_opening_table_does_not_create_a_blank_page()
    {
        var document = RichTextDocument.FromParagraphs([Para(1)]).WithTables([
            new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0)])], [180], pageBreakBefore: true)]);
        Assert.Single(Layout(document).Pages);
    }

    [Fact]
    public void Continuous_output_reports_and_ignores_table_breaks()
    {
        var document = RichTextDocument.FromParagraphs([Para(1), Para(1)]).WithTables([
            new DocumentTable(1, 1, [new TableRow([new TableCell(1, 1, 0)])], [180], pageBreakBefore: true)]);
        Assert.Equal(2, Assert.Single(Layout(document, continuous: true).Pages).Lines.Count);
    }

    private static RichTextParagraph Para(int lines) => RichTextParagraph.Create(
        string.Join("\u2028", Enumerable.Repeat("x", lines)), InlineStyle.Default,
        ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 10 });

    private static LayoutResult Layout(RichTextDocument document, bool continuous = false)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10, continuous: continuous));
    }

    private static RichTextDocument TableAfterFiller(bool keep, int tableLines = 2, int followingLines = 1) =>
        RichTextDocument.FromParagraphs([Para(6), Para(tableLines), Para(followingLines)])
            .WithTables([new DocumentTable(1, 1, [new TableRow([new TableCell(1, 1, 0)])],
                [180], cellPadding: 0, keepWithNext: keep)]);

    [Fact]
    public void A_table_moves_with_its_following_paragraph()
    {
        LayoutResult kept = Layout(TableAfterFiller(true));
        LayoutResult ordinary = Layout(TableAfterFiller(false));
        Assert.Equal(6, kept.Pages[0].Lines.Count);
        Assert.Equal(8, ordinary.Pages[0].Lines.Count);
        Assert.Equal(1, kept.Pages[1].Lines[0].ParagraphIndex);
        Assert.Equal(2, kept.Pages[1].Lines[^1].ParagraphIndex);
    }

    [Fact]
    public void Oversized_kept_tables_still_split_and_keep_all_lines()
    {
        LayoutResult result = Layout(TableAfterFiller(true, tableLines: 12));
        Assert.Equal(19, result.Pages.Sum(page => page.Lines.Count));
        Assert.All(result.Pages, page => Assert.NotEmpty(page.Lines));
    }

    [Fact]
    public void Continuous_output_does_not_move_a_kept_table()
    {
        Assert.Equal(9, Assert.Single(Layout(TableAfterFiller(true), continuous: true).Pages).Lines.Count);
    }

    [Fact]
    public void Padding_offsets_text_and_grows_the_row_without_insetting_its_border()
    {
        var cell = new TableCell(0, 1, 0, padding: new TableCellPadding(3, 7, 11, 13));
        RichTextDocument document = RichTextDocument.FromParagraphs([Para(1), Para(1)])
            .WithTables([new DocumentTable(0, 1, [new TableRow([cell])], [180], cellPadding: 0)]);
        LayoutPage page = Assert.Single(Layout(document).Pages);
        Assert.Equal(13, page.Lines[0].Pieces[0].X, 3);
        Assert.Equal(17, page.Lines[0].Top, 3);
        Assert.Equal(30, page.Cells[0].Bounds.Height, 3);
        Assert.Equal(10, page.Cells[0].Bounds.Left, 3);
        Assert.Equal(40, page.Lines[1].Top, 3);
    }

    [Fact]
    public void Padded_rows_split_without_losing_lines_or_nested_boxes()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs([Para(14)])
            .WithTables([new DocumentTable(0, 1, [new TableRow([
                new TableCell(0, 1, 0, padding: new TableCellPadding(0, 5, 0, 5))])], [180])]);
        LayoutResult result = Layout(document);
        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(14, result.Pages.Sum(page => page.Lines.Count));
        Assert.All(result.Pages.SelectMany(page => page.Lines), line => Assert.InRange(line.Top, 10, 80));
    }
    [Fact]
    public void Table_KeepTogether_Moves_Entire_Table_To_Fresh_Page_When_Fitting_A_Page()
    {
        RichTextParagraph filler = Para(5);
        RichTextParagraph r0 = Para(2);
        RichTextParagraph r1 = Para(2);
        var tableOrdinary = new DocumentTable(1, 2,
            [new TableRow([new TableCell(1, 1, 0)]), new TableRow([new TableCell(2, 1, 0)])],
            [180], cellPadding: 0, keepTogether: false);
        var tableKept = new DocumentTable(1, 2,
            [new TableRow([new TableCell(1, 1, 0)]), new TableRow([new TableCell(2, 1, 0)])],
            [180], cellPadding: 0, keepTogether: true);

        LayoutResult ordinaryResult = Layout(RichTextDocument.FromParagraphs([filler, r0, r1]).WithTables([tableOrdinary]));
        LayoutResult keptResult = Layout(RichTextDocument.FromParagraphs([filler, r0, r1]).WithTables([tableKept]));

        Assert.Equal(2, ordinaryResult.Pages.Count);
        Assert.Equal(8, ordinaryResult.Pages[0].Lines.Count); // 5 filler + 2 row 0 + 1 line of split row 1
        Assert.Single(ordinaryResult.Pages[1].Lines); // 1 remaining line of row 1

        Assert.Equal(2, keptResult.Pages.Count);
        Assert.Equal(5, keptResult.Pages[0].Lines.Count);
        Assert.Empty(keptResult.Pages[0].Cells);
        Assert.Equal(4, keptResult.Pages[1].Lines.Count);
        Assert.Equal(2, keptResult.Pages[1].Cells.Count);
    }

    [Fact]
    public void Table_KeepTogether_Taller_Than_Page_Splits_Across_Pages_Without_Freezing()
    {
        RichTextParagraph filler = Para(3);
        RichTextParagraph r0 = Para(6);
        RichTextParagraph r1 = Para(6);
        var table = new DocumentTable(1, 2,
            [new TableRow([new TableCell(1, 1, 0)]), new TableRow([new TableCell(2, 1, 0)])],
            [180], cellPadding: 0, keepTogether: true);

        LayoutResult result = Layout(RichTextDocument.FromParagraphs([filler, r0, r1]).WithTables([table]));

        Assert.True(result.Pages.Count >= 2);
        Assert.Equal(15, result.Pages.Sum(p => p.Lines.Count));
    }

    [Fact]
    public void TableRow_KeepWithNext_Moves_Row_To_Next_Page_When_Subsequent_Row_Cannot_Fit()
    {
        RichTextParagraph filler = Para(5);
        RichTextParagraph r0 = Para(2);
        RichTextParagraph r1 = Para(2);

        var tableWithoutKeep = new DocumentTable(1, 2,
            [new TableRow([new TableCell(1, 1, 0)], keepWithNext: false),
             new TableRow([new TableCell(2, 1, 0)])],
            [180], cellPadding: 0);

        var tableWithKeep = new DocumentTable(1, 2,
            [new TableRow([new TableCell(1, 1, 0)], keepWithNext: true),
             new TableRow([new TableCell(2, 1, 0)])],
            [180], cellPadding: 0);

        LayoutResult withoutKeepResult = Layout(RichTextDocument.FromParagraphs([filler, r0, r1]).WithTables([tableWithoutKeep]));
        LayoutResult withKeepResult = Layout(RichTextDocument.FromParagraphs([filler, r0, r1]).WithTables([tableWithKeep]));

        Assert.Equal(8, withoutKeepResult.Pages[0].Lines.Count); // 5 filler + 2 row 0 + 1 line of split row 1
        Assert.Single(withoutKeepResult.Pages[1].Lines); // 1 remaining line of row 1

        Assert.Equal(5, withKeepResult.Pages[0].Lines.Count);
        Assert.Equal(4, withKeepResult.Pages[1].Lines.Count);
    }

    [Fact]
    public void TableRow_KeepWithNext_Chain_Keeps_Multiple_Rows_Together()
    {
        RichTextParagraph filler = Para(3);
        RichTextParagraph r0 = Para(2);
        RichTextParagraph r1 = Para(2);
        RichTextParagraph r2 = Para(2);

        var table = new DocumentTable(1, 3,
            [new TableRow([new TableCell(1, 1, 0)], keepWithNext: true),
             new TableRow([new TableCell(2, 1, 0)], keepWithNext: true),
             new TableRow([new TableCell(3, 1, 0)])],
            [180], cellPadding: 0);

        LayoutResult result = Layout(RichTextDocument.FromParagraphs([filler, r0, r1, r2]).WithTables([table]));

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(3, result.Pages[0].Lines.Count);
        Assert.Equal(6, result.Pages[1].Lines.Count);
    }

    [Fact]
    public void Heading_KeepWithNext_Moves_With_KeepTogether_Table()
    {
        RichTextParagraph filler = Para(4);
        RichTextParagraph heading = Para(1).WithParagraphStyle(ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 10, KeepWithNext = true });
        RichTextParagraph r0 = Para(2);
        RichTextParagraph r1 = Para(2);

        var table = new DocumentTable(2, 2,
            [new TableRow([new TableCell(2, 1, 0)]),
             new TableRow([new TableCell(3, 1, 0)])],
            [180], cellPadding: 0, keepTogether: true);

        LayoutResult result = Layout(RichTextDocument.FromParagraphs([filler, heading, r0, r1]).WithTables([table]));

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(4, result.Pages[0].Lines.Count);
        Assert.Equal(5, result.Pages[1].Lines.Count);
    }

    [Fact]
    public void Nested_Table_With_KeepTogether_Is_Not_Cut_By_Outer_Row()
    {
        RichTextParagraph filler = Para(5);
        RichTextParagraph n0 = Para(2);
        RichTextParagraph n1 = Para(2);

        var innerTable = new DocumentTable(1, 2,
            [new TableRow([new TableCell(1, 1, 0)]),
             new TableRow([new TableCell(2, 1, 0)])],
            [160], cellPadding: 0, keepTogether: true);

        var outerTable = new DocumentTable(1, 2,
            [new TableRow([new TableCell(1, 2, 0, tables: [innerTable])], canSplit: true)],
            [180], cellPadding: 0);

        LayoutResult result = Layout(RichTextDocument.FromParagraphs([filler, n0, n1]).WithTables([outerTable]));

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(5, result.Pages[0].Lines.Count);
        Assert.Equal(4, result.Pages[1].Lines.Count);
    }

    [Fact]
    public void A_tables_spacing_stands_above_and_below_its_grid()
    {
        // fdo114306_2.odt sets its signature table 2.2 cm below the text with
        // fo:margin-top on the table.
        RichTextDocument document = RichTextDocument.FromParagraphs([Para(1), Para(1), Para(1)])
            .WithTables([new DocumentTable(1, 1, [new TableRow([new TableCell(1, 1, 0)])], [180], cellPadding: 0,
                spacingBefore: 15, spacingAfter: 5)]);

        LayoutPage page = Assert.Single(Layout(document).Pages);

        Assert.Equal([10, 35, 50], page.Lines.Select(line => Math.Round(line.Top, 3)));
        Assert.Equal(35, page.Cells[0].Bounds.Top, 3);
    }

    [Fact]
    public void A_tables_spacing_above_opens_no_page()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs([Para(1), Para(1)])
            .WithTables([new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0)])], [180], cellPadding: 0,
                spacingBefore: 15)]);

        LayoutPage page = Assert.Single(Layout(document).Pages);

        Assert.Equal(10, page.Lines[0].Top, 3);
    }
}
