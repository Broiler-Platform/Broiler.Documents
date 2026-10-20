using System.Text;

namespace Broiler.Documents.Html.Tests;

public sealed class HtmlEntityDecodingTests
{
    [Theory]
    [InlineData("<p>&amp;lt;tag&amp;gt;</p>", "&lt;tag&gt;")]
    [InlineData("<pre>&amp;nbsp; text</pre>", "&nbsp; text")]
    [InlineData("<p>&#38;copy;</p>", "&copy;")]
    public void Text_References_Are_Decoded_Only_Once(string html, string expected)
    {
        Assert.Equal(expected, Read(html).PlainText);
    }

    [Fact]
    public void Literal_Entity_Text_Survives_Writer_Reader_Round_Trip()
    {
        RichTextDocument source = RichTextDocument.FromPlainText("&amp; &lt; &#65;");
        using var stream = new MemoryStream();
        new HtmlDocumentCodec().Write(source, stream);
        stream.Position = 0;
        Assert.Equal(source.PlainText, new HtmlDocumentCodec().Read(stream).Document.PlainText);
    }

    [Fact]
    public void Link_Target_References_Are_Decoded_Only_Once()
    {
        RichTextDocument document = Read("<p><a href='https://example.test/?q=&amp;amp;'>link</a></p>");
        Assert.Equal("https://example.test/?q=&amp;", document.Paragraphs[0].Runs[0].Style.LinkHref);
    }

    [Theory]
    [InlineData("<p><font face='A&amp;amp;B'>text</font></p>")]
    [InlineData("<p style=\"font-family:'A&amp;amp;B'\">text</p>")]
    [InlineData("<style>p { font-family:'A&amp;B' }</style><p>text</p>")]
    public void Font_Family_Entity_Text_Is_Preserved_After_Dom_Parsing(string html)
    {
        Assert.Equal("A&amp;B", Read(html).Paragraphs[0].Runs[0].Style.FontFamily);
    }

    private static RichTextDocument Read(string html)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        return new HtmlDocumentCodec().Read(stream).Document;
    }
}
