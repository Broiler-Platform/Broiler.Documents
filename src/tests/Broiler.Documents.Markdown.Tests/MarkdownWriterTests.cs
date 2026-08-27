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
        RichTextDocument actual = new MarkdownDocumentCodec().Read(stream).Document;

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
    public void Writes_An_Embedded_Image_As_A_Data_Uri()
    {
        var image = new InlineImage(new byte[] { 1, 2, 3 }, "image/png", 40, 20, "a logo");
        RichTextDocument document = RichTextDocument.FromParagraphs(new[]
        {
            MakeParagraph(
                ParagraphStyle.Default,
                ("before ", InlineStyle.Default),
                (InlineImage.PlaceholderText, InlineStyle.Default with { Image = image })),
        });

        string markdown = Write(document);

        Assert.Contains("![a logo](data:image/png;base64,AQID)", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("\uFFFC", markdown, StringComparison.Ordinal);
    }

    private static string Write(RichTextDocument document) =>
        System.Text.Encoding.UTF8.GetString(MarkdownDocumentCodec.WriteToArray(document));

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
}
