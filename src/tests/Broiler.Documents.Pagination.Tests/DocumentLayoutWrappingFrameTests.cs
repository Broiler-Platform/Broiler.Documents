namespace Broiler.Documents.Pagination.Tests;

public sealed class DocumentLayoutWrappingFrameTests
{
    private static RichTextParagraph Paragraph(string text) => RichTextParagraph.Create(text,
        InlineStyle.Default, ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 10 });

    [Fact]
    public void A_Wrapping_Frame_Moves_With_Its_Anchor_When_It_Does_Not_Fit()
    {
        var document = RichTextDocument.FromParagraphs([Paragraph("one\u2028two\u2028three"), Paragraph("after")])
            .WithShapes([new DocumentShape(1, 0, 0, 100, 60, wrap: ShapeWrap.TopAndBottom)]);
        using var images = new ImageStore();
        var pages = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10)).Pages;
        Assert.Equal(2, pages.Count);
        Assert.Empty(pages[0].Shapes);
        Assert.Single(pages[1].Shapes);
        Assert.Equal(70, pages[1].Lines[^1].Top, 3);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(200)]
    public void A_Frame_Bounded_By_Its_Page_Keeps_Its_Anchor_On_The_Page_It_Runs_Past(double height)
    {
        // tdf123243.docx: a text box that runs into the bottom margin, or off
        // the page, stays with its paragraph on page one.
        var document = RichTextDocument.FromParagraphs([Paragraph("one\u2028two\u2028three"), Paragraph("after")])
            .WithShapes([new DocumentShape(1, 0, 0, 40, height, wrap: ShapeWrap.Square) { BoundedByPage = true }]);
        using var images = new ImageStore();
        var pages = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10)).Pages;
        Assert.Single(pages[0].Shapes);
        Assert.Equal(40, pages[0].Shapes[0].Bounds.Y, 3);
        Assert.Contains(pages[0].Lines, line => line.Pieces.Any(piece => piece.Text == "after"));
    }

    [Fact]
    public void A_Top_And_Bottom_Frame_Bounded_By_Its_Page_Still_Moves_With_Its_Anchor()
    {
        var document = RichTextDocument.FromParagraphs([Paragraph("one\u2028two\u2028three"), Paragraph("after")])
            .WithShapes([new DocumentShape(1, 0, 0, 100, 60, wrap: ShapeWrap.TopAndBottom) { BoundedByPage = true }]);
        using var images = new ImageStore();
        var pages = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10)).Pages;
        Assert.Equal(2, pages.Count);
        Assert.Empty(pages[0].Shapes);
        Assert.Single(pages[1].Shapes);
    }

    [Fact]
    public void A_Paragraph_Whose_Space_Above_Passes_The_Foot_Takes_Its_Shapes_To_The_Next_Page()
    {
        // tdf115719.docx: the space above is as tall as the shapes hanging from it.
        RichTextParagraph spaced = RichTextParagraph.Create("", InlineStyle.Default,
            ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 10, SpacingBefore = 70 });
        var document = RichTextDocument.FromParagraphs([Paragraph("one"), spaced])
            .WithShapes([new DocumentShape(1, 0, 0, 40, 30, wrap: ShapeWrap.Square) { BoundedByPage = true }]);
        using var images = new ImageStore();
        var pages = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10)).Pages;
        Assert.Equal(2, pages.Count);
        Assert.Empty(pages[0].Shapes);
        Assert.Equal(10, Assert.Single(pages[1].Shapes).Bounds.Y, 3);
    }

    [Fact]
    public void A_Frame_Bounded_By_Its_Page_Anchored_Below_It_Moves_With_Its_Anchor()
    {
        var document = RichTextDocument.FromParagraphs([Paragraph("one\u2028two\u2028three"), Paragraph("after")])
            .WithShapes([new DocumentShape(1, 0, 70, 40, 10, wrap: ShapeWrap.Square) { BoundedByPage = true }]);
        using var images = new ImageStore();
        var pages = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10)).Pages;
        Assert.Equal(2, pages.Count);
        Assert.Empty(pages[0].Shapes);
        Assert.Single(pages[1].Shapes);
    }

    [Fact]
    public void An_Oversized_Frame_Keeps_Its_Page_And_The_Anchor_Text_Continues()
    {
        var document = RichTextDocument.FromParagraphs([Paragraph("before"), Paragraph("after")])
            .WithShapes([new DocumentShape(1, 0, 0, 100, 85, wrap: ShapeWrap.TopAndBottom)]);
        using var images = new ImageStore();
        var pages = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10)).Pages;
        Assert.Equal(3, pages.Count);
        Assert.Empty(pages[0].Shapes);
        Assert.Single(pages[1].Shapes);
        Assert.Empty(pages[2].Shapes);
        Assert.Equal(10, Assert.Single(pages[2].Lines).Top, 3);
    }

    [Fact]
    public void A_Run_Through_Frame_Does_Not_Reserve_Body_Space()
    {
        var document = RichTextDocument.FromParagraphs([Paragraph("before"), Paragraph("after")])
            .WithShapes([new DocumentShape(1, 0, 0, 100, 100)]);
        using var images = new ImageStore();
        var pages = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 100, 10, 10, 10, 10)).Pages;
        Assert.Single(pages);
        Assert.Single(pages[0].Shapes);
        Assert.Equal(2, pages[0].Lines.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Text_Box_Tables_Keep_Their_Cell_Grid(bool grows)
    {
        var table = new DocumentTable(0, 2, [new TableRow([
            new TableCell(0, 1, 0), new TableCell(1, 1, 1)])], [40, 60], cellPadding: 0);
        var document = RichTextDocument.FromPlainText("body").WithShapes([
            new DocumentShape(0, 5, 10, 100, grows ? 1 : 80,
                paragraphs: [Paragraph("A"), Paragraph("B")], growsToText: grows, tables: [table])]);
        using var images = new ImageStore();
        var page = Assert.Single(new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 200, 10, 10, 10, 10)).Pages);
        var shape = Assert.Single(page.Shapes);
        Assert.Equal(2, shape.Cells.Count);
        Assert.Equal(2, shape.Lines.Count);
        Assert.Equal(40, shape.Lines[1].Pieces[0].X - shape.Lines[0].Pieces[0].X, 3);
        Assert.Equal(shape.Lines[0].Top, shape.Lines[1].Top, 3);
        Assert.True(shape.Bounds.Height >= 10);
    }

    [Fact]
    public void As_Character_Shape_Advances_Line_Height_In_Paragraph()
    {
        // dateFormFormats: as-char shape expands line.Height to match shape height
        var shape = new DocumentShape(0, 0, 0, 100, 50) { IsAsCharacter = true };
        var document = RichTextDocument.FromParagraphs([Paragraph("first"), Paragraph("second")])
            .WithShapes([shape]);
        using var images = new ImageStore();
        var pages = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 200, 10, 10, 10, 10)).Pages;

        var page = Assert.Single(pages);
        Assert.True(page.Lines[1].Top >= 50);
    }

    [Fact]
    public void Page_Relative_Wrapping_Frame_Anchored_To_Later_Paragraph_Wraps_Preceding_Text_On_Opening_Page()
    {
        var shape = new DocumentShape(1, 0, 10, 50, 40, wrap: ShapeWrap.Square, offsetFromPage: true);
        var document = RichTextDocument.FromParagraphs([Paragraph("preceding text that wraps"), Paragraph("anchor text")])
            .WithShapes([shape]);
        using var images = new ImageStore();
        var pages = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(200, 200, 10, 10, 10, 10)).Pages;

        var page = Assert.Single(pages);
        Assert.Single(page.Shapes);
        Assert.True(page.Lines[0].Pieces[0].X >= 50);
    }

    [Fact]
    public void Wrapping_Frame_With_WrapMargins_Narrows_Second_Column_When_Paragraph_Continues_Across_Columns()
    {
        var shape = new DocumentShape(0, 80, 10, 40, 40, wrap: ShapeWrap.Square, offsetFromPage: true)
        {
            WrapMargins = new TableCellPadding(10, 0, 10, 0),
        };
        var pageGeometry = new PageGeometry(220, 120, 10, 10, 10, 10).WithColumns(2, 0);
        string longText = string.Join(" ", System.Linq.Enumerable.Repeat("word", 60));
        var document = RichTextDocument.FromParagraphs([Paragraph(longText)])
            .WithPageGeometry(pageGeometry)
            .WithShapes([shape]);
        using var images = new ImageStore();
        var pages = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            DocumentPageBox.Default.WithGeometry(pageGeometry)).Pages;

        var page = pages[0];
        // Second column starts at x = 10 + 100 = 110; shape spans [90..130] + 10pt right margin = 140
        Assert.Contains(page.Lines, line => line.Pieces.Count > 0 && line.Pieces[0].X >= 135);
    }
}
