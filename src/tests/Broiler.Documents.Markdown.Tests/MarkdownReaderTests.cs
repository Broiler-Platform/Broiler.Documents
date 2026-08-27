namespace Broiler.Documents.Markdown.Tests;

public sealed class MarkdownReaderTests
{
    [Fact(Timeout = 600000)]
    public void Reads_Headings_Paragraphs_And_Inline_Styles()
    {
        RichTextDocument document = Read("# Title\n\nHello **bold** *italic* `code` ~~gone~~");

        Assert.Equal("Title\nHello bold italic code gone", document.PlainText);
        Assert.True(document.Paragraphs[0].StyleAt(0).Bold);
        Assert.Equal(24f, document.Paragraphs[0].StyleAt(0).FontSize);

        RichTextParagraph second = document.Paragraphs[1];
        Assert.True(second.StyleAt(6).Bold);
        Assert.True(second.StyleAt(11).Italic);
        Assert.Equal("monospace", second.StyleAt(18).FontFamily);
        Assert.True(second.StyleAt(23).Strikethrough);
    }

    [Fact(Timeout = 600000)]
    public void Reads_Links_And_Drops_Disallowed_Schemes()
    {
        DocumentReadResult result = ReadResult("[ok](https://example.test) [bad](javascript:alert(1))");

        RichTextParagraph paragraph = result.Document.Paragraphs[0];
        Assert.Equal("ok bad", paragraph.Text);
        Assert.Equal("https://example.test", paragraph.StyleAt(0).LinkHref);
        Assert.Null(paragraph.StyleAt(3).LinkHref);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "markdown.link");
    }

    [Fact(Timeout = 600000)]
    public void Reads_Bullet_And_Numbered_Lists()
    {
        RichTextDocument document = Read("- One\n- Two\n\n1. First\n2. Second");

        Assert.Equal("One\nTwo\nFirst\nSecond", document.PlainText);
        Assert.Equal(ListKind.Bullet, document.Paragraphs[0].Style.ListKind);
        Assert.Equal(ListKind.Bullet, document.Paragraphs[1].Style.ListKind);
        Assert.Equal(ListKind.Numbered, document.Paragraphs[2].Style.ListKind);
        Assert.Equal(ListKind.Numbered, document.Paragraphs[3].Style.ListKind);
    }

    [Fact(Timeout = 600000)]
    public void Reads_Blockquotes_As_Indented_Paragraphs()
    {
        RichTextDocument document = Read("> quoted text");

        Assert.Equal("quoted text", document.PlainText);
        Assert.Equal(1, document.Paragraphs[0].Style.IndentLevel);
    }

    [Fact(Timeout = 600000)]
    public void Reads_Hard_Line_Breaks_As_Soft_Breaks()
    {
        RichTextDocument document = Read("A  \nB");

        Assert.Equal("A" + (char)0x2028 + "B", document.PlainText);
    }

    [Fact(Timeout = 600000)]
    public void Reads_Fenced_Code_As_Monospace_With_Soft_Breaks()
    {
        RichTextDocument document = Read("```\na\nb\n```");

        Assert.Equal("a" + (char)0x2028 + "b", document.PlainText);
        Assert.Equal("monospace", document.Paragraphs[0].StyleAt(0).FontFamily);
    }

    private static RichTextDocument Read(string markdown) => ReadResult(markdown).Document;

    private static DocumentReadResult ReadResult(string markdown)
    {
        var codec = new MarkdownDocumentCodec();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(markdown));
        return codec.Read(stream);
    }
}
