namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Style references in headers draw the text found on their own page, and a
/// Word 2013 document's justified lines shrink their spaces to take a word more.
/// </summary>
/// <remarks>
/// <c>tdf163894.docx</c> heads a dictionary's four pages with its first and last
/// entry word; the words it saved are right on the last page only. LibreOffice
/// breaks its justified lines by shrinking spaces, so a page holds a line or two
/// more than spaces kept at the font's width allow, and the words on each page
/// follow from that.
/// </remarks>
public sealed class DocumentLayoutStyleReferenceTests
{
    private static readonly DocumentPageBox Small = new(200, 240, 20, 20, 40, 40);

    private static readonly InlineStyle Entry = InlineStyle.Default with { CharacterStyle = "Entry" };

    private static LayoutResult Layout(RichTextDocument document, DocumentPageBox? box = null)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, box ?? Small);
    }

    private static string[] Headers(LayoutResult result) =>
        [.. result.Pages.Select(page => string.Concat(page.Lines
            .Where(line => line.ParagraphIndex < 0 && line.Top < Small.MarginTopPoints)
            .SelectMany(line => line.Pieces).Select(piece => piece.Text)))];

    /// <summary>"first|last" in two style references, saved as "saved".</summary>
    private static RichTextParagraph Header() =>
        RichTextParagraph.Empty
            .InsertText(0, "saved", InlineStyle.Default with { StyleReference = new StyleReferenceField("Entry") })
            .InsertText(5, "|", InlineStyle.Default)
            .InsertText(6, "saved", InlineStyle.Default with { StyleReference = new StyleReferenceField("Entry", FromBottom: true) });

    /// <summary>
    /// Forty lines "Line i wi", wi an entry word, except lines 10 to 29, which
    /// hold none.
    /// </summary>
    private static RichTextDocument Dictionary() =>
        RichTextDocument.FromParagraphs([.. Enumerable.Range(0, 40).Select(i =>
            i is >= 10 and < 30
                ? RichTextParagraph.Plain("Line " + i)
                : RichTextParagraph.Plain("Line " + i + " ").InsertText(("Line " + i + " ").Length, "w" + i, Entry))])
            .WithRunningContent(RunningContent.Empty.WithHeader(PageSelection.Default, [Header()]));

    [Fact]
    public void Each_Header_Shows_The_First_And_Last_Entry_On_Its_Page()
    {
        LayoutResult result = Layout(Dictionary());
        string[] headers = Headers(result);

        Assert.True(result.Pages.Count >= 3);
        for (int page = 0; page < result.Pages.Count; page++)
        {
            int[] lines = [.. result.Pages[page].Lines.Where(line => line.ParagraphIndex >= 0).Select(line => line.ParagraphIndex)];
            int[] entries = [.. lines.Where(i => i is < 10 or >= 30)];

            // A page with no entry of its own shows the nearest before it.
            string expected = entries.Length > 0
                ? "w" + entries.Min() + "|w" + entries.Max()
                : "w9|w9";
            Assert.Equal(expected, headers[page]);
        }
    }

    [Fact]
    public void A_Header_With_Nothing_In_The_Style_Keeps_What_It_Saved()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs([.. Enumerable.Range(0, 30).Select(i => RichTextParagraph.Plain("Line " + i))])
            .WithRunningContent(RunningContent.Empty.WithHeader(PageSelection.Default, [Header()]));

        Assert.All(Headers(Layout(document)), header => Assert.Equal("saved|saved", header));
    }

    /// <summary>
    /// Where each word of <paramref name="words"/> starts and ends, measured
    /// from the text's left edge on a line wide enough for all of them.
    /// </summary>
    private static (double Start, double End)[] Words(string words)
    {
        var wide = new DocumentPageBox(5000, 240, 20, 20, 20, 20);
        LayoutLine line = Layout(RichTextDocument.FromPlainText(words), wide).Pages[0].Lines.Single();
        return [.. line.Pieces.Where(piece => piece.Text.Trim().Length > 0).Select(piece => (piece.X - 20, piece.X + piece.Width - 20))];
    }

    private static RichTextDocument Justified(string words, bool shrinks) =>
        RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create(words, InlineStyle.Default, ParagraphStyle.Default with { Alignment = TextAlignment.Justify })])
            .WithStyleDefaults(DocumentStyleDefaults.Default with { ShrinksJustifiedLines = shrinks });

    private static int WordsOnFirstLine(string words, double width, bool shrinks) =>
        Layout(Justified(words, shrinks), new DocumentPageBox(width + 40, 240, 20, 20, 20, 20))
            .Pages[0].Lines[0].Pieces.Count(piece => piece.Text.Trim().Length > 0);

    private const string Sentence = "alpha bravo delta gamma kappa sigma omega theta";

    [Fact]
    public void A_Justified_Line_Shrinks_Its_Spaces_To_Take_A_Word_More()
    {
        (double Start, double End)[] words = Words(Sentence);
        double gap = words[1].Start - words[0].End;

        // Narrower than five words by half a gap: four fit at the font's
        // spacing, and five once the line's three gaps and the one before
        // the fifth word can each give up a quarter.
        double width = words[4].End - (gap / 2);
        var box = new DocumentPageBox(width + 40, 240, 20, 20, 20, 20);
        LayoutLine shrunk = Layout(Justified(Sentence, shrinks: true), box).Pages[0].Lines[0];

        Assert.Equal(4, WordsOnFirstLine(Sentence, width, shrinks: false));
        Assert.Equal(5, WordsOnFirstLine(Sentence, width, shrinks: true));

        // Set to the column, its spaces narrower than the font's.
        LayoutPiece last = shrunk.Pieces.Last(piece => piece.Text.Trim().Length > 0);
        Assert.Equal(20 + width, last.X + last.Width, 3);
    }

    [Fact]
    public void A_Line_That_Shrinking_Cannot_Save_Still_Breaks()
    {
        (double Start, double End)[] words = Words(Sentence);
        double gap = words[1].Start - words[0].End;

        // Two gaps short of five words: four quarters make one gap, not two.
        Assert.Equal(4, WordsOnFirstLine(Sentence, words[4].End - (2 * gap), shrinks: true));
    }

    [Fact]
    public void A_Left_Aligned_Line_Does_Not_Shrink()
    {
        (double Start, double End)[] words = Words(Sentence);
        double gap = words[1].Start - words[0].End;
        RichTextDocument document = RichTextDocument.FromPlainText(Sentence)
            .WithStyleDefaults(DocumentStyleDefaults.Default with { ShrinksJustifiedLines = true });

        LayoutLine line = Layout(document, new DocumentPageBox(words[4].End - (gap / 2) + 40, 240, 20, 20, 20, 20)).Pages[0].Lines[0];

        Assert.Equal(4, line.Pieces.Count(piece => piece.Text.Trim().Length > 0));
    }
}
