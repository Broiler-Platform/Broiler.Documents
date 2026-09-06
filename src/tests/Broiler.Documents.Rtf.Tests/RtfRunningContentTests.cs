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
    public void A_Fields_Result_Stays_In_The_Footer_It_Is_In()
    {
        // The letterhead's footer, as LibreOffice writes it. \fldrslt used to
        // replace the footer destination rather than sit inside it, so the cached
        // page number arrived at the head of the body - and the word beside it
        // did not, which is what said the routing rather than the parsing was
        // wrong.
        const string rtf =
            "{\\rtf1\\ansi{\\footer Page {\\field{\\*\\fldinst PAGE }{\\fldrslt 2}}}Body\\par}";

        RichTextDocument document = RtfReader.Read(System.Text.Encoding.ASCII.GetBytes(rtf)).Document;

        Assert.Equal("Body", document.PlainText);
        Assert.Equal("Page 2", TextOf(document.RunningContent.Footer(PageSelection.Default)));
    }

    [Fact(Timeout = 600000)]
    public void A_Fields_Result_In_The_Body_Is_Still_Body_Text()
    {
        // The other side of the same rule: a field in the body puts its result
        // in the body, which is what the destination did unconditionally and now
        // does because that is where the field is.
        const string rtf =
            "{\\rtf1\\ansi Seen on {\\field{\\*\\fldinst PAGE }{\\fldrslt 7}}\\par}";

        Assert.Equal("Seen on 7", RtfReader.Read(System.Text.Encoding.ASCII.GetBytes(rtf)).Document.PlainText);
    }

    [Fact(Timeout = 600000)]
    public void A_Footer_Keeps_The_Characters_That_Are_Spelled_Rather_Than_Carried()
    {
        // An escape, a hex byte and a \uN are handled apart from plain text, and
        // all three were gated to the body: a footer's plain letters survived and
        // anything spelled out of them did not.
        const string rtf =
            "{\\rtf1\\ansi{\\footer Seite \\'e4 \\u8212? \\\\}Body\\par}";

        RunningContent running = RtfReader.Read(System.Text.Encoding.ASCII.GetBytes(rtf))
            .Document.RunningContent;

        Assert.Equal("Seite \u00e4 \u2014 \\", TextOf(running.Footer(PageSelection.Default)));
    }

    [Fact(Timeout = 600000)]
    public void Titlepg_Is_What_Makes_The_First_Page_Different()
    {
        // \titlepg is RTF's spelling of the fact w:titlePg carries in DOCX and a
        // master-page chain carries in ODF: the first page takes the bands it
        // names and none of the others.
        const string rtf =
            "{\\rtf1\\ansi\\titlepg{\\headerf a letterhead}{\\footer a page number}Body\\par}";

        RunningContent running = RtfReader.Read(System.Text.Encoding.ASCII.GetBytes(rtf))
            .Document.RunningContent;

        Assert.True(running.DifferentFirstPage);
        Assert.Equal("a letterhead", TextOf(running.EffectiveHeader(PageSelection.First)));
        Assert.Empty(running.EffectiveFooter(PageSelection.First));
        Assert.Equal("a page number", TextOf(running.EffectiveFooter(PageSelection.Default)));
    }

    [Fact(Timeout = 600000)]
    public void A_Different_First_Page_Round_Trips()
    {
        RichTextDocument source = RichTextDocument.FromPlainText("body").WithRunningContent(
            RunningContent.Empty
                .WithDifferentFirstPage(true)
                .WithFooter(PageSelection.Default, [RichTextParagraph.Plain("a page number")]));

        byte[] bytes = RtfWriter.WriteToArray(source);
        Assert.Contains("\\titlepg", System.Text.Encoding.ASCII.GetString(bytes), StringComparison.Ordinal);

        RunningContent back = RtfReader.Read(bytes).Document.RunningContent;

        Assert.True(back.DifferentFirstPage);
        Assert.Empty(back.EffectiveFooter(PageSelection.First));
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
