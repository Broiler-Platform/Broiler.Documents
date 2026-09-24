using Broiler.Documents.Resources;

namespace Broiler.Documents.Pdf.Tests;

public sealed class PdfDocumentCodecProbeTests
{
    private static DocumentProbeResult Probe(string prefix, DocumentSourceHints? hints = null) =>
        new PdfDocumentCodec().Probe(new DocumentProbeRequest(PdfFileBuilder.Latin1(prefix), hints));

    [Fact]
    public void Matches_A_Header_At_Byte_Zero_With_Certainty()
    {
        DocumentProbeResult result = Probe("%PDF-1.7\n%\xE2\xE3\xCF\xD3\n");

        Assert.Equal(DocumentProbeConfidence.Certain, result.Confidence);
        Assert.Equal("PDF", result.FormatName);
        Assert.Equal("application/pdf", result.MimeType);
    }

    [Fact]
    public void Matches_A_Header_After_A_Preamble_With_Lower_Confidence()
    {
        DocumentProbeResult result = Probe("some junk\n%PDF-1.4\n");

        Assert.Equal(DocumentProbeConfidence.High, result.Confidence);
        Assert.NotNull(result.Diagnostic);
    }

    [Fact]
    public void Falls_Back_To_Filename_And_Mime_Hints()
    {
        Assert.Equal(DocumentProbeConfidence.Low, Probe("no header", new DocumentSourceHints("report.pdf")).Confidence);
        Assert.Equal(DocumentProbeConfidence.Low, Probe("no header", new DocumentSourceHints(mimeType: "application/pdf")).Confidence);
    }

    [Fact]
    public void Does_Not_Match_Unrelated_Content()
    {
        Assert.False(Probe("{\\rtf1 hello}").IsMatch);
        Assert.False(Probe(string.Empty).IsMatch);
    }

    [Fact]
    public void A_Catalog_Selects_The_Pdf_Codec_Over_Its_Neighbours()
    {
        var catalog = new DocumentCodecCatalog([new PdfDocumentCodec()]);
        using var stream = new MemoryStream(PdfFileBuilder.SinglePage(PdfFileBuilder.ShowText("Hi")));

        DocumentCodecMatch match = Assert.IsType<DocumentCodecMatch>(catalog.Select(stream));

        Assert.Equal("PDF", match.Codec.Name);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void The_Descriptor_Names_The_Format_Consistently()
    {
        var codec = new PdfDocumentCodec();

        Assert.Equal("PDF", codec.Descriptor.Name);
        Assert.Contains("application/pdf", codec.Descriptor.MimeTypes);
        Assert.Contains(".pdf", codec.Descriptor.FileExtensions);
        Assert.True(codec.CanRead);
        Assert.True(codec.CanWrite);
    }

    [Fact]
    public void A_Plain_Shared_Option_Object_Is_Honoured_Rather_Than_Rejected()
    {
        // A caller that knows nothing about PDF passes the shared options; the
        // codec must apply them and fill in its own defaults for the rest.
        using var stream = new MemoryStream(PdfFileBuilder.SinglePage(PdfFileBuilder.ShowText("Shared")));
        DocumentReadResult result = new PdfDocumentCodec().Read(stream, new DocumentReadOptions());

        PdfReadResult typed = Assert.IsType<PdfReadResult>(result);
        Assert.Contains("Shared", typed.Document.PlainText);
    }

    [Fact]
    public void The_Resource_Policy_In_Plain_Options_Is_The_One_A_Read_Applies()
    {
        // The policy is a shared setting like the limits. Dropped on the way to
        // the PDF options, it left every caller that granted its pictures the
        // right to be written out again reading under the default, which
        // withholds that right - so a conversion out of a PDF lost its pictures.
        using var stream = new MemoryStream(PageWithPicture());
        DocumentReadResult result = new PdfDocumentCodec().Read(
            stream,
            new DocumentReadOptions(resourcePolicy: DocumentResourcePolicy.AllowOwnDocuments));

        DocumentResourceEntry entry = Assert.Single(result.Resources.Entries);
        Assert.True(entry.Allows(DocumentResourceOperations.ByteTransfer));
    }

    [Fact]
    public void The_Resource_Policy_In_A_Read_Request_Is_The_One_A_Read_Applies()
    {
        // The request contract, in the refusing direction: a policy that denies
        // everything keeps the picture out and the text in.
        using DocumentInput input = DocumentInput.FromBytes(PageWithPicture());
        DocumentReadResult result = new PdfDocumentCodec().Read(
            new DocumentReadRequest(input, new DocumentReadOptions(resourcePolicy: DocumentResourcePolicy.DenyAll)));

        Assert.DoesNotContain(
            result.Document.Paragraphs.SelectMany(paragraph => paragraph.Runs),
            run => run.Style.Image is not null);
        Assert.Contains("Body", result.Document.PlainText);
    }

    /// <summary>One page drawing a two-pixel gray picture above a line of text.</summary>
    private static byte[] PageWithPicture()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int image = builder.AddStream("/Type /XObject /Subtype /Image /Width 2 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8", [0x00, 0xFF], null);
        int content = builder.AddStream(string.Empty, "q 20 0 0 10 72 700 cm /Im0 Do Q\n" + PdfFileBuilder.ShowText("Body"));

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
            $"/Resources << /Font << /F1 {font} 0 R >> /XObject << /Im0 {image} 0 R >> >> /Contents {content} 0 R >>");

        return builder.Build(catalog);
    }
}

public sealed class PdfRoundTripTests
{
    private static RichTextDocument RoundTrip(RichTextDocument document, PdfWriteOptions? options = null)
    {
        var codec = new PdfDocumentCodec();
        using var buffer = new MemoryStream();
        PdfWriteResult write = codec.WritePdf(document, buffer, options);
        Assert.NotEqual(DocumentResultStatus.Rejected, write.Status);

        buffer.Position = 0;
        PdfReadResult read = codec.ReadPdf(buffer);
        Assert.NotEqual(DocumentResultStatus.Rejected, read.Status);
        return read.Document;
    }

    [Fact]
    public void Text_Survives_A_Write_Then_Read()
    {
        RichTextDocument original = RichTextDocument.FromPlainText("The quick brown fox jumps over the lazy dog.");
        RichTextDocument restored = RoundTrip(original);

        Assert.Contains("The quick brown fox jumps over the lazy dog.", restored.PlainText);
    }

    [Fact]
    public void Several_Paragraphs_Survive_As_Several_Paragraphs()
    {
        RichTextDocument original = RichTextDocument.FromPlainText(
            "First paragraph of the document.\nSecond paragraph of the document.\nThird paragraph of the document.");

        RichTextDocument restored = RoundTrip(original);

        Assert.Equal(3, restored.ParagraphCount);
        Assert.Contains("First paragraph", restored.Paragraphs[0].Text);
        Assert.Contains("Third paragraph", restored.Paragraphs[2].Text);
    }

    [Fact]
    public void Bold_And_Italic_Survive_Through_The_Standard_Faces()
    {
        RichTextDocument original = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create("Emphatic", new InlineStyle { Bold = true, Italic = true }),
        ]);

        RichTextDocument restored = RoundTrip(original);
        StyleRun run = restored.Paragraphs[0].Runs[0];

        Assert.True(run.Style.Bold);
        Assert.True(run.Style.Italic);
    }

    [Fact]
    public void A_Link_Survives_As_A_Link()
    {
        RichTextDocument original = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create("Documentation", new InlineStyle { LinkHref = "https://example.org/docs" }),
        ]);

        RichTextDocument restored = RoundTrip(original);

        Assert.Contains(
            restored.Paragraphs[0].Runs,
            run => run.Style.LinkHref == "https://example.org/docs");
    }

    [Fact]
    public void A_Bullet_List_Survives_As_A_Bullet_List()
    {
        ParagraphStyle bullet = ParagraphStyle.Default with { ListKind = ListKind.Bullet, IndentLevel = 1 };
        RichTextDocument original = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create("Alpha item", InlineStyle.Default, bullet),
            RichTextParagraph.Create("Beta item", InlineStyle.Default, bullet),
        ]);

        RichTextDocument restored = RoundTrip(original);

        Assert.Equal(2, restored.ParagraphCount);
        Assert.All(restored.Paragraphs, paragraph => Assert.Equal(ListKind.Bullet, paragraph.Style.ListKind));
        Assert.Equal("Alpha item", restored.Paragraphs[0].Text);
    }

    [Fact]
    public void Uncompressed_And_Compressed_Output_Read_Back_The_Same()
    {
        RichTextDocument original = RichTextDocument.FromPlainText("Compression must not change the text.");

        string compressed = RoundTrip(original, new PdfWriteOptions(compressStreams: true)).PlainText;
        string plain = RoundTrip(original, new PdfWriteOptions(compressStreams: false)).PlainText;

        Assert.Equal(compressed, plain);
    }

    [Fact]
    public void A_Multi_Page_Document_Reads_Back_Every_Page()
    {
        var paragraphs = new List<RichTextParagraph>();
        for (int i = 0; i < 120; i++)
            paragraphs.Add(RichTextParagraph.Plain($"Paragraph number {i} with enough words to occupy a line."));

        var codec = new PdfDocumentCodec();
        using var buffer = new MemoryStream();
        PdfWriteResult write = codec.WritePdf(RichTextDocument.FromParagraphs(paragraphs), buffer);
        Assert.True(write.PageCount > 1);

        buffer.Position = 0;
        PdfReadResult read = codec.ReadPdf(buffer);

        Assert.Equal(write.PageCount, read.PageCount);
        Assert.Contains("Paragraph number 0 ", read.Document.PlainText);
        Assert.Contains("Paragraph number 119 ", read.Document.PlainText);
    }
}
