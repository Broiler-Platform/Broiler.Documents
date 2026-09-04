namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// DOCX as a consumer of the document style defaults (PDF roadmap §6.4): what
/// <c>w:docDefaults</c> and the default paragraph style put on the document.
/// </summary>
public sealed class DocxStyleDefaultsTests
{
    [Fact]
    public void Doc_Defaults_Become_The_Documents_Own()
    {
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            "<w:p><w:r><w:t>body</w:t></w:r></w:p>",
            "<w:docDefaults><w:rPrDefault><w:rPr>" +
            "<w:rFonts w:ascii=\"Georgia\"/><w:sz w:val=\"20\"/>" +
            "</w:rPr></w:rPrDefault></w:docDefaults>");

        Assert.Equal(10f, result.Document.StyleDefaults.FontSizePoints);
        Assert.Equal("Georgia", result.Document.StyleDefaults.FontFamily);
    }

    [Fact]
    public void The_Default_Paragraph_Style_Outranks_The_Doc_Defaults()
    {
        // Word writes both and they disagree more often than not, so the defaults
        // are resolved through the chain a real run goes through rather than read
        // off w:docDefaults directly.
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            "<w:p><w:r><w:t>body</w:t></w:r></w:p>",
            "<w:docDefaults><w:rPrDefault><w:rPr><w:sz w:val=\"20\"/></w:rPr></w:rPrDefault></w:docDefaults>" +
            "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\">" +
            "<w:rPr><w:sz w:val=\"28\"/></w:rPr></w:style>");

        Assert.Equal(14f, result.Document.StyleDefaults.FontSizePoints);
    }

    [Fact]
    public void A_Package_That_States_No_Defaults_Keeps_The_Shared_Ones()
    {
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            "<w:p><w:r><w:t>body</w:t></w:r></w:p>",
            string.Empty);

        Assert.Equal(
            DocumentStyleDefaults.FallbackFontSizePoints,
            result.Document.StyleDefaults.FontSizePoints);
        Assert.Null(result.Document.StyleDefaults.FontFamily);
    }

    [Fact]
    public void Reading_The_Defaults_Does_Not_Change_What_A_Run_Resolves_To()
    {
        // The document defaults are added beside the style cascade, not in place
        // of it: Word's docDefaults still flow into each run the way they did.
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            "<w:p><w:r><w:t>body</w:t></w:r></w:p>",
            "<w:docDefaults><w:rPrDefault><w:rPr><w:sz w:val=\"20\"/></w:rPr></w:rPrDefault></w:docDefaults>");

        Assert.Equal(10f, Assert.Single(result.Document.Paragraphs).StyleAt(0).FontSize);
    }
}
