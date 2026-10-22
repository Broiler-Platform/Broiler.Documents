namespace Broiler.Documents.Docx.Tests;

public sealed class DocxExactIndentTests
{
    [Theory]
    [InlineData("firstLine", 5.25f)]
    [InlineData("hanging", -5.25f)]
    public void Exact_Indents_Override_Their_Style_And_Round_Trip(string attribute, float expectedFirst)
    {
        string styles = "<w:styles " + DocxTestPackage.BodyNamespaceDeclarations + ">" +
            DocxTestPackage.Style("base", "<w:pPr><w:ind w:left=\"500\" w:right=\"190\"/></w:pPr>") + "</w:styles>";
        using var stream = new MemoryStream(DocxTestPackage.FromBody(
            "<w:p><w:pPr><w:pStyle w:val=\"base\"/><w:ind w:left=\"0\" w:" + attribute +
            "=\"105\"/></w:pPr><w:r><w:t>body</w:t></w:r></w:p>",
            new Dictionary<string, string> { ["word/styles.xml"] = styles }));
        var document = new DocxDocumentCodec().Read(stream).Document;
        var style = Assert.Single(document.Paragraphs).Style;
        Assert.Equal(0, style.LeftIndent);
        Assert.Equal(9.5f, style.RightIndent);
        Assert.Equal(expectedFirst, style.FirstLineIndent);
        using var written = new MemoryStream(DocxDocumentCodec.WriteToArray(document));
        Assert.Equal(style, Assert.Single(new DocxDocumentCodec().Read(written).Document.Paragraphs).Style);
    }
}
