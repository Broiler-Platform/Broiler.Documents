using Broiler.Documents.Model.Fonts;
using Broiler.Documents.Pagination.Fonts;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;

namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Office issue #240, DOCX pages one side drew blank: WordArt stretched to its
/// box with its glyphs outlined, and a rotated box's outline turned with it.
/// </summary>
public sealed class DocumentLayoutIssue240DocxBlankTests
{
    private static readonly DocumentPageBox Box = new(400, 300, 10, 10, 10, 10);

    /// <summary>A face with glyph outlines: the host's DejaVu Sans, when it has one.</summary>
    private const string HostFace = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";

    private static (int Left, int Top, int Right, int Bottom)? Render(DocumentShape shape, LayoutSettings settings, out LayoutShape placed, out IReadOnlyList<BRenderCommand> commands)
    {
        var document = RichTextDocument.FromPlainText("").WithShapes([shape]);
        using var images = new ImageStore();
        var page = Assert.Single(new DocumentLayout(settings, images).Layout(document, Box).Pages);
        placed = Assert.Single(page.Shapes);
        var list = new BRenderList();
        new DocumentPagePainter(settings, images).Paint(page, Box, list, _ => null);
        commands = list.Commands;
        using var renderer = new BImageRenderer();
        using BBitmap bitmap = renderer.RenderToImage(list, new BSurfaceDescriptor(new BSize(400 - 1e-4, 300 - 1e-4), 1.0), new BFrameContext(BColor.White));
        return Ink(bitmap);
    }

    /// <summary>The bounds of the pixels darker than mid grey.</summary>
    private static (int Left, int Top, int Right, int Bottom)? Ink(BBitmap bitmap)
    {
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                int i = (y * bitmap.Width + x) * 4;
                if (bitmap.Rgba[i] + bitmap.Rgba[i + 1] + bitmap.Rgba[i + 2] >= 3 * 128)
                    continue;
                (left, top, right, bottom) = (Math.Min(left, x), Math.Min(top, y), Math.Max(right, x), Math.Max(bottom, y));
            }
        }
        return right < 0 ? null : (left, top, right, bottom);
    }

    /// <summary>tdf114308.docx's watermark: white glyphs outlined in black, in a box 300 by 200.</summary>
    private static DocumentShape WordArt(bool stretches) =>
        new(0, 40, 40, 300, 200, null, default,
            [RichTextParagraph.Empty
                .WithParagraphStyle(ParagraphStyle.Default with { Alignment = TextAlignment.Center })
                .InsertText(0, "DRAFT", InlineStyle.Default with { FontFamily = "DejaVu Sans", FontSize = 40, Foreground = BColor.White })],
            offsetFromPage: true, textVerticalAlignment: CellVerticalAlignment.Center)
        {
            StretchesTextToBox = stretches,
            TextOutline = BColor.Black,
            TextOutlineWidth = 0.75,
        };

    [Fact]
    public void WordArt_Glyph_Outlines_Fill_Its_Box()
    {
        if (!File.Exists(HostFace))
            return;
        using var service = new DocumentFontService();
        service.AddFontFile("DejaVu Sans", HostFace);
        var settings = new LayoutSettings { TextMeasurer = new PaginationFontAdapter(service) };

        var drawn = Render(WordArt(stretches: true), settings, out LayoutShape shape, out var commands);

        Assert.True(shape.StretchesTextToBox);
        Assert.Contains(commands, command => command is BRenderCommand.PushTransform);
        var ink = Assert.NotNull(drawn);
        BRect box = shape.Bounds;
        Assert.InRange(ink.Left, box.Left - 2, box.Left + 2);
        Assert.InRange(ink.Right, box.Right - 3, box.Right + 2);
        Assert.InRange(ink.Top, box.Top - 2, box.Top + 2);
        Assert.InRange(ink.Bottom, box.Bottom - 3, box.Bottom + 2);
    }

    [Fact]
    public void Without_Glyph_Outlines_WordArt_Is_Drawn_As_Laid_Out()
    {
        // No font service: the painter knows no outlines, so it neither
        // stretches the text nor strokes it.
        var drawn = Render(WordArt(stretches: true), new LayoutSettings(), out _, out var commands);

        Assert.Contains(commands, command => command is BRenderCommand.DrawText);
        Assert.Null(drawn);
    }

    [Fact]
    public void A_Rotated_Boxs_Outline_Turns_With_It()
    {
        // tdf76446.docx: a rectangle turned 31 degrees was outlined as an
        // upright box, because a stroked rectangle ignores the turn.
        var shape = new DocumentShape(0, 100, 150, 150, 100, null, BColor.Black, null, offsetFromPage: true)
        {
            RotationRadians = -31.28 * Math.PI / 180,
        };

        var drawn = Render(shape, new LayoutSettings(), out LayoutShape placed, out var commands);

        Assert.DoesNotContain(commands, command => command is BRenderCommand.StrokeRect);
        // Turned anticlockwise about its top-left corner: the top edge climbs
        // 150 sin 31.28 = 78 points to the right, and the lower right corner
        // reaches 150 cos 31.28 + 100 sin 31.28 = 180 points along.
        var ink = Assert.NotNull(drawn);
        BRect box = placed.Bounds;
        Assert.InRange(ink.Top, box.Top - 80, box.Top - 76);
        Assert.InRange(ink.Right, box.Left + 178, box.Left + 182);
    }
}
