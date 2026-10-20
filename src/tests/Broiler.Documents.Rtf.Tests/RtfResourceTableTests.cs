using Broiler.Graphics.Color;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfResourceTableTests
{
    private static readonly InlineStyle Decoration = InlineStyle.Default with
    {
        FontFamily = "Header Face",
        Foreground = new BColor(17, 34, 51),
        Background = new BColor(210, 220, 230),
    };

    [Theory]
    [InlineData(true, PageSelection.Default)]
    [InlineData(true, PageSelection.First)]
    [InlineData(true, PageSelection.Even)]
    [InlineData(false, PageSelection.Default)]
    [InlineData(false, PageSelection.First)]
    [InlineData(false, PageSelection.Even)]
    public void Resources_Used_Only_In_Running_Content_Survive_Export(bool header, PageSelection selection)
    {
        RichTextParagraph[] paragraphs = [RichTextParagraph.Create("decoration", Decoration)];
        RunningContent running = header
            ? RunningContent.Empty.WithHeader(selection, paragraphs)
            : RunningContent.Empty.WithFooter(selection, paragraphs);
        RichTextDocument source = RichTextDocument.FromPlainText("body").WithRunningContent(running);
        RichTextDocument restored = RtfReader.Read(RtfWriter.WriteToArray(source)).Document;
        RichTextParagraph paragraph = Assert.Single(header
            ? restored.RunningContent.Header(selection)
            : restored.RunningContent.Footer(selection));

        Assert.Equal(Decoration, Assert.Single(paragraph.Runs).Style);
        Assert.Equal("body", restored.PlainText);
        Assert.Equal(InlineStyle.Default, Assert.Single(restored.Paragraphs[0].Runs).Style);
    }

    [Fact]
    public void Resources_Used_Only_In_Shape_Text_Survive_Export()
    {
        RichTextDocument source = RichTextDocument.FromPlainText("body").WithShapes(
            [new DocumentShape(0, 0, 0, 100, 50,
                paragraphs: [RichTextParagraph.Create("badge", Decoration)])]);
        RichTextDocument restored = RtfReader.Read(RtfWriter.WriteToArray(source)).Document;
        DocumentShape shape = Assert.Single(restored.Shapes);

        Assert.Equal(Decoration, Assert.Single(Assert.Single(shape.Paragraphs).Runs).Style);
        Assert.Equal("body", restored.PlainText);
    }
}
