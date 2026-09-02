using System.Text;
using Broiler.Documents.Model;
using Broiler.Graphics;

namespace Broiler.Documents.Pdf.Images.Tests;

/// <summary>
/// Covers the composed half of the image boundary: what changes in a read when a
/// caller puts <see cref="JpegStreamFilter"/> into the service graph, and what
/// stays exactly as it was when they do not.
/// </summary>
/// <remarks>
/// The pairing is the point. Extension points §5.6 asks that both paths be
/// covered, because the not-composed diagnostic is part of the contract too — a
/// change that quietly started decoding by default would break the promise that
/// a build composing no image decoder links no image decoder, and only a test
/// that reads the same document both ways would notice.
/// </remarks>
public sealed class PdfComposedImageTests
{
    private static PdfReadResult Read(byte[] pdf, bool composed, DocumentResourcePolicy? policy = null)
    {
        PdfCodecServices services = composed
            ? PdfCodecServices.Base.WithStreamFilters(new JpegStreamFilter())
            : PdfCodecServices.Base;

        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec(services).ReadPdf(
            stream,
            policy is null ? null : new PdfReadOptions(resourcePolicy: policy));
    }

    private static DocumentDiagnostic Only(PdfReadResult result, string code) =>
        Assert.Single(result.Diagnostics.Where(d => d.Code == code));

    [Fact]
    public void Without_The_Filter_The_Image_Is_Reported_As_An_Unsupported_Tuple()
    {
        PdfReadResult result = Read(DocumentWithJpeg(32, 32), composed: false);

        DocumentDiagnostic skipped = Only(result, PdfDiagnosticCodes.FilterDctUnsupported);
        Assert.Contains("composes no image decoder", skipped.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ImageDecodedNotProjected);
    }

    [Fact]
    public void With_The_Filter_The_Same_Image_Is_Decoded()
    {
        PdfReadResult result = Read(DocumentWithJpeg(32, 32), composed: true);

        InlineImage image = Assert.Single(ImagesIn(result));
        Assert.Equal(BImagePayloadKind.Decoded, image.Resource.Kind);
        Assert.Equal(32, image.Resource.PixelWidth);
        Assert.Equal(32, image.Resource.PixelHeight);

        // The tuple diagnostic is gone: nothing was refused.
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.FilterDctUnsupported);
    }

    [Fact]
    public void The_Decoded_Image_Reaches_The_Document_Under_A_Policy()
    {
        // The inverse of what this used to assert. Decoding is still not
        // extraction — a policy decides that — but the default read policy
        // permits it, so the samples now arrive in the model instead of stopping
        // at the filter pipeline.
        PdfReadResult result = Read(DocumentWithJpeg(32, 32), composed: true);

        Assert.Single(ImagesIn(result));
        Assert.DoesNotContain(
            result.Diagnostics,
            d => d.Code == PdfDiagnosticCodes.ImageDecodedNotProjected);

        // The entry the picture was admitted under is in the result's context,
        // and it grants reading rather than writing.
        DocumentResourceEntry entry = Assert.Single(result.Resources.Entries);
        Assert.True(entry.Allows(DocumentResourceOperations.ExtractToModel));
        Assert.False(entry.Allows(DocumentResourceOperations.ByteTransfer));
    }

    [Fact]
    public void A_Policy_That_Refuses_Extraction_Keeps_The_Image_Out()
    {
        PdfReadResult result = Read(
            DocumentWithJpeg(32, 32),
            composed: true,
            policy: DocumentResourcePolicy.DenyAll);

        Assert.Empty(ImagesIn(result));
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ImageExtractionDenied);

        // A refusal is not the same as this build being unable to carry it.
        Assert.DoesNotContain(
            result.Diagnostics,
            d => d.Code == PdfDiagnosticCodes.ImageDecodedNotProjected);
    }

    [Fact]
    public void A_Dictionary_That_Disagrees_With_Its_Own_Samples_Is_Reported()
    {
        // The dictionary says 8x8; the JPEG inside says 32x32. Only a build that
        // actually decoded can catch a document contradicting itself.
        PdfReadResult result = Read(DocumentWithJpeg(32, 32, declaredWidth: 8, declaredHeight: 8), composed: true);

        Assert.Contains(
            "declared a pixel size the decoded samples do not match",
            Only(result, PdfDiagnosticCodes.ImageDecodedNotProjected).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_ColorTransform_Of_One_Is_Read_And_The_Image_Decodes()
    {
        PdfReadResult result = Read(
            DocumentWithJpeg(32, 32, decodeParms: "<< /ColorTransform 1 >>"),
            composed: true);

        // What the decode is for: the samples reach the document rather than
        // stopping at the filter pipeline.
        Assert.Single(ImagesIn(result));
        Assert.DoesNotContain(
            result.Diagnostics,
            d => d.Code == PdfDiagnosticCodes.ImageDecodedNotProjected);
    }

    [Fact]
    public void A_ColorTransform_Of_Zero_Is_Refused_As_A_Decoder_Limit()
    {
        PdfReadResult result = Read(
            DocumentWithJpeg(32, 32, decodeParms: "<< /ColorTransform 0 >>"),
            composed: true);

        DocumentDiagnostic refused = Only(result, PdfDiagnosticCodes.FilterDctUnsupported);
        Assert.Contains("already RGB", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ImageDecodedNotProjected);
    }

    [Fact]
    public void An_Adobe_Marker_And_A_Parameter_That_Disagree_Are_Reported_As_Uncertain()
    {
        // The marker says YCbCr, the stream dictionary says no transform. Which of
        // the two an implementation prefers is not settled, and the difference
        // shows up as wrong colour rather than as an error, so the document is
        // told it contradicts itself instead of being silently resolved.
        byte[] jpeg = JpegStreamFilterTests.WithAdobeMarker(JpegStreamFilterTests.Jpeg(32, 32), transform: 1);

        PdfReadResult result = Read(
            DocumentWithJpeg(32, 32, decodeParms: "<< /ColorTransform 0 >>", jpeg: jpeg),
            composed: true);

        DocumentDiagnostic uncertain = Only(result, PdfDiagnosticCodes.FilterDctColorTransformUncertain);
        Assert.Contains("contradicts itself", uncertain.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ImageDecodedNotProjected);
    }

    [Fact]
    public void An_Adobe_Marker_And_A_Parameter_That_Agree_Decode()
    {
        byte[] jpeg = JpegStreamFilterTests.WithAdobeMarker(JpegStreamFilterTests.Jpeg(32, 32), transform: 1);

        PdfReadResult result = Read(
            DocumentWithJpeg(32, 32, decodeParms: "<< /ColorTransform 1 >>", jpeg: jpeg),
            composed: true);

        // What the decode is for: the samples reach the document rather than
        // stopping at the filter pipeline.
        Assert.Single(ImagesIn(result));
        Assert.DoesNotContain(
            result.Diagnostics,
            d => d.Code == PdfDiagnosticCodes.ImageDecodedNotProjected);
    }

    [Fact]
    public void Composing_The_Filter_Does_Not_Disturb_The_Text()
    {
        foreach (bool composed in new[] { false, true })
        {
            PdfReadResult result = Read(DocumentWithJpeg(32, 32), composed);
            Assert.Contains("Body text", result.Document.PlainText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_Corrupt_Jpeg_Costs_The_Image_And_Not_The_Document()
    {
        byte[] jpeg = JpegStreamFilterTests.Jpeg(32, 32);

        // Keep the headers, wreck the scan data. The decoder is the component its
        // own review calls security-sensitive; a fault inside it must surface as
        // a skipped image, never as an exception out of ReadPdf.
        for (int i = jpeg.Length / 2; i < jpeg.Length; i++)
            jpeg[i] ^= 0x5A;

        PdfReadResult result = Read(DocumentWithJpeg(32, 32, jpeg: jpeg), composed: true);

        Assert.Contains("Body text", result.Document.PlainText, StringComparison.Ordinal);
        Assert.Contains(
            result.Diagnostics,
            d => d.Code is PdfDiagnosticCodes.FilterMalformed or PdfDiagnosticCodes.ImageDecodedNotProjected);
    }

    // ---- fixtures -------------------------------------------------------------

    /// <summary>
    /// A one-page document that shows some text and draws one JPEG image XObject.
    /// </summary>
    internal static byte[] DocumentWithJpeg(
        int width,
        int height,
        int? declaredWidth = null,
        int? declaredHeight = null,
        string? decodeParms = null,
        byte[]? jpeg = null)
    {
        jpeg ??= JpegStreamFilterTests.Jpeg(width, height);

        var objects = new List<byte[]>();
        string parms = decodeParms is null ? string.Empty : $" /DecodeParms {decodeParms}";

        int image = Add(objects, Stream(
            $"/Type /XObject /Subtype /Image /Width {declaredWidth ?? width} /Height {declaredHeight ?? height} " +
            $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode{parms}",
            jpeg));

        return Assemble(objects, image);
    }

    /// <summary>
    /// The same one page of text and one image XObject, for a caller that wants to
    /// write the image dictionary itself.
    /// </summary>
    internal static byte[] DocumentWithImage(string imageDictionary, byte[] imageData)
    {
        var objects = new List<byte[]>();
        return Assemble(objects, Add(objects, Stream(imageDictionary, imageData)));
    }

    private static byte[] Assemble(List<byte[]> objects, int image)
    {
        int font = Add(objects, Latin1("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
        int content = Add(objects, Stream(
            string.Empty,
            Latin1("BT /F1 12 Tf 1 0 0 1 72 720 Tm (Body text) Tj ET\nq 100 0 0 100 72 500 cm /Im0 Do Q\n")));

        int pages = Add(objects, []);
        int page = Add(objects, Latin1(
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
            $"/Resources << /Font << /F1 {font} 0 R >> /XObject << /Im0 {image} 0 R >> >> /Contents {content} 0 R >>"));
        int catalog = Add(objects, []);

        objects[pages - 1] = Latin1($"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        objects[catalog - 1] = Latin1($"<< /Type /Catalog /Pages {pages} 0 R >>");

        return Build(objects, catalog);
    }

    private static int Add(List<byte[]> objects, byte[] body)
    {
        objects.Add(body);
        return objects.Count;
    }

    private static byte[] Stream(string dictionary, byte[] data)
    {
        var bytes = new List<byte>();
        bytes.AddRange(Latin1($"<< {dictionary} /Length {data.Length} >>\nstream\n"));
        bytes.AddRange(data);
        bytes.AddRange(Latin1("\nendstream"));
        return bytes.ToArray();
    }

    private static byte[] Build(List<byte[]> objects, int rootObject)
    {
        var output = new MemoryStream();
        Append(output, "%PDF-1.7\n");

        var offsets = new long[objects.Count + 1];
        for (int i = 1; i <= objects.Count; i++)
        {
            offsets[i] = output.Length;
            Append(output, $"{i} 0 obj\n");
            output.Write(objects[i - 1], 0, objects[i - 1].Length);
            Append(output, "\nendobj\n");
        }

        long xref = output.Length;
        Append(output, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        for (int i = 1; i <= objects.Count; i++)
            Append(output, $"{offsets[i]:D10} 00000 n \n");

        Append(output, $"trailer\n<< /Size {objects.Count + 1} /Root {rootObject} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    private static void Append(MemoryStream stream, string text)
    {
        byte[] bytes = Latin1(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static byte[] Latin1(string text)
    {
        var bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++)
            bytes[i] = (byte)text[i];
        return bytes;
    }

    /// <summary>
    /// The images a read carried into the document, in reading order.
    /// </summary>
    private static List<InlineImage> ImagesIn(PdfReadResult result)
    {
        var images = new List<InlineImage>();
        foreach (RichTextParagraph paragraph in result.Document.Paragraphs)
        {
            foreach (StyleRun run in paragraph.Runs)
            {
                if (run.Style.Image is InlineImage image)
                    images.Add(image);
            }
        }

        return images;
    }
}
