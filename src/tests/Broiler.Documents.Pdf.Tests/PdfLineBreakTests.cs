using System.Text;
using System.Text.RegularExpressions;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// The forced line break inside a paragraph, which the model spells U+2028.
/// </summary>
/// <remarks>
/// The PDF writer had the same defect the render layout had, wearing different
/// clothes. Its word splitter breaks on <c>' '</c> and <c>'\t'</c> and knows
/// nothing about U+2028, so the character was swept into the word around it and
/// handed to the content stream as a glyph no standard font has - an address
/// block came out as one run with a hole in it rather than as five lines.
/// </remarks>
public sealed class PdfLineBreakTests
{
    private const char Break = '\u2028';

    private static string Write(string text)
    {
        using var stream = new MemoryStream();
        new PdfDocumentCodec().WritePdf(
            RichTextDocument.FromPlainText(text),
            stream,
            new PdfWriteOptions(compressStreams: false));
        return Encoding.Latin1.GetString(stream.ToArray());
    }

    /// <summary>The text of every <c>Tj</c> the content stream shows, in order.</summary>
    private static List<string> Shown(string content) =>
        Regex.Matches(content, @"\(((?:\\.|[^()\\])*)\) Tj")
            .Select(match => match.Groups[1].Value)
            .ToList();

    [Fact(Timeout = 600000)]
    public void A_Break_Puts_The_Next_Word_On_Its_Own_Line()
    {
        List<string> shown = Shown(Write("Sender Name" + Break + "Broiler Platform"));

        Assert.Equal(2, shown.Count);
        Assert.Equal("Sender Name", shown[0]);
        Assert.Equal("Broiler Platform", shown[1]);
    }

    [Fact(Timeout = 600000)]
    public void A_Break_Is_Not_A_Space()
    {
        Assert.Single(Shown(Write("alpha bravo")));
        Assert.Equal(2, Shown(Write("alpha" + Break + "bravo")).Count);
    }

    [Fact(Timeout = 600000)]
    public void The_Break_Leaves_No_Mark_Of_Its_Own()
    {
        // U+2028 has no glyph in any of the standard fourteen, so one reaching a
        // string operand is a character the reader cannot draw and the writer
        // could not have measured. Asserted as the text that did get shown rather
        // than as the absence of a byte: the encoder is free to substitute, and a
        // substitute is exactly the mark this is looking for.
        Assert.Equal("alphabravo", string.Concat(Shown(Write("alpha" + Break + "bravo"))));
    }

    [Fact(Timeout = 600000)]
    public void Two_Breaks_In_A_Row_Leave_The_Blank_Line_Between_Them()
    {
        // Three shown strings would mean an empty line was drawn as an empty run;
        // two mean the blank line is a gap, which is what it is. The assertion
        // that matters is the second word's baseline, two line heights down.
        List<string> shown = Shown(Write("alpha" + Break + Break + "bravo"));

        Assert.Equal(["alpha", "bravo"], shown);
    }
}
