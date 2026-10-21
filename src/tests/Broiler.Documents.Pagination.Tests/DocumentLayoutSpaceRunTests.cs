namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// A document that lays runs of spaces out wider - an RTF document Word wrote -
/// gives each space in a run of two or more a sixth of an em more than its font
/// does, as LibreOffice does by putting a U+2006 in front of it (tdf#123703).
/// </summary>
public sealed class DocumentLayoutSpaceRunTests
{
    private static readonly DocumentPageBox Letter = new(612, 792, 72, 72, 72, 72);

    private static LayoutPage Layout(string text, bool widens, string? family = null)
    {
        using var images = new ImageStore();
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create(text, InlineStyle.Default with { FontSize = 12f, FontFamily = family }),
        ]).WithStyleDefaults(DocumentStyleDefaults.Default with { WidensSpaceRuns = widens });
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, Letter).Pages[0];
    }

    private static double LastWordX(LayoutPage page) => Assert.Single(page.Lines).Pieces[^1].X;

    [Fact]
    public void Each_Space_In_A_Run_Is_A_Sixth_Of_An_Em_Wider()
    {
        string text = "a" + new string(' ', 10) + "b";

        Assert.Equal(LastWordX(Layout(text, widens: false)) + 20, LastWordX(Layout(text, widens: true)), 6);
    }

    [Fact]
    public void A_Lone_Space_Keeps_Its_Width()
    {
        Assert.Equal(LastWordX(Layout("a b", widens: false)), LastWordX(Layout("a b", widens: true)), 6);
    }

    [Fact]
    public void Courier_Keeps_Its_Columns()
    {
        string text = "a" + new string(' ', 10) + "b";

        Assert.Equal(
            LastWordX(Layout(text, widens: false, "Courier New")),
            LastWordX(Layout(text, widens: true, "Courier New")),
            6);
    }

    [Fact]
    public void A_Wider_Run_Pushes_The_Next_Word_Onto_A_Line_Of_Its_Own()
    {
        // tdf123703.rtf: a word, a run of spaces that nearly fills the line, and
        // a second word that LibreOffice puts on the next line.
        string text = "word" + new string(' ', 90) + "word2";

        Assert.Single(Layout(text, widens: false).Lines);

        LayoutPage widened = Layout(text, widens: true);
        Assert.Equal(2, widened.Lines.Count);
        Assert.Equal("word2", widened.Lines[1].Pieces[0].Text);
        Assert.Equal(widened.Lines[0].Pieces[0].X, widened.Lines[1].Pieces[0].X, 6);
    }
}
