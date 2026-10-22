namespace Broiler.Documents.Odt.Tests;

public sealed class OdtParagraphPaginationTests
{
    [Theory]
    [InlineData("always", "always", 2, 3, true, true)]
    [InlineData("auto", "auto", 0, 0, false, false)]
    public void Reads_paragraph_pagination_attributes(string next, string together, int orphans, int widows,
        bool expectedNext, bool expectedTogether)
    {
        var result = OdtTestPackage.ReadBody("<text:p text:style-name=\"P\">body</text:p>",
            automaticStylesXml: "<style:style style:name=\"P\" style:family=\"paragraph\"><style:paragraph-properties " +
            "fo:keep-with-next=\"" + next + "\" fo:keep-together=\"" + together +
            "\" fo:orphans=\"" + orphans + "\" fo:widows=\"" + widows + "\"/></style:style>");
        ParagraphStyle style = result.Document.Paragraphs[0].Style;
        Assert.Equal(expectedNext, style.KeepWithNext);
        Assert.Equal(expectedTogether, style.KeepTogether);
        Assert.Equal(orphans, style.OrphanLines);
        Assert.Equal(widows, style.WidowLines);
    }

    [Fact]
    public void Direct_auto_and_zero_override_parent_pagination_rules()
    {
        var result = OdtTestPackage.ReadBody("<text:p text:style-name=\"Child\">body</text:p>", automaticStylesXml:
            "<style:style style:name=\"Parent\" style:family=\"paragraph\"><style:paragraph-properties " +
            "fo:keep-with-next=\"always\" fo:keep-together=\"always\" fo:orphans=\"2\" fo:widows=\"2\"/></style:style>" +
            "<style:style style:name=\"Child\" style:family=\"paragraph\" style:parent-style-name=\"Parent\">" +
            "<style:paragraph-properties fo:keep-with-next=\"auto\" fo:keep-together=\"auto\" " +
            "fo:orphans=\"0\" fo:widows=\"0\"/></style:style>");
        ParagraphStyle style = result.Document.Paragraphs[0].Style;
        Assert.False(style.KeepWithNext);
        Assert.False(style.KeepTogether);
        Assert.Equal(0, style.OrphanLines);
        Assert.Equal(0, style.WidowLines);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 3)]
    public void Distinct_widow_and_orphan_counts_survive_a_round_trip(int orphans, int widows)
    {
        var source = RichTextDocument.FromParagraphs([RichTextParagraph.Create("body", InlineStyle.Default,
            ParagraphStyle.Default with { KeepWithNext = true, KeepTogether = true, OrphanLines = orphans, WidowLines = widows })]);
        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(source));
        var read = new OdtDocumentCodec().Read(stream);
        Assert.Equal(source.Paragraphs[0].Style, read.Document.Paragraphs[0].Style);
    }
}
