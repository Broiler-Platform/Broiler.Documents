namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfDefaultSizeTests
{
    [Theory]
    [InlineData("body")]
    [InlineData("header")]
    [InlineData("shape")]
    public void Export_Preserves_Inherited_Size_And_Explicit_Overrides(string location)
    {
        RichTextParagraph paragraph = RichTextParagraph.Create("inherited ", InlineStyle.Default)
            .InsertText(10, "explicit", InlineStyle.Default with { FontSize = 9 });
        RichTextDocument source = RichTextDocument.FromPlainText("body")
            .WithStyleDefaults(DocumentStyleDefaults.Default with { FontSizePoints = 18 });
        source = location switch
        {
            "body" => RichTextDocument.FromParagraphs([paragraph]).WithStyleDefaults(source.StyleDefaults),
            "header" => source.WithRunningContent(RunningContent.Empty.WithHeader(PageSelection.Default, [paragraph])),
            _ => source.WithShapes([new DocumentShape(0, 0, 0, 200, 50, paragraphs: [paragraph])]),
        };

        RichTextDocument restored = RtfReader.Read(RtfWriter.WriteToArray(source)).Document;
        RichTextParagraph actual = location switch
        {
            "body" => restored.Paragraphs[0],
            "header" => Assert.Single(restored.RunningContent.Header(PageSelection.Default)),
            _ => Assert.Single(Assert.Single(restored.Shapes).Paragraphs),
        };

        Assert.Equal(paragraph.Text, actual.Text);
        Assert.Equal(18, restored.StyleDefaults.FontSizeOf(actual.Runs[0].Style));
        Assert.Equal(9, restored.StyleDefaults.FontSizeOf(actual.Runs[1].Style));
    }
}
