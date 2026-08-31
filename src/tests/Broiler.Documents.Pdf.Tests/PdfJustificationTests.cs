using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Covers justified paragraphs. The writer spends a line's slack on its own
/// spaces through PDF word spacing rather than moving the line, so both edges
/// come out flush; the last line of a paragraph keeps its slack, because
/// stretching a short closing line across the column is the one thing no
/// typesetter does.
/// </summary>
public sealed class PdfJustificationTests
{
    private const string Long =
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod " +
        "tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim " +
        "veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea " +
        "commodo consequat.";

    private static byte[] Write(TextAlignment alignment)
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create(
                Long,
                InlineStyle.Default,
                ParagraphStyle.Default with { Alignment = alignment })]);

        using var stream = new MemoryStream();
        new PdfDocumentCodec().WritePdf(
            document,
            stream,
            new PdfWriteOptions(compressStreams: false));
        return stream.ToArray();
    }

    private static string Latin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    /// <summary>Every <c>Tw</c> value the content stream sets, in order.</summary>
    private static List<double> WordSpacings(string content)
    {
        var values = new List<double>();
        foreach (Match match in Regex.Matches(content, @"(-?[0-9.]+) Tw"))
        {
            if (double.TryParse(
                    match.Groups[1].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    [Fact(Timeout = 600000)]
    public void A_Justified_Paragraph_Stretches_Its_Spaces()
    {
        List<double> spacings = WordSpacings(Latin1(Write(TextAlignment.Justify)));

        // The paragraph wraps, so at least one line is not the last and carries a
        // real stretch rather than the zero the state resets to.
        Assert.NotEmpty(spacings);
        Assert.Contains(spacings, value => value > 0);
    }

    [Fact(Timeout = 600000)]
    public void A_Left_Aligned_Paragraph_Sets_No_Word_Spacing()
    {
        Assert.DoesNotContain(" Tw", Latin1(Write(TextAlignment.Left)));
    }

    [Fact(Timeout = 600000)]
    public void The_Last_Line_Of_A_Justified_Paragraph_Is_Not_Stretched()
    {
        string content = Latin1(Write(TextAlignment.Justify));

        // Word spacing is graphics state, so the writer has to put it back to
        // zero before the closing line. Without that reset the last line would
        // inherit the stretch of the line above it.
        Assert.Contains("0 Tw", content);
    }

    [Fact(Timeout = 600000)]
    public void Justification_Does_Not_Disturb_The_Text_On_The_Way_Back()
    {
        using var stream = new MemoryStream(Write(TextAlignment.Justify), writable: false);
        RichTextDocument actual = new PdfDocumentCodec().Read(stream).Document;

        // The reader applies Tw to space glyphs when it advances, so a stretched
        // line must still read back as the words that were written, with single
        // spaces and no invented ones.
        Assert.Contains("consectetur adipiscing elit", actual.PlainText);
        Assert.DoesNotContain("  ", actual.PlainText);
    }
}
