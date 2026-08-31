namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Headers and footers in ODT. ODF hangs them off a master page in styles.xml
/// rather than putting them in the content, so they were never read: the reader
/// only ever walked office:text.
/// </summary>
public sealed class OdtRunningContentTests
{
    private static RichTextDocument WithRunning(string? header, string? footer)
    {
        RunningContent running = RunningContent.Empty;
        if (header is not null)
            running = running.WithHeader(PageSelection.Default, [RichTextParagraph.Plain(header)]);
        if (footer is not null)
            running = running.WithFooter(PageSelection.First, [RichTextParagraph.Plain(footer)]);
        return RichTextDocument.FromPlainText("body").WithRunningContent(running);
    }

    private static RichTextDocument RoundTrip(RichTextDocument document)
    {
        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(document), writable: false);
        return new OdtDocumentCodec().Read(stream).Document;
    }

    private static string TextOf(IReadOnlyList<RichTextParagraph> paragraphs) =>
        string.Join("|", paragraphs.Select(p => p.Text));

    [Fact]
    public void A_Header_And_Footer_Round_Trip_Through_The_Master_Page()
    {
        RunningContent running = RoundTrip(WithRunning("letterhead", "pagefooter")).RunningContent;

        Assert.Equal("letterhead", TextOf(running.Header(PageSelection.Default)));
        Assert.Equal("pagefooter", TextOf(running.Footer(PageSelection.First)));
    }

    [Fact]
    public void The_Body_Keeps_Its_Own_Text()
    {
        Assert.Equal("body", RoundTrip(WithRunning("letterhead", null)).PlainText);
    }

    [Fact]
    public void A_Document_Without_Running_Content_Gets_None()
    {
        Assert.True(RoundTrip(RichTextDocument.FromPlainText("body")).RunningContent.IsEmpty);
    }
}
