namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// <c>w:spacing w:line</c> under <c>w:lineRule="exact"</c> and <c>"atLeast"</c>
/// is a height in twips (ECMA-376 §17.3.1.33). Only the auto rule was read, so
/// a paragraph set at exactly ten points drew at its font's own height.
/// </summary>
public sealed class DocxLineHeightTests
{
    private static ParagraphStyle ReadSpacing(string spacing) =>
        Assert.Single(DocxTestPackage.ReadBody(
            "<w:p><w:pPr>" + spacing + "</w:pPr><w:r><w:t>x</w:t></w:r></w:p>").Document.Paragraphs).Style;

    [Fact]
    public void An_Exact_Rule_Is_A_Height_In_Twips()
    {
        ParagraphStyle style = ReadSpacing("<w:spacing w:line=\"200\" w:lineRule=\"exact\"/>");

        Assert.Equal(LineHeightRule.Exact, style.LineHeightRule);
        Assert.Equal(10f, style.LineHeight);
        Assert.Equal(1f, style.LineSpacing);
    }

    [Fact]
    public void An_At_Least_Rule_Is_The_Least_A_Line_Is()
    {
        ParagraphStyle style = ReadSpacing("<w:spacing w:line=\"360\" w:lineRule=\"atLeast\"/>");

        Assert.Equal(LineHeightRule.AtLeast, style.LineHeightRule);
        Assert.Equal(18f, style.LineHeight);
    }

    [Theory]
    [InlineData("<w:spacing w:line=\"360\" w:lineRule=\"auto\"/>")]
    [InlineData("<w:spacing w:line=\"360\"/>")]
    public void The_Auto_Rule_And_No_Rule_Are_A_Multiple(string spacing)
    {
        ParagraphStyle style = ReadSpacing(spacing);

        Assert.Equal(LineHeightRule.Proportional, style.LineHeightRule);
        Assert.Equal(1.5f, style.LineSpacing);
    }

    [Fact]
    public void A_Height_Beyond_Words_Limit_Is_Clamped()
    {
        ParagraphStyle style = ReadSpacing("<w:spacing w:line=\"2147483647\" w:lineRule=\"exact\"/>");

        Assert.Equal(ParagraphStyle.MaxLineHeight, style.LineHeight);
    }

    [Fact]
    public void A_Paragraph_Inherits_Its_Styles_Height_And_Can_Replace_It()
    {
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            DocxTestPackage.StyledParagraph("Form", "inherits") +
            "<w:p><w:pPr><w:pStyle w:val=\"Form\"/><w:spacing w:line=\"480\" w:lineRule=\"auto\"/></w:pPr>" +
            "<w:r><w:t>replaces</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:pStyle w:val=\"Form\"/><w:spacing w:after=\"120\"/></w:pPr>" +
            "<w:r><w:t>keeps</w:t></w:r></w:p>",
            DocxTestPackage.Style("Form", "<w:pPr><w:spacing w:line=\"200\" w:lineRule=\"exact\"/></w:pPr>"));

        ParagraphStyle inherits = result.Document.Paragraphs[0].Style;
        ParagraphStyle replaces = result.Document.Paragraphs[1].Style;
        ParagraphStyle keeps = result.Document.Paragraphs[2].Style;

        Assert.Equal(LineHeightRule.Exact, inherits.LineHeightRule);
        Assert.Equal(10f, inherits.LineHeight);
        Assert.Equal(LineHeightRule.Proportional, replaces.LineHeightRule);
        Assert.Equal(2f, replaces.LineSpacing);
        Assert.Equal(LineHeightRule.Exact, keeps.LineHeightRule);
    }

    [Theory]
    [InlineData(LineHeightRule.Exact, "exact")]
    [InlineData(LineHeightRule.AtLeast, "atLeast")]
    public void A_Height_Round_Trips_With_Its_Rule(LineHeightRule rule, string written)
    {
        RichTextDocument source = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Plain("x").WithParagraphStyle(
                ParagraphStyle.Default with { LineHeightRule = rule, LineHeight = 10f }),
        ]);

        byte[] docx = DocxWriter.WriteToArray(source);
        using var stream = new MemoryStream(docx, writable: false);
        ParagraphStyle back = Assert.Single(new DocxDocumentCodec().Read(stream).Document.Paragraphs).Style;

        Assert.Equal(rule, back.LineHeightRule);
        Assert.Equal(10f, back.LineHeight);
        Assert.Contains("w:lineRule=\"" + written + "\"", DocumentXml(docx));
    }

    private static string DocumentXml(byte[] docx)
    {
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(docx), System.IO.Compression.ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
        return reader.ReadToEnd();
    }
}
