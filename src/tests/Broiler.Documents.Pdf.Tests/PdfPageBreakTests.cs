using System.Text;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// An explicit page break in the PDF writer.
/// </summary>
/// <remarks>
/// <para>
/// The writer paginated on overflow alone, so a document that said where its
/// pages divide came out as one flow. That is the same defect the office
/// conformance suite found in the CLI layout, one engine later: the model
/// carries <c>PageBreakBefore</c>, every interchange codec reads and writes it,
/// and the two renderers disagreed about whether it meant anything.
/// </para>
/// <para>
/// The reader is not the other half of this: <c>MapPageBreaks</c> represents a
/// source boundary as an empty paragraph rather than as
/// <c>PageBreakBefore</c>, deliberately, because it is the weaker claim and a
/// re-pagination cannot keep the stronger one. So a mapped read still does not
/// round trip as pages, and that is a decision to revisit rather than a defect
/// this fixes.
/// </para>
/// </remarks>
public sealed class PdfPageBreakTests
{
    /// <summary>One short paragraph per flag, each stating whether it opens a page.</summary>
    private static RichTextDocument Document(params bool[] breakBefore) =>
        RichTextDocument.FromParagraphs(
            breakBefore.Select((wants, index) => RichTextParagraph.Create(
                "paragraph " + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                InlineStyle.Default,
                ParagraphStyle.Default with { PageBreakBefore = wants })));

    private static (byte[] Bytes, PdfWriteResult Result) Write(RichTextDocument document)
    {
        using var stream = new MemoryStream();
        PdfWriteResult result = new PdfDocumentCodec()
            .WritePdf(document, stream, new PdfWriteOptions(compressStreams: false));
        return (stream.ToArray(), result);
    }

    private static string Latin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    [Fact(Timeout = 600000)]
    public void A_Break_Starts_A_New_Page()
    {
        // Two short paragraphs that would otherwise share a page.
        Assert.Equal(2, Write(Document(false, true)).Result.PageCount);
    }

    [Fact(Timeout = 600000)]
    public void Without_A_Break_They_Share_A_Page()
    {
        // The other half of the assertion, and the one that stops the test above
        // passing for a reason that has nothing to do with the break.
        Assert.Equal(1, Write(Document(false, false)).Result.PageCount);
    }

    [Fact(Timeout = 600000)]
    public void The_Broken_Paragraph_Starts_At_The_Top_Of_Its_Page()
    {
        // A page count can be right while the text is in the wrong place. Both
        // paragraphs open a page here, so both baselines are the first line's -
        // where without the break the second would sit one line lower.
        string broken = Latin1(Write(Document(false, true)).Bytes);
        string flowed = Latin1(Write(Document(false, false)).Bytes);

        Assert.Equal(BaselineOf(broken, "paragraph 0"), BaselineOf(broken, "paragraph 1"), 3);
        Assert.True(
            BaselineOf(flowed, "paragraph 1") < BaselineOf(flowed, "paragraph 0"),
            "without a break the second paragraph should sit below the first");
    }

    [Fact(Timeout = 600000)]
    public void A_Break_On_The_First_Paragraph_Does_Not_Open_An_Empty_Page()
    {
        // A document that opens with a break is asking to start on a fresh page,
        // and it is already on one. Emitting a blank page in front of it would be
        // honouring the letter of the break against its evident meaning.
        Assert.Equal(1, Write(Document(true, false)).Result.PageCount);
    }

    [Fact(Timeout = 600000)]
    public void Every_Break_Is_Taken()
    {
        Assert.Equal(4, Write(Document(false, true, true, true)).Result.PageCount);
    }

    [Fact(Timeout = 600000)]
    public void Consecutive_Breaks_Each_Open_A_Page()
    {
        // Three paragraphs, the last two both asking to start a page. The middle
        // one occupies its own page and the last one starts another, which is what
        // both breaks said even though it leaves a page holding one line.
        Assert.Equal(3, Write(Document(false, true, true)).Result.PageCount);
    }

    [Fact(Timeout = 600000)]
    public void An_Empty_Paragraph_Can_Carry_A_Break()
    {
        // A break lives on the paragraph rather than in the text, so a paragraph
        // with nothing in it still states one. The layout takes the break before
        // it measures anything, which is what makes this work rather than an
        // accident of an empty paragraph's height.
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Plain("first"),
            RichTextParagraph.Create(
                string.Empty, InlineStyle.Default,
                ParagraphStyle.Default with { PageBreakBefore = true }),
        ]);

        Assert.Equal(2, Write(document).Result.PageCount);
    }

    [Fact(Timeout = 600000)]
    public void A_Mapped_Boundary_Does_Not_Come_Back_As_A_Break()
    {
        // Pinning the limit rather than the capability, because the shape of this
        // is inviting and wrong: the writer paginates what the model states, and
        // a mapped read does not state it. MapPageBreaks adds an empty paragraph,
        // which carries no break, so three pages in is one page out. Whether it
        // should map to the real property now that the model has one is a
        // decision about what a re-paginated read may claim - and this test is
        // here to fail when somebody takes it.
        (byte[] original, PdfWriteResult written) = Write(Document(false, true, true));
        Assert.Equal(3, written.PageCount);

        using var source = new MemoryStream(original, writable: false);
        PdfReadResult read = new PdfDocumentCodec()
            .ReadPdf(source, new PdfReadOptions(mapPageBreaks: true));

        Assert.Equal(3, read.PageCount);
        Assert.DoesNotContain(read.Document.Paragraphs, p => p.Style.PageBreakBefore);
        Assert.Equal(1, Write(read.Document).Result.PageCount);
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
}
