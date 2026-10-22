namespace Broiler.Documents.Pagination.Tests;

public sealed class DocumentLayoutExactIndentTests
{
    private static LayoutResult Layout(ParagraphStyle style, string text = "one\u2028two")
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(
            RichTextDocument.FromParagraphs([RichTextParagraph.Create(text, InlineStyle.Default, style)]),
            new DocumentPageBox(200, 200, 10, 10, 10, 10));
    }

    [Theory]
    [InlineData(5, 22.5)]
    [InlineData(-5, 12.5)]
    public void Only_The_First_Line_Takes_The_Exact_First_Line_Offset(float first, double expected)
    {
        var lines = Layout(ParagraphStyle.Default with { LeftIndent = 7.5f, FirstLineIndent = first }).Pages[0].Lines;
        Assert.Equal(expected, lines[0].Pieces[0].X, 3);
        Assert.Equal(17.5, lines[1].Pieces[0].X, 3);
    }

    [Fact]
    public void An_Exact_Zero_Cancels_A_Legacy_Indent_And_A_Right_Indent_Reduces_Wrapping_Width()
    {
        var style = ParagraphStyle.Default with { IndentLevel = 3, LeftIndent = 0 };
        Assert.Equal(10, Layout(style).Pages[0].Lines[0].Pieces[0].X, 3);
        const string text = "one two three four five six seven eight nine ten";
        Assert.True(Layout(style with { RightIndent = 140 }, text).Pages[0].Lines.Count > Layout(style, text).Pages[0].Lines.Count);
    }

    [Fact]
    public void A_List_Marker_Hangs_Outside_The_Body_Indent_And_Continuation_Lines_Align_With_The_Body()
    {
        var lines = Layout(ParagraphStyle.Default with
        {
            ListKind = ListKind.Numbered, LeftIndent = 36, FirstLineIndent = -18,
        }).Pages[0].Lines;
        Assert.Equal(28, lines[0].Pieces[0].X, 3);
        Assert.Equal(46, lines[0].Pieces[1].X, 3);
        Assert.Equal(46, lines[1].Pieces[0].X, 3);
    }
}
