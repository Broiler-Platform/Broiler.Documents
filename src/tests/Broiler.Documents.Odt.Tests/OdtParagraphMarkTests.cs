namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// ODF has no paragraph mark of its own: an empty paragraph is as tall as its
/// paragraph style's text properties say, which is what the reader gives the
/// model's mark.
/// </summary>
public sealed class OdtParagraphMarkTests
{
    [Fact]
    public void An_Empty_Paragraph_Has_Its_Paragraph_Style_Text_Properties_As_Its_Mark()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p text:style-name=\"P1\"/>" + OdtTestPackage.Paragraph("after"),
            OdtTestPackage.Style("P1", "<style:text-properties fo:font-size=\"8pt\"/>"));

        RichTextParagraph empty = result.Document.Paragraphs[0];
        Assert.Equal(0, empty.Length);
        Assert.Equal(8f, empty.MarkStyle.FontSize);
        Assert.Null(result.Document.Paragraphs[1].MarkStyle.FontSize);
    }
}
