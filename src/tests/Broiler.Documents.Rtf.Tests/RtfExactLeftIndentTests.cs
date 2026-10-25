using System.Text;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfExactLeftIndentTests
{
    [Theory]
    [InlineData(500, 25)]
    [InlineData(510, 25.5)]
    [InlineData(-100, -5)]
    [InlineData(0, 0)]
    public void Exact_Left_Indents_Are_Kept_On_Read_And_Write(int twips, double expected)
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes("{\\rtf1\\li" + twips + " offset}"));
        var document = new RtfDocumentCodec().Read(input).Document;
        var paragraph = Assert.Single(document.Paragraphs);
        Assert.Equal(expected, paragraph.Style.LeftIndentIn(), 4);
        using var written = new MemoryStream();
        new RtfDocumentCodec().Write(document, written);
        written.Position = 0;
        Assert.Equal(expected, Assert.Single(new RtfDocumentCodec().Read(written).Document.Paragraphs).Style.LeftIndentIn(), 4);
    }
}
