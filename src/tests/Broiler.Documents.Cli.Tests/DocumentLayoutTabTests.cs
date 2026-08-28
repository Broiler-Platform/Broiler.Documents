using Broiler.Documents.Cli.Rendering;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// Tabs in the render layout. A tab is a gap to the next stop rather than a
/// character with a width of its own, which is what lets a tabbed table line its
/// columns up — and what a pixel comparison of two exports depends on.
/// </summary>
public sealed class DocumentLayoutTabTests
{
    private const double TabStop = 36.0;

    private static LayoutResult Layout(RichTextDocument document, LayoutSettings? settings = null)
    {
        using var images = new ImageStore();
        return new DocumentLayout(settings ?? new LayoutSettings(), images).Layout(document, PageSetup.Default);
    }

    private static LayoutPiece Piece(LayoutResult result, string text) =>
        Assert.Single(result.Pages
            .SelectMany(page => page.Lines)
            .SelectMany(line => line.Pieces)
            .Where(piece => piece.Text == text));

    [Fact]
    public void Text_After_A_Tab_Starts_At_The_Next_Tab_Stop()
    {
        LayoutResult result = Layout(RichTextDocument.FromPlainText("a\tone"));

        Assert.Equal(Piece(result, "a").X + TabStop, Piece(result, "one").X, 3);
    }

    [Fact]
    public void Words_Of_Different_Lengths_Line_Up_On_The_Same_Tab_Stop()
    {
        LayoutResult result = Layout(RichTextDocument.FromPlainText("a\tone\nbc\ttwo"));

        Assert.Equal(Piece(result, "one").X, Piece(result, "two").X, 3);
    }

    [Fact]
    public void A_Word_That_Overruns_A_Stop_Pushes_The_Text_To_The_Following_One()
    {
        LayoutResult result = Layout(RichTextDocument.FromPlainText("a\tone\noverrunningword\ttwo"));

        // The long row's text clears the first stop and still lands on the grid,
        // rather than being pushed along by whatever the row happened to measure.
        double offset = Piece(result, "two").X - Piece(result, "overrunningword").X;
        Assert.True(offset > TabStop, $"expected the overrunning row past the first stop, got {offset}");
        Assert.Equal(0, (Math.Round(offset / TabStop) * TabStop) - offset, 3);
    }

    [Fact]
    public void A_Tab_That_Opens_A_Paragraph_Indents_It()
    {
        LayoutResult result = Layout(RichTextDocument.FromPlainText("plain\n\tindented"));

        Assert.Equal(Piece(result, "plain").X + TabStop, Piece(result, "indented").X, 3);
    }

    [Fact]
    public void The_Tab_Stop_Follows_The_Setting()
    {
        LayoutResult result = Layout(
            RichTextDocument.FromPlainText("a\tone"),
            new LayoutSettings { TabStopPoints = 100 });

        Assert.Equal(Piece(result, "a").X + 100, Piece(result, "one").X, 3);
    }

    [Fact]
    public void A_Tab_Draws_No_Glyphs_Of_Its_Own()
    {
        LayoutResult result = Layout(RichTextDocument.FromPlainText("a\tone"));

        Assert.DoesNotContain(
            result.Pages.SelectMany(page => page.Lines).SelectMany(line => line.Pieces),
            piece => piece.Text.Contains('\t'));
    }
}
