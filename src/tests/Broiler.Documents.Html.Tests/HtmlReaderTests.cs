using System.Text;
using Broiler.Graphics;

namespace Broiler.Documents.Html.Tests;

public sealed class HtmlReaderTests
{
    [Fact(Timeout = 600000)]
    public void Reads_Paragraphs_And_Common_Inline_Formatting()
    {
        RichTextDocument document = Read(
            "<p>Hello <strong>bold</strong> <em>italic</em> <u>under</u> <s>gone</s></p><p>Second</p>");

        Assert.Equal("Hello bold italic under gone\nSecond", document.PlainText);
        RichTextParagraph first = document.Paragraphs[0];
        Assert.Equal(8, first.Runs.Count);
        Assert.True(first.StyleAt(6).Bold);
        Assert.True(first.StyleAt(11).Italic);
        Assert.True(first.StyleAt(18).Underline);
        Assert.True(first.StyleAt(24).Strikethrough);
    }

    [Fact(Timeout = 600000)]
    public void Reads_Style_Attributes_For_Model_Inline_And_Paragraph_Styles()
    {
        RichTextDocument document = Read(
            "<p style='text-align:center; line-height:1.5; margin-top:6pt; margin-bottom:12pt; margin-left:36pt'>" +
            "<span style='color:#336699; background-color:yellow; font-family:\"Segoe UI\"; font-size:14pt; font-weight:bold; font-style:italic; text-decoration: underline line-through'>Styled</span>" +
            "</p>");

        RichTextParagraph paragraph = document.Paragraphs[0];
        Assert.Equal("Styled", paragraph.Text);
        Assert.Equal(TextAlignment.Center, paragraph.Style.Alignment);
        Assert.Equal(1.5f, paragraph.Style.LineSpacing);
        Assert.Equal(2, paragraph.Style.IndentLevel);
        Assert.Equal(6f, paragraph.Style.SpacingBefore);
        Assert.Equal(12f, paragraph.Style.SpacingAfter);

        InlineStyle style = paragraph.StyleAt(0);
        Assert.True(style.Bold);
        Assert.True(style.Italic);
        Assert.True(style.Underline);
        Assert.True(style.Strikethrough);
        Assert.Equal("Segoe UI", style.FontFamily);
        Assert.Equal(14f, style.FontSize);
        Assert.Equal(BColor.FromArgb(0x33, 0x66, 0x99), style.Foreground);
        Assert.Equal(BColor.FromName("yellow"), style.Background);
    }

    [Fact(Timeout = 600000)]
    public void Reads_Links_And_Drops_Disallowed_Schemes()
    {
        DocumentReadResult result = ReadResult(
            "<p><a href='https://example.test'>ok</a> <a href='javascript:alert(1)'>bad</a></p>");

        RichTextParagraph paragraph = result.Document.Paragraphs[0];
        Assert.Equal("ok bad", paragraph.Text);
        Assert.Equal("https://example.test", paragraph.StyleAt(0).LinkHref);
        Assert.Null(paragraph.StyleAt(3).LinkHref);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "html.link");
    }

    [Fact(Timeout = 600000)]
    public void Reads_Br_As_Soft_Line_Break_And_Decodes_Entities()
    {
        RichTextDocument document = Read("<p>A&amp;B<br>C</p>");

        Assert.Equal("A&B" + (char)0x2028 + "C", document.PlainText);
    }

    [Fact(Timeout = 600000)]
    public void Reads_List_Items_As_List_Paragraphs()
    {
        RichTextDocument document = Read("<ol><li>One</li><li>Two</li></ol>");

        Assert.Equal("One\nTwo", document.PlainText);
        Assert.Equal(ListKind.Numbered, document.Paragraphs[0].Style.ListKind);
        Assert.Equal(ListKind.Numbered, document.Paragraphs[1].Style.ListKind);
        Assert.Equal(1, document.Paragraphs[0].Style.IndentLevel);
    }

    [Fact(Timeout = 600000)]
    public void Skips_Script_Style_And_External_Content()
    {
        DocumentReadResult result = ReadResult(
            "<style>p{color:red}</style><p>Keep</p><script>alert('x')</script><img src='https://example.test/a.png'>");

        Assert.Equal("Keep", result.Document.PlainText);
        Assert.DoesNotContain("alert", result.Document.PlainText);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "html.skip.external");
    }

    private static RichTextDocument Read(string html) => ReadResult(html).Document;

    private static DocumentReadResult ReadResult(string html)
    {
        var codec = new HtmlDocumentCodec();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        return codec.Read(stream);
    }
}
