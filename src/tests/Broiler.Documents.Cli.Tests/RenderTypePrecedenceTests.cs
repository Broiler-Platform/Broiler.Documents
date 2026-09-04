using Broiler.Documents.Cli.Commands;
using Broiler.Documents.Cli.Rendering;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// Whose type a render uses when the document states a default and the caller
/// also has an opinion — the same rule as
/// <see cref="RenderPagePrecedenceTests"/>, applied to size and family.
/// </summary>
/// <remarks>
/// A renderer is the half of PDF roadmap §6.4 that is allowed to guess: it draws
/// to a screen or an image a person looks at, so falling back to its own face
/// when a document names none is reasonable, and the manifest records what it
/// used. A paginator or a writer is the other half and may not.
/// </remarks>
public sealed class RenderTypePrecedenceTests
{
    private static readonly CommandSpec Spec = RenderCommand.Create().Spec;

    private static readonly DocumentStyleDefaults Stated =
        new() { FontSizePoints = 20f, FontFamily = "Georgia" };

    private static RichTextDocument Stating(DocumentStyleDefaults defaults) =>
        RichTextDocument.FromPlainText("body").WithStyleDefaults(defaults);

    /// <summary>The settings a render actually laid the document out with.</summary>
    private static LayoutSettings Resolve(RichTextDocument document, params string[] arguments)
    {
        RenderPipeline pipeline = RenderPipeline.Create(CommandLine.Parse(Spec, arguments));
        using RenderOutcome outcome = pipeline.Render(document);
        return outcome.Layout.Settings;
    }

    [Fact]
    public void A_Render_That_Asked_For_No_Type_Takes_The_Documents()
    {
        LayoutSettings settings = Resolve(Stating(Stated));

        Assert.Equal(20.0, settings.DefaultFontSizePoints, 3);
        Assert.Equal("Georgia", settings.DefaultFontFamily);
    }

    [Theory]
    [InlineData("--font-size", "9")]
    [InlineData("--font", "Courier New")]
    public void An_Asked_For_Type_Outranks_The_Documents(params string[] arguments)
    {
        LayoutSettings asked = Resolve(RichTextDocument.FromPlainText("body"), arguments);
        LayoutSettings settings = Resolve(Stating(Stated), arguments);

        Assert.Equal(asked.DefaultFontSizePoints, settings.DefaultFontSizePoints, 3);
        Assert.Equal(asked.DefaultFontFamily, settings.DefaultFontFamily);
        Assert.NotEqual(Stated.FontFamily, settings.DefaultFontFamily);
    }

    [Fact]
    public void A_Document_That_Names_No_Family_Leaves_The_Renderers_Face()
    {
        // The display half of §6.4: a renderer may fall back to its own face,
        // because what it produces is looked at rather than published. The size
        // still comes from the document, which always has one.
        LayoutSettings settings = Resolve(Stating(new DocumentStyleDefaults { FontSizePoints = 20f }));

        Assert.Equal(20.0, settings.DefaultFontSizePoints, 3);
        Assert.Equal("sans-serif", settings.DefaultFontFamily);
    }

    [Fact]
    public void A_Document_That_States_Nothing_Gets_Twelve_Points()
    {
        // Not the renderer's historical 11: the document's default is the
        // document's, and it is the same number for every consumer now.
        LayoutSettings settings = Resolve(RichTextDocument.FromPlainText("body"));

        Assert.Equal(
            DocumentStyleDefaults.FallbackFontSizePoints,
            settings.DefaultFontSizePoints,
            3);
    }
}
