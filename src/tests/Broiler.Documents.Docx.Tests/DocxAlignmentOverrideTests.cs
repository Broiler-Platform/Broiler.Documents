namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Covers a paragraph overriding the alignment its style set. The reader mapped
/// the values it knew and let everything else fall through to the inherited
/// alignment — which quietly included <c>left</c>, the one value whose whole
/// purpose is to say "not what the style said".
/// </summary>
public sealed class DocxAlignmentOverrideTests
{
    private static ParagraphStyle Read(string styleJc, string paragraphJc)
    {
        string paragraph =
            "<w:p><w:pPr><w:pStyle w:val=\"Aligned\"/>" + paragraphJc + "</w:pPr>" +
            "<w:r><w:t>text</w:t></w:r></w:p>";

        DocumentReadResult result = DocxTestPackage.ReadStyled(
            paragraph,
            DocxTestPackage.Style("Aligned", "<w:pPr>" + styleJc + "</w:pPr>"));

        return Assert.Single(result.Document.Paragraphs).Style;
    }

    [Theory(Timeout = 600000)]
    [InlineData("<w:jc w:val=\"end\"/>")]
    [InlineData("<w:jc w:val=\"right\"/>")]
    [InlineData("<w:jc w:val=\"center\"/>")]
    [InlineData("<w:jc w:val=\"both\"/>")]
    public void A_Paragraph_Can_Say_Left_Against_Any_Aligned_Style(string styleJc)
    {
        Assert.Equal(TextAlignment.Left, Read(styleJc, "<w:jc w:val=\"left\"/>").Alignment);
    }

    [Theory(Timeout = 600000)]
    [InlineData("<w:jc w:val=\"end\"/>")]
    [InlineData("<w:jc w:val=\"center\"/>")]
    public void Start_Resets_An_Inherited_Alignment_The_Way_Left_Does(string styleJc)
    {
        // LibreOffice writes start where Word writes left, so a reader that knows
        // only one of the two spellings still loses half the overrides.
        Assert.Equal(TextAlignment.Left, Read(styleJc, "<w:jc w:val=\"start\"/>").Alignment);
    }

    [Fact(Timeout = 600000)]
    public void A_Paragraph_With_No_Jc_Keeps_What_Its_Style_Set()
    {
        Assert.Equal(TextAlignment.Right, Read("<w:jc w:val=\"end\"/>", string.Empty).Alignment);
    }

    [Fact(Timeout = 600000)]
    public void An_Unknown_Jc_Value_Leaves_The_Inherited_Alignment_Alone()
    {
        // Not a guess at what the value meant: the style chain already decided,
        // and an alignment nobody implements is no reason to overrule it.
        Assert.Equal(
            TextAlignment.Right,
            Read("<w:jc w:val=\"end\"/>", "<w:jc w:val=\"lowKashida\"/>").Alignment);
    }

    [Fact(Timeout = 600000)]
    public void A_Paragraph_Can_Still_Override_Left_With_Another_Alignment()
    {
        Assert.Equal(
            TextAlignment.Justify,
            Read("<w:jc w:val=\"left\"/>", "<w:jc w:val=\"both\"/>").Alignment);
    }
}
