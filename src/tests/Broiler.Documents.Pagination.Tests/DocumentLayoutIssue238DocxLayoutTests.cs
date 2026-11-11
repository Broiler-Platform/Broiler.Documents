namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Issue #238: the space between two paragraphs where it collapses, a line's
/// height beside a larger tab, and a frame that narrows to its text.
/// </summary>
public sealed class DocumentLayoutIssue238DocxLayoutTests
{
    private static readonly DocumentPageBox Letter = new(612, 792, 72, 72, 72, 72);

    private static LayoutResult Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, Letter);
    }

    private static RichTextParagraph Spaced(string text, double before, double after) =>
        RichTextParagraph.Create(text, InlineStyle.Default, ParagraphStyle.Default with
        {
            SpacingBefore = (float)before,
            SpacingAfter = (float)after,
            LineHeightRule = LineHeightRule.Exact,
            LineHeight = 12,
        });

    [Theory]
    [InlineData(false, 12 + 6 + 6)]
    [InlineData(true, 12 + 6)]
    public void The_Space_Between_Paragraphs_Is_The_Larger_Of_Below_And_Above_Where_It_Collapses(bool collapses, double pitch)
    {
        // tdf164905.docx's table of contents entries have 6 pt above and below
        // and stand 6 pt apart in LibreOffice, as a Word document without
        // w:doNotUseHTMLParagraphAutoSpacing lays them out.
        RichTextDocument document = RichTextDocument.FromParagraphs([Spaced("one", 6, 6), Spaced("two", 6, 6), Spaced("three", 6, 6)])
            .WithStyleDefaults(DocumentStyleDefaults.Default with { CollapsesParagraphSpacing = collapses });

        IReadOnlyList<LayoutLine> lines = Assert.Single(Layout(document).Pages).Lines;

        Assert.Equal(pitch, lines[1].Top - lines[0].Top, 3);
        Assert.Equal(pitch, lines[2].Top - lines[1].Top, 3);
    }

    [Fact]
    public void A_Larger_Space_Above_Still_Wins_Where_Spacing_Collapses()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs([Spaced("body", 0, 10), Spaced("heading", 24, 0)])
            .WithStyleDefaults(DocumentStyleDefaults.Default with { CollapsesParagraphSpacing = true });

        IReadOnlyList<LayoutLine> lines = Assert.Single(Layout(document).Pages).Lines;

        Assert.Equal(12 + 24, lines[1].Top - lines[0].Top, 3);
    }

    [Fact]
    public void Collapsed_Spacing_Applies_Between_Paragraphs_In_A_Cell()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs([Spaced("one", 6, 6), Spaced("two", 6, 6)])
            .WithStyleDefaults(DocumentStyleDefaults.Default with { CollapsesParagraphSpacing = true })
            .WithTables([new DocumentTable(0, 1, [new TableRow([new TableCell(0, 2, 0)])], [200])]);

        IReadOnlyList<LayoutLine> lines = Assert.Single(Layout(document).Pages).Lines;

        Assert.Equal(12 + 6, lines[1].Top - lines[0].Top, 3);
    }

    private static RichTextParagraph WithBigTab(string before, string after)
    {
        var small = InlineStyle.Default with { FontSize = 9 };
        var big = InlineStyle.Default with { FontSize = 30 };
        return RichTextParagraph.Create(before, small)
            .InsertText(before.Length, "\t", big)
            .InsertText(before.Length + 1, after, small);
    }

    [Fact]
    public void A_Tab_Set_Larger_Than_The_Text_Beside_It_Does_Not_Make_The_Line_Taller()
    {
        // Writer sizes a line by its text: tdf164905.docx's table of contents
        // entries have 11 pt tab runs before and between 9 pt words.
        double plain = Assert.Single(Assert.Single(Layout(RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("entry 3", InlineStyle.Default with { FontSize = 9 })])).Pages).Lines).Height;

        foreach (RichTextParagraph paragraph in new[] { WithBigTab("", "entry"), WithBigTab("entry", "3"), WithBigTab("entry", "") })
        {
            LayoutLine line = Assert.Single(Assert.Single(Layout(RichTextDocument.FromParagraphs([paragraph])).Pages).Lines);
            Assert.Equal(plain, line.Height, 3);
        }
    }

    [Fact]
    public void A_Line_Of_Nothing_But_A_Tab_Keeps_The_Tabs_Height()
    {
        double small = Assert.Single(Assert.Single(Layout(RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("x", InlineStyle.Default with { FontSize = 9 })])).Pages).Lines).Height;

        LayoutLine line = Assert.Single(Assert.Single(Layout(RichTextDocument.FromParagraphs([WithBigTab("", "")])).Pages).Lines);

        Assert.True(line.Height > small);
    }

    [Fact]
    public void A_Table_In_A_Footnote_Is_Drawn_As_A_Table_In_The_Footnote_Area()
    {
        // tdf136778.docx: the note's label paragraph, then a 2 x 2 grid whose
        // cells stand side by side under it.
        var border = new TableBorder(Broiler.Graphics.Color.BColor.Black, 1);
        var note = new DocumentNote(NoteKind.Footnote,
            [RichTextParagraph.Plain(""), RichTextParagraph.Plain("a"), RichTextParagraph.Plain("b"),
             RichTextParagraph.Plain("c"), RichTextParagraph.Plain("d")])
        {
            Tables = [new DocumentTable(1, 4,
                [new TableRow([new TableCell(1, 1, 0, borders: CellBorders.All(border)), new TableCell(2, 1, 1, borders: CellBorders.All(border))]),
                 new TableRow([new TableCell(3, 1, 0, borders: CellBorders.All(border)), new TableCell(4, 1, 1, borders: CellBorders.All(border))])],
                [200, 200])],
        };
        RichTextParagraph citing = RichTextParagraph.Plain("text")
            .InsertText(4, DocumentNote.PlaceholderText, InlineStyle.Default with { Note = note });

        LayoutPage page = Assert.Single(Layout(RichTextDocument.FromParagraphs([citing])).Pages);

        LayoutLine a = page.Lines.Single(line => line.Pieces.Any(piece => piece.Text == "a"));
        LayoutLine b = page.Lines.Single(line => line.Pieces.Any(piece => piece.Text == "b"));
        LayoutLine c = page.Lines.Single(line => line.Pieces.Any(piece => piece.Text == "c"));
        Assert.Equal(a.Top, b.Top, 3);
        Assert.True(b.Pieces[0].X > a.Pieces[0].X + 150);
        Assert.True(c.Top > a.Top);
        Assert.True(a.Top > page.Lines[0].Top + 300);
        Assert.Equal(4, page.Cells.Count(cell => cell.Bounds.Width >= 199 && cell.Bounds.Top > a.Top - 20));
    }

    [Fact]
    public void A_Frame_That_Shrinks_To_Its_Text_Narrows_And_Wraps_At_Its_Width()
    {
        // tdf135035.docx: a Word frame without a width fits its one paragraph,
        // no wider than its column.
        var shortFrame = new DocumentShape(0, 0, 0, 120, 10, paragraphs: [RichTextParagraph.Plain("ab")], growsToText: true)
        {
            ShrinksWidthToText = true,
        };
        var longFrame = new DocumentShape(0, 0, 100, 120, 10, paragraphs: [RichTextParagraph.Plain(new string('a', 200))], growsToText: true)
        {
            ShrinksWidthToText = true,
        };
        RichTextDocument document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("body")])
            .WithShapes([shortFrame, longFrame]);

        LayoutPage page = Assert.Single(Layout(document).Pages);
        LayoutShape[] shapes = [.. page.Shapes];

        Assert.Equal(2, shapes.Length);
        Assert.True(shapes[0].Bounds.Width < 30);
        Assert.Equal(120, shapes[1].Bounds.Width, 3);
        Assert.True(shapes[1].Bounds.Height > 20);
    }

    [Theory]
    [InlineData(true, 72 + 50)]
    [InlineData(false, 72)]
    public void A_Page_Break_That_Keeps_The_Space_Above_Starts_Its_Page_Below_It(bool keeps, double top)
    {
        var opened = ParagraphStyle.Default with { PageBreakBefore = true, PageBreakKeepsSpaceAbove = keeps, SpacingBefore = 50 };
        RichTextDocument document = RichTextDocument.FromParagraphs(
                [RichTextParagraph.Plain("one"), RichTextParagraph.Create("two", InlineStyle.Default, opened)])
            .WithStyleDefaults(DocumentStyleDefaults.Default with { PreserveOpeningParagraphSpacing = true });

        IReadOnlyList<LayoutPage> pages = Layout(document).Pages;

        Assert.Equal(2, pages.Count);
        Assert.Equal(top, pages[1].Lines[0].Top, 3);
    }

    [Fact]
    public void A_Page_Number_In_The_Body_Shows_The_Page_Its_Paragraph_Starts_On()
    {
        // tdf170003_bottomSpacing.docx: Word saved "- 2 -" for PAGE \* ArabicDash,
        // which LibreOffice draws as the page's number.
        RichTextParagraph field = RichTextParagraph.Plain("page  end")
            .InsertText(5, "- 2 -", InlineStyle.Default with { PageField = new PageField(PageFieldKind.PageNumber) });
        RichTextParagraph second = field.WithParagraphStyle(ParagraphStyle.Default with { PageBreakBefore = true });
        RichTextDocument document = RichTextDocument.FromParagraphs([field, second]);

        IReadOnlyList<LayoutPage> pages = Layout(document).Pages;

        Assert.Equal("page 1 end", string.Concat(pages[0].Lines[0].Pieces.Select(piece => piece.Text)));
        Assert.Equal("page 2 end", string.Concat(pages[1].Lines[0].Pieces.Select(piece => piece.Text)));
    }
}
