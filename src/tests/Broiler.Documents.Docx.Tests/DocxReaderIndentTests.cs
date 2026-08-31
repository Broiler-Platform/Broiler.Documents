namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Covers <c>w:ind</c>, whose leading indent has two legal spellings:
/// <c>w:left</c> and the writing-direction name <c>w:start</c>. Which one a
/// file carries depends on the filter that wrote it — LibreOffice's "Office
/// Open XML Text" export writes <c>w:start</c>, its "Word 2007-365" export
/// writes <c>w:left</c> — so a reader that knows a single name flattens every
/// indent in the files the other filter wrote.
/// </summary>
public sealed class DocxReaderIndentTests
{
    [Fact(Timeout = 600000)]
    public void Reads_An_Indent_Written_With_The_Left_Attribute()
    {
        Assert.Equal(2, IndentLevelOf("<w:ind w:left=\"720\"/>"));
    }

    [Fact(Timeout = 600000)]
    public void Reads_An_Indent_Written_With_The_Start_Attribute()
    {
        Assert.Equal(2, IndentLevelOf("<w:ind w:start=\"720\"/>"));
    }

    [Fact(Timeout = 600000)]
    public void Prefers_The_Start_Attribute_When_A_Paragraph_Carries_Both()
    {
        Assert.Equal(8, IndentLevelOf("<w:ind w:start=\"2880\" w:left=\"720\"/>"));
    }

    [Fact(Timeout = 600000)]
    public void Reads_A_Start_Attribute_Indent_From_A_Paragraph_Style()
    {
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            DocxTestPackage.StyledParagraph("Quote", "indented by its style"),
            DocxTestPackage.Style("Quote", "<w:pPr><w:ind w:start=\"1440\"/></w:pPr>"));

        Assert.Equal(4, Assert.Single(result.Document.Paragraphs).Style.IndentLevel);
    }

    /// <summary>The indent level of a lone paragraph carrying <paramref name="indXml"/>.</summary>
    private static int IndentLevelOf(string indXml)
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:pPr>" + indXml + "</w:pPr><w:r><w:t>indented</w:t></w:r></w:p>");

        return Assert.Single(result.Document.Paragraphs).Style.IndentLevel;
    }
}
