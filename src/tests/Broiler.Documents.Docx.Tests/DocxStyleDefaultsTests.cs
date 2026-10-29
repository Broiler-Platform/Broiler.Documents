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
    public void A_Package_Without_Run_Defaults_Falls_Back_To_Word_2007s()
    {
        // LibreOffice (tdf#108350): no w:rPrDefault means Calibri 11.
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            "<w:p><w:r><w:t>body</w:t></w:r></w:p>",
            string.Empty);

        Assert.Equal(11f, result.Document.StyleDefaults.FontSizePoints);
        Assert.Equal("Calibri", result.Document.StyleDefaults.FontFamily);
    }

    [Fact]
    public void Run_Defaults_That_State_No_Size_Or_Face_Fall_Back_To_Times_New_Roman_10()
    {
        // A w:rPrDefault resets LibreOffice's defaults to 10 point Times New Roman,
        // and the default style here names only a face: the size stays 10, not 12.
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            "<w:p><w:r><w:t>body</w:t></w:r></w:p>",
            "<w:docDefaults><w:rPrDefault><w:rPr><w:lang w:val=\"de-AT\"/></w:rPr></w:rPrDefault></w:docDefaults>");

        Assert.Equal(10f, result.Document.StyleDefaults.FontSizePoints);
        Assert.Equal("Times New Roman", result.Document.StyleDefaults.FontFamily);

        DocumentReadResult arial = DocxTestPackage.ReadStyled(
            "<w:p><w:r><w:t>body</w:t></w:r></w:p>",
            "<w:docDefaults><w:rPrDefault><w:rPr><w:lang w:val=\"de-AT\"/></w:rPr></w:rPrDefault></w:docDefaults>" +
            "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\">" +
            "<w:rPr><w:rFonts w:ascii=\"Arial\" w:hAnsi=\"Arial\"/><w:szCs w:val=\"24\"/></w:rPr></w:style>");

        Assert.Equal(10f, arial.Document.StyleDefaults.FontSizePoints);
        Assert.Equal("Arial", arial.Document.StyleDefaults.FontFamily);
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
