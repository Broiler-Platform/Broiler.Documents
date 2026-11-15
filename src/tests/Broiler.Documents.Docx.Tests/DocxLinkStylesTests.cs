namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// <c>w:linkStyles</c> with no template to update the styles from: LibreOffice
/// takes Word's template defaults, 10 points below every paragraph and lines
/// at 115 %, under what the document states (tdf#166141, office issue 249).
/// </summary>
public sealed class DocxLinkStylesTests
{
    private static RichTextParagraph Read(string settingsInnerXml, string normalPropertiesXml)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/settings.xml"] = "<w:settings " + DocxTestPackage.BodyNamespaceDeclarations + ">" + settingsInnerXml + "</w:settings>",
            ["word/styles.xml"] = "<w:styles " + DocxTestPackage.BodyNamespaceDeclarations + ">" +
                DocxTestPackage.Style("Normal", normalPropertiesXml, isDefault: true) + "</w:styles>",
        };
        using var stream = new MemoryStream(DocxTestPackage.FromBody(DocxTestPackage.Paragraph("text"), parts), writable: false);
        return new DocxDocumentCodec().Read(stream).Document.Paragraphs[0];
    }

    [Fact]
    public void Link_Styles_Without_A_Template_Takes_Words_Paragraph_Defaults()
    {
        RichTextParagraph paragraph = Read("<w:linkStyles/>", "");

        Assert.Equal(10, paragraph.Style.SpacingAfter, 3);
        Assert.Equal(1.15f, paragraph.Style.LineSpacing, 3);
    }

    [Fact]
    public void What_The_Default_Style_States_Wins_Over_The_Template_Defaults()
    {
        // tdf166141_linkedStyles2.docx: Normal states w:line="264" and no space
        // after, and LibreOffice keeps the 110 % lines and adds the 10 points.
        RichTextParagraph paragraph = Read("<w:linkStyles/>", "<w:pPr><w:spacing w:line=\"264\" w:lineRule=\"auto\"/></w:pPr>");

        Assert.Equal(10, paragraph.Style.SpacingAfter, 3);
        Assert.Equal(1.1f, paragraph.Style.LineSpacing, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<w:attachedTemplate r:id=\"rId1\"/><w:linkStyles/>")]
    [InlineData("<w:linkStyles w:val=\"0\"/>")]
    public void Without_Link_Styles_Or_With_A_Template_The_Styles_Are_Left_Alone(string settings)
    {
        RichTextParagraph paragraph = Read(settings, "");

        Assert.Equal(0, paragraph.Style.SpacingAfter, 3);
        Assert.Equal(1f, paragraph.Style.LineSpacing, 3);
    }
}
