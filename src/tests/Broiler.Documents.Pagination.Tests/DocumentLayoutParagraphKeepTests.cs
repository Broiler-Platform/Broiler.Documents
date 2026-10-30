namespace Broiler.Documents.Pagination.Tests;

public sealed class DocumentLayoutParagraphKeepTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_Heading_Stays_With_The_First_Row_Of_A_Following_Table(bool tallRow)
    {
        using var images = new ImageStore();
        var document = RichTextDocument.FromParagraphs([Para(7), Para(1, Style with { KeepWithNext = true }),
                Para(tallRow ? 12 : 2)])
            .WithTables([new DocumentTable(2, 1, [new TableRow([new TableCell(2, 1, 0)])], [180], cellPadding: 0)]);
        var result = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10));
        Assert.Equal(7, result.Pages[0].Lines.Count);
        Assert.Equal(1, result.Pages[1].Lines[0].ParagraphIndex);
        Assert.Contains(result.Pages[1].Lines, line => line.ParagraphIndex == 2);
        Assert.Equal(tallRow ? 20 : 10, result.Pages.Sum(page => page.Lines.Count));
    }

    private static ParagraphStyle Style => ParagraphStyle.Default with
    { LineHeightRule = LineHeightRule.Exact, LineHeight = 10 };
    private static RichTextParagraph Para(int lines, ParagraphStyle? style = null) =>
        RichTextParagraph.Create(string.Join("\u2028", Enumerable.Repeat("x", lines)), InlineStyle.Default, style ?? Style);
    private static LayoutResult Layout(params RichTextParagraph[] paragraphs)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(
            RichTextDocument.FromParagraphs(paragraphs), new DocumentPageBox(200, 100, 10, 10, 10, 10));
    }

    [Fact]
    public void A_Heading_Can_Stay_With_The_Start_Of_A_Row_That_Continues_On_The_Next_Page()
    {
        using var images = new ImageStore();
        var document = RichTextDocument.FromParagraphs([Para(6), Para(1, Style with { KeepWithNext = true }), Para(4)])
            .WithTables([new DocumentTable(2, 1, [new TableRow([new TableCell(2, 1, 0)])], [180], cellPadding: 0)]);
        var result = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10));
        Assert.Equal(new[] { 8, 3 }, result.Pages.Select(page => page.Lines.Count));
        Assert.Contains(result.Pages[0].Lines, line => line.ParagraphIndex == 1);
        Assert.Equal(2, result.Pages[0].Lines[^1].ParagraphIndex);
    }

    [Fact]
    public void A_Table_Page_Break_Wins_Over_The_Preceding_Heading_Keep()
    {
        using var images = new ImageStore();
        var document = RichTextDocument.FromParagraphs([Para(7), Para(1, Style with { KeepWithNext = true }), Para(2)])
            .WithTables([new DocumentTable(2, 1, [new TableRow([new TableCell(2, 1, 0)])], [180],
                cellPadding: 0, pageBreakBefore: true)]);
        var result = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10));
        Assert.Equal(new[] { 8, 2 }, result.Pages.Select(page => page.Lines.Count));
    }

    [Fact]
    public void Measuring_The_Following_Table_Does_Not_Consume_Its_List_Numbers()
    {
        using var images = new ImageStore();
        var definition = new ListDefinition([new ListLevel { Format = ListNumberFormat.Decimal, Template = "%1." }]);
        var numbered = Style with { List = new ListReference(definition, 0), ListKind = ListKind.Numbered };
        var document = RichTextDocument.FromParagraphs([Para(7), Para(1, numbered with { KeepWithNext = true }), Para(2, numbered)])
            .WithTables([new DocumentTable(2, 1, [new TableRow([new TableCell(2, 1, 0)])], [180], cellPadding: 0)]);
        var result = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10));
        Assert.Equal(new[] { "1.", "2." }, result.Pages.SelectMany(page => page.Lines).SelectMany(line => line.Pieces)
            .Select(piece => piece.Text).Where(text => text.EndsWith('.')));
    }

    [Fact]
    public void KeepTogether_moves_a_paragraph_that_fits_a_fresh_page()
    {
        LayoutResult result = Layout(Para(6), Para(3, Style with { KeepTogether = true }));
        Assert.Equal(new[] { 6, 3 }, result.Pages.Select(p => p.Lines.Count));
        Assert.Equal(10, result.Pages[1].Lines[0].Top, 3);
    }

    [Fact]
    public void KeepNext_moves_a_heading_with_the_following_paragraph()
    {
        LayoutResult result = Layout(Para(7), Para(1, Style with { KeepWithNext = true }), Para(2));
        Assert.Equal(new[] { 7, 3 }, result.Pages.Select(p => p.Lines.Count));
    }

    [Fact]
    public void KeepNext_chains_are_measured_together()
    {
        LayoutResult result = Layout(Para(6), Para(1, Style with { KeepWithNext = true }),
            Para(1, Style with { KeepWithNext = true }), Para(2));
        Assert.Equal(new[] { 6, 4 }, result.Pages.Select(p => p.Lines.Count));
    }

    [Fact]
    public void A_heading_stays_with_the_start_of_a_body_longer_than_a_page()
    {
        LayoutResult result = Layout(Para(7), Para(1, Style with { KeepWithNext = true }), Para(12));
        Assert.Equal(7, result.Pages[0].Lines.Count);
        Assert.Equal(1, result.Pages[1].Lines[0].ParagraphIndex);
    }

    [Fact]
    public void Keep_next_lookahead_does_not_consume_numbered_list_labels()
    {
        var definition = new ListDefinition([new ListLevel { Format = ListNumberFormat.Decimal, Template = "%1." }]);
        ParagraphStyle numbered = Style with { List = new ListReference(definition, 0), ListKind = ListKind.Numbered };
        LayoutResult result = Layout(Para(7), Para(1, numbered with { KeepWithNext = true }), Para(2, numbered));
        string[] labels = [.. result.Pages.SelectMany(page => page.Lines)
            .SelectMany(line => line.Pieces).Select(piece => piece.Text).Where(text => text.EndsWith('.'))];
        Assert.Equal(new[] { "1.", "2." }, labels);
    }

    [Fact]
    public void Keep_together_uses_the_next_column_before_opening_a_page()
    {
        using var images = new ImageStore();
        var page = new PageGeometry(200, 100, 10, 10, 10, 10, ColumnCount: 2, ColumnGap: 20);
        var document = RichTextDocument.FromParagraphs([Para(6), Para(3, Style with { KeepTogether = true })])
            .WithPageGeometry(page);
        LayoutResult result = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10));
        LayoutPage only = Assert.Single(result.Pages);
        Assert.All(only.Lines.Where(line => line.ParagraphIndex == 1),
            line => Assert.True(line.Pieces[0].X >= 110));
        Assert.Equal(10, only.Lines.First(line => line.ParagraphIndex == 1).Top, 3);
    }

    [Fact]
    public void Orphan_control_moves_a_paragraph_when_only_one_line_would_fit()
    {
        LayoutResult result = Layout(Para(7), Para(3, Style with { OrphanLines = 2 }));
        Assert.Equal(new[] { 7, 3 }, result.Pages.Select(p => p.Lines.Count));
    }

    [Fact]
    public void A_body_paragraph_does_not_take_the_document_default()
    {
        // DocumentStyleDefaults.WidowOrphanLines goes to floating tables' cells
        // only; a body paragraph keeps what it states.
        using var images = new ImageStore();
        var document = RichTextDocument.FromParagraphs([Para(7), Para(3)])
            .WithStyleDefaults(DocumentStyleDefaults.Default with { WidowOrphanLines = 2 });
        LayoutResult result = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10));
        Assert.Equal(new[] { 8, 2 }, result.Pages.Select(p => p.Lines.Count));
    }

    [Theory]
    [InlineData(true, 0, null, new[] { 9, 1 })]
    [InlineData(true, 2, null, new[] { 7, 3 })]
    [InlineData(true, 2, 0, new[] { 9, 1 })]
    [InlineData(false, 2, null, new[] { 9, 1 })]
    public void A_floating_row_whose_two_line_cell_cannot_keep_its_lines_moves_whole(
        bool floating, int lines, int? own, int[] pages)
    {
        // fdo68607.docx: one line of a two-line definition had room at the foot
        // of the page, and the row of its floating table broke there; Writer
        // keeps two lines in a floating table of a Word document and moves the
        // row whole, and turns the control off in a row of an inline table.
        using var images = new ImageStore();
        var document = RichTextDocument.FromParagraphs([Para(6), Para(1), Para(1), Para(2, Style with { OrphanLines = own, WidowLines = own })])
            .WithTables([new DocumentTable(1, 3, [new TableRow([new TableCell(1, 1, 0, columnSpan: 2)]),
                    new TableRow([new TableCell(2, 1, 0), new TableCell(3, 1, 1)])],
                [90, 90], cellPadding: 0,
                position: floating ? new TablePosition(TableAnchor.Margin, 0, TableAnchor.Text, 0) : null)])
            .WithStyleDefaults(DocumentStyleDefaults.Default with { WidowOrphanLines = lines });
        LayoutResult result = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10));
        Assert.Equal(pages, result.Pages.Select(p => p.Lines.Count));
    }

    [Fact]
    public void Widow_control_leaves_two_lines_for_the_next_page()
    {
        LayoutResult result = Layout(Para(5), Para(4, Style with { WidowLines = 2 }));
        Assert.Equal(new[] { 7, 2 }, result.Pages.Select(p => p.Lines.Count));
    }

    [Fact]
    public void Impossible_constraints_relax_without_blank_pages_or_lost_lines()
    {
        LayoutResult result = Layout(Para(20, Style with
        { KeepTogether = true, OrphanLines = 100, WidowLines = 100 }));
        Assert.Equal(new[] { 8, 8, 4 }, result.Pages.Select(p => p.Lines.Count));
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Explicit_page_breaks_win_over_keep_next()
    {
        LayoutResult result = Layout(Para(7), Para(1, Style with { KeepWithNext = true }),
            Para(1, Style with { PageBreakBefore = true }));
        Assert.Equal(new[] { 8, 1 }, result.Pages.Select(p => p.Lines.Count));
    }

    [Theory]
    [InlineData(6, 4, true, false, null, null, 6, 4)]
    [InlineData(7, 3, false, false, 2, null, 7, 3)]
    [InlineData(5, 4, false, false, null, 2, 7, 2)]
    public void Table_cell_paragraphs_obey_keep_and_line_count_rules(int filler, int bodyLines,
        bool together, bool next, int? orphans, int? widows, int firstCount, int secondCount)
    {
        using var images = new ImageStore();
        var document = RichTextDocument.FromParagraphs([Para(filler), Para(bodyLines, Style with
            { KeepTogether = together, KeepWithNext = next, OrphanLines = orphans, WidowLines = widows })])
            .WithTables([new DocumentTable(1, 1, [new TableRow([new TableCell(1, 1, 0)])], [100], cellPadding: 0)]);
        var result = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10));
        Assert.Equal(new[] { firstCount, secondCount }, result.Pages.Select(page => page.Lines.Count));
    }

    [Fact]
    public void Table_cell_keeps_a_heading_with_the_next_paragraph()
    {
        using var images = new ImageStore();
        var document = RichTextDocument.FromParagraphs([Para(7),
                Para(1, Style with { KeepWithNext = true }), Para(2)])
            .WithTables([new DocumentTable(1, 2, [new TableRow([new TableCell(1, 2, 0)])], [100], cellPadding: 0)]);
        var result = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10));
        Assert.Equal(new[] { 7, 3 }, result.Pages.Select(page => page.Lines.Count));
    }

    [Fact]
    public void Continuous_output_does_not_paginate_for_keep_rules()
    {
        using var images = new ImageStore();
        var document = RichTextDocument.FromParagraphs([Para(7), Para(3, Style with
        { KeepTogether = true, KeepWithNext = true, OrphanLines = 2, WidowLines = 2 })]);
        LayoutResult result = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10, continuous: true));
        Assert.Equal(10, Assert.Single(result.Pages).Lines.Count);
    }
}
