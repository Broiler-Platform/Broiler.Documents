namespace Broiler.Documents.Pagination.Tests;

public sealed class DocumentLayoutTextFrameTests
{
    private static RichTextParagraph Text(string text) => RichTextParagraph.Create(text, InlineStyle.Default,
        ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 10 });

    private static LayoutPage Layout(params DocumentShape[] shapes)
    {
        using var images = new ImageStore();
        return Assert.Single(new DocumentLayout(new LayoutSettings(), images).Layout(
            RichTextDocument.FromPlainText("body").WithShapes(shapes), new DocumentPageBox(300, 300, 10, 10, 10, 10)).Pages);
    }

    [Theory]
    [InlineData(CellVerticalAlignment.Top, 4)]
    [InlineData(CellVerticalAlignment.Center, 24)]
    [InlineData(CellVerticalAlignment.Bottom, 44)]
    public void Alignment_Uses_The_Padded_Interior(CellVerticalAlignment alignment, double offset)
    {
        var frame = Assert.Single(Layout(new DocumentShape(0, 0, 0, 100, 60, paragraphs: [Text("one")],
            textPadding: new TableCellPadding(3, 4, 3, 6), textVerticalAlignment: alignment)).Shapes);
        Assert.Equal(frame.Bounds.Top + offset, Assert.Single(frame.Lines).Top, 3);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 3)]
    public void Explicit_Overflow_Retains_Lines_Beyond_The_Frame(bool overflow, int lines)
    {
        var frame = Assert.Single(Layout(new DocumentShape(0, 0, 0, 100, 15,
            paragraphs: [Text("one\u2028two\u2028three")], textOverflow: overflow)).Shapes);
        Assert.Equal(lines, frame.Lines.Count);
        Assert.Equal(15, frame.Bounds.Height);
    }

    [Fact]
    public void Minimum_Width_Grows_To_An_Unwrapped_Word()
    {
        var frame = Assert.Single(Layout(new DocumentShape(0, 0, 0, 1, 1,
            paragraphs: [Text("Vestibulum")], growsToText: true, growsWidthToText: true)).Shapes);
        Assert.True(frame.Bounds.Width > 20);
        Assert.Equal(10, frame.Bounds.Height, 3);
        Assert.Single(frame.Lines);
    }

    [Fact]
    public void Empty_Link_Targets_Receive_Overflow_And_Keep_The_Source_Unchanged()
    {
        var source = new DocumentShape(0, 0, 0, 100, 20, paragraphs: [Text("one"), Text("two"), Text("three")],
            frameName: "first", nextFrameName: "second");
        var page = Layout(source, new DocumentShape(0, 110, 0, 100, 20, frameName: "second"));
        Assert.Equal(2, page.Shapes[0].Lines.Count);
        Assert.Equal("three", Assert.Single(page.Shapes[1].Lines).Pieces.Single().Text);
        Assert.Equal(3, source.Paragraphs.Count);
    }

    [Fact]
    public void A_Paragraph_Can_Continue_In_A_Narrower_Linked_Frame()
    {
        var page = Layout(new DocumentShape(0, 0, 0, 100, 10, paragraphs: [Text("one\u2028two\u2028three")],
            frameName: "first", nextFrameName: "second"),
            new DocumentShape(0, 110, 0, 50, 30, frameName: "second"));
        Assert.Equal("one", Assert.Single(page.Shapes[0].Lines).Pieces.Single().Text);
        Assert.Equal(new[] { "two", "three" }, page.Shapes[1].Lines.Select(line => line.Pieces.Single().Text));
    }

    [Fact]
    public void A_Cyclic_Chain_Is_Bounded()
    {
        var page = Layout(new DocumentShape(0, 0, 0, 100, 10, paragraphs: [Text("one"), Text("two")],
            frameName: "a", nextFrameName: "b"),
            new DocumentShape(0, 110, 0, 100, 10, frameName: "b", nextFrameName: "a"));
        Assert.Equal(new[] { "one", "two" }, page.Shapes.SelectMany(shape => shape.Lines).Select(line => line.Pieces.Single().Text));
    }

    [Fact]
    public void Linked_Frames_Continue_List_Counters_In_Their_Own_Flow()
    {
        var definition = new ListDefinition([new ListLevel { Template = "%1." }]);
        RichTextParagraph Item(string text) => Text(text).WithParagraphStyle(Text(text).Style with
            { ListKind = ListKind.Numbered, List = new ListReference(definition, 0) });
        var page = Layout(new DocumentShape(0, 0, 0, 100, 10, paragraphs: [Item("one"), Item("two")],
            frameName: "first", nextFrameName: "second"), new DocumentShape(0, 110, 0, 100, 20, frameName: "second"));
        Assert.Equal("1.", page.Shapes[0].Lines[0].Pieces[0].Text);
        Assert.Equal("2.", page.Shapes[1].Lines[0].Pieces[0].Text);
    }

    [Fact]
    public void A_Merged_Group_Moves_To_The_Next_Page_When_It_Fits_There()
    {
        var document = RichTextDocument.FromParagraphs([Text("before"), Text("one\u2028two\u2028three"), Text("neighbor"), Text("below")])
            .WithTables([new DocumentTable(1, 3, [
                new TableRow([new TableCell(1, 1, 0, rowSpan: 2), new TableCell(2, 1, 1)]),
                new TableRow([new TableCell(3, 1, 1)])], [100, 100], cellPadding: 0)]);
        using var images = new ImageStore();
        var pages = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(300, 55, 10, 10, 10, 10)).Pages;
        Assert.Equal(2, pages.Count);
        Assert.Empty(pages[0].Cells);
        Assert.Equal(3, pages[1].Cells.Count);
        Assert.Contains(pages[1].Lines.SelectMany(line => line.Pieces), piece => piece.Text == "three");
    }

    [Fact]
    public void A_Merged_Cell_Uses_The_Combined_Row_Height()
    {
        var document = RichTextDocument.FromParagraphs([Text("one\u2028two\u2028three"), Text("neighbor"), Text("below"), Text("after")])
            .WithTables([new DocumentTable(0, 3, [
                new TableRow([new TableCell(0, 1, 0, rowSpan: 2), new TableCell(1, 1, 1)]),
                new TableRow([new TableCell(2, 1, 1)])], [100, 100], cellPadding: 0)]);
        using var images = new ImageStore();
        var page = Assert.Single(new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(300, 300, 10, 10, 10, 10)).Pages);
        Assert.Equal(30, page.Cells[0].Bounds.Height, 3);
        Assert.Equal(10, page.Cells[1].Bounds.Height, 3);
        Assert.Equal(20, page.Cells[2].Bounds.Height, 3);
        Assert.Equal(40, page.Lines.Last().Top, 3);
    }
}
