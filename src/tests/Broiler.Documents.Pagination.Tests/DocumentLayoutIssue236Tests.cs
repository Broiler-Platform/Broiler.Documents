using Broiler.Graphics.Color;
using Broiler.Graphics.RenderList;

namespace Broiler.Documents.Pagination.Tests;

/// <summary>Office conformance issue #236: trailing tab leaders and hairline rules.</summary>
public sealed class DocumentLayoutIssue236Tests
{
    private static readonly DocumentPageBox Box = new(400, 200, 10, 10, 10, 10);

    private static (LayoutPage Page, IReadOnlyList<BRenderCommand> Commands) Paint(RichTextDocument document)
    {
        using var images = new ImageStore();
        var page = Assert.Single(new DocumentLayout(new LayoutSettings(), images).Layout(document, Box).Pages);
        var list = new BRenderList();
        new DocumentPagePainter(new LayoutSettings(), images).Paint(page, Box, list, _ => null);
        return (page, list.Commands);
    }

    [Fact]
    public void A_Tab_With_A_Leader_That_Ends_The_Paragraph_Draws_Its_Leader()
    {
        // rtfimport/data/fdo81033.rtf: a form's blank, two tabs and no text.
        var paragraph = RichTextParagraph.Create("\t\t", InlineStyle.Default,
            ParagraphStyle.Default with { TabStops = new ParagraphTabStops([new(100), new(300, TabAlignment.Left, "_")]) });
        var (page, commands) = Paint(RichTextDocument.FromParagraphs([paragraph]));

        LayoutLine line = Assert.Single(page.Lines);
        LayoutPiece leaderTab = Assert.Single(line.Pieces, piece => piece.IsTab && piece.TabLeader == "_");
        Assert.Equal(310, leaderTab.X + leaderTab.Width, 3); // the stop, past the 10 pt margin
        Assert.NotEmpty(commands.OfType<BRenderCommand.DrawText>().Where(command => command.Text.Text == "_"));
    }

    [Fact]
    public void A_Trailing_Tab_With_No_Leader_Is_Still_Dropped()
    {
        var paragraph = RichTextParagraph.Create("a\t", InlineStyle.Default,
            ParagraphStyle.Default with { TabStops = new ParagraphTabStops([new(300)]) });
        var (page, _) = Paint(RichTextDocument.FromParagraphs([paragraph]));

        Assert.DoesNotContain(Assert.Single(page.Lines).Pieces, piece => piece.IsTab);
    }

    [Fact]
    public void A_Leader_Tab_Before_A_Forced_Break_Draws_Its_Leader()
    {
        var paragraph = RichTextParagraph.Create("name\t\u2028next", InlineStyle.Default,
            ParagraphStyle.Default with { TabStops = new ParagraphTabStops([new(200, TabAlignment.Left, ".")]) });
        var (page, _) = Paint(RichTextDocument.FromParagraphs([paragraph]));

        Assert.Contains(page.Lines[0].Pieces, piece => piece.IsTab && piece.TabLeader == ".");
    }

    [Fact]
    public void A_Hairline_Rule_Covers_A_Whole_Pixel_At_96_Dpi()
    {
        // rtfimport/data/tdf115153.rtf: \dplinew0, a vertical line LibreOffice
        // draws one pixel wide. A quarter point fell between pixel centres.
        var document = RichTextDocument.FromPlainText("anchor").WithShapes([new DocumentShape(0, 71.8, 20, 0, 36.4,
            null, BColor.Black, geometry: ShapeGeometry.Line, outlineWidth: 0)]);
        var (_, commands) = Paint(document);

        var rule = Assert.Single(commands.OfType<BRenderCommand.FillRect>(), command => command.Color == BColor.Black);
        Assert.Equal(0.75, rule.Rect.Height, 3);
    }
}
