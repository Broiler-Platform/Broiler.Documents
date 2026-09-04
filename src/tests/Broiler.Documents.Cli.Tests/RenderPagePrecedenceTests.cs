using Broiler.Documents.Cli.Commands;
using Broiler.Documents.Cli.Rendering;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// Whose page a render uses when the document states one and the caller also has
/// an opinion. <see cref="PageGeometryRenderTests"/> covers what taking a
/// document's page does; this covers when it is taken at all.
/// </summary>
/// <remarks>
/// The rule is one line in <c>RenderPipeline</c> and easy to get backwards in
/// either direction. Overruling the caller makes <c>--page-size</c> a
/// suggestion; overruling the document puts a letterhead's stripe off the edge
/// of the paper, which is the bug the document's page was read to fix. It is
/// also the rule the CLI documentation states, and a documented precedence
/// nothing tests is a claim rather than a behaviour.
/// </remarks>
public sealed class RenderPagePrecedenceTests
{
    /// <summary>
    /// US Letter with a left margin no default here uses, so every assertion
    /// below can tell whose page it ended up with.
    /// </summary>
    private static readonly PageGeometry StatedPage = new(612, 792, 90, 54, 54, 54);

    private static RichTextDocument Stating(PageGeometry geometry) =>
        RichTextDocument.FromPlainText("body").WithPageGeometry(geometry);

    /// <summary>The render command's own spec, so these parse what a user types.</summary>
    private static readonly CommandSpec Spec = RenderCommand.Create().Spec;

    /// <summary>The page a render actually laid the document out on.</summary>
    private static PageSetup Resolve(RichTextDocument document, params string[] arguments)
    {
        RenderPipeline pipeline = RenderPipeline.Create(CommandLine.Parse(Spec, arguments));

        using RenderOutcome outcome = pipeline.Render(document);
        return outcome.Layout.Setup;
    }

    [Fact]
    public void A_Render_That_Asked_For_No_Page_Takes_The_One_The_Document_States()
    {
        PageSetup setup = Resolve(Stating(StatedPage));

        Assert.Equal(612, setup.WidthPoints, 3);
        Assert.Equal(792, setup.HeightPoints, 3);
        Assert.Equal(90, setup.MarginLeftPoints, 3);
    }

    [Theory]
    [InlineData("--page-size", "a5")]
    [InlineData("--margin", "2in")]
    [InlineData("--landscape")]
    public void An_Asked_For_Page_Outranks_The_One_The_Document_States(params string[] arguments)
    {
        // Compared against what the flags alone produce rather than against
        // hardcoded paper dimensions: the point is that the document changed
        // nothing, not that a5 is a particular number of points.
        PageSetup asked = PageSetup.FromCommandLine(CommandLine.Parse(Spec, arguments));

        PageSetup setup = Resolve(Stating(StatedPage), arguments);

        Assert.Equal(asked.WidthPoints, setup.WidthPoints, 3);
        Assert.Equal(asked.HeightPoints, setup.HeightPoints, 3);
        Assert.Equal(asked.MarginLeftPoints, setup.MarginLeftPoints, 3);
        Assert.NotEqual(StatedPage.MarginLeft, setup.MarginLeftPoints, 3);
    }

    [Fact]
    public void Asking_For_A_Resolution_Is_Not_Asking_For_A_Page()
    {
        // --dpi says how finely to render a page, not which page, so it is the
        // caller's either way and does not suppress the document's. The three
        // options that do are --page-size, --margin and --landscape.
        PageSetup setup = Resolve(Stating(StatedPage), "--dpi", "300");

        Assert.Equal(612, setup.WidthPoints, 3);
        Assert.Equal(90, setup.MarginLeftPoints, 3);
        Assert.Equal(300, setup.Dpi, 3);
    }

    [Fact]
    public void A_Document_That_States_No_Page_Leaves_The_Callers_Alone()
    {
        PageSetup setup = Resolve(RichTextDocument.FromPlainText("body"));

        Assert.Equal(PageSetup.Default.WidthPoints, setup.WidthPoints, 3);
        Assert.Equal(PageSetup.Default.HeightPoints, setup.HeightPoints, 3);
        Assert.Equal(PageSetup.Default.MarginLeftPoints, setup.MarginLeftPoints, 3);
    }

    [Fact]
    public void A_Page_Stated_Nonsensically_Is_Not_Taken()
    {
        // Margins wider than the paper leave no column to write in. A producer
        // that states that is better ignored than honoured, so the render keeps
        // the page it would have used and does not lay text out on nothing.
        PageSetup setup = Resolve(Stating(new PageGeometry(612, 792, 400, 400, 54, 54)));

        Assert.Equal(PageSetup.Default.WidthPoints, setup.WidthPoints, 3);
        Assert.Equal(PageSetup.Default.MarginLeftPoints, setup.MarginLeftPoints, 3);
    }
}
