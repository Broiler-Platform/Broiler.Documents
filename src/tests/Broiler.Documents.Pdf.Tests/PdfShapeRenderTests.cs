using System.Text;
using Broiler.Graphics;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Painting floating shapes. A shape is the bottom layer of the page: it is
/// emitted before the run backgrounds and before any text, so a letterhead's
/// stripe sits under the letter rather than over it.
/// </summary>
public sealed class PdfShapeRenderTests
{
    private static RichTextDocument WithShape(DocumentShape shape) =>
        RichTextDocument.FromPlainText("body").WithShapes([shape]);

    private static DocumentShape Stripe(ShapeFill? fill, BColor outline = default) =>
        new(0, -40, 0, 30, 200, fill, outline);

    private static string Write(RichTextDocument document)
    {
        using var stream = new MemoryStream();
        new PdfDocumentCodec().WritePdf(document, stream, new PdfWriteOptions(compressStreams: false));
        return Encoding.Latin1.GetString(stream.ToArray());
    }

    /// <summary>The content stream's rectangle-fill operators, in order.</summary>
    private static List<string> Fills(string content)
    {
        var fills = new List<string>();
        foreach (string line in content.Split('\n'))
        {
            if (line.EndsWith(" re f", StringComparison.Ordinal))
                fills.Add(line);
        }

        return fills;
    }

    [Fact(Timeout = 600000)]
    public void A_Solid_Shape_Is_Filled_Once()
    {
        string content = Write(WithShape(Stripe(ShapeFill.Solid(BColor.FromArgb(0xFF, 0xAE, 0xCF, 0x00)))));

        Assert.Single(Fills(content));
        // 0xAE/255 is about 0.682 - the green the stripe is painted in.
        Assert.Contains("0.68", content, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void A_Gradient_Shape_Is_Banded()
    {
        var fill = new ShapeFill(BColor.FromArgb(0xFF, 0xAE, 0xCF, 0x00), BColor.White, 90);
        string content = Write(WithShape(Stripe(fill)));

        // One band per point of height, so a 200pt stripe is banded, not flat.
        Assert.True(Fills(content).Count > 100, $"expected the gradient to be banded, got {Fills(content).Count} fill(s)");
    }

    [Fact(Timeout = 600000)]
    public void A_Shape_Is_Painted_Before_Any_Text()
    {
        string content = Write(WithShape(Stripe(ShapeFill.Solid(BColor.Black))));

        int fill = content.IndexOf(" re f", StringComparison.Ordinal);
        int text = content.IndexOf("BT", StringComparison.Ordinal);

        Assert.True(fill > 0 && text > 0, "expected both a fill and a text block");
        Assert.True(fill < text, "the shape was painted over the text instead of under it");
    }

    [Fact(Timeout = 600000)]
    public void An_Outlined_Shape_Strokes_Its_Box()
    {
        string content = Write(WithShape(Stripe(ShapeFill.Solid(BColor.White), BColor.Black)));

        Assert.Contains(" re S", content, StringComparison.Ordinal);
        Assert.Contains(" RG", content, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void A_Shape_Without_A_Fill_Paints_Nothing()
    {
        Assert.Empty(Fills(Write(WithShape(Stripe(fill: null)))));
    }

    [Fact(Timeout = 600000)]
    public void A_Shapes_Own_Text_Is_Drawn()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("body").WithShapes(
        [
            new DocumentShape(
                0, -40, 0, 120, 60,
                ShapeFill.Solid(BColor.White),
                BColor.Black,
                [RichTextParagraph.Plain("logohere")]),
        ]);

        Assert.Contains("logohere", Write(document), StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void A_Document_Without_Shapes_Is_Unchanged()
    {
        string plain = Write(RichTextDocument.FromPlainText("body"));

        Assert.Empty(Fills(plain));
        Assert.DoesNotContain(" re S", plain, StringComparison.Ordinal);
    }
}
