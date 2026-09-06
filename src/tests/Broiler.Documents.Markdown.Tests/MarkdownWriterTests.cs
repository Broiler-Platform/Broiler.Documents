namespace Broiler.Documents.Markdown.Tests;

public sealed class MarkdownWriterTests
{
    [Fact(Timeout = 600000)]
    public void Writes_Deterministic_Markdown()
    {
        string markdown = Write(RichTextDocument.FromPlainText("hello\nworld"));

        Assert.Equal("hello\n\nworld\n", markdown);
    }

    [Fact(Timeout = 600000)]
    public void Writes_Inline_Styles_Links_Lists_And_Soft_Breaks()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(new[]
        {
            MakeParagraph(
                ParagraphStyle.Default with { ListKind = ListKind.Bullet, IndentLevel = 1 },
                ("Hi", InlineStyle.Default with { Bold = true, Italic = true }),
                (((char)0x2028).ToString(), InlineStyle.Default),
                ("link", InlineStyle.Default with { LinkHref = "https://example.test" })),
        });

        string markdown = Write(document);

        Assert.Contains("- ***Hi***", markdown);
        Assert.Contains("  \n[link](https://example.test)", markdown);
    }

    [Fact(Timeout = 600000)]
    public void Model_To_Markdown_To_Model_RoundTrips_Supported_Subset()
    {
        RichTextDocument expected = RichTextDocument.FromParagraphs(new[]
        {
            MakeParagraph(
                ParagraphStyle.Default,
                ("Hello ", InlineStyle.Default),
                ("bold", InlineStyle.Default with { Bold = true }),
                (" and ", InlineStyle.Default),
                ("italic", InlineStyle.Default with { Italic = true }),
                (" plus ", InlineStyle.Default),
                ("code", InlineStyle.Default with { FontFamily = "monospace" }),
                (" link", InlineStyle.Default with { LinkHref = "mailto:test@example.test" })),
            MakeParagraph(
                ParagraphStyle.Default with { ListKind = ListKind.Numbered, IndentLevel = 1 },
                ("Item", InlineStyle.Default)),
        });

        byte[] bytes = MarkdownDocumentCodec.WriteToArray(expected);
        using var stream = new MemoryStream(bytes);
        RichTextDocument actual = new MarkdownDocumentCodec().Read(stream, RoundTripReadOptions).Document;

        DocumentAssert.Equivalent(expected, actual);
    }

    [Fact(Timeout = 600000)]
    public void Writing_Unsupported_Styles_Reports_Diagnostics()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(new[]
        {
            MakeParagraph(
                ParagraphStyle.Default with { Alignment = TextAlignment.Center },
                ("styled", InlineStyle.Default with { Underline = true, FontSize = 16f })),
        });

        using var stream = new MemoryStream();
        DocumentWriteResult result = new MarkdownDocumentCodec().Write(document, stream);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "markdown.paragraph-style");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "markdown.inline-style");
    }

    [Fact(Timeout = 600000)]
    public void Writing_A_Page_Break_Says_Markdown_Cannot_Express_One()
    {
        // Markdown has no page and so no page break, and there is no fallback
        // that would be honest - a horizontal rule is a rule, and a form feed is
        // a character in the prose. What is left is to say so, which is the only
        // thing that separates a codec that dropped something from one that was
        // never given it.
        RichTextDocument document = RichTextDocument.FromParagraphs(new[]
        {
            MakeParagraph(ParagraphStyle.Default, ("first", InlineStyle.Default)),
            MakeParagraph(
                ParagraphStyle.Default with { PageBreakBefore = true },
                ("second", InlineStyle.Default)),
        });

        using var stream = new MemoryStream();
        DocumentWriteResult result = new MarkdownDocumentCodec().Write(document, stream);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "markdown.page-break");
        Assert.Equal("first\n\nsecond\n", System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact(Timeout = 600000)]
    public void A_Document_That_Breaks_No_Page_Reports_No_Page_Break()
    {
        // The other half of the same claim. A diagnostic every document carries
        // says nothing about any of them.
        using var stream = new MemoryStream();
        DocumentWriteResult result = new MarkdownDocumentCodec()
            .Write(RichTextDocument.FromPlainText("first\nsecond"), stream);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "markdown.page-break");
    }

    [Fact(Timeout = 600000)]
    public void Writes_An_Embedded_Image_As_A_Data_Uri()
    {
        var image = new InlineImage(new byte[] { 1, 2, 3 }, "image/png", 40, 20, "a logo");
        (image, DocumentWriteOptions writeOptions) = Writable(image);
        RichTextDocument document = RichTextDocument.FromParagraphs(new[]
        {
            MakeParagraph(
                ParagraphStyle.Default,
                ("before ", InlineStyle.Default),
                (InlineImage.PlaceholderText, InlineStyle.Default with { Image = image })),
        });

        string markdown = Write(document, writeOptions);

        Assert.Contains("![a logo](data:image/png;base64,AQID)", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("\uFFFC", markdown, StringComparison.Ordinal);
    }

    private static string Write(RichTextDocument document, DocumentWriteOptions? options = null) =>
        System.Text.Encoding.UTF8.GetString(MarkdownDocumentCodec.WriteToArray(document, options));

    private static RichTextParagraph MakeParagraph(
        ParagraphStyle paragraphStyle,
        params (string Text, InlineStyle Style)[] segments)
    {
        RichTextParagraph paragraph = RichTextParagraph.Empty.WithParagraphStyle(paragraphStyle);
        int offset = 0;
        foreach ((string text, InlineStyle style) in segments)
        {
            paragraph = paragraph.InsertText(offset, text, style);
            offset += text.Length;
        }

        return paragraph;
    }

    /// <summary>
    /// Admits <paramref name="image"/> under a policy that permits writing it,
    /// and returns the image bound to that decision together with the options a
    /// writer needs.
    /// </summary>
    /// <remarks>
    /// A writer refuses a picture nobody decided on, so a write test has to say
    /// which decision it is testing under. Reading a document is not that
    /// decision: it grants extraction into the model and nothing that puts the
    /// bytes into an output.
    /// </remarks>
    private static (InlineImage Image, DocumentWriteOptions Options) Writable(InlineImage image)
    {
        var builder = new DocumentConversionContextBuilder(DocumentResourcePolicy.AllowOwnDocuments);
        InlineImage admitted = builder.AdmitImage(
            image,
            DocumentResourceProvenance.CallerSupplied,
            DocumentResourceDisposition.Embedded);

        return (admitted, new DocumentWriteOptions(resources: builder.Build()));
    }

    /// <summary>Read options that also permit writing what was read back out.</summary>
    private static DocumentReadOptions RoundTripReadOptions { get; } =
        new(resourcePolicy: DocumentResourcePolicy.AllowOwnDocuments);

    [Theory(Timeout = 600000)]
    [InlineData("Pipe | tilde ~~s~~ backtick `c`")]
    [InlineData("approx ~5 items")]
    [InlineData("~~~")]
    public void Literal_Tildes_Are_Escaped_Rather_Than_Read_Back_As_Strikethrough(string text)
    {
        // Doubled, a tilde is GitHub-flavored strikethrough, which this codec's
        // own reader honours - so literal tildes written out bare came back as a
        // struck run with the tildes gone. Every other delimiter in the writer
        // was already escaped; this one was missed.
        RichTextDocument expected = RichTextDocument.FromPlainText(text);

        byte[] bytes = MarkdownDocumentCodec.WriteToArray(expected);
        using var stream = new MemoryStream(bytes);
        RichTextDocument actual = new MarkdownDocumentCodec().Read(stream, RoundTripReadOptions).Document;

        Assert.Equal(text, actual.Paragraphs[0].Text);
        Assert.False(actual.Paragraphs[0].StyleAt(0).Strikethrough);
    }

    [Fact(Timeout = 600000)]
    public void A_Struck_Run_Still_Writes_The_Delimiter_It_Means()
    {
        // The escape must not reach the delimiters the writer emits itself.
        RichTextDocument document = RichTextDocument.FromParagraphs(new[]
        {
            MakeParagraph(
                ParagraphStyle.Default,
                ("gone", InlineStyle.Default with { Strikethrough = true })),
        });

        Assert.Contains("~~gone~~", Write(document), StringComparison.Ordinal);
    }

    // The allow-list was enforced on the way in and not on the way out, so a
    // target no reader here would accept could still be written into a document,
    // with no diagnostic. The round trip cannot see that: every reader refuses
    // the same schemes, so a document that wrote the link and one that dropped
    // it both read back without it. These assert the bytes.

    [Theory(Timeout = 600000)]
    [InlineData("javascript:alert(1)")]
    [InlineData("vbscript:msgbox")]
    [InlineData("file:///etc/passwd")]
    [InlineData("/relative/path")]
    public void A_Refused_Link_Target_Never_Reaches_The_Output(string href)
    {
        (string written, DocumentWriteResult result) = WriteLink(href);

        Assert.DoesNotContain(href, written, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "markdown.link");
    }

    [Theory(Timeout = 600000)]
    [InlineData("https://example.test/page")]
    [InlineData("http://example.test/page")]
    [InlineData("mailto:someone@example.test")]
    public void A_Permitted_Link_Target_Is_Still_Written(string href)
    {
        (string written, DocumentWriteResult result) = WriteLink(href);

        Assert.Contains(href, written, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "markdown.link");
    }

    private static (string Written, DocumentWriteResult Result) WriteLink(string href)
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("link", new InlineStyle { LinkHref = href })]);

        using var stream = new MemoryStream();
        DocumentWriteResult result = new MarkdownDocumentCodec().Write(document, stream);
        return (System.Text.Encoding.UTF8.GetString(stream.ToArray()), result);
    }

    [Fact(Timeout = 600000)]
    public void An_Empty_Href_Is_Not_A_Link_And_Is_Not_A_Diagnostic()
    {
        // The other four writers guard on IsNullOrEmpty and say nothing; this
        // one guarded on null, so the empty string the edit language uses to
        // mean "remove this link" was reported as a refused target.
        (string written, DocumentWriteResult result) = WriteLink(string.Empty);

        Assert.Equal("link", written.Trim());
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "markdown.link");
    }
}
