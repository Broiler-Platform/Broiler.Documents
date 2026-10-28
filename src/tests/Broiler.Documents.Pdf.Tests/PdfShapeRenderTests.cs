using System.Text;
using Broiler.Graphics.Color;

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

    [Fact]
    public void Ellipses_Use_Bezier_Paths_And_Clip_Their_Fill()
    {
        string pdf = Write(WithShape(new DocumentShape(0, 10, 20, 80, 40, ShapeFill.Solid(BColor.Black),
            BColor.Black, geometry: ShapeGeometry.Ellipse, outlineWidth: 3)));
        Assert.Contains(" c h", pdf);
        Assert.Contains("W n", pdf);
        Assert.Contains("3 w", pdf);
    }

    [Theory]
    [InlineData(ShapeGeometry.Line)]
    [InlineData(ShapeGeometry.LineAscending)]
    public void Diagonal_Shapes_Emit_Line_Operators(ShapeGeometry geometry)
    {
        string pdf = Write(WithShape(new DocumentShape(0, 10, 20, 80, 40,
            outline: BColor.Black, geometry: geometry, outlineWidth: 2.5)));
        Assert.Contains(" l S", pdf);
        Assert.Contains("2.5 w", pdf);
        Assert.DoesNotContain(" re S", pdf);
    }

    [Fact]
    public void A_Content_Only_Page_Fill_Uses_The_Page_Margins()
    {
        string pdf = Write(RichTextDocument.Empty.WithPageGeometry(new PageGeometry(200, 100, 10, 20, 15, 25))
            .WithPageBackground(ShapeFill.Solid(BColor.Black) with { ContentOnly = true }));
        Assert.Contains("10 25 170 60 re f", pdf);
    }

    [Fact]
    public void A_Hatch_Produces_Clipped_Stroke_Operators()
    {
        string pdf = Write(WithShape(Stripe(ShapeFill.Solid(BColor.Empty) with { Hatch = new HatchFill(BColor.Black, 6, 0, 2) })));
        Assert.Contains("re W n", pdf);
        Assert.Contains("0.5 w", pdf);
        Assert.Contains(" l S", pdf);
    }

    [Fact]
    public void A_Bitmap_Fill_Reports_The_Standalone_Pdf_Limitation()
    {
        var document = RichTextDocument.Empty.WithPageBackground(ShapeFill.Solid(BColor.Empty) with
            { Image = new ImageFill(new InlineImage(new byte[] { 1 }, "image/png", 12, 12)) });
        using var stream = new MemoryStream();
        var result = new PdfDocumentCodec().WritePdf(document, stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == PdfDiagnosticCodes.WriteFeatureUnsupported);
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

    [Fact]
    public void A_Solid_Shape_Is_Filled_Once()
    {
        string content = Write(WithShape(Stripe(ShapeFill.Solid(BColor.FromArgb(0xFF, 0xAE, 0xCF, 0x00)))));

        Assert.Single(Fills(content));
        // 0xAE/255 is about 0.682 - the green the stripe is painted in.
        Assert.Contains("0.68", content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Translucent_Fill_Is_Blended_Over_White_Rather_Than_Painted_Opaque()
    {
        // Half-transparent black: with no transparency group, mid grey is the
        // colour it shows on a white page. Painted opaque it would be black.
        string content = Write(WithShape(Stripe(ShapeFill.Solid(BColor.FromArgb(0x80, 0x00, 0x00, 0x00)))));

        Assert.Contains("0.498 0.498 0.498 rg", content, StringComparison.Ordinal);
        Assert.DoesNotContain("\n0 0 0 rg\n0 -", content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Gradient_Shape_Is_Banded()
    {
        var fill = new ShapeFill(BColor.FromArgb(0xFF, 0xAE, 0xCF, 0x00), BColor.White, 90);
        string content = Write(WithShape(Stripe(fill)));

        // One band per point of height, so a 200pt stripe is banded, not flat.
        Assert.True(Fills(content).Count > 100, $"expected the gradient to be banded, got {Fills(content).Count} fill(s)");
    }

    [Fact]
    public void A_Shape_Is_Painted_Before_Any_Text()
    {
        string content = Write(WithShape(Stripe(ShapeFill.Solid(BColor.Black))));

        int fill = content.IndexOf(" re f", StringComparison.Ordinal);
        int text = content.IndexOf("BT", StringComparison.Ordinal);

        Assert.True(fill > 0 && text > 0, "expected both a fill and a text block");
        Assert.True(fill < text, "the shape was painted over the text instead of under it");
    }

    [Fact]
    public void An_Outlined_Shape_Strokes_Its_Box()
    {
        string content = Write(WithShape(Stripe(ShapeFill.Solid(BColor.White), BColor.Black)));

        Assert.Contains(" re S", content, StringComparison.Ordinal);
        Assert.Contains(" RG", content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Shape_Without_A_Fill_Paints_Nothing()
    {
        Assert.Empty(Fills(Write(WithShape(Stripe(fill: null)))));
    }

    [Fact]
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

    [Fact]
    public void A_Document_Without_Shapes_Is_Unchanged()
    {
        string plain = Write(RichTextDocument.FromPlainText("body"));

        Assert.Empty(Fills(plain));
        Assert.DoesNotContain(" re S", plain, StringComparison.Ordinal);
    }
}
