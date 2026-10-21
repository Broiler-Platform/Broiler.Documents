using Broiler.Graphics.Color;

namespace Broiler.Documents.Model.Tests;

/// <summary>
/// What editing does to a document's page background: nothing. Typing on a
/// coloured page is not a reason to lose the colour.
/// </summary>
public sealed class PageBackgroundTests
{
    private static readonly ShapeFill Background =
        new(BColor.FromArgb(255, 0, 255), BColor.FromArgb(0, 255, 0), 225, Axial: true);

    private static RichTextDocument Coloured(string text) =>
        RichTextDocument.FromPlainText(text).WithPageBackground(Background);

    [Fact]
    public void A_Document_Has_No_Background_Unless_Given_One()
    {
        Assert.Null(RichTextDocument.Empty.PageBackground);
        Assert.Null(RichTextDocument.FromPlainText("text").PageBackground);
    }

    [Fact]
    public void Every_Edit_Keeps_The_Background()
    {
        RichTextDocument document = Coloured("one\ntwo");
        var all = new RichTextRange(RichTextDocument.Start, document.End);

        Assert.Equal(Background, document.InsertText(document.End, "!", InlineStyle.Default).Document.PageBackground);
        Assert.Equal(Background, document.SplitParagraph(new RichTextPosition(0, 1)).Document.PageBackground);
        Assert.Equal(Background, document.DeleteRange(all).Document.PageBackground);
        Assert.Equal(Background, document.ApplyInlineStyle(all, InlineStyleDelta.ToggleBold(true)).PageBackground);
        Assert.Equal(
            Background,
            document.ApplyParagraphStyle(all, new ParagraphStyleDelta { Alignment = TextAlignment.Center }).PageBackground);
    }

    [Fact]
    public void Every_Other_With_Keeps_The_Background()
    {
        RichTextDocument document = Coloured("text");

        Assert.Equal(Background, document.WithRunningContent(RunningContent.Empty).PageBackground);
        Assert.Equal(Background, document.WithShapes([]).PageBackground);
        Assert.Equal(Background, document.WithPageGeometry(PageGeometry.A4).PageBackground);
        Assert.Equal(Background, document.WithTables([]).PageBackground);
        Assert.Equal(Background, document.WithSections([]).PageBackground);
        Assert.Equal(Background, document.WithStyleDefaults(DocumentStyleDefaults.Default).PageBackground);
    }

    [Fact]
    public void A_Gradient_Is_Interpolated_Between_Its_Two_Colours()
    {
        var fill = new ShapeFill(BColor.FromArgb(0, 0, 0), BColor.FromArgb(200, 100, 50), 0);

        Assert.Equal(BColor.FromArgb(0, 0, 0), fill.ColorAt(0));
        Assert.Equal(BColor.FromArgb(100, 50, 25), fill.ColorAt(0.5));
        Assert.Equal(BColor.FromArgb(200, 100, 50), fill.ColorAt(1));
        Assert.Equal(BColor.FromArgb(200, 100, 50), fill.ColorAt(7));
    }
}
