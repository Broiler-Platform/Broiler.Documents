using Broiler.Graphics.RenderList;

namespace Broiler.Documents.Pagination.Tests;

/// <summary>Office conformance issue #238: text boxes whose text runs down or up their side.</summary>
public sealed class DocumentLayoutIssue238Tests
{
    private static readonly DocumentPageBox Box = new(400, 600, 10, 10, 10, 10);

    private static (LayoutShape Shape, IReadOnlyList<BRenderCommand> Commands) Paint(DocumentShape shape)
    {
        var document = RichTextDocument.FromPlainText("anchor").WithShapes([shape]);
        using var images = new ImageStore();
        var page = Assert.Single(new DocumentLayout(new LayoutSettings(), images).Layout(document, Box).Pages);
        var list = new BRenderList();
        new DocumentPagePainter(new LayoutSettings(), images).Paint(page, Box, list, _ => null);
        return (Assert.Single(page.Shapes), list.Commands);
    }

    /// <summary>A box 40 points wide and 300 tall, as narrow as a spine label.</summary>
    private static DocumentShape SpineLabel(ShapeTextFlow flow, bool growsToText = false, double width = 40) =>
        new(0, 0, 0, width, 300, null, default, [RichTextParagraph.Plain("Vertical Text Label")],
            growsToText: growsToText, textPadding: new TableCellPadding(2, 4, 2, 4))
        {
            TextFlow = flow,
        };

    [Theory]
    [InlineData(ShapeTextFlow.TopToBottom)]
    [InlineData(ShapeTextFlow.BottomToTop)]
    public void Turned_Text_Runs_Along_The_Box_And_Is_Turned_Onto_It(ShapeTextFlow flow)
    {
        // btlr-frame-vml.docx, tbrl-frame-vml.docx, fdo69636.docx: the label
        // broke after "Vertica" in a box 57 points wide.
        var (shape, commands) = Paint(SpineLabel(flow));

        Assert.Equal(flow, shape.TextFlow);
        Assert.Equal(40, shape.Bounds.Width, 3);
        Assert.Equal(300, shape.Bounds.Height, 3);
        LayoutLine line = Assert.Single(shape.Lines);

        // Laid out in the box turned about its centre: 300 wide, 40 tall,
        // inside the padding.
        var frame = LayoutShape.TextFrame(shape.Bounds, flow);
        Assert.Equal(300, frame.Width, 3);
        Assert.Equal(40, frame.Height, 3);
        Assert.Equal(shape.Bounds.Left + shape.Bounds.Width / 2, frame.Left + frame.Width / 2, 3);
        Assert.Equal(shape.Bounds.Top + shape.Bounds.Height / 2, frame.Top + frame.Height / 2, 3);
        Assert.True(line.Top >= frame.Top + 4 - 0.01, "the line starts above the turned box's padding");

        // The text is drawn inside a quarter turn.
        int push = commands.ToList().FindIndex(command => command is BRenderCommand.PushTransform);
        int text = commands.ToList().FindIndex(command => command is BRenderCommand.DrawText);
        int pop = commands.ToList().FindLastIndex(command => command is BRenderCommand.PopTransform);
        Assert.True(push >= 0 && push < text && text < pop, "the text is not drawn turned");
    }

    [Fact]
    public void Horizontal_Text_Is_Not_Turned()
    {
        var (shape, commands) = Paint(SpineLabel(ShapeTextFlow.Horizontal));

        Assert.True(shape.Lines.Count > 1, "a 36-point line holds the whole label");
        Assert.DoesNotContain(commands, command => command is BRenderCommand.PushTransform);
    }

    [Fact]
    public void A_Box_Fitted_To_Turned_Text_Takes_The_Width_Its_Lines_Need()
    {
        // tdf127825.docx: a downward text box with spAutoFit, stated 186
        // points wide, is as wide as its lines in LibreOffice.
        var (shape, _) = Paint(SpineLabel(ShapeTextFlow.TopToBottom, growsToText: true, width: 186));

        LayoutLine line = Assert.Single(shape.Lines);
        Assert.Equal(300, shape.Bounds.Height, 3);
        Assert.Equal(line.Height + 8, shape.Bounds.Width, 1);
    }
}
