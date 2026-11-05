using System.Text;
using Broiler.Documents.Docx;
using Broiler.Documents.Model;
using Broiler.Documents.Pagination;
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
        Assert.Single(result.Pages[0].Shapes);
        Assert.Single(result.Pages[1].Shapes);
        Assert.Equal("first", string.Concat(result.Pages[0].Shapes[0].Lines.SelectMany(l => l.Pieces.Select(p => p.Text))));
        Assert.Equal("second", string.Concat(result.Pages[1].Shapes[0].Lines.SelectMany(l => l.Pieces.Select(p => p.Text))));
        Assert.Equal(20d, result.Pages[0].Shapes[0].Bounds.Top);
        Assert.Equal(80d, result.Pages[1].Shapes[0].Bounds.Top);
    }
}
