namespace Broiler.Documents.Pagination.Tests;

public sealed class DocumentLayoutPageStyleTests
{
    [Fact]
    public void A_Paragraph_Whose_First_Line_Overflows_Is_Measured_On_Its_Destination_Page()
    {
        var narrow = new PageGeometry(100, 80, 10, 10, 10, 10);
        var wide = new PageGeometry(300, 100, 20, 10, 20, 10);
        var sequence = new PageStyleSequence([new PageStyle(narrow, RunningContent.Empty),
            new PageStyle(wide, RunningContent.Empty)], 1);
        var first = RichTextParagraph.Create("filler", InlineStyle.Default,
            ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 55 });
        var second = RichTextParagraph.Create("one two three four five six seven", InlineStyle.Default,
            ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 15 });
        var document = RichTextDocument.FromParagraphs([first, second]).WithPageGeometry(narrow)
            .WithSections([new DocumentSection(0, 2, 1, 0, narrow, pageStyles: sequence)]);
        using var images = new ImageStore();
        var pages = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(100, 80, 10, 10, 10, 10)).Pages;
        Assert.Equal(2, pages.Count);
        Assert.Single(pages[0].Lines);
        LayoutLine line = Assert.Single(pages[1].Lines);
        Assert.Equal("one two three four five six seven", string.Concat(line.Pieces.Select(piece => piece.Text)));
        Assert.True(line.Pieces[0].X >= 20);
    }

    [Fact]
    public void An_Explicit_Page_Break_Uses_The_Next_Page_Styles_Text_Width()
    {
        var wide = new PageGeometry(300, 100, 10, 10, 10, 10);
        var narrow = new PageGeometry(100, 100, 10, 10, 10, 10);
        var sequence = new PageStyleSequence([new PageStyle(wide, RunningContent.Empty),
            new PageStyle(narrow, RunningContent.Empty)], 1);
        var document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("first"),
            RichTextParagraph.Create("one two three four five six seven", InlineStyle.Default,
                ParagraphStyle.Default with { PageBreakBefore = true })]).WithPageGeometry(wide)
            .WithSections([new DocumentSection(0, 2, 1, 0, wide, pageStyles: sequence)]);
        using var images = new ImageStore();
        var pages = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(300, 100, 10, 10, 10, 10)).Pages;
        Assert.Equal(2, pages.Count);
        Assert.True(pages[1].Lines.Count > 1);
        Assert.All(pages[1].Lines.SelectMany(line => line.Pieces), piece => Assert.True(piece.X + piece.Width <= 90.001));
    }

    [Fact]
    public void A_Fixed_Row_Does_Not_Clip_A_Cell_That_Spans_The_Next_Row()
    {
        var style = ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 10 };
        var document = RichTextDocument.FromParagraphs([
            RichTextParagraph.Create("m1\u2028m2\u2028m3", InlineStyle.Default, style),
            RichTextParagraph.Create("n1\u2028n2\u2028n3", InlineStyle.Default, style)])
            .WithTables([new DocumentTable(0, 2, [
                new TableRow([new TableCell(0, 1, 0, rowSpan: 2), new TableCell(1, 1, 1)], minHeight: 15, exactHeight: true),
                new TableRow([new TableCell(2, 0, 1)], minHeight: 15, exactHeight: true)], [60, 60], cellPadding: 0)]);
        using var images = new ImageStore();
        var page = Assert.Single(new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10)).Pages);
        Assert.Contains(page.Lines.SelectMany(line => line.Pieces), piece => piece.Text == "m3");
        Assert.DoesNotContain(page.Lines.SelectMany(line => line.Pieces), piece => piece.Text == "n3");
        Assert.Equal(30, page.Cells[0].Bounds.Height, 3);
    }

    [Fact]
    public void Text_Box_Insets_And_Paragraph_Spacing_Are_Measured_Inside_The_Frame()
    {
        var style = ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 10, SpacingBefore = 5, SpacingAfter = 3 };
        var document = RichTextDocument.FromPlainText("body").WithShapes([
            new DocumentShape(0, 0, 0, 100, 1, paragraphs: [RichTextParagraph.Create("A", InlineStyle.Default, style)],
                growsToText: true, textPadding: new TableCellPadding(7, 4, 9, 6))]);
        using var images = new ImageStore();
        var page = Assert.Single(new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10)).Pages);
        var shape = Assert.Single(page.Shapes);
        var line = Assert.Single(shape.Lines);
        Assert.Equal(28, shape.Bounds.Height, 3);
        Assert.Equal(shape.Bounds.Top + 9, line.Top, 3);
        Assert.Equal(shape.Bounds.Left + 7, line.Pieces[0].X, 3);
    }

    [Fact]
    public void Lists_Inside_Text_Boxes_Are_Numbered_In_Their_Own_Flow()
    {
        var list = new ListDefinition([new ListLevel { Template = "%1." }]);
        var style = ParagraphStyle.Default with { List = new ListReference(list, 0), ListKind = ListKind.Numbered };
        var document = RichTextDocument.FromParagraphs([RichTextParagraph.Create("body", InlineStyle.Default, style)])
            .WithShapes([new DocumentShape(0, 0, 30, 150, 60, paragraphs: [
                RichTextParagraph.Create("first", InlineStyle.Default, style), RichTextParagraph.Create("second", InlineStyle.Default, style)])]);
        using var images = new ImageStore();
        var page = Assert.Single(new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 150, 10, 10, 10, 10)).Pages);
        Assert.Equal(new[] { "1.", "2." }, Assert.Single(page.Shapes).Lines.Select(line => line.Pieces[0].Text));
        Assert.Equal("1.", page.Lines[0].Pieces[0].Text);
    }

    [Fact]
    public void A_Repeating_Page_Style_Changes_Paper_Margins_And_Running_Content()
    {
        var a = new PageGeometry(200, 100, 10, 10, 10, 10);
        var b = new PageGeometry(300, 120, 20, 20, 20, 20);
        var head = RunningContent.Empty.WithHeader(PageSelection.Default, [RichTextParagraph.Plain("header A")]);
        var foot = RunningContent.Empty.WithFooter(PageSelection.Default, [RichTextParagraph.Plain("footer B")]);
        var sequence = new PageStyleSequence([new PageStyle(a, head), new PageStyle(b, foot)], 0);
        var paragraphs = Enumerable.Range(0, 4).Select(i => RichTextParagraph.Create("body " + i,
            InlineStyle.Default, ParagraphStyle.Default with { PageBreakBefore = i > 0 })).ToArray();
        var document = RichTextDocument.FromParagraphs(paragraphs).WithPageGeometry(a)
            .WithSections([new DocumentSection(0, 4, 1, 0, a, head, pageStyles: sequence)]);
        using var images = new ImageStore();
        var pages = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10)).Pages;
        Assert.Equal(4, pages.Count);
        Assert.Equal(new[] { 200d, 300, 200, 300 }, pages.Select(p => p.WidthPoints));
        Assert.Equal(new[] { 100d, 120, 100, 120 }, pages.Select(p => p.HeightPoints));
        Assert.Contains(pages[0].Lines.SelectMany(line => line.Pieces), piece => piece.Text.Contains("header"));
        Assert.DoesNotContain(pages[1].Lines.SelectMany(line => line.Pieces), piece => piece.Text.Contains("header"));
        Assert.Contains(pages[1].Lines.SelectMany(line => line.Pieces), piece => piece.Text.Contains("footer"));
    }

    [Fact]
    public void Exact_Table_Rows_Clip_Overflow_Without_Removing_Model_Text()
    {
        var document = RichTextDocument.FromParagraphs([
            RichTextParagraph.Create("first\u2028second\u2028third", InlineStyle.Default,
                ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 10 }),
            RichTextParagraph.Plain("after")]).WithTables([
                new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0)], minHeight: 15, exactHeight: true)], [100], cellPadding: 0)]);
        using var images = new ImageStore();
        var page = Assert.Single(new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10)).Pages);
        Assert.Equal(15, Assert.Single(page.Cells).Bounds.Height);
        Assert.Equal(3, page.Lines.Count);
        Assert.Equal(5, page.Lines[1].ClipHeight);
        Assert.Equal(25, page.Lines[2].Top, 3);
        Assert.Contains("third", document.PlainText);
    }

    [Fact]
    public void Hidden_Runs_Do_Not_Contribute_Width_Or_Font_Height()
    {
        var paragraph = RichTextParagraph.Plain("visible")
            .InsertText(0, "hidden long text", InlineStyle.Default with { Hidden = true, FontSize = 100 });
        using var images = new ImageStore();
        var page = Assert.Single(new DocumentLayout(new LayoutSettings(), images).Layout(
            RichTextDocument.FromParagraphs([paragraph]), new DocumentPageBox(200, 100, 10, 10, 10, 10)).Pages);
        var line = Assert.Single(page.Lines);
        Assert.Equal("visible", string.Concat(line.Pieces.Select(p => p.Text)));
        Assert.True(line.Height < 30);
    }
}
