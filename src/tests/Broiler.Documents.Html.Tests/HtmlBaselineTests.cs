using System.Text;

namespace Broiler.Documents.Html.Tests;

public sealed class HtmlBaselineTests
{
    [Fact]
    public void Superscript_And_Subscript_Roundtrip()
    {
        var codec = new HtmlDocumentCodec();
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("<p>x<sup>2</sup> H<sub>2</sub>O</p>"));
        var document = codec.Read(input).Document;
        using var output = new MemoryStream(HtmlWriter.WriteToArray(document));
        var paragraph = Assert.Single(codec.Read(output).Document.Paragraphs);

        Assert.Equal("x2 H2O", paragraph.Text);
        Assert.Equal(BaselineShift.Superscript(), paragraph.StyleAt(1).Shift);
        Assert.Equal(BaselineShift.Subscript(), paragraph.StyleAt(4).Shift);
        Assert.Null(paragraph.StyleAt(5).Shift);
    }
}
