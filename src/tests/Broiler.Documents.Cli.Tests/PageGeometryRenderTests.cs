using Broiler.Documents.Cli.Rendering;
using Broiler.Graphics;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// A document that states its own page gets it. Before this the renderer laid
/// every document out on whatever page the caller named, so a letter whose left
/// margin exists to hold a letterhead stripe was rendered with the stripe
/// hanging off the edge.
/// </summary>
public sealed class PageGeometryRenderTests
{
    private static readonly PageGeometry A4Letterhead =
        new(595.276, 841.89, 127.55, 56.7, 56.7, 56.7, 36.15, 56.7);

    private static LayoutResult Layout(RichTextDocument document, PageSetup setup)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, setup);
    }

    [Fact]
    public void A_Setup_Takes_The_Page_A_Document_States()
    {
        PageSetup setup = PageSetup.Default.WithGeometry(A4Letterhead);

        Assert.Equal(595.276, setup.WidthPoints, 3);
        Assert.Equal(841.89, setup.HeightPoints, 3);
        Assert.Equal(127.55, setup.MarginLeftPoints, 3);
    }

    [Fact]
    public void Taking_A_Page_Keeps_What_Belongs_To_The_Caller()
    {
        PageSetup setup = PageSetup.Default.WithGeometry(A4Letterhead);

        // The resolution and the background are the caller's business, not the
        // document's, so they survive.
        Assert.Equal(PageSetup.Default.Dpi, setup.Dpi, 3);
        Assert.Equal(PageSetup.Default.Background, setup.Background);
        Assert.Equal(PageSetup.Default.Continuous, setup.Continuous);
    }

    [Fact]
    public void The_Text_Column_Follows_The_Documents_Margins()
    {
        LayoutResult result = Layout(
            RichTextDocument.FromPlainText("body"),
            PageSetup.Default.WithGeometry(A4Letterhead));

        Assert.Equal(127.55, result.Setup.ContentLeftPoints, 2);
        Assert.Equal(
            595.276 - 127.55 - 56.7,
            result.Setup.ContentWidthPoints,
            2);
    }

    [Fact]
    public void A_Shape_In_The_Margin_Lands_On_The_Page()
    {
        // The stripe is anchored 111.8pt left of a text column that starts at
        // 127.55pt, so it belongs on the page - which it does not with a 1in
        // margin, where the same offset puts it off the left edge.
        RichTextDocument document = RichTextDocument.FromPlainText("body")
            .WithShapes([new DocumentShape(0, -111.8, 0, 100, 780, ShapeFill.Solid(BColor.Black))]);

        LayoutShape onPage = Assert.Single(
            Layout(document, PageSetup.Default.WithGeometry(A4Letterhead))
                .Pages.SelectMany(page => page.Shapes));
        LayoutShape offPage = Assert.Single(
            Layout(document, PageSetup.Default).Pages.SelectMany(page => page.Shapes));

        Assert.True(onPage.Bounds.Left >= 0, $"the stripe started at {onPage.Bounds.Left}, off the page");
        Assert.True(offPage.Bounds.Left < 0, "with a 1in margin the same shape should fall off the page");
    }
}
