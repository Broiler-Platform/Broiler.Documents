using System.Text;

namespace Broiler.Documents.Html.Tests;

public sealed class HtmlFiniteLengthTests
{
    [Theory]
    [InlineData("NaNpt")]
    [InlineData("Infinitypt")]
    [InlineData("1e100pt")]
    [InlineData("1e38in")]
    public void Nonfinite_Lengths_Do_Not_Replace_Inherited_Values(string invalid)
    {
        RichTextDocument document = Read("<div style='font-size:16pt;margin-top:6pt;margin-bottom:8pt'>" +
            "<p style='font-size:" + invalid + ";margin-top:" + invalid + ";margin-bottom:" + invalid + "'>text</p></div>");
        RichTextParagraph paragraph = Assert.Single(document.Paragraphs);
        Assert.Equal(16, paragraph.Runs[0].Style.FontSize);
        Assert.Equal(6, paragraph.Style.SpacingBefore);
        Assert.Equal(8, paragraph.Style.SpacingAfter);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e100")]
    [InlineData("1e100%")]
    [InlineData("1e38in")]
    public void Nonfinite_Line_Heights_Do_Not_Replace_Inherited_Values(string invalid)
    {
        RichTextDocument document = Read("<div style='line-height:1.5'><p style='line-height:" + invalid + "'>text</p></div>");
        Assert.Equal(1.5f, Assert.Single(document.Paragraphs).Style.LineSpacing);
    }

    private static RichTextDocument Read(string html) =>
        new HtmlDocumentCodec().Read(new MemoryStream(Encoding.UTF8.GetBytes(html))).Document;
}
