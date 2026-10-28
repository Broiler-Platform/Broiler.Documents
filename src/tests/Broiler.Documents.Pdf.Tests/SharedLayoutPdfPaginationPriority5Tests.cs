using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Pagination;
using Broiler.Documents.Pdf.Text;
using Broiler.Documents.Pdf.Writing;
using Broiler.Graphics.Color;
using Xunit;

namespace Broiler.Documents.Pdf.Tests;

public sealed class SharedLayoutPdfPaginationPriority5Tests
{
    private sealed class StubPaginatedPiece(
        string text,
        double x,
        double width,
        string fontFamily = "Helvetica",
        double fontSize = 12,
        bool bold = false,
        bool italic = false,
        BColor color = default,
        string? link = null,
        bool underline = false,
        bool strikethrough = false,
        double rise = 0) : IPaginatedPieceArtifact
    {
        public string Text { get; } = text;
        public double X { get; } = x;
        public double Width { get; } = width;
        public double Ascent { get; } = fontSize * 0.8;
        public double Descent { get; } = fontSize * 0.2;
        public double Rise { get; } = rise;
        public string FontFamily { get; } = fontFamily;
        public double FontSizePoints { get; } = fontSize;
        public bool Bold { get; } = bold;
        public bool Italic { get; } = italic;
        public BColor Color { get; } = color;
        public BColor Highlight { get; } = BColor.Empty;
        public bool Underline { get; } = underline;
        public bool Strikethrough { get; } = strikethrough;
        public string? Link { get; } = link;
        public bool IsImage => false;
        public bool IsTab => false;
    }

    private sealed class StubPaginatedLine(
        double top,
        double height,
        double baseline,
        IReadOnlyList<IPaginatedPieceArtifact> pieces) : IPaginatedLineArtifact
    {
        public double Top { get; } = top;
        public double Height { get; } = height;
        public double Baseline { get; } = baseline;
        public int ParagraphIndex { get; } = 0;
        public IReadOnlyList<IPaginatedPieceArtifact> Pieces { get; } = pieces;
    }

    private sealed class StubPaginatedShape(
        double x,
        double y,
        double width,
        double height,
        ShapeFill? fill = null,
        BColor outline = default,
        double outlineWidth = 1) : IPaginatedShapeArtifact
    {
        public double X { get; } = x;
        public double Y { get; } = y;
        public double Width { get; } = width;
        public double Height { get; } = height;
        public ShapeFill? Fill { get; } = fill;
        public BColor Outline { get; } = outline;
        public double OutlineWidth { get; } = outlineWidth;
    }

    private sealed class StubPaginatedPage(
        int pageNumber,
        double width,
        double height,
        IReadOnlyList<IPaginatedLineArtifact> lines,
        IReadOnlyList<IPaginatedShapeArtifact> shapes,
        ShapeFill? background = null) : IPaginatedPageArtifact
    {
        public int PageNumber { get; } = pageNumber;
        public double WidthPoints { get; } = width;
        public double HeightPoints { get; } = height;
        public IReadOnlyList<IPaginatedLineArtifact> Lines { get; } = lines;
        public IReadOnlyList<IPaginatedShapeArtifact> Shapes { get; } = shapes;
        public ShapeFill? Background { get; } = background;
    }

    private sealed class StubPaginatedDocument(IReadOnlyList<IPaginatedPageArtifact> pages) : IPaginatedDocumentArtifact
    {
        public IReadOnlyList<IPaginatedPageArtifact> Pages { get; } = pages;
    }

    [Fact]
    public void BuildFromArtifact_PreservesResolvedGeometry_WithoutRemeasuring()
    {
        var options = PdfWriteOptions.Default;
        var diagnostics = new PdfDiagnosticSink(options.PdfLimits.MaxDiagnostics);
        var layout = new PdfPageLayout(options, new PdfApproximateFontMetrics(), PdfUriPolicy.Default, diagnostics, CancellationToken.None);

        var piece1 = new StubPaginatedPiece("Hello", x: 72, width: 30, fontFamily: "Helvetica", fontSize: 12, bold: true);
        var piece2 = new StubPaginatedPiece(" World", x: 102, width: 35, fontFamily: "Times New Roman", fontSize: 12, italic: true, link: "https://example.com");
        var line = new StubPaginatedLine(top: 100, height: 14, baseline: 110, [piece1, piece2]);

        var shape = new StubPaginatedShape(x: 50, y: 50, width: 200, height: 100, outline: new BColor(0, 0, 0));
        var page = new StubPaginatedPage(pageNumber: 1, width: 612, height: 792, [line], [shape]);
        var documentArtifact = new StubPaginatedDocument([page]);

        var pdfPages = layout.BuildFromArtifact(documentArtifact);

        Assert.Single(pdfPages);
        var pdfPage = pdfPages[0];

        // Shapes: verified Y coordinate inversion from top-left (y=50, h=100) to PDF bottom-left: 792 - (50 + 100) = 642
        Assert.Single(pdfPage.Shapes);
        var placedShape = pdfPage.Shapes[0];
        Assert.Equal(50, placedShape.X);
        Assert.Equal(642, placedShape.Y);
        Assert.Equal(200, placedShape.Width);
        Assert.Equal(100, placedShape.Height);

        // Runs: verified baseline coordinate inversion: 792 - 110 = 682
        Assert.Equal(2, pdfPage.Runs.Count);

        var run1 = pdfPage.Runs[0];
        Assert.Equal("Hello", run1.Text);
        Assert.Equal(72, run1.X);
        Assert.Equal(682, run1.Baseline);
        Assert.Equal(PdfStandardFont.HelveticaBold, run1.Font);
        Assert.Null(run1.LinkHref);

        var run2 = pdfPage.Runs[1];
        Assert.Equal(" World", run2.Text);
        Assert.Equal(102, run2.X);
        Assert.Equal(682, run2.Baseline);
        Assert.Equal(PdfStandardFont.TimesItalic, run2.Font);
        Assert.Equal("https://example.com", run2.LinkHref);
    }

    [Fact]
    public void PdfWriter_EmitsValidPdf_FromPaginatedArtifact()
    {
        var piece = new StubPaginatedPiece("Sample Content", x: 72, width: 100, fontSize: 14);
        var line = new StubPaginatedLine(top: 100, height: 18, baseline: 114, [piece]);
        var page = new StubPaginatedPage(1, 612, 792, [line], []);
        var artifact = new StubPaginatedDocument([page]);

        var doc = RichTextDocument.FromPlainText("Sample Content");
        var options = new PdfWriteOptions(compressStreams: false).WithPaginatedArtifact(artifact);

        using var stream = new MemoryStream();
        var result = PdfWriter.Write(doc, stream, options, PdfCodecServices.Base);

        Assert.Equal(DocumentResultStatus.Success, result.Status);
        Assert.Equal(1, result.PageCount);
        Assert.True(result.BytesWritten > 0);

        string pdfAscii = Encoding.ASCII.GetString(stream.ToArray());
        Assert.StartsWith("%PDF-1.7", pdfAscii);
        Assert.Contains("/Helvetica", pdfAscii);
        Assert.Contains("Sample Content", pdfAscii);

        // Also verify that PdfReader reads the document back
        var readResult = PdfReader.Read(stream.ToArray(), PdfReadOptions.Default, PdfCodecServices.Base);
        Assert.Equal(DocumentResultStatus.Success, readResult.Status);
        Assert.Equal(1, readResult.PageCount);
        Assert.Contains("Sample Content", readResult.Document.PlainText);
    }

    [Fact]
    public void PdfWriter_MultiPageArtifact_EmitsAllPagesPreservingContent()
    {
        var line1 = new StubPaginatedLine(50, 15, 62, [new StubPaginatedPiece("Page One Line", 72, 80)]);
        var page1 = new StubPaginatedPage(1, 612, 792, [line1], []);

        var line2 = new StubPaginatedLine(50, 15, 62, [new StubPaginatedPiece("Page Two Line", 72, 80)]);
        var page2 = new StubPaginatedPage(2, 612, 792, [line2], []);

        var artifact = new StubPaginatedDocument([page1, page2]);
        var doc = RichTextDocument.FromPlainText("Page One Line\nPage Two Line");
        var options = new PdfWriteOptions(compressStreams: false).WithPaginatedArtifact(artifact);

        using var stream = new MemoryStream();
        var result = PdfWriter.Write(doc, stream, options, PdfCodecServices.Base);

        Assert.Equal(DocumentResultStatus.Success, result.Status);
        Assert.Equal(2, result.PageCount);

        var readResult = PdfReader.Read(stream.ToArray(), PdfReadOptions.Default, PdfCodecServices.Base);
        Assert.Equal(2, readResult.PageCount);
        Assert.Contains("Page One Line", readResult.Document.PlainText);
        Assert.Contains("Page Two Line", readResult.Document.PlainText);
    }

    [Fact]
    public void BuildFromArtifact_RejectsDisallowedUris_UnderDefaultUriPolicy()
    {
        var options = PdfWriteOptions.Default;
        var diagnostics = new PdfDiagnosticSink(options.PdfLimits.MaxDiagnostics);
        var layout = new PdfPageLayout(options, new PdfApproximateFontMetrics(), PdfUriPolicy.Default, diagnostics, CancellationToken.None);

        // mailto: is rejected by default policy unless explicitly opted-in
        var piece = new StubPaginatedPiece("Contact", 72, 50, link: "mailto:support@example.com");
        var line = new StubPaginatedLine(50, 15, 62, [piece]);
        var page = new StubPaginatedPage(1, 612, 792, [line], []);
        var artifact = new StubPaginatedDocument([page]);

        var pdfPages = layout.BuildFromArtifact(artifact);
        Assert.Single(pdfPages);
        // Link should be rejected/nullified under default policy
        Assert.Null(pdfPages[0].Runs[0].LinkHref);
        Assert.True(diagnostics.Contains(PdfDiagnosticCodes.UriRejected));
    }
}
