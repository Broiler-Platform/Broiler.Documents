namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// The model holds one family per run, the one its Latin text is drawn in.
/// <c>w:cs</c> and <c>w:eastAsia</c> name the fonts for complex-script and East
/// Asian text (ECMA-376 §17.3.2.26), and they were taken for the Latin font when
/// <c>w:ascii</c> was absent: <c>tdf170602_exportContext.docx</c>'s headings,
/// whose styles say only <c>w:cs="Arial"</c>, were drawn in Arial over a Times
/// New Roman document.
/// </summary>
public sealed class DocxScriptFontTests
{
    private static InlineStyle Read(string headingFonts, string defaultFonts) =>
        Assert.Single(DocxTestPackage.ReadStyled(
            DocxTestPackage.StyledParagraph("Heading1", "Einleitung"),
            DocxTestPackage.DocumentDefaults("<w:rPr>" + defaultFonts + "</w:rPr>") +
            DocxTestPackage.Style("Heading1", "<w:rPr>" + headingFonts + "</w:rPr>")).Document.Paragraphs).StyleAt(0);

    [Theory]
    [InlineData("<w:rFonts w:cs=\"Arial\"/>")]
    [InlineData("<w:rFonts w:eastAsia=\"SimSun\"/>")]
    [InlineData("<w:rFonts w:eastAsia=\"SimSun\" w:cs=\"Arial\"/>")]
    public void A_Style_Naming_Only_Other_Scripts_Fonts_Keeps_The_Latin_Font(string fonts)
    {
        InlineStyle style = Read(fonts, "<w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/>");

        Assert.Equal("Times New Roman", style.FontFamily);
    }

    [Theory]
    [InlineData("<w:rFonts w:ascii=\"Arial\"/>")]
    [InlineData("<w:rFonts w:hAnsi=\"Arial\"/>")]
    [InlineData("<w:rFonts w:ascii=\"Arial\" w:cs=\"Tahoma\"/>")]
    public void A_Latin_Font_Overrides_The_Inherited_One(string fonts)
    {
        InlineStyle style = Read(fonts, "<w:rFonts w:ascii=\"Times New Roman\"/>");

        Assert.Equal("Arial", style.FontFamily);
    }

    [Fact]
    public void An_East_Asian_Font_Names_The_Family_Of_A_Run_That_Inherited_None()
    {
        InlineStyle style = Read("<w:rFonts w:eastAsia=\"SimSun\"/>", string.Empty);

        Assert.Equal("SimSun", style.FontFamily);
    }
}
