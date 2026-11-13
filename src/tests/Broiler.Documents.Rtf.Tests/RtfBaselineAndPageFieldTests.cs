namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfBaselineAndPageFieldTests
{
    [Fact]
    public void Character_Style_References_Roundtrip_With_Their_Cached_Text()
    {
        var input = System.Text.Encoding.ASCII.GetBytes(
            @"{\rtf1{\stylesheet{\*\cs1\b Entry Word;}}" +
            @"{\header{\field{\*\fldinst STYLEREF ""Entry Word"" \\l}{\fldrslt Saved}}\par}" +
            @"{\cs1 Actual}}");
        var result = RtfReader.Read(input).Document;
        result = RtfReader.Read(RtfWriter.WriteToArray(result)).Document;
        var header = Assert.Single(result.RunningContent.Header(PageSelection.Default));

        Assert.Equal("Saved", header.Text);
        var field = Assert.IsType<StyleReferenceField>(header.StyleAt(0).StyleReference);
        Assert.Equal("Entry Word", field.StyleName);
        Assert.True(field.FromBottom);
        Assert.Equal("Actual", field.Text(result.Paragraphs, (0, 0), (0, 6)));
    }

    [Theory]
    [InlineData(BaselinePosition.AutoSuperscript, 0, 0.58)]
    [InlineData(BaselinePosition.AutoSubscript, 0, 0.58)]
    [InlineData(BaselinePosition.Offset, 0.25, 1)]
    [InlineData(BaselinePosition.Offset, -0.25, 1)]
    public void Baseline_Formatting_Roundtrips(BaselinePosition position, double offset, double scale)
    {
        var shift = new BaselineShift(position, offset, scale);
        var document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("Text", new InlineStyle { FontSize = 12, Shift = shift })]);
        var result = RtfReader.Read(RtfWriter.WriteToArray(document));
        Assert.Equal(shift, Assert.Single(result.Document.Paragraphs).StyleAt(0).Shift);
    }

    [Theory]
    [InlineData(PageFieldKind.PageNumber)]
    [InlineData(PageFieldKind.PageCount)]
    public void Page_Fields_Roundtrip_With_Cached_Text(PageFieldKind kind)
    {
        var field = new PageField(kind);
        var document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("12", new InlineStyle { PageField = field })]);
        var result = RtfReader.Read(RtfWriter.WriteToArray(document));
        var paragraph = Assert.Single(result.Document.Paragraphs);
        Assert.Equal("12", paragraph.Text);
        Assert.Equal(field, paragraph.StyleAt(0).PageField);
    }
}
