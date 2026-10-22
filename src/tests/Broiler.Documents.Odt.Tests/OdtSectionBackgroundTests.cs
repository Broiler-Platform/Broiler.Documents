using Broiler.Graphics.Color;

namespace Broiler.Documents.Odt.Tests;

public sealed class OdtSectionBackgroundTests
{
    [Fact]
    public void Nested_Sections_Keep_Their_Colours_And_Return_To_The_Outer_Colour()
    {
        string styles = "<style:style style:name=\"outer\" style:family=\"section\">" +
            "<style:section-properties fo:background-color=\"#81d41a\"/></style:style>" +
            "<style:style style:name=\"inner\" style:family=\"section\">" +
            "<style:section-properties fo:background-color=\"#158466\"/></style:style>";
        var document = OdtTestPackage.ReadBody(
            "<text:p>before</text:p><text:section text:style-name=\"outer\"><text:p>A</text:p>" +
            "<text:section text:style-name=\"inner\"><text:p>B</text:p></text:section>" +
            "<text:p>C</text:p></text:section><text:p>after</text:p>", styles).Document;
        Assert.Equal([1, 2, 3], document.Sections.Select(section => section.ParagraphIndex));
        Assert.Equal([BColor.FromArgb(0x81, 0xd4, 0x1a), BColor.FromArgb(0x15, 0x84, 0x66), BColor.FromArgb(0x81, 0xd4, 0x1a)],
            document.Sections.Select(section => section.Background));

        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(document));
        var restored = new OdtDocumentCodec().Read(stream).Document;
        Assert.Equal(document.Sections.Select(section => section.Background), restored.Sections.Select(section => section.Background));
        Assert.Equal(document.Sections[0].Background, document.Sections[0].Shifted(0, 0, 1)!.Background);
    }
}
