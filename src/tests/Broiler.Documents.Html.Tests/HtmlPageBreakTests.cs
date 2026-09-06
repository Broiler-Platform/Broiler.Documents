using System.Text;

namespace Broiler.Documents.Html.Tests;

/// <summary>
/// The page break a paragraph declares, in either of the two spellings CSS has
/// for it.
/// </summary>
/// <remarks>
/// <para>
/// Neither half of this codec carried one, and neither half could have: until
/// <c>ParagraphStyle.PageBreakBefore</c> existed there was nowhere in the model
/// for a break to arrive, so the reader had nothing to set and the writer had
/// nothing to write. That is the worst shape a loss can take - not a diagnostic,
/// not a wrong value, but a document that says less than it said with nothing
/// anywhere to notice it, because the codec and the model agreed the construct
/// did not exist.
/// </para>
/// <para>
/// Found by the office conformance suite, where <c>page-break-explicit</c> came
/// back one page here against two in LibreOffice, through all four formats at
/// once.
/// </para>
/// </remarks>
public sealed class HtmlPageBreakTests
{
    [Theory(Timeout = 600000)]
    // The CSS2 property, which is what LibreOffice writes, and the CSS3
    // replacement it is now an alias for. Both are current and both are read.
    [InlineData("page-break-before: always")]
    [InlineData("break-before: page")]
    [InlineData("PAGE-BREAK-BEFORE: ALWAYS")]
    [InlineData("break-before:always")]
    public void A_Paragraph_Declaring_A_Break_Starts_A_New_Page(string declaration)
    {
        RichTextDocument document = Read("<p>first</p><p style='" + declaration + "'>second</p>");

        Assert.False(document.Paragraphs[0].Style.PageBreakBefore);
        Assert.True(document.Paragraphs[1].Style.PageBreakBefore);
    }

    [Theory(Timeout = 600000)]
    [InlineData("p")]
    [InlineData("h2")]
    [InlineData("li")]
    public void Every_Element_That_Makes_A_Paragraph_Can_State_One(string tag)
    {
        RichTextDocument document = Read(
            "<" + tag + " style='page-break-before: always'>text</" + tag + ">");

        Assert.True(Assert.Single(document.Paragraphs).Style.PageBreakBefore);
    }

    [Theory(Timeout = 600000)]
    // auto is the initial value; avoid asks for the opposite; the column and
    // region values name a fragmentation into containers this model does not
    // have. None of them is a page break, and reading any of them as one would
    // put a break into a document that never asked for it.
    [InlineData("page-break-before: auto")]
    [InlineData("break-before: auto")]
    [InlineData("page-break-before: avoid")]
    [InlineData("break-before: avoid-page")]
    [InlineData("break-before: column")]
    [InlineData("break-before: region")]
    [InlineData("page-break-after: always")]
    [InlineData("text-align: center")]
    public void A_Declaration_That_Is_Not_A_Page_Break_Does_Not_Start_One(string declaration)
    {
        RichTextDocument document = Read("<p style='" + declaration + "'>text</p>");

        Assert.False(Assert.Single(document.Paragraphs).Style.PageBreakBefore);
    }

    [Fact(Timeout = 600000)]
    public void An_Ordinary_Paragraph_States_No_Break()
    {
        RichTextDocument document = Read("<p>first</p><p>second</p>");

        Assert.All(document.Paragraphs, paragraph => Assert.False(paragraph.Style.PageBreakBefore));
    }

    [Fact(Timeout = 600000)]
    public void A_Break_To_A_Named_Side_Is_Kept_And_Its_Side_Is_Reported()
    {
        // The break is the larger half of what the document said and it survives;
        // which face of the sheet the page lands on has nowhere to go in a model
        // holding one flag, so it is named rather than dropped in silence.
        DocumentReadResult result = ReadResult("<p style='page-break-before: left'>second</p>");

        Assert.True(Assert.Single(result.Document.Paragraphs).Style.PageBreakBefore);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "html.page-break");
    }

    [Fact(Timeout = 600000)]
    public void A_Plain_Break_Reports_Nothing()
    {
        DocumentReadResult result = ReadResult("<p style='page-break-before: always'>second</p>");

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "html.page-break");
    }

    [Fact(Timeout = 600000)]
    public void When_The_Two_Spellings_Disagree_The_Break_Wins()
    {
        // CSS settles this by source order, which the declaration parser does not
        // keep - it returns a dictionary. Taking the break rather than inventing
        // an order is the answer that cannot lose one.
        RichTextDocument document = Read(
            "<p style='page-break-before: always; break-before: auto'>second</p>");

        Assert.True(Assert.Single(document.Paragraphs).Style.PageBreakBefore);
    }

    [Fact(Timeout = 600000)]
    public void A_Break_On_A_Container_Is_Not_Inherited_By_The_Paragraphs_Inside_It()
    {
        // Alignment descends and a break does not. A div carrying one is one
        // break stated somewhere this codec makes no paragraph, not a break on
        // each of the three paragraphs it holds.
        RichTextDocument document = Read(
            "<div style='page-break-before: always; text-align: center'>" +
            "<p>one</p><p>two</p></div>");

        Assert.All(document.Paragraphs, paragraph => Assert.False(paragraph.Style.PageBreakBefore));
        Assert.All(document.Paragraphs, paragraph => Assert.Equal(TextAlignment.Center, paragraph.Style.Alignment));
    }

    [Fact(Timeout = 600000)]
    public void A_Break_Stated_In_A_Type_Rule_Reaches_The_Paragraphs_It_Names()
    {
        // This used to assert the opposite, and the reason it did was sound while
        // it lasted: a rule selecting paragraphs is the cascade and this codec had
        // none. It now matches one selector, the bare element name, and the break
        // goes wherever every other declaration in such a rule goes. Keeping the
        // break out of it would have left a document whose line spacing came from
        // its stylesheet and whose page breaks did not.
        RichTextDocument document = Read(
            "<html><head><style>p { page-break-before: always }</style></head>" +
            "<body><p>text</p></body></html>");

        Assert.True(Assert.Single(document.Paragraphs).Style.PageBreakBefore);
    }

    [Fact(Timeout = 600000)]
    public void A_Break_Stated_In_A_Class_Rule_Is_Still_Not_Read()
    {
        // The line moved from "no selector" to "one selector", not off the
        // stylesheet altogether. Working out which paragraphs a class rule picked
        // out is the cascade, and that is still a browser's job.
        RichTextDocument document = Read(
            "<html><head><style>.breaks { page-break-before: always }</style></head>" +
            "<body><p class='breaks'>text</p></body></html>");

        Assert.False(Assert.Single(document.Paragraphs).Style.PageBreakBefore);
    }

    [Fact(Timeout = 600000)]
    public void A_Paragraph_Can_Refuse_The_Break_Its_Type_Rule_States()
    {
        // Inline beats type for this property as for every other, and the value
        // that turns a break off has to be honoured or the override is only half
        // of one.
        RichTextDocument document = Read(
            "<html><head><style>p { page-break-before: always }</style></head>" +
            "<body><p style='page-break-before: auto'>text</p></body></html>");

        Assert.False(Assert.Single(document.Paragraphs).Style.PageBreakBefore);
    }

    [Fact(Timeout = 600000)]
    public void The_Writer_States_The_Break_On_The_Paragraph_That_Has_It()
    {
        string html = Write(RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create("first", InlineStyle.Default),
            RichTextParagraph.Create(
                "second", InlineStyle.Default, ParagraphStyle.Default with { PageBreakBefore = true }),
        ]));

        Assert.Contains("<p>first</p>", html, StringComparison.Ordinal);
        Assert.Contains(
            "<p style=\"" + HtmlWriter.PageBreakBeforeDeclaration + "\">second</p>",
            html,
            StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void The_Writer_States_Nothing_For_A_Paragraph_That_Starts_No_Page()
    {
        Assert.DoesNotContain(
            "break", Write(RichTextDocument.FromPlainText("body")), StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void The_Break_Joins_The_Declarations_The_Paragraph_Already_Makes()
    {
        // The declaration list is composed, not replaced. A paragraph that was
        // already carrying its alignment and its spacing keeps them.
        string html = Write(RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create(
                "second",
                InlineStyle.Default,
                ParagraphStyle.Default with
                {
                    PageBreakBefore = true,
                    Alignment = TextAlignment.Center,
                    SpacingBefore = 6f,
                }),
        ]));

        Assert.Contains(HtmlWriter.PageBreakBeforeDeclaration, html, StringComparison.Ordinal);
        Assert.Contains("text-align: center", html, StringComparison.Ordinal);
        Assert.Contains("margin-top: 6pt", html, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void The_Break_Survives_A_Round_Trip()
    {
        // Reading it and never writing it would move the loss rather than end it.
        RichTextDocument expected = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create("first", InlineStyle.Default),
            RichTextParagraph.Create(
                "second",
                InlineStyle.Default,
                ParagraphStyle.Default with
                {
                    PageBreakBefore = true,
                    Alignment = TextAlignment.Right,
                    LineSpacing = 1.5f,
                }),
        ]);

        byte[] bytes = HtmlDocumentCodec.WriteToArray(expected);
        using var stream = new MemoryStream(bytes);
        RichTextDocument actual = new HtmlDocumentCodec().Read(stream).Document;

        DocumentAssert.Equivalent(expected, actual);
    }

    [Fact(Timeout = 600000)]
    public void A_Break_And_A_Tab_Do_Not_Displace_Each_Other()
    {
        // Two independent reasons to put a style attribute on the same paragraph,
        // and the writer appends one declaration list to the other. A paragraph
        // needing both used to be the shape that would prove it did not.
        RichTextDocument expected = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create(
                "Name\tRole",
                InlineStyle.Default,
                ParagraphStyle.Default with { PageBreakBefore = true }),
        ]);

        string html = Write(expected);
        using var stream = new MemoryStream(HtmlDocumentCodec.WriteToArray(expected));
        RichTextDocument actual = new HtmlDocumentCodec().Read(stream).Document;

        Assert.Contains(HtmlWriter.PageBreakBeforeDeclaration, html, StringComparison.Ordinal);
        Assert.Contains(HtmlWriter.PreserveWhitespaceDeclaration, html, StringComparison.Ordinal);
        DocumentAssert.Equivalent(expected, actual);
    }

    /// <summary>
    /// The document the office conformance suite paginates, in the markup
    /// LibreOffice actually produced for it - the seed that reported one page
    /// here against two there.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void The_Seed_That_Found_This_Reads_As_Two_Pages_Worth_Of_Paragraphs()
    {
        RichTextDocument document = Read(
            "<html><head><meta charset=\"utf-8\"><title>page-break-explicit</title>" +
            "<style>@page { size: 21.59cm 27.94cm; margin: 2.54cm }</style></head>" +
            "<body style=\"font-family:'Liberation Serif';font-size:12pt\">" +
            "<p>The paragraph before the break.</p>" +
            "<p style=\"page-break-before:always\">The paragraph after the break.</p>" +
            "</body></html>");

        Assert.Equal(2, document.ParagraphCount);
        Assert.False(document.Paragraphs[0].Style.PageBreakBefore);
        Assert.True(document.Paragraphs[1].Style.PageBreakBefore);
        Assert.NotNull(document.PageGeometry);
    }

    private static RichTextDocument Read(string html) => ReadResult(html).Document;

    private static DocumentReadResult ReadResult(string html)
    {
        var codec = new HtmlDocumentCodec();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        return codec.Read(stream);
    }

    private static string Write(RichTextDocument document) =>
        Encoding.UTF8.GetString(HtmlWriter.WriteToArray(document));
}
