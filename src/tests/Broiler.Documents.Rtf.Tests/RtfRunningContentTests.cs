namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// Headers and footers in RTF. Both destinations used to be routed straight to
/// RtfDestination.Skip, so their text was dropped on the way in and the writer
/// had none to put back.
/// </summary>
public sealed class RtfRunningContentTests
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

    private static string TextOf(IReadOnlyList<RichTextParagraph> paragraphs) =>
        string.Join("|", paragraphs.Select(p => p.Text));

    [Fact(Timeout = 600000)]
    public void Reads_A_Header_Into_The_Running_Content()
    {
        RichTextDocument document = RtfReader.Read(
            "{\\rtf1{\\header a header}Body\\par}"u8.ToArray()).Document;

        Assert.Equal("a header", TextOf(document.RunningContent.Header(PageSelection.Default)));
        Assert.Equal("Body", document.PlainText);
    }

    [Fact(Timeout = 600000)]
    public void Reads_The_First_Page_And_Even_Page_Destinations_Apart()
    {
        RichTextDocument document = RtfReader.Read(
            "{\\rtf1{\\headerf first}{\\headerl even}Body\\par}"u8.ToArray()).Document;

        RunningContent running = document.RunningContent;
        Assert.Equal("first", TextOf(running.Header(PageSelection.First)));
        Assert.Equal("even", TextOf(running.Header(PageSelection.Even)));
        Assert.Empty(running.Header(PageSelection.Default));
    }

    [Fact(Timeout = 600000)]
    public void A_Header_And_Footer_Round_Trip()
    {
        RichTextDocument source = WithRunning("letterhead", "pagefooter");
        RunningContent running = RtfReader.Read(RtfWriter.WriteToArray(source)).Document.RunningContent;

        Assert.Equal("letterhead", TextOf(running.Header(PageSelection.Default)));
        Assert.Equal("pagefooter", TextOf(running.Footer(PageSelection.First)));
    }

    [Fact(Timeout = 600000)]
    public void A_Document_Without_Running_Content_Writes_No_Destinations()
    {
        string rtf = System.Text.Encoding.ASCII.GetString(
            RtfWriter.WriteToArray(RichTextDocument.FromPlainText("body")));

        Assert.DoesNotContain("\\header", rtf);
        Assert.DoesNotContain("\\footer", rtf);
    }
}
