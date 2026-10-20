using System.Text;

namespace Broiler.Documents.Html.Tests;

/// <summary>
/// Content that is in the DOM and is not text the document says: the elements
/// the HTML rendering rules hide, and the NUL the tree builder never lets into
/// body text in the first place.
/// </summary>
public sealed class HtmlHiddenContentTests
{
    [Theory]
    [InlineData("<p>Before</p><noframes><p>hidden</p></noframes><p>After</p>")]
    [InlineData("<p>Before</p><noembed><p>hidden</p></noembed><p>After</p>")]
    [InlineData("<p>Before</p><datalist><option>hidden</option></datalist><p>After</p>")]
    [InlineData("<p>Before</p><map><area alt='hidden'></map><p>After</p>")]
    public void Elements_The_Rendering_Rules_Hide_Contribute_No_Text(string html)
    {
        Assert.Equal("Before\nAfter", Read(html).PlainText);
    }

    [Fact]
    public void Markup_Inside_Noframes_Does_Not_Surface_As_Prose()
    {
        // The parser reads noframes as raw text, so what is inside is never
        // markup - it used to come out as the document's text, tags and all.
        RichTextDocument document = Read(
            "<body><noframes>><abbr><head>\n<progress><h5>title</h5></noframes><p>Body</p></body>");

        Assert.Equal("Body", document.PlainText);
    }

    [Fact]
    public void Ruby_Parentheses_Are_Kept_For_A_Model_Without_Ruby()
    {
        Assert.Equal(
            "漢(kan)",
            Read("<p><ruby>漢<rp>(</rp><rt>kan</rt><rp>)</rp></ruby></p>").PlainText);
    }

    [Theory]
    [InlineData("<p>A\0B</p>")]
    [InlineData("<p>A\0\0B</p>")]
    [InlineData("<table><tr><td>A\0B</td></tr></table>")]
    [InlineData("<pre>A\0B</pre>")]
    [InlineData("<p style='white-space:pre'>A\0B</p>")]
    [InlineData("<p>A&#0;B</p>")]
    public void A_Nul_Is_Not_Text(string html)
    {
        Assert.Equal("AB", Read(html).PlainText);
    }

    [Fact]
    public void A_Nul_Does_Not_Break_Whitespace_Collapsing()
    {
        Assert.Equal("A B", Read("<p>A \0 B</p>").PlainText);
    }

    [Fact]
    public void Other_Control_Characters_Are_Left_Alone()
    {
        // CSS asks for these to be drawn visibly rather than dropped, so they
        // stay in the text; only the NUL, which the parser itself drops, goes.
        Assert.Equal("A\u0001B\u007FC", Read("<p>A\u0001B\u007FC</p>").PlainText);
    }

    private static RichTextDocument Read(string html)
    {
        var codec = new HtmlDocumentCodec();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        return codec.Read(stream).Document;
    }
}
