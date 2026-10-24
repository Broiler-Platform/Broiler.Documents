using Broiler.Graphics.Color;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.Resources;

namespace Broiler.Documents.Pagination.Tests;

public sealed class DocumentTableImageClipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_Image_In_A_Fixed_Row_Remains_Visible_Up_To_The_Row_Edge(bool inFrame)
    {
        var image = new InlineImage(BImageResource.FromPixels(new BPixelBuffer(1, 1, new byte[] { 255, 0, 0, 255 })),
            width: 40, height: 40);
        var paragraph = RichTextParagraph.Create(InlineImage.PlaceholderText, InlineStyle.Default with { Image = image });
        var table = new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0)], minHeight: 20, exactHeight: true)],
            [80], cellPadding: 0);
        RichTextDocument document = inFrame
            ? RichTextDocument.FromPlainText("").WithShapes([new DocumentShape(0, 0, 0, 80, 20,
                paragraphs: [paragraph], tables: [table])])
            : RichTextDocument.FromParagraphs([paragraph]).WithTables([table]);
        using var images = new ImageStore();
        var settings = new LayoutSettings();
        var box = new DocumentPageBox(100, 100, 10, 10, 10, 10);
        var page = Assert.Single(new DocumentLayout(settings, images).Layout(document, box).Pages);
        var frozen = new DocumentPagePainter(settings, images).Paint(page, box);
        Assert.Single(frozen.Commands.OfType<BRenderCommand.DrawImage>());
        var clip = Assert.Single(frozen.Commands.OfType<BRenderCommand.PushClip>());
        Assert.Equal(10, clip.Rect.Top, 4);
        Assert.Equal(20, clip.Rect.Height, 4);
        Assert.Equal(frozen.Commands.OfType<BRenderCommand.PushClip>().Count(),
            frozen.Commands.OfType<BRenderCommand.PopClip>().Count());
        using var pixels = BPageRasterizer.Render(frozen, 72, BColor.White);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, pixels.Rgba.Slice((15 * pixels.Width + 15) * 4, 4).ToArray());
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, pixels.Rgba.Slice((35 * pixels.Width + 15) * 4, 4).ToArray());
    }
}
