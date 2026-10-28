using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;

namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Covers what printing needs from the paginator: pages of the setup it was asked for, pages
/// that carry their own images, and pages that draw exactly what the CLI has always drawn.
/// </summary>
public sealed class DocumentPaginatorTests
{
    private static RichTextDocument Lines(int count) =>
        RichTextDocument.FromParagraphs(Enumerable.Range(1, count).Select(i => RichTextParagraph.Plain($"Line {i} of the document.")));

    private static BPixelBuffer Solid(int width, int height, byte r, byte g, byte b)
    {
        byte[] rgba = new byte[width * height * 4];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = r;
            rgba[i + 1] = g;
            rgba[i + 2] = b;
            rgba[i + 3] = 255;
        }

        return new BPixelBuffer(width, height, rgba);
    }

    private static RichTextDocument WithPicture(InlineImage image) => RichTextDocument.FromParagraphs(
    [
        RichTextParagraph.Plain("Before the picture."),
        RichTextParagraph.Create(InlineImage.PlaceholderText, new InlineStyle { Image = image }),
        RichTextParagraph.Plain("After the picture."),
    ]);

    [Fact]
    public void Pages_Are_The_Size_The_Setup_Asks_For()
    {
        var setup = new BPageSetup(BPaperSize.Letter, BPageOrientation.Landscape, BPageMargins.Uniform(36));

        using PaginatedDocument pages = new DocumentPaginator().Paginate(Lines(3), setup, TestContext.Current.CancellationToken);

        Assert.Equal(1, pages.PageCount);
        Assert.Equal(new BSize(792, 612), pages.GetPage(0).Size);
        Assert.Equal(36, pages.Layout.Setup.ContentLeftPoints, 6);
    }

    [Fact]
    public void A_Long_Document_Runs_Onto_More_Pages_And_A_Smaller_Sheet_Needs_More()
    {
        RichTextDocument document = Lines(150);
        var paginator = new DocumentPaginator();

        using PaginatedDocument a4 = paginator.Paginate(document, BPageSetup.Default, TestContext.Current.CancellationToken);
        using PaginatedDocument a5 = paginator.Paginate(document, BPageSetup.Default with { Paper = BPaperSize.A5 }, TestContext.Current.CancellationToken);

        Assert.True(a4.PageCount > 1, $"150 lines should not fit one A4 page; got {a4.PageCount}.");
        Assert.True(a5.PageCount > a4.PageCount, $"A5 ({a5.PageCount}) should need more pages than A4 ({a4.PageCount}).");
        Assert.All(Enumerable.Range(0, a4.PageCount), i => Assert.NotEmpty(a4.GetPage(i).Commands));
    }

    [Fact]
    public void A_Page_Is_Painted_Once_And_Kept()
    {
        using PaginatedDocument pages = new DocumentPaginator().Paginate(Lines(2), BPageSetup.Default, TestContext.Current.CancellationToken);

        Assert.Same(pages.GetPage(0), pages.GetPage(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => pages.GetPage(1));
    }

    [Fact]
    public void A_Page_Carries_The_Pixels_Of_Every_Picture_It_Draws()
    {
        var image = new InlineImage(BImageResource.FromPixels(Solid(8, 4, 255, 0, 0)));

        using PaginatedDocument pages = new DocumentPaginator().Paginate(WithPicture(image), BPageSetup.Default, TestContext.Current.CancellationToken);
        BPage page = pages.GetPage(0);

        BRenderCommand.DrawImage draw = Assert.Single(page.Commands.OfType<BRenderCommand.DrawImage>());
        Assert.True(page.Images.TryGetValue(draw.Image, out BPixelBuffer? pixels));
        Assert.Equal(8, pixels!.Width);
        Assert.Equal(255, pixels.Rgba[0]);
        Assert.Empty(pages.Notes);
    }

    [Fact]
    public void A_Picture_That_Cannot_Be_Decoded_Is_Marked_And_Reported()
    {
        var broken = new InlineImage(new byte[] { 1, 2, 3, 4 }, "image/png", 0, 0, name: "broken");

        using PaginatedDocument pages = new DocumentPaginator().Paginate(WithPicture(broken), BPageSetup.Default, TestContext.Current.CancellationToken);
        BPage page = pages.GetPage(0);

        Assert.Empty(page.Images);
        Assert.Contains(page.Commands, command => command is BRenderCommand.StrokeRect);
        Assert.Contains(pages.Notes, note => note.Contains("broken", StringComparison.Ordinal));
    }

    /// <summary>
    /// The CLI draws a page through renderer handles; a printer draws the frozen page through
    /// its own. Both are the painter's commands, so at one resolution they are the same pixels.
    /// </summary>
    [Fact]
    public void A_Frozen_Page_Rasterizes_Exactly_As_The_Painter_Draws_Directly()
    {
        var image = new InlineImage(BImageResource.FromPixels(Solid(16, 16, 0, 128, 255)));
        RichTextDocument document = WithPicture(image);
        var paginator = new DocumentPaginator();
        LayoutSettings settings = paginator.SettingsFor(document);

        using PaginatedDocument pages = paginator.Paginate(document, BPageSetup.Default, TestContext.Current.CancellationToken);
        using BBitmap frozen = BPageRasterizer.Render(pages.GetPage(0), 72, BColor.White);

        using var images = new ImageStore();
        using var renderer = new BImageRenderer();
        LayoutResult layout = new DocumentLayout(settings, images).Layout(document, DocumentPageBox.FromSetup(BPageSetup.Default));
        var list = new BRenderList();
        new DocumentPagePainter(settings, images).Paint(layout.Pages[0], layout.Setup, list, picture => images.Handle(renderer, picture));
        (int width, int height) = BPageRasterizer.GetPixelSize(pages.GetPage(0).Size, 72);
        using BBitmap direct = renderer.RenderToImage(list,
            new BSurfaceDescriptor(new BSize((width - 1e-4), (height - 1e-4)), 1.0), new BFrameContext(BColor.White));

        Assert.Equal(direct.Width, frozen.Width);
        Assert.Equal(direct.Height, frozen.Height);
        Assert.True(direct.Rgba.SequenceEqual(frozen.Rgba), "The frozen page drew something the painter did not.");
    }

    [Fact]
    public void A_Document_Asks_For_The_Page_It_States()
    {
        RichTextDocument document = Lines(1).WithPageGeometry(new PageGeometry(792, 612, 54, 54, 36, 36));

        var paged = new RichTextPagedDocument(document, "Letter");

        Assert.Equal(BPaperSize.Letter, paged.PreferredSetup.Paper);
        Assert.Equal(BPageOrientation.Landscape, paged.PreferredSetup.Orientation);
        Assert.Equal(new BPageMargins(54, 36, 54, 36), paged.PreferredSetup.Margins);
        Assert.Equal("Letter", paged.Title);
    }

    [Fact]
    public void A_Document_That_States_No_Page_Asks_For_The_Default()
    {
        var paged = new RichTextPagedDocument(Lines(1), " ");

        Assert.Same(BPageSetup.Default, paged.PreferredSetup);
        Assert.Equal("Document", paged.Title);
        Assert.Equal(1, paged.Paginate(BPageSetup.Default, TestContext.Current.CancellationToken).PageCount);
    }

    [Fact]
    public void The_Document_Defaults_Fill_The_Settings_Unless_Turned_Off()
    {
        RichTextDocument document = Lines(1).WithStyleDefaults(new DocumentStyleDefaults { FontFamily = "Georgia", FontSizePoints = 14 });

        LayoutSettings honoured = new DocumentPaginator().SettingsFor(document);
        LayoutSettings pinned = new DocumentPaginator(new LayoutSettings(), honourDocumentDefaults: false).SettingsFor(document);

        Assert.Equal("Georgia", honoured.DefaultFontFamily);
        Assert.Equal(14, honoured.DefaultFontSizePoints);
        Assert.Equal("sans-serif", pinned.DefaultFontFamily);
    }

    [Fact]
    public void Cancellation_Is_Honoured_Before_Layout()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => new DocumentPaginator().Paginate(Lines(1), BPageSetup.Default, cancelled.Token));
    }
    [Fact]
    public void Missing_Payload_Inline_Image_Paints_Placeholder_Box()
    {
        var image = InlineImage.CreateMissingPayload(100, 50, "Missing");
        RichTextDocument document = WithPicture(image);
        var paginator = new DocumentPaginator();
        using PaginatedDocument pages = paginator.Paginate(document, BPageSetup.Default, TestContext.Current.CancellationToken);
        using BBitmap rasterized = BPageRasterizer.Render(pages.GetPage(0), 72, BColor.White);
        Assert.NotNull(rasterized);
        Assert.True(rasterized.Width > 0 && rasterized.Height > 0);
    }

    [Fact]
    public void Missing_Payload_Floating_Shape_Image_Paints_Placeholder_Box()
    {
        var image = InlineImage.CreateMissingPayload(100, 50, "Missing");
        var shape = new DocumentShape(0, 10, 20, 100, 50, image: image);
        RichTextDocument document = RichTextDocument.FromPlainText("Anchor").WithShapes([shape]);
        var paginator = new DocumentPaginator();
        using PaginatedDocument pages = paginator.Paginate(document, BPageSetup.Default, TestContext.Current.CancellationToken);
        using BBitmap rasterized = BPageRasterizer.Render(pages.GetPage(0), 72, BColor.White);
        Assert.NotNull(rasterized);
        Assert.True(rasterized.Width > 0 && rasterized.Height > 0);
    }

    [Fact]
    public void Missing_Payload_Image_Emits_Missing_Image_Render_Commands()
    {
        var image = InlineImage.CreateMissingPayload(100, 50, "Missing");
        RichTextDocument document = WithPicture(image);
        using var images = new ImageStore();
        using var renderer = new BImageRenderer();
        var paginator = new DocumentPaginator();
        LayoutSettings settings = paginator.SettingsFor(document);
        LayoutResult layout = new DocumentLayout(settings, images).Layout(document, DocumentPageBox.FromSetup(BPageSetup.Default));
        var list = new BRenderList();
        new DocumentPagePainter(settings, images).Paint(layout.Pages[0], layout.Setup, list, picture => images.Handle(renderer, picture));

        var stroke = Assert.Single(list.Commands.OfType<BRenderCommand.StrokeRect>());
        Assert.Equal(new BColor(0xB0, 0xB0, 0xB0), stroke.Color);
        Assert.Equal(100, stroke.Rect.Width, 2);
    }

}
