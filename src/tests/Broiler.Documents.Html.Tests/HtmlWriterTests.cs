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
        RichTextDocument actual = new HtmlDocumentCodec().Read(stream).Document;

        DocumentAssert.Equivalent(expected, actual);
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
        RichTextDocument document = SingleParagraph(
            ("before", InlineStyle.Default),
            (InlineImage.PlaceholderText, InlineStyle.Default with { Image = image }));

        string html = Write(document);

        Assert.Contains("<img src=\"data:image/png;base64,AQID\"", html, StringComparison.Ordinal);
        Assert.Contains("alt=\"a logo\"", html, StringComparison.Ordinal);
        Assert.Contains("width: 40pt", html, StringComparison.Ordinal);
        Assert.Contains("before", html, StringComparison.Ordinal);
        Assert.DoesNotContain("\uFFFC", html, StringComparison.Ordinal);
    }

    private static string Write(RichTextDocument document) =>
        Encoding.UTF8.GetString(HtmlDocumentCodec.WriteToArray(document));

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
}
