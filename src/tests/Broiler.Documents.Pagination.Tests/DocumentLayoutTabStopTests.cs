namespace Broiler.Documents.Pagination.Tests;

public sealed class DocumentLayoutTabStopTests
{
    [Theory]
    [InlineData(TabAlignment.Left)]
    [InlineData(TabAlignment.Center)]
    [InlineData(TabAlignment.Right)]
    [InlineData(TabAlignment.Decimal)]
    public void Explicit_Tabs_Align_The_Whole_Styled_Text_Segment(TabAlignment alignment)
    {
        var paragraph = RichTextParagraph.Create("label\t12.5 units", InlineStyle.Default,
            ParagraphStyle.Default with { TabStops = new ParagraphTabStops([new(140, alignment, ".")]) })
            .ApplyInlineStyle(8, 2, new InlineStyleDelta { Bold = true });
        LayoutLine line = Assert.Single(Layout(paragraph).Pages).Lines.Single();
        var tab = Assert.Single(line.Pieces, piece => piece.IsTab);
        var text = line.Pieces.Where(piece => piece.X > tab.X && !piece.IsTab).ToArray();
        double start = text[0].X;
        double width = text.Sum(piece => piece.Width);
        double decimalPrefix = Broiler.Graphics.Text.BTextMeasurer.MeasureAdvance("12", text[0].Font);
        double point = alignment switch
        {
            TabAlignment.Right => start + width,
            TabAlignment.Center => start + width / 2,
            TabAlignment.Decimal => start + decimalPrefix,
            _ => start,
        };
        Assert.Equal(150, point, 3);
        Assert.Equal(".", tab.TabLeader);
    }

    [Fact]
    public void Tabs_Use_The_Text_Area_Origin_Even_With_A_First_Line_Indent()
    {
        var paragraph = RichTextParagraph.Create("\tx", InlineStyle.Default,
            ParagraphStyle.Default with { LeftIndent = 20, FirstLineIndent = 10,
                TabStops = new ParagraphTabStops([new(100)]) });
        LayoutLine line = Assert.Single(Assert.Single(Layout(paragraph).Pages).Lines);
        Assert.Equal(110, line.Pieces.Single(piece => piece.Text == "x").X, 3);
    }

    [Fact]
    public void An_Explicit_Tab_After_A_Manual_Line_Break_Still_Aligns_Its_Text()
    {
        var paragraph = RichTextParagraph.Create("first\u2028\tx", InlineStyle.Default,
            ParagraphStyle.Default with { TabStops = new ParagraphTabStops([new(100)]) });
        var lines = Assert.Single(Layout(paragraph).Pages).Lines;
        Assert.Equal(2, lines.Count);
        Assert.Equal(110, lines[1].Pieces.Single(piece => piece.Text == "x").X, 3);
    }

    [Fact]
    public void A_Right_Tab_At_The_Margin_Does_Not_Wrap_Its_Final_Word()
    {
        var paragraph = RichTextParagraph.Create("title\t123", InlineStyle.Default,
            ParagraphStyle.Default with { TabStops = new ParagraphTabStops([new(380, TabAlignment.Right)]) });
        LayoutLine line = Assert.Single(Assert.Single(Layout(paragraph).Pages).Lines);
        LayoutPiece last = line.Pieces[^1];
        Assert.Equal(390, last.X + last.Width, 3);
    }

    [Fact]
    public void A_Page_Wide_Right_Tab_Is_Brought_Back_To_A_Narrow_Frame_Edge()
    {
        var paragraph = RichTextParagraph.Create("title\t12", InlineStyle.Default,
            ParagraphStyle.Default with { TabStops = new ParagraphTabStops([new(1000, TabAlignment.Right, ".")]) });
        LayoutLine line = Assert.Single(Assert.Single(Layout(paragraph).Pages).Lines);
        Assert.Equal(390, line.Pieces[^1].X + line.Pieces[^1].Width, 3);
        Assert.Equal(".", line.Pieces.Single(piece => piece.IsTab).TabLeader);
    }

    [Fact]
    public void A_Tab_Leader_Is_Painted_Inside_The_Reserved_Gap()
    {
        var paragraph = RichTextParagraph.Create("title\t12", InlineStyle.Default,
            ParagraphStyle.Default with { TabStops = new ParagraphTabStops([new(180, TabAlignment.Right, ".")]) });
        var box = new DocumentPageBox(400, 200, 10, 10, 10, 10);
        using var images = new ImageStore();
        var page = Assert.Single(new DocumentLayout(new LayoutSettings(), images)
            .Layout(RichTextDocument.FromParagraphs([paragraph]), box).Pages);
        var commands = new Broiler.Graphics.RenderList.BRenderList();
        new DocumentPagePainter(new LayoutSettings(), images).Paint(page, box, commands, _ => null);
        var tab = Assert.Single(Assert.Single(page.Lines).Pieces, piece => piece.IsTab);
        Assert.Contains(commands.Commands.OfType<Broiler.Graphics.RenderList.BRenderCommand.DrawText>(),
            command => command.Text.Text.Length > 1 && command.Text.Text.All(c => c == '.'));
        Assert.True(tab.Width > 0);
    }

    private static LayoutResult Layout(RichTextParagraph paragraph)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(RichTextDocument.FromParagraphs([paragraph]),
            new DocumentPageBox(400, 200, 10, 10, 10, 10));
    }
}
