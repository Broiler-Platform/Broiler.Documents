using System.Text;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfSecurityTests
{
    private static byte[] Bytes(string s) => Encoding.Latin1.GetBytes(s);

    private static DocumentReadResult ReadResult(string rtf) => RtfReader.Read(Bytes(rtf));

    private static RichTextDocument Read(string rtf) => ReadResult(rtf).Document;

    [Fact]
    public void Embedded_Objects_Are_Skipped_Not_Instantiated()
    {
        DocumentReadResult result = ReadResult(
            "{\\rtf1 before {\\object\\objemb{\\*\\objdata 0102030405}}after}");

        Assert.Equal("before after", result.Document.PlainText);
        Assert.Contains(result.Diagnostics, d => d.Code == "rtf.embedded");
    }

    [Fact]
    public void Non_Hyperlink_Fields_Do_Not_Produce_A_Link()
    {
        // INCLUDEPICTURE points at a URL; it must never be fetched or turned into a link.
        RichTextDocument document = Read(
            "{\\rtf1{\\field{\\*\\fldinst{INCLUDEPICTURE \"http://evil.example/x.png\"}}{\\fldrslt img}}}");

        Assert.Equal("img", document.PlainText);
        Assert.Null(document.Paragraphs[0].StyleAt(0).LinkHref);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>x</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("vbscript:evil")]
    public void Disallowed_Url_Schemes_Are_Dropped(string url)
    {
        DocumentReadResult result = ReadResult(
            "{\\rtf1{\\field{\\*\\fldinst{HYPERLINK \"" + url + "\"}}{\\fldrslt click}}}");

        Assert.Equal("click", result.Document.PlainText);
        Assert.Null(result.Document.Paragraphs[0].StyleAt(0).LinkHref);
        Assert.Contains(result.Diagnostics, d => d.Code == "rtf.link");
    }

    [Theory]
    [InlineData("https://example.com/path")]
    [InlineData("http://example.com")]
    [InlineData("mailto:a@b.com")]
    public void Allowed_Url_Schemes_Are_Kept_As_Inert_Text(string url)
    {
        RichTextDocument document = Read(
            "{\\rtf1{\\field{\\*\\fldinst{HYPERLINK \"" + url + "\"}}{\\fldrslt click}}}");

        Assert.Equal(url, document.Paragraphs[0].StyleAt(0).LinkHref);
    }

    [Fact]
    public void Reading_Urls_Completes_Without_Blocking_On_The_Network()
    {
        // Completing at all proves nothing was fetched (the hosts do not resolve).
        RichTextDocument document = Read(
            "{\\rtf1 {\\field{\\*\\fldinst{HYPERLINK \"https://nonexistent.invalid/a\"}}{\\fldrslt a}} " +
            "{\\field{\\*\\fldinst{HYPERLINK \"http://also.invalid/b\"}}{\\fldrslt b}}}");

        Assert.Contains("a", document.PlainText);
        Assert.Contains("b", document.PlainText);
    }
}
