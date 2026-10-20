using System.Text;

namespace Broiler.Documents.Html.Tests;

public sealed class HtmlQuotedFontTests
{
    [Theory]
    [InlineData(@"Family\Face")]
    [InlineData("Family \"Quoted\"")]
    [InlineData("'Family'")]
    [InlineData("\"Family\"")]
    public void Writer_Escaped_Font_Names_Survive_Import(string family)
    {
        RichTextDocument source = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("text", InlineStyle.Default with { FontFamily = family })]);
        using var stream = new MemoryStream();
        var codec = new HtmlDocumentCodec();
        codec.Write(source, stream);
        stream.Position = 0;
        RichTextDocument restored = codec.Read(stream).Document;

        Assert.Equal(family, restored.Paragraphs[0].Runs[0].Style.FontFamily);
        Assert.Equal("text", restored.PlainText);
    }

    [Theory]
    [InlineData("Alpha;Beta", false)]
    [InlineData("Alpha;Beta", true)]
    [InlineData("Alpha,Beta", false)]
    [InlineData("Alpha,Beta", true)]
    public void Quoted_Font_Name_Punctuation_Is_Preserved(string family, bool stylesheet)
    {
        string declaration = "font-family:'" + family + "', serif; font-size:16pt; color:red";
        string html = stylesheet
            ? "<style>p { " + declaration + " }</style><p>text</p>"
            : "<p style=\"" + declaration + "\">text</p>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        RichTextDocument document = new HtmlDocumentCodec().Read(stream).Document;
        InlineStyle style = Assert.Single(Assert.Single(document.Paragraphs).Runs).Style;

        Assert.Equal(family, style.FontFamily);
        Assert.Equal(16, style.FontSize);
        Assert.Equal("text", document.PlainText);
    }
}
