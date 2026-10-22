using System.Text;

namespace Broiler.Documents.Pdf.Tests;

public sealed class PdfParagraphPaginationTests
{
    private static ParagraphStyle Style => ParagraphStyle.Default with
    { LineHeightRule = LineHeightRule.Exact, LineHeight = 10 };
    private static RichTextParagraph Para(string name, int lines, ParagraphStyle? style = null) =>
        RichTextParagraph.Create(string.Join("\u2028", Enumerable.Repeat(name, lines)), InlineStyle.Default, style ?? Style);
    private static string Write(params RichTextParagraph[] paragraphs)
    {
        using var stream = new MemoryStream();
        new PdfDocumentCodec().WritePdf(RichTextDocument.FromParagraphs(paragraphs)
                .WithPageGeometry(new PageGeometry(200, 100, 10, 10, 10, 10)),
            stream, new PdfWriteOptions(compressStreams: false));
        return Encoding.Latin1.GetString(stream.ToArray());
    }
    private static double Baseline(string pdf, string text)
    {
        int textAt = pdf.IndexOf("(" + text + ")", StringComparison.Ordinal);
        Assert.True(textAt > 0);
        string before = pdf[..textAt];
        int tm = before.LastIndexOf(" Tm", StringComparison.Ordinal);
        return double.Parse(before[..tm].Split('\n')[^1].Split(' ')[^1], System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void KeepTogether_moves_the_first_line_to_the_next_page()
    {
        string together = Write(Para("filler", 6), Para("body", 3, Style with { KeepTogether = true }));
        string ordinary = Write(Para("filler", 6), Para("body", 3));
        Assert.Equal(Baseline(together, "filler"), Baseline(together, "body"), 3);
        Assert.True(Baseline(ordinary, "body") < Baseline(together, "body"));
    }

    [Fact]
    public void KeepNext_moves_a_heading_with_its_body()
    {
        string pdf = Write(Para("filler", 7), Para("heading", 1, Style with { KeepWithNext = true }), Para("body", 2));
        Assert.Equal(Baseline(pdf, "filler"), Baseline(pdf, "heading"), 3);
        string adjacent = Write(Para("heading", 1), Para("body", 2));
        Assert.Equal(Baseline(adjacent, "heading") - Baseline(adjacent, "body"),
            Baseline(pdf, "heading") - Baseline(pdf, "body"), 3);
    }

    [Fact]
    public void Orphan_control_moves_the_first_line_to_the_next_page()
    {
        string pdf = Write(Para("filler", 7), Para("body", 3, Style with { OrphanLines = 2 }));
        Assert.Equal(Baseline(pdf, "filler"), Baseline(pdf, "body"), 3);
    }

    [Fact]
    public void Widow_control_moves_two_lines_instead_of_one()
    {
        string pdf = Write(Para("filler", 5),
            RichTextParagraph.Create("a\u2028b\u2028c\u2028d", InlineStyle.Default, Style with { WidowLines = 2 }));
        Assert.Equal(Baseline(pdf, "filler"), Baseline(pdf, "c"), 3);
        Assert.Equal(10, Baseline(pdf, "c") - Baseline(pdf, "d"), 3);
    }
}
