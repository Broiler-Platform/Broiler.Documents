namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// A paragraph whose text and mark are all hidden takes no room, as LibreOffice
/// hides it; one with hidden text and a shown mark is still an empty line.
/// </summary>
/// <remarks>
/// <c>fdo114306_2.odt</c> starts its approval sheet with such a paragraph, which
/// pushed the sheet a line down the page LibreOffice starts it at the top of.
/// </remarks>
public sealed class DocumentLayoutHiddenParagraphTests
{
    private static readonly InlineStyle Hidden = InlineStyle.Default with { Hidden = true };

    private static RichTextParagraph Line(string text) => RichTextParagraph.Plain(text);

    private static LayoutResult Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, new DocumentPageBox(300, 300, 20, 20, 20, 20));
    }

    private static double TopOf(LayoutResult result, int paragraph) =>
        result.Pages.SelectMany(page => page.Lines).First(line => line.ParagraphIndex == paragraph).Top;

    [Fact]
    public void A_Wholly_Hidden_Paragraph_Takes_No_Room()
    {
        RichTextParagraph hidden = RichTextParagraph.Create("hint", Hidden).WithMarkStyle(Hidden);
        RichTextParagraph emptyHidden = RichTextParagraph.Plain("").WithMarkStyle(Hidden);

        LayoutResult with = Layout(RichTextDocument.FromParagraphs([Line("a"), hidden, emptyHidden, Line("b")]));
        LayoutResult without = Layout(RichTextDocument.FromParagraphs([Line("a"), Line("b")]));

        Assert.Equal(TopOf(without, 1), TopOf(with, 3), 3);
        Assert.DoesNotContain(with.Pages.SelectMany(page => page.Lines), line => line.ParagraphIndex is 1 or 2);
    }

    [Fact]
    public void Hidden_Text_Under_A_Shown_Mark_Is_Still_An_Empty_Line()
    {
        RichTextParagraph hiddenText = RichTextParagraph.Create("hint", Hidden);

        LayoutResult result = Layout(RichTextDocument.FromParagraphs([Line("a"), hiddenText, Line("b")]));

        Assert.True(TopOf(result, 2) > TopOf(result, 1));
    }

    [Fact]
    public void A_Wholly_Hidden_Paragraph_Still_Breaks_The_Page_It_Asks_To()
    {
        RichTextParagraph breaking = RichTextParagraph.Plain("").WithMarkStyle(Hidden)
            .WithParagraphStyle(ParagraphStyle.Default with { PageBreakBefore = true });

        LayoutResult result = Layout(RichTextDocument.FromParagraphs([Line("a"), breaking, Line("b")]));

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(20, TopOf(result, 2), 3);
    }

    [Fact]
    public void A_Wholly_Hidden_Paragraph_In_A_Cell_Takes_No_Room()
    {
        RichTextParagraph hidden = RichTextParagraph.Create("0", Hidden).WithMarkStyle(Hidden);
        var table = new DocumentTable(0, 2, [new TableRow([new TableCell(0, 2, 0)])], [100], cellPadding: 0);

        LayoutPage page = Assert.Single(Layout(RichTextDocument.FromParagraphs([Line("a"), hidden, Line("b")]).WithTables([table])).Pages);
        LayoutPage plain = Assert.Single(Layout(RichTextDocument.FromParagraphs([Line("a"), Line("b")])
            .WithTables([new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0)])], [100], cellPadding: 0)])).Pages);

        Assert.Equal(plain.Cells[0].Bounds.Height, page.Cells[0].Bounds.Height, 3);
    }
}
