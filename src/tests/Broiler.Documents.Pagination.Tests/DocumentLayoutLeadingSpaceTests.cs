namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Spaces at the start of a line: LibreOffice sets the ones that open a
/// paragraph, or a line a forced break opened, and drops only the space a
/// line wrapped at. <c>tdf123163-1.docx</c> opens every paragraph with one,
/// and its 129-point title was drawn a space's width too far left.
/// </summary>
public sealed class DocumentLayoutLeadingSpaceTests
{
    private static readonly DocumentPageBox Narrow = new(100, 400, 10, 10, 10, 10);

    private static LayoutPage Layout(string text)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images)
            .Layout(RichTextDocument.FromParagraphs([RichTextParagraph.Plain(text)]), Narrow).Pages[0];
    }

    private static double WordX(LayoutLine line, string word) =>
        line.Pieces.First(piece => piece.Text.Contains(word, StringComparison.Ordinal)).X;

    [Fact]
    public void A_Space_That_Opens_The_Paragraph_Is_Set()
    {
        double plain = WordX(Layout("x").Lines[0], "x");

        Assert.True(WordX(Layout("  x").Lines[0], "x") > plain + 1);
    }

    [Fact]
    public void A_Space_After_A_Forced_Break_Is_Set()
    {
        LayoutPage page = Layout("a" + (char)0x2028 + "  b");

        Assert.True(WordX(page.Lines[1], "b") > WordX(page.Lines[0], "a") + 1);
    }

    [Fact]
    public void The_Space_A_Line_Wrapped_At_Is_Dropped()
    {
        LayoutPage page = Layout("aaaaaaaa bbbbbbbb");

        Assert.Equal(2, page.Lines.Count);
        Assert.Equal(WordX(page.Lines[0], "a"), WordX(page.Lines[1], "b"), 3);
    }
}
