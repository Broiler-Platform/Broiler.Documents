using System.Text;
using Broiler.Graphics;

namespace Broiler.Documents.Html.Tests;

public sealed class HtmlWriterTests
{
    [Fact(Timeout = 600000)]
    public void Writes_A_Deterministic_Html_Document()
    {
        string html = Write(RichTextDocument.FromPlainText("hello"));

        Assert.StartsWith("<!DOCTYPE html><html>", html);
        Assert.Contains("<meta charset=\"utf-8\">", html);
        Assert.Contains("<p>hello</p>", html);
    }

    [Fact(Timeout = 600000)]
    public void Writes_Inline_Styles_Links_And_Soft_Breaks()
    {
        RichTextDocument document = SingleParagraph(
            ("Hi", InlineStyle.Default with
            {
                Bold = true,
                Italic = true,
                Underline = true,
                Strikethrough = true,
                FontFamily = "Segoe UI",
                FontSize = 14f,
                Foreground = BColor.Red,
                Background = BColor.FromName("yellow"),
            }),
            (((char)0x2028).ToString(), InlineStyle.Default),
            ("link", InlineStyle.Default with { LinkHref = "https://example.test" }));

        string html = Write(document);

        Assert.Contains("font-weight: bold", html);
        Assert.Contains("font-style: italic", html);
        Assert.Contains("text-decoration: underline line-through", html);
        Assert.Contains("font-family: &quot;Segoe UI&quot;", html);
        Assert.Contains("font-size: 14pt", html);
        Assert.Contains("color: #FF0000", html);
        Assert.Contains("background-color: #FFFF00", html);
        Assert.Contains("<br>", html);
        Assert.Contains("<a href=\"https://example.test\">link</a>", html);
    }

    [Fact(Timeout = 600000)]
    public void Model_To_Html_To_Model_RoundTrips_Supported_Subset()
    {
        RichTextDocument expected = RichTextDocument.FromParagraphs(new[]
        {
            MakeParagraph(
                new ParagraphStyle
                {
                    Alignment = TextAlignment.Right,
                    LineSpacing = 1.25f,
                    IndentLevel = 2,
                    SpacingBefore = 3f,
                    SpacingAfter = 4f,
                },
                ("Hello ", InlineStyle.Default),
                ("world", InlineStyle.Default with
                {
                    Bold = true,
                    Italic = true,
                    FontFamily = "Serif",
                    FontSize = 13f,
                    Foreground = BColor.Blue,
                    Background = BColor.FromName("lavender"),
                }),
                (" link", InlineStyle.Default with { LinkHref = "mailto:test@example.test" })),
            RichTextParagraph.Create("Second", InlineStyle.Default),
        });

        byte[] bytes = HtmlDocumentCodec.WriteToArray(expected);
        using var stream = new MemoryStream(bytes);
        RichTextDocument actual = new HtmlDocumentCodec().Read(stream, RoundTripReadOptions).Document;

        DocumentAssert.Equivalent(expected, actual);
    }

    [Fact(Timeout = 600000)]
    public void A_Paragraph_Holding_A_Tab_Keeps_It_Through_Html()
    {
        // HTML collapses a tab to a space unless the paragraph says otherwise, so
        // the writer declares it and the reader honours that declaration.
        RichTextDocument expected = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("Name\tRole", InlineStyle.Default)]);

        string html = Write(expected);
        using var stream = new MemoryStream(HtmlDocumentCodec.WriteToArray(expected));
        RichTextDocument actual = new HtmlDocumentCodec().Read(stream, RoundTripReadOptions).Document;

        Assert.Contains("white-space: pre-wrap", html, StringComparison.Ordinal);
        Assert.Equal("Name\tRole", actual.Paragraphs[0].Text);
        DocumentAssert.Equivalent(expected, actual);
    }

    [Fact(Timeout = 600000)]
    public void A_Paragraph_With_No_Tab_Is_Not_Given_A_Whitespace_Declaration()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("Name Role", InlineStyle.Default)]);

        Assert.DoesNotContain("white-space", Write(document), StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void Writing_ListKind_Reports_A_Predictable_Diagnostic()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(new[]
        {
            RichTextParagraph.Create("item", InlineStyle.Default, ParagraphStyle.Default with { ListKind = ListKind.Bullet, IndentLevel = 1 }),
        });

        using var stream = new MemoryStream();
        DocumentWriteResult result = new HtmlDocumentCodec().Write(document, stream);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "html.list");
    }

    [Fact(Timeout = 600000)]
    public void Writes_An_Embedded_Image_As_A_Data_Uri()
    {
        var image = new InlineImage(new byte[] { 1, 2, 3 }, "image/png", 40, 20, "a logo");
        (image, DocumentWriteOptions writeOptions) = Writable(image);
        RichTextDocument document = SingleParagraph(
            ("before", InlineStyle.Default),
            (InlineImage.PlaceholderText, InlineStyle.Default with { Image = image }));

        string html = Write(document, writeOptions);

        Assert.Contains("<img src=\"data:image/png;base64,AQID\"", html, StringComparison.Ordinal);
        Assert.Contains("alt=\"a logo\"", html, StringComparison.Ordinal);
        Assert.Contains("width: 40pt", html, StringComparison.Ordinal);
        Assert.Contains("before", html, StringComparison.Ordinal);
        Assert.DoesNotContain("\uFFFC", html, StringComparison.Ordinal);
    }

    private static string Write(RichTextDocument document, DocumentWriteOptions? options = null) =>
        Encoding.UTF8.GetString(HtmlDocumentCodec.WriteToArray(document, options));

    private static RichTextDocument SingleParagraph(params (string Text, InlineStyle Style)[] segments) =>
        RichTextDocument.FromParagraphs(new[] { MakeParagraph(ParagraphStyle.Default, segments) });

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

    [Fact(Timeout = 600000)]
    public void A_Justified_Paragraph_Keeps_Its_Alignment_Through_Html()
    {
        RichTextDocument expected = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create(
                "flush",
                InlineStyle.Default,
                ParagraphStyle.Default with { Alignment = TextAlignment.Justify })]);

        Assert.Contains("text-align: justify", Write(expected));

        byte[] bytes = HtmlDocumentCodec.WriteToArray(expected);
        using var stream = new MemoryStream(bytes);
        RichTextDocument actual = new HtmlDocumentCodec().Read(stream, RoundTripReadOptions).Document;

        Assert.Equal(
            TextAlignment.Justify,
            Assert.Single(actual.Paragraphs).Style.Alignment);
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

    // HTML collapses white space, so a document whose text depends on it needs
    // the paragraph to say otherwise. The writer used to ask for that only when
    // the paragraph held a tab, and a doubled space was therefore written out
    // bare and read back one character shorter - with nothing reported, because
    // from the writer's side nothing had gone wrong.

    [Theory(Timeout = 600000)]
    [InlineData("Lead and  gap and trail.")]
    [InlineData("  leading kept")]
    [InlineData("trailing kept  ")]
    [InlineData("  both  ends  ")]
    [InlineData("a\tb")]
    [InlineData("a     b")]
    // A non-breaking space is not HTML white space and must not be folded into
    // the space beside it. char.IsWhiteSpace says otherwise, which is a Unicode
    // answer to a CSS question, and the character the document was written with
    // was lost to it.
    [InlineData("a  b and   c")]
    public void White_Space_That_Html_Would_Collapse_Survives_A_Round_Trip(string text)
    {
        RichTextDocument expected = RichTextDocument.FromPlainText(text);

        byte[] bytes = HtmlDocumentCodec.WriteToArray(expected);
        using var stream = new MemoryStream(bytes);
        RichTextDocument actual = new HtmlDocumentCodec().Read(stream, RoundTripReadOptions).Document;

        Assert.Equal(text, actual.Paragraphs[0].Text);
    }

    [Fact(Timeout = 600000)]
    public void A_Paragraph_That_Needs_No_Help_Does_Not_Ask_For_It()
    {
        // The declaration is not free - it changes how a browser lays the
        // paragraph out - so it goes on the paragraphs that need it and no
        // others.
        string html = Write(RichTextDocument.FromPlainText("nothing special here"));

        Assert.Contains("<p>nothing special here</p>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(HtmlWriter.PreserveWhitespaceDeclaration, html, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void A_Paragraph_Whose_Spaces_Would_Collapse_Asks_To_Keep_Them()
    {
        string html = Write(RichTextDocument.FromPlainText("two  spaces"));

        Assert.Contains(HtmlWriter.PreserveWhitespaceDeclaration, html, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void Ordinary_Html_Still_Collapses_Its_Source_Formatting()
    {
        // The other half of the same rule, and the one a change here could
        // break: markup indented for a human to read must not arrive with that
        // indentation in the text.
        byte[] bytes = Encoding.UTF8.GetBytes(
            "<html><body>\n  <p>\n    collapsed   text\n  </p>\n</body></html>");

        using var stream = new MemoryStream(bytes);
        RichTextDocument document = new HtmlDocumentCodec().Read(stream).Document;

        Assert.StartsWith("collapsed text", document.Paragraphs[0].Text, StringComparison.Ordinal);
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
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "html.link");
    }

    [Theory(Timeout = 600000)]
    [InlineData("https://example.test/page")]
    [InlineData("http://example.test/page")]
    [InlineData("mailto:someone@example.test")]
    public void A_Permitted_Link_Target_Is_Still_Written(string href)
    {
        (string written, DocumentWriteResult result) = WriteLink(href);

        Assert.Contains(href, written, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "html.link");
    }

    private static (string Written, DocumentWriteResult Result) WriteLink(string href)
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("link", new InlineStyle { LinkHref = href })]);

        using var stream = new MemoryStream();
        DocumentWriteResult result = new HtmlDocumentCodec().Write(document, stream);
        return (Encoding.UTF8.GetString(stream.ToArray()), result);
    }
}
