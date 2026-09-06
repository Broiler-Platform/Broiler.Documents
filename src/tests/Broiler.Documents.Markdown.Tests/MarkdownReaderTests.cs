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

    [Fact(Timeout = 600000)]
    public void An_Image_Becomes_Its_Description_And_Says_So()
    {
        // This reader builds no image - they are outside the subset - so the
        // question is what it leaves behind, and CommonMark nominates the
        // description as the fallback. The marker used to survive as prose,
        // because `!` was not special and the `[...](...)` after it went through
        // the link path: the paragraph came back with a stray exclamation mark
        // and the loss was reported as a dropped hyperlink, which it was not.
        DocumentReadResult result = ReadResult("before ![the alt](https://example.test/x.png) after");

        Assert.Equal("before the alt after", result.Document.Paragraphs[0].Text);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "markdown.image.dropped");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "markdown.link");
    }

    [Fact(Timeout = 600000)]
    public void An_Image_With_No_Description_Leaves_Nothing_Behind()
    {
        // A decorative image is written with an empty description, and that is
        // not a malformed one. A link keeps the opposite rule - one with no text
        // has nothing to click - so the two part company here.
        DocumentReadResult result = ReadResult("before ![](https://example.test/x.png) after");

        Assert.Equal("before  after", result.Document.Paragraphs[0].Text);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "markdown.image.dropped");
    }

    [Fact(Timeout = 600000)]
    public void The_Writers_Own_Data_Uri_Image_Reads_Back_As_Its_Description()
    {
        // The round trip this codec actually performs on itself: the writer
        // emits the picture as a data URI, and the reader will not build an
        // image from it. What it must not do is leave half the syntax in the
        // text.
        DocumentReadResult result = ReadResult("a ![square](data:image/png;base64,iVBORw0KGgo=) b");

        Assert.Equal("a square b", result.Document.Paragraphs[0].Text);
    }

    [Theory(Timeout = 600000)]
    // Not images, and none of them should be mistaken for one.
    [InlineData(@"literal \!\[not an image\] here", "literal ![not an image] here")]
    [InlineData("Look! [label](https://example.test) after", "Look! label after")]
    [InlineData("before ![alt]() after", "before ![alt]() after")]
    [InlineData("before ![oops and no close", "before ![oops and no close")]
    [InlineData("Hello! World", "Hello! World")]
    public void What_Is_Not_Image_Syntax_Is_Left_Alone(string markdown, string expected)
    {
        DocumentReadResult result = ReadResult(markdown);

        Assert.Equal(expected, result.Document.Paragraphs[0].Text);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "markdown.image.dropped");
    }
}
