using System.Text;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Headers and footers in the PDF writer. They are drawn in the page margins on
/// every page, once the body has decided how many pages there are, rather than
/// flowing with the text.
/// </summary>
public sealed class PdfRunningContentTests
{
    private static RichTextDocument Document(string body, string? header, string? footer, int paragraphs = 1)
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
            Enumerable.Range(0, paragraphs).Select(_ => RichTextParagraph.Plain(body)));

        RunningContent running = RunningContent.Empty;
        if (header is not null)
            running = running.WithHeader(PageSelection.Default, [RichTextParagraph.Plain(header)]);
        if (footer is not null)
            running = running.WithFooter(PageSelection.Default, [RichTextParagraph.Plain(footer)]);
        return document.WithRunningContent(running);
    }

    private static (byte[] Bytes, PdfWriteResult Result) Write(RichTextDocument document)
    {
        using var stream = new MemoryStream();
        PdfWriteResult result = new PdfDocumentCodec()
            .WritePdf(document, stream, new PdfWriteOptions(compressStreams: false));
        return (stream.ToArray(), result);
    }

    private static string Latin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    [Fact(Timeout = 600000)]
    public void A_Header_And_Footer_Are_Drawn()
    {
        string content = Latin1(Write(Document("body", "letterhead", "pagefooter")).Bytes);

        Assert.Contains("letterhead", content);
        Assert.Contains("pagefooter", content);
    }

    [Fact(Timeout = 600000)]
    public void The_Header_Sits_Above_The_Body_And_The_Footer_Below_It()
    {
        string content = Latin1(Write(Document("bodytext", "letterhead", "pagefooter")).Bytes);

        // PDF y grows upward, so the header's baseline is the highest of the three
        // and the footer's the lowest.
        double header = BaselineOf(content, "letterhead");
        double body = BaselineOf(content, "bodytext");
        double footer = BaselineOf(content, "pagefooter");

        Assert.True(header > body, $"header baseline {header} was not above the body at {body}");
        Assert.True(footer < body, $"footer baseline {footer} was not below the body at {body}");
    }

    [Fact(Timeout = 600000)]
    public void They_Repeat_On_Every_Page()
    {
        (byte[] bytes, PdfWriteResult result) = Write(Document("body", "letterhead", null, paragraphs: 400));

        Assert.True(result.PageCount > 1, $"expected the body to span pages, got {result.PageCount}");
        int drawn = Occurrences(Latin1(bytes), "letterhead");
        Assert.Equal(result.PageCount, drawn);
    }

    [Fact(Timeout = 600000)]
    public void A_Document_With_No_Running_Content_Is_Unchanged()
    {
        string plain = Latin1(Write(RichTextDocument.FromPlainText("body")).Bytes);
        string decorated = Latin1(Write(Document("body", null, null)).Bytes);

        Assert.Equal(plain.Length, decorated.Length);
    }

    [Fact(Timeout = 600000)]
    public void A_Header_Taller_Than_Its_Margin_Is_Reported_Rather_Than_Drawn()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("body").WithRunningContent(
            RunningContent.Empty.WithHeader(
                PageSelection.Default,
                Enumerable.Range(0, 40).Select(_ => RichTextParagraph.Plain("tallheader")).ToList()));

        (byte[] bytes, PdfWriteResult result) = Write(document);

        Assert.DoesNotContain("tallheader", Latin1(bytes));
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("top margin", StringComparison.Ordinal));
    }

    /// <summary>The y of the text matrix that placed <paramref name="text"/>.</summary>
    private static double BaselineOf(string content, string text)
    {
        int at = content.IndexOf("(" + text, StringComparison.Ordinal);
        Assert.True(at > 0, $"{text} was not drawn");

        string before = content[..at];
        int tm = before.LastIndexOf(" Tm", StringComparison.Ordinal);
        Assert.True(tm > 0, $"no text matrix preceded {text}");

        string[] parts = before[..tm].Split('\n')[^1].Split(' ');
        return double.Parse(parts[^1], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int Occurrences(string content, string text)
    {
        int count = 0;
        int at = 0;
        while ((at = content.IndexOf(text, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += text.Length;
        }

        return count;
    }
}
