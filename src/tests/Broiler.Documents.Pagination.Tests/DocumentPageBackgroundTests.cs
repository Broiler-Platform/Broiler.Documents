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
    public void A_Content_Only_Background_Leaves_The_Margins_Unpainted()
    {
        LayoutPage page = Layout(Lines(1).WithPageBackground(ShapeFill.Solid(Red) with { ContentOnly = true })).Pages[0];
        var first = Assert.IsType<BRenderCommand.FillRect>(Paint(page)[0]);
        Assert.Equal(new BRect(72, 72, 468, 648), first.Rect);
    }

    [Fact]
    public void Changing_Page_Styles_Use_Their_Own_Background_Bounds()
    {
        var a = new PageGeometry(200, 100, 10, 10, 10, 10);
        var b = new PageGeometry(300, 120, 20, 20, 20, 20);
        var fill = ShapeFill.Solid(Red) with { ContentOnly = true };
        var sequence = new PageStyleSequence([new PageStyle(a, RunningContent.Empty, fill), new PageStyle(b, RunningContent.Empty, fill)], 0);
        var document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("one"), RichTextParagraph.Plain("two")
            .WithParagraphStyle(ParagraphStyle.Default with { PageBreakBefore = true })]).WithPageGeometry(a)
            .WithSections([new DocumentSection(0, 2, 1, 0, a, pageStyles: sequence)]);
        using var images = new ImageStore();
        var pages = new DocumentLayout(new LayoutSettings(), images).Layout(document, new DocumentPageBox(200, 100, 10, 10, 10, 10)).Pages;
        Assert.Equal(new BRect(10, 10, 180, 80), pages[0].BackgroundBounds);
        Assert.Equal(new BRect(20, 20, 260, 80), pages[1].BackgroundBounds);
    }

    [Fact]
    public void Hatch_Paint_Is_Clipped_And_Contains_Strokes()
    {
        var fill = ShapeFill.Solid(BColor.Empty) with { Hatch = new HatchFill(Blue, 12, -45, 2), ContentOnly = true };
        var commands = Paint(Layout(Lines(1).WithPageBackground(fill)).Pages[0]);
        Assert.Equal(new BRect(72, 72, 468, 648), Assert.IsType<BRenderCommand.PushClip>(commands[0]).Rect);
        Assert.Contains(commands, command => command is BRenderCommand.FillTriangle);
        Assert.Contains(commands, command => command is BRenderCommand.PopClip);
    }

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
