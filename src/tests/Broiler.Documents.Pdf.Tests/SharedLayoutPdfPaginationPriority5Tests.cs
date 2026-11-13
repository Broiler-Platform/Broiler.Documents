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
        public IReadOnlyList<Broiler.Graphics.Geometry.BRect> ColumnSeparators { get; init; } = [];
    }

    private sealed class StubPaginatedDocument(IReadOnlyList<IPaginatedPageArtifact> pages) : IPaginatedDocumentArtifact
    {
        public IReadOnlyList<IPaginatedPageArtifact> Pages { get; } = pages;
    }

    [Theory]
    [InlineData(SectionBreakType.OddPage, 3)]
    [InlineData(SectionBreakType.EvenPage, 2)]
    public void Standalone_Pdf_Starts_The_Next_Section_On_The_Requested_Parity(
        SectionBreakType breakType, int expectedPages)
    {
        var document = RichTextDocument.FromPlainText("First\nSecond")
            .WithRunningContent(RunningContent.Empty.WithHeader(PageSelection.Default, [RichTextParagraph.Plain("Header")]))
            .WithSections(
            [
                new DocumentSection(0, 1, 1, 0),
                new DocumentSection(1, 1, 1, 0, breakType: breakType),
            ]);
        var options = PdfWriteOptions.Default;
        var layout = new PdfPageLayout(options, new PdfApproximateFontMetrics(), PdfUriPolicy.Default,
            new PdfDiagnosticSink(options.PdfLimits.MaxDiagnostics), CancellationToken.None);

        var pages = layout.Build(document);
        Assert.Equal(expectedPages, pages.Count);
        Assert.Contains(pages[^1].Runs, run => run.Text == "Second");
        if (expectedPages == 3)
            Assert.Empty(pages[1].Runs);
    }

    [Fact]
    public void Section_Paper_And_Margins_Control_Standalone_Pdf_Pages()
    {
        var second = new PageGeometry(700, 500, 90, 50, 40, 30);
        var document = RichTextDocument.FromPlainText("First\nSecond")
            .WithPageGeometry(new PageGeometry(612, 792, 72, 72, 72, 72))
            .WithSections(
            [
                new DocumentSection(0, 1, 1, 0),
                new DocumentSection(1, 1, 1, 0, page: second, breakType: SectionBreakType.NextPage),
            ]);
        using var stream = new MemoryStream();
        var result = PdfWriter.Write(document, stream, new PdfWriteOptions(compressStreams: false),
            PdfCodecServices.Base, TestContext.Current.CancellationToken);

        Assert.Equal(DocumentResultStatus.Success, result.Status);
        Assert.Equal(2, result.PageCount);
        string pdf = Encoding.ASCII.GetString(stream.ToArray());
        Assert.Contains("/MediaBox [0 0 612 792]", pdf);
        Assert.Contains("/MediaBox [0 0 700 500]", pdf);

        var options = PdfWriteOptions.Default;
        var layout = new PdfPageLayout(options, new PdfApproximateFontMetrics(), PdfUriPolicy.Default,
            new PdfDiagnosticSink(options.PdfLimits.MaxDiagnostics), CancellationToken.None);
        Assert.Equal(90, Assert.Single(layout.Build(document)[1].Runs).X, 6);
    }

    [Fact]
    public void Continuous_Section_Starts_Below_The_Preceding_Columns()
    {
        var style = ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 14, SpacingAfter = 0.01f };
        var document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Plain("Left").WithParagraphStyle(style),
            RichTextParagraph.Plain("Right").WithParagraphStyle(style with { ColumnBreakBefore = true }),
            RichTextParagraph.Plain("Below").WithParagraphStyle(style),
        ]).WithPageGeometry(new PageGeometry(612, 792, 72, 72, 72, 72))
          .WithSections(
          [
              new DocumentSection(0, 2, 2, 36) { ColumnSeparator = true },
              new DocumentSection(2, 1, 1, 0),
          ]);
        var options = PdfWriteOptions.Default;
        var layout = new PdfPageLayout(options, new PdfApproximateFontMetrics(), PdfUriPolicy.Default,
            new PdfDiagnosticSink(options.PdfLimits.MaxDiagnostics), CancellationToken.None);

        var page = Assert.Single(layout.Build(document));
        Assert.Equal(3, page.Runs.Count);
        Assert.Equal(252, page.Runs[1].X - page.Runs[0].X, 6);
        Assert.Equal(page.Runs[0].Baseline, page.Runs[1].Baseline, 6);
        Assert.Equal(14.01, page.Runs[0].Baseline - page.Runs[2].Baseline, 6);
        Assert.Equal(14.01, Assert.Single(page.Shapes).Height, 6);
    }

    [Fact]
    public void Standalone_Pdf_Changes_Column_Settings_At_A_Next_Page_Section()
    {
        var document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Plain("First"),
            RichTextParagraph.Plain("Second column").WithParagraphStyle(
                ParagraphStyle.Default with { ColumnBreakBefore = true }),
            RichTextParagraph.Plain("Single column"),
        ]).WithPageGeometry(new PageGeometry(612, 792, 72, 72, 72, 72))
          .WithSections(
          [
              new DocumentSection(0, 2, 2, 36, breakType: SectionBreakType.NextPage) { ColumnSeparator = true },
              new DocumentSection(2, 1, 1, 0, breakType: SectionBreakType.NextPage),
          ]);
        var options = PdfWriteOptions.Default;
        var layout = new PdfPageLayout(options, new PdfApproximateFontMetrics(), PdfUriPolicy.Default,
            new PdfDiagnosticSink(options.PdfLimits.MaxDiagnostics), CancellationToken.None);

        var pages = layout.Build(document);
        Assert.Equal(2, pages.Count);
        Assert.Equal(2, pages[0].Runs.Count);
        Assert.Equal(252, pages[0].Runs[1].X - pages[0].Runs[0].X, 6);
        Assert.Single(pages[0].Shapes);
        Assert.Empty(pages[1].Shapes);
        Assert.Equal(72, Assert.Single(pages[1].Runs).X, 6);
    }

    [Fact]
    public void Standalone_Pdf_Moves_A_Merged_Row_Group_Together()
    {
        var fill = new BColor(51, 102, 153);
        var document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Plain("First"), RichTextParagraph.Plain("Merged"), RichTextParagraph.Plain("Last")])
            .WithPageGeometry(new PageGeometry(612, 792, 72, 72, 72, 72, ColumnCount: 2, ColumnGap: 36))
            .WithTables([new DocumentTable(0, 3,
            [
                new TableRow([new TableCell(0, 1, 0)], minHeight: 400),
                new TableRow([new TableCell(1, 1, 0, rowSpan: 2, shading: fill)], minHeight: 150),
                new TableRow([new TableCell(2, 1, 1)], minHeight: 150),
            ])]);
        var options = PdfWriteOptions.Default;
        var layout = new PdfPageLayout(options, new PdfApproximateFontMetrics(), PdfUriPolicy.Default,
            new PdfDiagnosticSink(options.PdfLimits.MaxDiagnostics), CancellationToken.None);

        var page = Assert.Single(layout.Build(document));
        Assert.Equal(3, page.Runs.Count);
        Assert.Equal(252, page.Runs[1].X - page.Runs[0].X, 6);
        Assert.True(page.Runs[2].X > page.Runs[1].X);
        var merged = Assert.Single(page.Shapes);
        Assert.Equal(324, merged.X, 6);
        Assert.Equal(300, merged.Height, 6);
    }

    [Fact]
    public void Standalone_Pdf_Places_Table_Rows_And_Shading_In_The_Next_Column()
    {
        var fill = new BColor(51, 102, 153);
        var document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Plain("First"), RichTextParagraph.Plain("Second")])
            .WithPageGeometry(new PageGeometry(612, 792, 72, 72, 72, 72, ColumnCount: 2, ColumnGap: 36))
            .WithTables([new DocumentTable(0, 2,
            [
                new TableRow([new TableCell(0, 1, 0, shading: fill)], minHeight: 400),
                new TableRow([new TableCell(1, 1, 0, shading: fill)], minHeight: 400),
            ])]);
        var options = PdfWriteOptions.Default;
        var layout = new PdfPageLayout(options, new PdfApproximateFontMetrics(), PdfUriPolicy.Default,
            new PdfDiagnosticSink(options.PdfLimits.MaxDiagnostics), CancellationToken.None);

        var page = Assert.Single(layout.Build(document));
        Assert.Equal(2, page.Runs.Count);
        Assert.Equal(252, page.Runs[1].X - page.Runs[0].X, 6);
        Assert.Equal(2, page.Shapes.Count);
        Assert.Equal(252, page.Shapes[1].X - page.Shapes[0].X, 6);
    }

    [Fact]
    public void Standalone_Pdf_Advances_Through_Columns_And_Draws_Separators()
    {
        var style = ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 14 };
        var document = RichTextDocument.FromParagraphs(
            System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(1, 108),
                i => RichTextParagraph.Plain("Line " + i).WithParagraphStyle(style)))
            .WithPageGeometry(new PageGeometry(612, 792, 72, 72, 72, 72, ColumnCount: 2, ColumnGap: 36)
                { ColumnSeparator = true });

        using var stream = new MemoryStream();
        var result = PdfWriter.Write(document, stream, new PdfWriteOptions(compressStreams: false),
            PdfCodecServices.Base, TestContext.Current.CancellationToken);

        Assert.Equal(DocumentResultStatus.Success, result.Status);
        Assert.Equal(2, result.PageCount);
        Assert.Contains("305.75 72 0.5 648 re", Encoding.ASCII.GetString(stream.ToArray()));
    }

    [Fact]
    public void BuildFromArtifact_Paints_Column_Separators()
    {
        var options = PdfWriteOptions.Default;
        var layout = new PdfPageLayout(options, new PdfApproximateFontMetrics(), PdfUriPolicy.Default,
            new PdfDiagnosticSink(options.PdfLimits.MaxDiagnostics), CancellationToken.None);
        var page = new StubPaginatedPage(1, 700, 792, [], [])
        {
            ColumnSeparators = [new(305.75, 72, 0.5, 100)],
        };

        var rule = Assert.Single(Assert.Single(layout.BuildFromArtifact(new StubPaginatedDocument([page]))).Shapes);
        Assert.Equal(305.75, rule.X);
        Assert.Equal(620, rule.Y);
        Assert.Equal(0.5, rule.Width);
        Assert.Equal(100, rule.Height);

        using var stream = new MemoryStream();
        var artifact = new StubPaginatedDocument([page]);
        var writeOptions = new PdfWriteOptions(compressStreams: false).WithPaginatedArtifact(artifact);
        var result = PdfWriter.Write(RichTextDocument.FromPlainText("Columns"), stream,
            writeOptions, PdfCodecServices.Base, TestContext.Current.CancellationToken);
        Assert.Equal(DocumentResultStatus.Success, result.Status);
        Assert.Contains("305.75 620 0.5 100 re", Encoding.ASCII.GetString(stream.ToArray()));
        Assert.Contains("/MediaBox [0 0 700 792]", Encoding.ASCII.GetString(stream.ToArray()));
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
        var result = PdfWriter.Write(doc, stream, options, PdfCodecServices.Base, TestContext.Current.CancellationToken);

        Assert.Equal(DocumentResultStatus.Success, result.Status);
        Assert.Equal(1, result.PageCount);
        Assert.True(result.BytesWritten > 0);

        string pdfAscii = Encoding.ASCII.GetString(stream.ToArray());
        Assert.StartsWith("%PDF-1.7", pdfAscii);
        Assert.Contains("/Helvetica", pdfAscii);
        Assert.Contains("Sample Content", pdfAscii);

        // Also verify that PdfReader reads the document back
        var readResult = PdfReader.Read(stream.ToArray(), PdfReadOptions.Default, PdfCodecServices.Base, TestContext.Current.CancellationToken);
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
        var result = PdfWriter.Write(doc, stream, options, PdfCodecServices.Base, TestContext.Current.CancellationToken);

        Assert.Equal(DocumentResultStatus.Success, result.Status);
        Assert.Equal(2, result.PageCount);

        var readResult = PdfReader.Read(stream.ToArray(), PdfReadOptions.Default, PdfCodecServices.Base, TestContext.Current.CancellationToken);
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
