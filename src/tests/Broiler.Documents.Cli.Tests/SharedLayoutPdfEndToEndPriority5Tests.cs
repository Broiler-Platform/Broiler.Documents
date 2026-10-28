using System.IO;
using System.Text;
using Broiler.Documents;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Pagination;
using Broiler.Documents.Pagination;
using Broiler.Documents.Pdf;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Xunit;

namespace Broiler.Documents.Cli.Tests;

public sealed class SharedLayoutPdfEndToEndPriority5Tests
{
    [Fact]
    public void DocumentLayout_To_PdfCodec_EndToEnd_GeneratesAccuratePdf()
    {
        // 1. Build document with paragraphs and styles
        var p1 = RichTextParagraph.Create("Shared Layout Heading", InlineStyle.Default with { Bold = true, FontSize = 18f });
        var p2 = RichTextParagraph.Create("This is body text laid out by DocumentLayout and written by PdfWriter.", InlineStyle.Default with { FontSize = 12f });
        var doc = RichTextDocument.FromParagraphs([p1, p2]);

        // 2. Perform layout using shared DocumentLayout engine
        var images = new ImageStore();
        var layoutEngine = new DocumentLayout(new LayoutSettings(), images);
        LayoutResult layoutResult = layoutEngine.Layout(doc, DocumentPageBox.Default);

        // Verify layout artifact contract implementation
        Assert.True(layoutResult is IPaginatedDocumentArtifact);
        Assert.NotEmpty(layoutResult.Pages);
        Assert.NotEmpty(layoutResult.Pages[0].Lines);

        // 3. Hand layoutResult directly to Pdf codec without remeasuring
        var options = new PdfWriteOptions(compressStreams: false).WithPaginatedArtifact(layoutResult);
        var codec = new PdfDocumentCodec();
        using var stream = new MemoryStream();
        var writeResult = codec.WritePdf(doc, stream, options);

        Assert.Equal(DocumentResultStatus.Success, writeResult.Status);
        Assert.Equal(layoutResult.Pages.Count, writeResult.PageCount);
        Assert.True(writeResult.BytesWritten > 0);

        // 4. Verify PDF structure and contents
        string pdfText = Encoding.ASCII.GetString(stream.ToArray());
        Assert.StartsWith("%PDF-1.7", pdfText);
        Assert.Contains("Shared", pdfText);
        Assert.Contains("Heading", pdfText);
        Assert.Contains("DocumentLayout", pdfText);

        // 5. Read back with Pdf codec to verify roundtrip integrity
        stream.Position = 0;
        var readResult = codec.ReadPdf(stream, PdfReadOptions.Default);
        Assert.Equal(DocumentResultStatus.Success, readResult.Status);
        Assert.Equal(layoutResult.Pages.Count, readResult.PageCount);
        Assert.Contains("Shared Layout Heading", readResult.Document.PlainText);
        Assert.Contains("This is body text", readResult.Document.PlainText);
    }

    [Fact]
    public void DocumentLayout_MultiPage_ProducesMatchingPdfPages()
    {
        // Generate enough paragraphs to spill across multiple pages
        var paragraphs = new List<RichTextParagraph>();
        for (int i = 0; i < 40; i++)
        {
            paragraphs.Add(RichTextParagraph.Create(
                $"Paragraph {i + 1}: The quick brown fox jumps over the lazy dog. Continuous layout text verification for pagination splitting.",
                InlineStyle.Default with { FontSize = 14f }));
        }
        var doc = RichTextDocument.FromParagraphs(paragraphs);

        var images = new ImageStore();
        var layoutEngine = new DocumentLayout(new LayoutSettings(), images);
        LayoutResult layoutResult = layoutEngine.Layout(doc, DocumentPageBox.Default);

        Assert.True(layoutResult.Pages.Count > 1, "Document should spill across multiple pages.");

        var options = new PdfWriteOptions(compressStreams: false).WithPaginatedArtifact(layoutResult);
        var codec = new PdfDocumentCodec();
        using var stream = new MemoryStream();
        var writeResult = codec.WritePdf(doc, stream, options);

        Assert.Equal(DocumentResultStatus.Success, writeResult.Status);
        Assert.Equal(layoutResult.Pages.Count, writeResult.PageCount);

        stream.Position = 0;
        var readResult = codec.ReadPdf(stream, PdfReadOptions.Default);
        Assert.Equal(layoutResult.Pages.Count, readResult.PageCount);
        Assert.Contains("Paragraph 1:", readResult.Document.PlainText);
        Assert.Contains("Paragraph 40:", readResult.Document.PlainText);
    }

    [Fact]
    public void DocumentLayout_LinksAndShapes_PreservedInPdfOutput()
    {
        var shape = new DocumentShape(
            paragraphIndex: 0,
            offsetX: 100,
            offsetY: 100,
            width: 200,
            height: 150,
            fill: ShapeFill.Solid(new BColor(220, 230, 240)),
            outline: new BColor(50, 50, 50),
            anchorPage: 1,
            outlineWidth: 2);

        var p = RichTextParagraph.Create("Visit our website: ", InlineStyle.Default)
            .InsertText(19, "Broiler Project", InlineStyle.Default with { LinkHref = "https://example.org/project" });

        var doc = RichTextDocument.FromParagraphs([p]).WithShapes([shape]);

        var images = new ImageStore();
        var layoutEngine = new DocumentLayout(new LayoutSettings(), images);
        LayoutResult layoutResult = layoutEngine.Layout(doc, DocumentPageBox.Default);

        var options = new PdfWriteOptions(compressStreams: false).WithPaginatedArtifact(layoutResult);
        Assert.NotEmpty(layoutResult.Pages[0].Shapes);
        var codec = new PdfDocumentCodec();
        using var stream = new MemoryStream();
        var writeResult = codec.WritePdf(doc, stream, options);

        Assert.Equal(DocumentResultStatus.Success, writeResult.Status);
        string pdfText = Encoding.ASCII.GetString(stream.ToArray());
        Assert.Contains("/URI (https://example.org/project)", pdfText);
        Assert.Contains("/Link", pdfText);
        Assert.Contains("172 591.89 200 150 re f\n", pdfText);
        Assert.Contains("172 591.89 200 150 re S\n", pdfText);
    }
}
