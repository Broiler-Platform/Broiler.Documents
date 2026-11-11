using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;

namespace Broiler.Documents.Pagination.Tests;

/// <summary>Office conformance issue #238: diamond shapes and auto-width frames.</summary>
public sealed class DocumentLayoutIssue238OdtTests
{
    // A 400 x 400 pt page with 50 pt margins: a 300 pt text column.
    private static readonly DocumentPageBox Box = new(400, 400, 50, 50, 50, 50);

    private static (LayoutPage Page, IReadOnlyList<BRenderCommand> Commands) Paint(RichTextDocument document)
    {
        using var images = new ImageStore();
        var page = new DocumentLayout(new LayoutSettings(), images).Layout(document, Box).Pages[0];
        var list = new BRenderList();
        new DocumentPagePainter(new LayoutSettings(), images).Paint(page, Box, list, _ => null);
        return (page, list.Commands);
    }

    [Fact]
    public void A_Diamond_Fills_Its_Four_Edge_Midpoints_Not_Its_Box()
    {
        // layout/data/tdf137185.odt: a draw:type="diamond" custom shape was
        // painted as the rectangle around it.
        BColor blue = BColor.FromArgb(255, 0x72, 0x9f, 0xcf);
        var document = RichTextDocument.FromPlainText("anchor").WithShapes([new DocumentShape(0, 0, 0, 200, 100,
            ShapeFill.Solid(blue), blue, geometry: ShapeGeometry.Diamond)]);
        var (_, commands) = Paint(document);

        var triangles = commands.OfType<BRenderCommand.FillTriangle>().Where(command => command.Color == blue).ToList();
        Assert.Equal(2, triangles.Count);
        Assert.DoesNotContain(commands.OfType<BRenderCommand.FillRect>(), command => command.Color == blue && command.Rect.Width >= 200);
        var corners = triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }).Distinct().ToList();
        Assert.Contains(new BPoint(150, 50), corners);  // top: the middle of the box's top edge
        Assert.Contains(new BPoint(250, 100), corners); // right
        Assert.Contains(new BPoint(150, 150), corners); // bottom
        Assert.Contains(new BPoint(50, 100), corners);  // left
    }

    [Fact]
    public void A_Frame_That_Widens_To_Its_Text_Stops_At_The_Right_Of_The_Text_Column()
    {
        // layout/data/tdf135035.odt: an auto-width frame 50 pt into the column
        // with a line longer than the column wraps at the column's right edge.
        var text = RichTextParagraph.Create(new string('a', 200), InlineStyle.Default, ParagraphStyle.Default);
        var document = RichTextDocument.FromPlainText("anchor").WithShapes([new DocumentShape(0, 50, 0, 2, 20,
            paragraphs: [text], growsToText: true, growsWidthToText: true)]);
        var (page, _) = Paint(document);

        LayoutShape shape = Assert.Single(page.Shapes);
        Assert.Equal(100, shape.Bounds.Left, 3);
        Assert.Equal(350, shape.Bounds.Right, 3);
        Assert.True(shape.Lines.Count > 1);
    }

    [Fact]
    public void A_Frame_That_Widens_To_A_Short_Text_Is_As_Wide_As_The_Text()
    {
        var text = RichTextParagraph.Create("abc", InlineStyle.Default, ParagraphStyle.Default);
        var document = RichTextDocument.FromPlainText("anchor").WithShapes([new DocumentShape(0, 50, 0, 2, 20,
            paragraphs: [text], growsToText: true, growsWidthToText: true)]);
        var (page, _) = Paint(document);

        LayoutShape shape = Assert.Single(page.Shapes);
        Assert.InRange(shape.Bounds.Width, 5, 60);
        Assert.Single(shape.Lines);
    }
}
