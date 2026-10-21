using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;

namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// A document's page background is on every page it lays out, and painted
/// under everything else on it.
/// </summary>
public sealed class DocumentPageBackgroundTests
{
    /// <summary>US Letter with one-inch margins.</summary>
    private static readonly DocumentPageBox Letter = new(612, 792, 72, 72, 72, 72);

    private static readonly BColor Red = BColor.FromArgb(255, 0, 0);
    private static readonly BColor Blue = BColor.FromArgb(0, 0, 255);

    private static LayoutResult Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, Letter);
    }

    private static IReadOnlyList<BRenderCommand> Paint(LayoutPage page)
    {
        using var images = new ImageStore();
        var list = new BRenderList();
        new DocumentPagePainter(new LayoutSettings(), images).Paint(page, Letter, list, _ => null);
        return list.Commands;
    }

    private static RichTextDocument Lines(int count) =>
        RichTextDocument.FromParagraphs(Enumerable.Range(0, count).Select(i => RichTextParagraph.Plain("Line " + i)));

    [Fact]
    public void Every_Page_Carries_The_Background()
    {
        ShapeFill background = ShapeFill.Solid(Red);
        LayoutResult result = Layout(Lines(200).WithPageBackground(background));

        Assert.True(result.Pages.Count > 1);
        Assert.All(result.Pages, page => Assert.Equal(background, page.Background));
    }

    [Fact]
    public void A_Document_Without_One_Leaves_The_Page_Alone()
    {
        Assert.All(Layout(Lines(3)).Pages, page => Assert.Null(page.Background));
    }

    [Fact]
    public void A_Colour_Is_Painted_First_Over_The_Whole_Page()
    {
        LayoutPage page = Layout(Lines(3).WithPageBackground(ShapeFill.Solid(Red))).Pages[0];

        BRenderCommand.FillRect first = Assert.IsType<BRenderCommand.FillRect>(Paint(page)[0]);
        Assert.Equal(new BRect(0, 0, 612, 792), first.Rect);
        Assert.Equal(Red, first.Color);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(180, false)]
    public void A_Gradient_Along_An_Axis_Starts_Where_Its_Angle_Points_From(double angle, bool redAtLeft)
    {
        LayoutPage page = Layout(Lines(1).WithPageBackground(new ShapeFill(Red, Blue, angle))).Pages[0];

        List<BRenderCommand.FillRect> bands = [.. Paint(page).OfType<BRenderCommand.FillRect>()];
        BRenderCommand.FillRect leftmost = bands.MinBy(band => band.Rect.Left)!;

        Assert.Equal(redAtLeft ? Red : Blue, leftmost.Color);
        Assert.Equal(792, leftmost.Rect.Height, 3);
    }

    [Fact]
    public void A_Translucent_Gradient_Lays_Its_Bands_Edge_To_Edge()
    {
        // Overlapping bands lay a translucent colour down twice where they meet,
        // and a fade from transparent draws as stripes.
        var fill = new ShapeFill(new BColor(255, 0, 0, 0), Red, 90);
        LayoutPage page = Layout(Lines(1).WithPageBackground(fill)).Pages[0];

        List<BRenderCommand.FillRect> bands = [.. Paint(page).OfType<BRenderCommand.FillRect>().OrderBy(band => band.Rect.Top)];
        Assert.True(bands.Count > 100);
        for (int i = 1; i < bands.Count; i++)
            Assert.True(bands[i].Rect.Top >= bands[i - 1].Rect.Bottom - 1e-6, $"band {i} overlaps the one above");
    }

    [Fact]
    public void A_Diagonal_Gradient_Is_Clipped_To_The_Page_Rather_Than_Snapped_To_An_Axis()
    {
        var fill = new ShapeFill(Red, Blue, 45);
        LayoutPage page = Layout(Lines(1).WithPageBackground(fill)).Pages[0];

        IReadOnlyList<BRenderCommand> commands = Paint(page);

        Assert.Equal(new BRect(0, 0, 612, 792), Assert.IsType<BRenderCommand.PushClip>(commands[0]).Rect);
        Assert.Contains(commands, command => command is BRenderCommand.FillTriangle);
        Assert.Contains(commands, command => command is BRenderCommand.PopClip);
    }

    [Fact]
    public void An_Axial_Fill_Is_Its_Start_At_Both_Edges_And_Its_End_In_The_Middle()
    {
        var fill = new ShapeFill(Red, Blue, 0, Axial: true);

        Assert.Equal(Red, fill.ColorAt(0));
        Assert.Equal(Blue, fill.ColorAt(0.5));
        Assert.Equal(Red, fill.ColorAt(1));
    }
}
