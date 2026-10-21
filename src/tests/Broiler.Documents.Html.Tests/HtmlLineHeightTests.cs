using System.Text;

namespace Broiler.Documents.Html.Tests;

/// <summary>
/// A CSS <c>line-height</c> given as a length is the least a line is, which is
/// how LibreOffice's HTML import reads it. It used to become a multiple of
/// twelve points, so <c>24pt</c> was double spacing at any font size.
/// </summary>
public sealed class HtmlLineHeightTests
{
    private static RichTextDocument Read(string html) =>
        new HtmlDocumentCodec().Read(new MemoryStream(Encoding.UTF8.GetBytes(html))).Document;

    [Theory]
    [InlineData("24pt", 24f)]
    [InlineData("0.5in", 36f)]
    public void A_Length_Is_An_At_Least_Height(string value, float points)
    {
        ParagraphStyle style = Assert.Single(Read("<p style='line-height:" + value + "'>text</p>").Paragraphs).Style;

        Assert.Equal(LineHeightRule.AtLeast, style.LineHeightRule);
        Assert.Equal(points, style.LineHeight, 3);
        Assert.Equal(1f, style.LineSpacing);
    }

    [Fact]
    public void A_Number_Over_An_Inherited_Length_Is_A_Multiple_Again()
    {
        ParagraphStyle style = Assert.Single(Read(
            "<div style='line-height:24pt'><p style='line-height:1.5'>text</p></div>").Paragraphs).Style;

        Assert.Equal(LineHeightRule.Proportional, style.LineHeightRule);
        Assert.Equal(1.5f, style.LineSpacing);
    }

    [Fact]
    public void An_Exact_Height_Is_Written_As_A_Length_And_Said()
    {
        RichTextDocument source = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Plain("x").WithParagraphStyle(
                ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 10f }),
        ]);

        using var stream = new MemoryStream();
        DocumentWriteResult result = new HtmlDocumentCodec().Write(source, stream);
        string html = Encoding.UTF8.GetString(stream.ToArray());

        Assert.Contains("line-height: 10pt", html);
        Assert.Contains(result.Diagnostics, d => d.Code == "html.lineheight.exact");
        Assert.Equal(LineHeightRule.AtLeast, Read(html).Paragraphs[0].Style.LineHeightRule);
    }
}
