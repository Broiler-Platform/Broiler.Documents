using System.Globalization;
using System.Text;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// A document that lays runs of spaces out wider - an RTF document Word wrote -
/// gives each space in a run of two or more a sixth of an em more than its font
/// does (tdf#123703), in the PDF writer as in the CLI layout.
/// </summary>
/// <remarks>
/// The writer folds the spaces after a word into the word, and a viewer sets a
/// run's glyphs at the font's own advances. So a widened run has to end its
/// piece: the text after it is placed by a text matrix of its own, which is
/// what these tests read.
/// </remarks>
public sealed class PdfSpaceRunTests
{
    // Helvetica at 12 points, from its AFM widths.
    private const double A = 556 * 0.012;
    private const double Space = 278 * 0.012;

    private static string Write(string text, bool widens, ParagraphStyle? style = null)
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create(text, InlineStyle.Default with { FontSize = 12f }, style ?? ParagraphStyle.Default),
        ]).WithStyleDefaults(DocumentStyleDefaults.Default with { WidensSpaceRuns = widens });

        using var stream = new MemoryStream();
        new PdfDocumentCodec().WritePdf(document, stream, new PdfWriteOptions(compressStreams: false));
        return Encoding.Latin1.GetString(stream.ToArray());
    }

    [Fact]
    public void The_Word_After_A_Run_Starts_Where_The_Wider_Run_Ends()
    {
        string content = Write("a" + new string(' ', 10) + "b", widens: true);

        Assert.Equal(XOf(content, "a") + A + (10 * (Space + 2)), XOf(content, "b"), 3);
    }

    [Fact]
    public void Without_It_A_Run_Is_Its_Font_Width_And_One_Piece()
    {
        string content = Write("a" + new string(' ', 10) + "b", widens: false);

        Assert.Contains("(a" + new string(' ', 10) + "b)", content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Wider_Run_Pushes_The_Next_Word_Onto_A_Line_Of_Its_Own()
    {
        string text = "word" + new string(' ', 90) + "word2";

        string plain = Write(text, widens: false);
        Assert.Contains("word2)", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("(word2", plain, StringComparison.Ordinal);

        string widened = Write(text, widens: true);
        Assert.Equal(XOf(widened, "word"), XOf(widened, "word2"), 3);
        Assert.True(YOf(widened, "word2") < YOf(widened, "word"), "word2 stayed on the first line");
    }

    [Fact]
    public void A_Run_That_Ends_A_Line_Is_Trimmed_With_Its_Extra_Width()
    {
        // Right-aligned text ends at the margin; the run after it is not drawn
        // and has to take all of its width with it, the extra included.
        var right = ParagraphStyle.Default with { Alignment = TextAlignment.Right };
        string text = "a" + new string(' ', 10);

        Assert.Equal(XOf(Write(text, widens: false, right), "a"), XOf(Write(text, widens: true, right), "a"), 3);
    }

    private static double XOf(string content, string text) => Operand(MatrixBefore(content, text), 2);

    private static double YOf(string content, string text) => Operand(MatrixBefore(content, text), 1);

    /// <summary>The content up to the text matrix that placed <paramref name="text"/>.</summary>
    private static string MatrixBefore(string content, string text)
    {
        int at = content.IndexOf("(" + text, StringComparison.Ordinal);
        while (at >= 0 && content[at + 1 + text.Length] is not (' ' or ')'))
            at = content.IndexOf("(" + text, at + 1, StringComparison.Ordinal);
        Assert.True(at > 0, $"{text} was not drawn");

        string before = content[..at];
        int tm = before.LastIndexOf(" Tm", StringComparison.Ordinal);
        Assert.True(tm > 0, $"no text matrix preceded {text}");
        return before[..tm];
    }

    /// <summary>The nth operand from the end of a text matrix: 1 is y, 2 is x.</summary>
    private static double Operand(string upToOperator, int fromEnd)
    {
        string[] parts = upToOperator.Split('\n')[^1].Split(' ');
        return double.Parse(parts[^fromEnd], CultureInfo.InvariantCulture);
    }
}
