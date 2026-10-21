using Broiler.Graphics.Color;

namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Covers a paragraph's <c>fo:background-color</c> (ODF 1.3 §20.183), the
/// sibling of DOCX's paragraph shading.
/// </summary>
public sealed class OdtParagraphShadingTests
{
    private static readonly BColor Green = BColor.FromArgb(0x00, 0x79, 0x29);

    [Fact]
    public void Reads_A_Paragraphs_Background_Colour()
    {
        RichTextDocument document = OdtTestPackage.ReadBody(
            OdtTestPackage.StyledParagraph("P1", "title"),
            OdtTestPackage.Style("P1", "<style:paragraph-properties fo:background-color=\"#007929\"/>")).Document;

        Assert.Equal(Green, document.Paragraphs[0].Style.Background);
    }

    [Fact]
    public void Transparent_Takes_Away_What_The_Parent_Gave()
    {
        RichTextDocument document = OdtTestPackage.ReadStyled(
            OdtTestPackage.StyledParagraph("P1", "title"),
            OdtTestPackage.Style("Bar", "<style:paragraph-properties fo:background-color=\"#007929\"/>"),
            OdtTestPackage.Style("P1", "<style:paragraph-properties fo:background-color=\"transparent\"/>", parent: "Bar")).Document;

        Assert.True(document.Paragraphs[0].Style.Background.IsEmpty);
    }

    [Fact]
    public void Survives_A_Round_Trip()
    {
        RichTextDocument source = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Plain("title").WithParagraphStyle(ParagraphStyle.Default with { Background = Green }),
            RichTextParagraph.Plain("plain"),
        ]);

        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(source), writable: false);
        RichTextDocument actual = new OdtDocumentCodec().Read(stream).Document;

        Assert.Equal(Green, actual.Paragraphs[0].Style.Background);
        Assert.True(actual.Paragraphs[1].Style.Background.IsEmpty);
    }
}
