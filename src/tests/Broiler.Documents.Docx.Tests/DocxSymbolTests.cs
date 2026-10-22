namespace Broiler.Documents.Docx.Tests;

public sealed class DocxSymbolTests
{
    [Fact]
    public void Symbols_Keep_Their_Private_Use_Character_And_Font_Without_Changing_Adjacent_Text()
    {
        using var stream = new MemoryStream(DocxTestPackage.FromBody(
            "<w:p><w:r><w:rPr><w:b/><w:sz w:val=\"24\"/></w:rPr>" +
            "<w:t>before</w:t><w:sym w:font=\"Wingdings 2\" w:char=\"F0A3\"/>" +
            "<w:t>after</w:t></w:r></w:p>"));

        RichTextParagraph paragraph = Assert.Single(new DocxDocumentCodec().Read(stream).Document.Paragraphs);
        Assert.Equal("before\uF0A3after", paragraph.Text);
        Assert.Equal("Wingdings 2", paragraph.StyleAt(6).FontFamily);
        Assert.True(paragraph.StyleAt(6).Bold);
        Assert.Equal(12f, paragraph.StyleAt(6).FontSize);
        Assert.Null(paragraph.StyleAt(7).FontFamily);

        using var written = new MemoryStream(DocxDocumentCodec.WriteToArray(RichTextDocument.FromParagraphs([paragraph])));
        RichTextParagraph restored = Assert.Single(new DocxDocumentCodec().Read(written).Document.Paragraphs);
        Assert.Equal(paragraph.Text, restored.Text);
        Assert.Equal("Wingdings 2", restored.StyleAt(6).FontFamily);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("110000")]
    [InlineData("D800")]
    [InlineData("0000")]
    public void Invalid_Symbols_Are_Diagnosed_And_Leave_Adjacent_Text_Intact(string code)
    {
        using var stream = new MemoryStream(DocxTestPackage.FromBody(
            "<w:p><w:r><w:t>A</w:t><w:sym w:font=\"Wingdings\" w:char=\"" + code + "\"/>" +
            "<w:t>B</w:t></w:r></w:p>"));
        DocumentReadResult result = new DocxDocumentCodec().Read(stream);
        Assert.Equal("AB", result.Document.PlainText);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.symbol");
    }
}
