using Broiler.Graphics.Color;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Covers paragraph shading, <c>w:pPr/w:shd</c> (ECMA-376 §17.3.1.31): the
/// band behind a title or a boxed note. It was read for runs and cells but not
/// for paragraphs, so tdf167527's dark-green title bar came back white.
/// </summary>
public sealed class DocxParagraphShadingTests
{
    private static readonly BColor Green = BColor.FromArgb(0x00, 0x79, 0x29);

    private static RichTextParagraph Read(string pPr) =>
        DocxTestPackage.ReadBody("<w:p><w:pPr>" + pPr + "</w:pPr><w:r><w:t>title</w:t></w:r></w:p>").Document.Paragraphs[0];

    [Fact]
    public void Reads_A_Clear_Shading_As_Its_Fill()
    {
        Assert.Equal(Green, Read("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"007929\"/>").Style.Background);
    }

    [Fact]
    public void A_Solid_Shading_Shows_Its_Pattern_Colour()
    {
        Assert.Equal(Green, Read("<w:shd w:val=\"solid\" w:color=\"007929\" w:fill=\"FFFFFF\"/>").Style.Background);
    }

    [Fact]
    public void Shading_Leaves_The_Runs_Alone()
    {
        RichTextParagraph paragraph = Read("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"007929\"/>");

        Assert.All(paragraph.Runs, run => Assert.True(run.Style.Background.IsEmpty));
    }

    [Fact]
    public void Reads_Shading_From_The_Paragraph_Style()
    {
        RichTextDocument document = DocxTestPackage.ReadStyled(
            DocxTestPackage.StyledParagraph("Bar", "title"),
            DocxTestPackage.Style("Bar", "<w:pPr><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"007929\"/></w:pPr>")).Document;

        Assert.Equal(Green, document.Paragraphs[0].Style.Background);
    }

    [Theory]
    [InlineData("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"auto\"/>")]
    [InlineData("<w:shd w:val=\"nil\"/>")]
    public void A_Paragraph_Can_Take_Away_Its_Styles_Shading(string shading)
    {
        RichTextDocument document = DocxTestPackage.ReadStyled(
            "<w:p><w:pPr><w:pStyle w:val=\"Bar\"/>" + shading + "</w:pPr><w:r><w:t>title</w:t></w:r></w:p>",
            DocxTestPackage.Style("Bar", "<w:pPr><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"007929\"/></w:pPr>")).Document;

        Assert.True(document.Paragraphs[0].Style.Background.IsEmpty);
    }

    [Fact]
    public void Survives_A_Round_Trip()
    {
        RichTextDocument source = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Plain("title").WithParagraphStyle(ParagraphStyle.Default with { Background = Green, SpacingAfter = 6 }),
        ]);

        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(source), writable: false);
        RichTextDocument actual = new DocxDocumentCodec().Read(stream).Document;

        Assert.Equal(Green, actual.Paragraphs[0].Style.Background);
    }
}
