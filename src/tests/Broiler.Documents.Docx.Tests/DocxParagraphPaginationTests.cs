namespace Broiler.Documents.Docx.Tests;

public sealed class DocxParagraphPaginationTests
{
    [Theory]
    [InlineData("", false, false, null)]
    [InlineData("<w:keepNext/><w:keepLines/><w:widowControl/>", true, true, 2)]
    [InlineData("<w:keepNext w:val=\"0\"/><w:keepLines w:val=\"false\"/><w:widowControl w:val=\"off\"/>", false, false, 0)]
    public void Reads_explicit_pagination_controls(string properties, bool next, bool together, int? lines)
    {
        var result = DocxTestPackage.ReadBody("<w:p><w:pPr>" + properties +
            "</w:pPr><w:r><w:t>body</w:t></w:r></w:p>");
        ParagraphStyle style = result.Document.Paragraphs[0].Style;
        Assert.Equal(next, style.KeepWithNext);
        Assert.Equal(together, style.KeepTogether);
        Assert.Equal(lines, style.OrphanLines);
        Assert.Equal(lines, style.WidowLines);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Pagination_controls_survive_a_round_trip(int lines)
    {
        var source = RichTextDocument.FromParagraphs([RichTextParagraph.Create("body", InlineStyle.Default,
            ParagraphStyle.Default with { KeepWithNext = true, KeepTogether = true, OrphanLines = lines, WidowLines = lines })]);
        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(source));
        var read = new DocxDocumentCodec().Read(stream);
        Assert.Equal(source.Paragraphs[0].Style, read.Document.Paragraphs[0].Style);
    }

    [Fact]
    public void Custom_line_counts_are_reported_when_reduced_to_DOCX_on_off()
    {
        var document = RichTextDocument.FromParagraphs([RichTextParagraph.Create("body", InlineStyle.Default,
            ParagraphStyle.Default with { OrphanLines = 2, WidowLines = 3 })]);
        using var stream = new MemoryStream();
        var result = new DocxDocumentCodec().Write(document, stream);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.paragraph.pagination");
    }

    [Fact]
    public void Direct_off_overrides_an_inherited_keep()
    {
        var parts = new Dictionary<string, string>
        {
            ["word/styles.xml"] = "<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                "<w:style w:type=\"paragraph\" w:styleId=\"Heading\"><w:pPr>" +
                "<w:keepNext/><w:keepLines/><w:widowControl/></w:pPr></w:style></w:styles>",
        };
        byte[] bytes = DocxTestPackage.FromBody("<w:p><w:pPr><w:pStyle w:val=\"Heading\"/>" +
            "<w:keepNext w:val=\"0\"/><w:keepLines w:val=\"0\"/><w:widowControl w:val=\"0\"/>" +
            "</w:pPr><w:r><w:t>body</w:t></w:r></w:p>", extraParts: parts);
        using var stream = new MemoryStream(bytes);
        var result = new DocxDocumentCodec().Read(stream);
        ParagraphStyle style = result.Document.Paragraphs[0].Style;
        Assert.False(style.KeepWithNext);
        Assert.False(style.KeepTogether);
        Assert.Equal(0, style.WidowLines);
    }

    [Theory]
    [InlineData("<w:docDefaults><w:rPrDefault/><w:pPrDefault/></w:docDefaults>", 2)]
    [InlineData("<w:docDefaults><w:rPrDefault/></w:docDefaults>", 0)]
    [InlineData("", 0)]
    public void Paragraph_defaults_keep_two_lines_where_a_paragraph_states_nothing(string defaults, int lines)
    {
        // LibreOffice's StyleSheetTable::applyDefaults sets two-line widow and
        // orphan control when it reads a w:pPrDefault (tdf#87533), as Word
        // applies it where w:widowControl is absent. fdo68607.docx states none,
        // and a two-line cell no longer breaks across a page.
        var result = DocxTestPackage.ReadStyled(DocxTestPackage.Paragraph("body"), defaults);
        Assert.Equal(lines, result.Document.StyleDefaults.WidowOrphanLines);
        Assert.Null(result.Document.Paragraphs[0].Style.OrphanLines);
        Assert.Equal(lines == 0 ? null : lines, result.Document.StyleDefaults.OrphanLinesOf(result.Document.Paragraphs[0].Style));
    }

    [Fact]
    public void A_paragraph_turning_widow_control_off_outranks_the_default()
    {
        var result = DocxTestPackage.ReadStyled(
            "<w:p><w:pPr><w:widowControl w:val=\"0\"/></w:pPr><w:r><w:t>body</w:t></w:r></w:p>",
            "<w:docDefaults><w:pPrDefault/></w:docDefaults>");
        ParagraphStyle style = result.Document.Paragraphs[0].Style;
        Assert.Equal(0, result.Document.StyleDefaults.OrphanLinesOf(style));
        Assert.Equal(0, result.Document.StyleDefaults.WidowLinesOf(style));
    }
}
