namespace Broiler.Documents.Docx.Tests;

public sealed class DocxBaselineTests
{
    [Theory]
    [InlineData(PageFieldKind.PageNumber, ListNumberFormat.LowerRoman, false)]
    [InlineData(PageFieldKind.PageNumber, ListNumberFormat.Decimal, true)]
    [InlineData(PageFieldKind.PageCount, ListNumberFormat.Decimal, false)]
    [InlineData(PageFieldKind.SectionPageCount, ListNumberFormat.Decimal, false)]
    public void Page_Fields_Roundtrip_With_Their_Cached_Text(
        PageFieldKind kind, ListNumberFormat format, bool followsPageFormat)
    {
        var field = new PageField(kind, format) { FollowsPageFormat = followsPageFormat };
        var document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("cached", new InlineStyle { PageField = field })]);
        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(document));
        var result = new DocxDocumentCodec().Read(stream);
        var paragraph = Assert.Single(result.Document.Paragraphs);
        Assert.Equal("cached", paragraph.Text);
        Assert.Equal(field, paragraph.StyleAt(0).PageField);
    }

    [Theory]
    [InlineData(BaselinePosition.AutoSuperscript, 0, 0.58)]
    [InlineData(BaselinePosition.AutoSubscript, 0, 0.58)]
    [InlineData(BaselinePosition.Offset, 0.25, 1)]
    [InlineData(BaselinePosition.Offset, -0.25, 1)]
    public void Baseline_Formatting_Roundtrips(BaselinePosition position, double offset, double scale)
    {
        var shift = new BaselineShift(position, offset, scale);
        var paragraph = RichTextParagraph.Create("Text", new InlineStyle { FontSize = 12, Shift = shift });
        var document = RichTextDocument.FromParagraphs([paragraph]);

        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(document));
        var result = new DocxDocumentCodec().Read(stream);
        var back = Assert.Single(result.Document.Paragraphs);
        Assert.Equal("Text", back.Text);
        Assert.Equal(shift, back.StyleAt(0).Shift);
    }
}
