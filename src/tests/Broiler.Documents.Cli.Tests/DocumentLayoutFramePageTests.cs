using System.Text;
using Broiler.Documents.Rtf;

namespace Broiler.Documents.Cli.Tests;

public sealed class DocumentLayoutFramePageTests
{
    [Fact]
    public void Positioned_Rtf_Paragraphs_Follow_Their_Anchor_Across_A_Page_Break()
    {
        const string rtf = "{\\rtf1\\ansi" +
            "{\\pard\\pvpg\\posy400\\absw2000 first\\par}\\page" +
            "{\\pard\\pvpg\\posy1600\\absw2000 second\\par}}";
        using var source = new MemoryStream(Encoding.ASCII.GetBytes(rtf));
        var document = new RtfDocumentCodec().Read(source).Document;
        using var images = new ImageStore();
        var result = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(300, 300, 10, 10, 10, 10));

        Assert.Equal(2, result.Pages.Count);
        Assert.Empty(result.Pages[0].Shapes);
        Assert.Equal(2, result.Pages[1].Shapes.Count);
        Assert.Equal(new[] { "first", "second" }, result.Pages[1].Shapes.SelectMany(shape => shape.Lines)
            .Select(line => string.Concat(line.Pieces.Select(piece => piece.Text))));
        Assert.Equal(new[] { 20d, 80d }, result.Pages[1].Shapes.Select(shape => shape.Bounds.Top));
    }
}
