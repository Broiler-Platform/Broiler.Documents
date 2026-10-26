namespace Broiler.Documents.Docx.Tests;

public sealed class DocxTabStopTests
{
    [Fact]
    public void Direct_Stops_Merge_With_The_Style_And_Clear_Only_The_Named_Position()
    {
        string styles = "<w:styles " + DocxTestPackage.BodyNamespaceDeclarations + ">" +
            DocxTestPackage.Style("base", "<w:pPr><w:tabs><w:tab w:val=\"left\" w:pos=\"720\"/>" +
                "<w:tab w:val=\"center\" w:pos=\"1440\"/><w:tab w:val=\"right\" w:pos=\"2880\"/></w:tabs></w:pPr>") + "</w:styles>";
        using var stream = new MemoryStream(DocxTestPackage.FromBody(
            "<w:p><w:pPr><w:pStyle w:val=\"base\"/><w:tabs><w:tab w:val=\"clear\" w:pos=\"720\"/>" +
            "<w:tab w:val=\"decimal\" w:pos=\"1440\" w:leader=\"dot\"/>" +
            "<w:tab w:val=\"right\" w:pos=\"NaN\"/></w:tabs></w:pPr><w:r><w:t>name</w:t><w:tab/><w:t>12.5</w:t></w:r></w:p>",
            new Dictionary<string, string> { ["word/styles.xml"] = styles }));
        var document = new DocxDocumentCodec().Read(stream).Document;
        var tabs = Assert.IsType<ParagraphTabStops>(Assert.Single(document.Paragraphs).Style.TabStops);
        Assert.Equal([new ParagraphTabStop(72, TabAlignment.Decimal, "."), new ParagraphTabStop(144, TabAlignment.Right)], tabs);
        using var written = new MemoryStream(DocxDocumentCodec.WriteToArray(document));
        Assert.Equal(document.Paragraphs[0].Style.TabStops,
            Assert.Single(new DocxDocumentCodec().Read(written).Document.Paragraphs).Style.TabStops);
    }
}
