namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Office issue #242, DOCX pages one side drew blank: symbol characters, the
/// <c>IF</c> fields LibreOffice works out again on the page, and a drawing
/// group of one shape.
/// </summary>
public sealed class DocxIssue242BlankTests
{
    [Fact]
    public void A_Symbol_Character_Keeps_Its_Font_And_Is_Marked_As_One()
    {
        // fdo78384.docx: a 72 point Wingdings arrow inserted as w:sym.
        RichTextDocument document = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:t>A</w:t></w:r><w:r><w:sym w:font=\"Wingdings\" w:char=\"F0E7\"/></w:r></w:p>").Document;

        RichTextParagraph paragraph = document.Paragraphs[0];
        Assert.Equal("A\uF0E7", paragraph.Text);
        Assert.Equal(2, paragraph.Runs.Count);
        Assert.False(paragraph.Runs[0].Style.IsSymbolCharacter);
        Assert.Equal("Wingdings", paragraph.Runs[1].Style.FontFamily);
        Assert.True(paragraph.Runs[1].Style.IsSymbolCharacter);
    }
}
