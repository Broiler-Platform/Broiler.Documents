namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// A paragraph's opening characters set as a drop cap, as <c>tdf150200.docx</c>
/// sets a 130-point dash over three lines of 12-point text: the lines beside it
/// start past it, and no line is as tall as the drop.
/// </summary>
public sealed class DocumentLayoutDropCapTests
{
    /// <summary>300 by 400 points, ten-point margins: a column 280 wide.</summary>
    private static readonly DocumentPageBox Page = new(300, 400, 10, 10, 10, 10);

    private static readonly ParagraphStyle Exact = ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 10 };

    private static RichTextParagraph Opened(string body, DropCap? dropCap)
    {
        var paragraph = RichTextParagraph.Empty.WithParagraphStyle(Exact with { DropCap = dropCap });
        paragraph = paragraph.InsertText(0, "W", InlineStyle.Default with { FontSize = 40 });
        return paragraph.InsertText(1, body, InlineStyle.Default with { FontSize = 10 });
    }

    private static LayoutPage Layout(params RichTextParagraph[] paragraphs)
    {
        using var images = new ImageStore();
        return Assert.Single(new DocumentLayout(new LayoutSettings(), images)
            .Layout(RichTextDocument.FromParagraphs(paragraphs), Page).Pages);
    }

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("word", count));

    [Fact]
    public void The_Lines_A_Drop_Cap_Spans_Start_Past_It_And_Keep_Their_Height()
    {
        LayoutPage page = Layout(Opened(Words(120), new DropCap(3, 1, 2)));

        LayoutPiece drop = page.Lines[0].Pieces[0];
        Assert.Equal("W", drop.Text);
        Assert.Equal(10, drop.X, 3);
        double textStart = drop.X + drop.Width + 2;
        for (int i = 0; i < 3; i++)
            Assert.Equal(textStart, page.Lines[i].Pieces.First(piece => piece.Text != "W").X, 3);
        Assert.Equal(10, page.Lines[3].Pieces[0].X, 3);
        Assert.All(page.Lines, line => Assert.Equal(10, line.Height, 3));

        // Its baseline is the third line's.
        Assert.Equal(-20, drop.Rise, 3);
    }

    [Fact]
    public void Without_A_Drop_Cap_The_Characters_Stay_Inline()
    {
        LayoutPage page = Layout(Opened(Words(120), null));

        Assert.Equal("W", page.Lines[0].Pieces[0].Text);
        Assert.Equal(0, page.Lines[0].Pieces[0].Rise, 3);
        Assert.Equal(10, page.Lines[1].Pieces[0].X, 3);
    }

    [Fact]
    public void A_Paragraph_Shorter_Than_Its_Drop_Cap_Is_As_Tall_As_The_Drop()
    {
        LayoutPage page = Layout(Opened("ord", new DropCap(3, 1, 0)), RichTextParagraph.Create("next", InlineStyle.Default, Exact));

        Assert.Equal(2, page.Lines.Count);
        Assert.Equal(30, page.Lines[0].Height, 3);
        Assert.Equal(40, page.Lines[1].Top, 3);
    }

    [Fact]
    public void A_Numbered_Paragraphs_Number_Precedes_The_Drop_Cap_At_The_Texts_Size()
    {
        // tdf109308_dropCapNumbered.docx: "1." then the large "L", then "orem".
        RichTextParagraph paragraph = Opened(Words(120), new DropCap(3, 1, 0));
        paragraph = paragraph.WithParagraphStyle(paragraph.Style with { ListKind = ListKind.Numbered });

        LayoutPage page = Layout(paragraph);

        IReadOnlyList<LayoutPiece> first = page.Lines[0].Pieces;
        Assert.StartsWith("1", first[0].Text);
        Assert.Equal(10, first[0].FontSizePoints, 3);
        Assert.Equal("W", first[1].Text);
        Assert.True(first[0].X + first[0].Width <= first[1].X);
        Assert.Equal(first[1].X + first[1].Width, first[2].X, 3);
        Assert.Equal(10, page.Lines[0].Height, 3);
    }

    [Fact]
    public void A_Margin_Drop_Cap_Hangs_Left_Of_Lines_That_Keep_Their_Place()
    {
        LayoutPage page = Layout(Opened(Words(120), new DropCap(3, 1, 0) { InMargin = true }));

        LayoutPiece drop = page.Lines[0].Pieces[0];
        Assert.Equal(10, drop.X + drop.Width, 3);
        Assert.Equal(10, page.Lines[0].Pieces[1].X, 3);
        Assert.Equal(10, page.Lines[1].Pieces[0].X, 3);
    }
}
