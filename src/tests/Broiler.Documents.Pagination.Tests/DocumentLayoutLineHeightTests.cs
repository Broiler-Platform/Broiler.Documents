namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// A paragraph that states its line height is laid out at it. An exact line
/// sets its baseline four fifths of the way down, as LibreOffice does
/// (SwTextFormatter::CalcRealHeight); an at-least line keeps its text at the
/// top and the extra below.
/// </summary>
public sealed class DocumentLayoutLineHeightTests
{
    private static readonly DocumentPageBox Letter = new(612, 792, 72, 72, 72, 72);

    private static LayoutPage Layout(ParagraphStyle style, string text = "one two three")
    {
        using var images = new ImageStore();
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create(text, InlineStyle.Default with { FontSize = 12f }, style),
        ]);
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, Letter).Pages[0];
    }

    [Fact]
    public void An_Exact_Line_Is_Its_Height_With_Its_Baseline_Four_Fifths_Down()
    {
        LayoutLine line = Assert.Single(Layout(
            ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 10f }).Lines);

        Assert.Equal(10, line.Height, 6);
        Assert.Equal(8, line.Baseline, 6);
    }

    [Fact]
    public void An_At_Least_Line_Grows_To_Its_Height_And_Keeps_Its_Baseline()
    {
        LayoutLine natural = Assert.Single(Layout(ParagraphStyle.Default).Lines);
        LayoutLine tall = Assert.Single(Layout(
            ParagraphStyle.Default with { LineHeightRule = LineHeightRule.AtLeast, LineHeight = 40f }).Lines);
        LayoutLine small = Assert.Single(Layout(
            ParagraphStyle.Default with { LineHeightRule = LineHeightRule.AtLeast, LineHeight = 1f }).Lines);

        Assert.Equal(40, tall.Height, 6);
        Assert.Equal(natural.Baseline, tall.Baseline, 6);
        Assert.Equal(natural.Height, small.Height, 6);
    }

    [Fact]
    public void Exact_Lines_Follow_One_Another_At_Their_Height()
    {
        LayoutPage page = Layout(
            ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 10f },
            string.Join(' ', Enumerable.Repeat("word", 400)));

        Assert.True(page.Lines.Count > 2);
        Assert.Equal(10, page.Lines[1].Top - page.Lines[0].Top, 6);
        Assert.Equal(10, page.Lines[2].Top - page.Lines[1].Top, 6);
    }
}
