namespace Broiler.Documents.Docx.Tests;

public sealed class DocxRubyTests
{
    [Fact]
    public void Ruby_Element_Extracts_Ruby_Base_Text()
    {
        string bodyXml =
            "<w:p>" +
            "<w:r><w:t>A</w:t></w:r>" +
            "<w:r>" +
            "<w:ruby>" +
            "<w:rubyPr><w:rubyAlign w:val=\"center\"/><w:hps w:val=\"16\"/><w:hpsRaise w:val=\"38\"/></w:rubyPr>" +
            "<w:rt><w:r><w:t>きょう</w:t></w:r></w:rt>" +
            "<w:rubyBase><w:r><w:t>今日</w:t></w:r></w:rubyBase>" +
            "</w:ruby>" +
            "</w:r>" +
            "<w:r><w:t>B</w:t></w:r>" +
            "</w:p>";

        DocumentReadResult result = DocxTestPackage.ReadBody(bodyXml);
        RichTextParagraph paragraph = Assert.Single(result.Document.Paragraphs);
        Assert.Equal("A今日B", paragraph.Text);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.ruby");
    }

    [Fact]
    public void Empty_Result_Ruby_Equation_Field_Extracts_Base_Text()
    {
        string bodyXml =
            "<w:p>" +
            "<w:r><w:t>前</w:t></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
            "<w:r><w:instrText>EQ \\* jc0 \\* \"Font:TakaoExMincho\" \\* hps16 \\o(\\s\\up 19(にがつ),二月)</w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>" +
            "<w:r><w:t>後</w:t></w:r>" +
            "</w:p>";

        DocumentReadResult result = DocxTestPackage.ReadBody(bodyXml);
        RichTextParagraph paragraph = Assert.Single(result.Document.Paragraphs);
        Assert.Equal("前二月後", paragraph.Text);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.field.ruby");
    }
}
