using System.IO.Compression;
using System.Text;

namespace Broiler.Documents.Pdf.Tests;

public sealed class PdfStructureTests
{
    private static PdfReadResult Read(byte[] pdf, PdfReadOptions? options = null)
    {
        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec().ReadPdf(stream, options);
    }

    [Fact]
    public void Reads_A_Classic_Cross_Reference_Table()
    {
        PdfReadResult result = Read(PdfFileBuilder.SinglePage(PdfFileBuilder.ShowText("Hello")));

        Assert.NotEqual(DocumentResultStatus.Rejected, result.Status);
        Assert.Equal(1, result.PageCount);
        Assert.Contains("Hello", result.Document.PlainText);
    }

    [Fact]
    public void Resolves_Offsets_Relative_To_A_Header_That_Is_Not_At_Byte_Zero()
    {
        var builder = new PdfFileBuilder().WithPreamble("junk bytes before the header\n");
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int content = builder.AddStream(string.Empty, PdfFileBuilder.ShowText("Preamble"));
        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");

        PdfReadResult result = Read(builder.Build(catalog));

        Assert.Contains("Preamble", result.Document.PlainText);
    }

    [Fact]
    public void Rejects_An_Encrypted_Document_Before_Interpreting_Any_Content()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int encrypt = builder.AddObject("<< /Filter /Standard /V 2 /R 3 /Length 128 /P -44 >>");
        int content = builder.AddStream(string.Empty, PdfFileBuilder.ShowText("Secret"));
        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, $"<< /Type /Page /Parent {pages} 0 R /Contents {content} 0 R >>");

        PdfReadResult result = Read(builder.Build(catalog, $"/Encrypt {encrypt} 0 R"));

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.EncryptionUnsupported);
        Assert.Equal(0, result.PageCount);

        // Nothing about the document's content may leak through a rejection.
        Assert.DoesNotContain("Secret", result.Document.PlainText);
        Assert.All(result.Diagnostics, d => Assert.DoesNotContain("Secret", d.Message));
    }

    [Fact]
    public void Reads_A_Cross_Reference_Stream_And_An_Object_Stream()
    {
        byte[] pdf = BuildStreamedFile();
        PdfReadResult result = Read(pdf);

        Assert.NotEqual(DocumentResultStatus.Rejected, result.Status);
        Assert.Equal(1, result.PageCount);
        Assert.Contains("Streamed", result.Document.PlainText);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.XrefRecovered);
    }

    [Fact]
    public void Recovers_A_File_Whose_Cross_Reference_Table_Is_Missing()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int content = builder.AddStream(string.Empty, PdfFileBuilder.ShowText("Recovered"));
        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");

        PdfReadResult result = Read(builder.BuildWithoutXref());

        Assert.Contains("Recovered", result.Document.PlainText);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.XrefRecovered);

        // A recovered document is never reported as a clean parse.
        Assert.Equal(DocumentResultStatus.Partial, result.Status);
    }

    [Fact]
    public void An_Incremental_Update_Yields_The_Latest_Revision_Only()
    {
        byte[] original = PdfFileBuilder.SinglePage(PdfFileBuilder.ShowText("First revision"));
        byte[] updated = AppendRevision(original, PdfFileBuilder.ShowText("Second revision"));

        PdfReadResult result = Read(updated);

        Assert.Contains("Second revision", result.Document.PlainText);
        Assert.DoesNotContain("First revision", result.Document.PlainText);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.RevisionsHistoryDropped);
    }

    [Fact]
    public void Records_A_Pdf_2_Declaration_As_Tolerance_Rather_Than_Conformance()
    {
        var builder = new PdfFileBuilder().WithVersion("2.0");
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int content = builder.AddStream(string.Empty, PdfFileBuilder.ShowText("Tolerated"));
        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");

        PdfReadResult result = Read(builder.Build(catalog));

        Assert.Equal(2, result.DeclaredVersion.Major);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.VersionToleratedNotSupported);
        Assert.Contains("Tolerated", result.Document.PlainText);
    }

    [Fact]
    public void A_Catalog_Version_Overrides_The_Header_Only_Upward()
    {
        byte[] higher = PdfFileBuilder.SinglePage(
            PdfFileBuilder.ShowText("x"),
            extraCatalogEntries: " /Version /1.7 ");
        Assert.Equal(new Structure.PdfVersion(1, 7), Read(higher).DeclaredVersion);

        var builder = new PdfFileBuilder().WithVersion("1.7");
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int content = builder.AddStream(string.Empty, "BT ET");
        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R /Version /1.3 >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, $"<< /Type /Page /Parent {pages} 0 R /Contents {content} 0 R >>");

        Assert.Equal(new Structure.PdfVersion(1, 7), Read(builder.Build(catalog)).DeclaredVersion);
    }

    [Fact]
    public void Inventories_Developer_Extensions_Without_Enabling_Anything()
    {
        byte[] pdf = PdfFileBuilder.SinglePage(
            PdfFileBuilder.ShowText("Extended"),
            extraCatalogEntries: " /Extensions << /ADBE << /BaseVersion /1.7 /ExtensionLevel 3 >> >> ");

        PdfReadResult result = Read(pdf);

        Assert.Single(result.Extensions);
        Assert.Equal("ADBE", result.Extensions[0].Prefix);
        Assert.Equal(3, result.Extensions[0].ExtensionLevel);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ExtensionUnsupported);
    }

    [Fact]
    public void Inherits_Page_Attributes_Down_The_Page_Tree()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int root = builder.Reserve();
        int branch = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int content = builder.AddStream(string.Empty, PdfFileBuilder.ShowText("Inherited"));

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {root} 0 R >>");
        builder.SetObject(root, $"<< /Type /Pages /Kids [{branch} 0 R] /Count 1 /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> >>");
        builder.SetObject(branch, $"<< /Type /Pages /Parent {root} 0 R /Kids [{page} 0 R] /Count 1 >>");
        // The leaf declares neither MediaBox nor Resources; both must be inherited.
        builder.SetObject(page, $"<< /Type /Page /Parent {branch} 0 R /Contents {content} 0 R >>");

        PdfReadResult result = Read(builder.Build(catalog));

        Assert.Equal(1, result.PageCount);
        Assert.Contains("Inherited", result.Document.PlainText);
    }

    [Fact]
    public void A_Page_Tree_Cycle_Terminates_With_A_Diagnostic()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int root = builder.Reserve();
        int branch = builder.Reserve();

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {root} 0 R >>");
        builder.SetObject(root, $"<< /Type /Pages /Kids [{branch} 0 R] /Count 1 >>");
        builder.SetObject(branch, $"<< /Type /Pages /Parent {root} 0 R /Kids [{root} 0 R] /Count 1 >>");

        PdfReadResult result = Read(builder.Build(catalog));

        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ObjectCycle);
    }

    [Fact]
    public void Reads_The_Normalized_Metadata_Allowlist_And_Drops_The_Rest()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int content = builder.AddStream(string.Empty, "BT ET");
        int info = builder.AddObject(
            "<< /Title (A Title) /Author (Ada; Grace) /Subject (Testing) /Keywords (one, two) " +
            "/Creator (Broiler) /Producer (Broiler) /CreationDate (D:20260101120000+02'00') " +
            "/CustomPrivateKey (should not survive) >>");

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R /Lang (en-GB) >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, $"<< /Type /Page /Parent {pages} 0 R /Contents {content} 0 R >>");

        PdfReadResult result = Read(builder.Build(catalog, $"/Info {info} 0 R"));

        Assert.Equal("A Title", result.Metadata.Title);
        Assert.Equal(["Ada", "Grace"], result.Metadata.Authors);
        Assert.Equal(["one", "two"], result.Metadata.Keywords);
        Assert.Equal("en-GB", result.Metadata.Language);
        Assert.True(result.Metadata.CreationDate!.Value.HasUtcOffset);
        Assert.Equal(TimeSpan.FromHours(2), result.Metadata.CreationDate!.Value.Value.Offset);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.MetadataDropped);
    }

    [Fact]
    public void A_Zone_Less_Date_Keeps_Its_Missing_Offset()
    {
        Assert.True(Structure.PdfMetadataReader.TryParseDate("D:20260101120000", out DocumentDate date));
        Assert.False(date.HasUtcOffset);

        Assert.True(Structure.PdfMetadataReader.TryParseDate("D:20260101120000Z", out DocumentDate utc));
        Assert.True(utc.HasUtcOffset);

        Assert.False(Structure.PdfMetadataReader.TryParseDate("D:20261301120000", out _));
        Assert.False(Structure.PdfMetadataReader.TryParseDate("not a date", out _));
    }

    [Fact]
    public void An_Xmp_Packet_Is_Detected_And_Dropped()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int content = builder.AddStream(string.Empty, "BT ET");
        int metadata = builder.AddStream("/Type /Metadata /Subtype /XML", "<?xpacket begin=''?><x:xmpmeta/>");

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R /Metadata {metadata} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, $"<< /Type /Page /Parent {pages} 0 R /Contents {content} 0 R >>");

        PdfReadResult result = Read(builder.Build(catalog));

        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.MetadataRawDropped);
    }

    [Fact]
    public void A_Stream_Whose_Filter_Is_Not_Composed_Is_Skipped_With_Its_Own_Code()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int content = builder.AddStream(string.Empty, "not really fax data", filter: "CCITTFaxDecode");

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");

        PdfReadResult result = Read(builder.Build(catalog));

        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.FilterCcittUnsupported);
        Assert.Equal(DocumentResultStatus.Partial, result.Status);
    }

    [Fact]
    public void Rejects_Input_Larger_Than_The_Configured_Limit()
    {
        byte[] pdf = PdfFileBuilder.SinglePage(PdfFileBuilder.ShowText("Hello"));
        var options = new PdfReadOptions(pdfLimits: new PdfLimits(maxInputBytes: 32));

        PdfReadResult result = Read(pdf, options);

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.Limit);
    }

    [Fact]
    public void Rejects_Input_With_No_Pdf_Header()
    {
        PdfReadResult result = Read(PdfFileBuilder.Latin1("this is not a PDF at all"));

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.HeaderMissing);
    }

    // ---- fixtures -------------------------------------------------------------

    private static byte[] BuildStreamedFile()
    {
        // Objects 1 (catalog), 2 (pages) and 3 (page) live inside object stream 5;
        // object 6 is the cross-reference stream that points at all of them.
        const string Catalog = "<< /Type /Catalog /Pages 2 0 R >>";
        const string Pages = "<< /Type /Pages /Kids [3 0 R] /Count 1 >>";
        const string Page = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
                            "/Resources << /Font << /F1 7 0 R >> >> /Contents 4 0 R >>";

        string[] bodies = [Catalog, Pages, Page];
        var offsets = new StringBuilder();
        var payload = new StringBuilder();
        for (int i = 0; i < bodies.Length; i++)
        {
            offsets.Append(i + 1).Append(' ').Append(payload.Length).Append(' ');
            payload.Append(bodies[i]).Append(' ');
        }

        string header = offsets.ToString();
        byte[] objectStreamData = Deflate(PdfFileBuilder.Latin1(header + payload));

        var output = new MemoryStream();
        Write(output, "%PDF-1.5\n");

        var positions = new Dictionary<int, long>();

        positions[4] = output.Length;
        string content = PdfFileBuilder.ShowText("Streamed");
        Write(output, $"4 0 obj\n<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n");

        positions[7] = output.Length;
        Write(output, "7 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");

        positions[5] = output.Length;
        Write(output, $"5 0 obj\n<< /Type /ObjStm /N {bodies.Length} /First {header.Length} /Filter /FlateDecode /Length {objectStreamData.Length} >>\nstream\n");
        output.Write(objectStreamData, 0, objectStreamData.Length);
        Write(output, "\nendstream\nendobj\n");

        long xrefPosition = output.Length;

        // /W [1 4 2]: one type byte, a four-byte field, and a two-byte field.
        var rows = new List<byte>();
        void Row(byte type, long second, int third)
        {
            rows.Add(type);
            rows.Add((byte)(second >> 24));
            rows.Add((byte)(second >> 16));
            rows.Add((byte)(second >> 8));
            rows.Add((byte)second);
            rows.Add((byte)(third >> 8));
            rows.Add((byte)third);
        }

        Row(0, 0, 65535);                 // object 0, the free head
        Row(2, 5, 0);                     // object 1 in stream 5, index 0
        Row(2, 5, 1);                     // object 2 in stream 5, index 1
        Row(2, 5, 2);                     // object 3 in stream 5, index 2
        Row(1, positions[4], 0);
        Row(1, positions[5], 0);
        Row(1, xrefPosition, 0);          // object 6, this stream
        Row(1, positions[7], 0);

        byte[] xrefData = Deflate(rows.ToArray());
        Write(output, $"6 0 obj\n<< /Type /XRef /Size 8 /W [1 4 2] /Root 1 0 R /Filter /FlateDecode /Length {xrefData.Length} >>\nstream\n");
        output.Write(xrefData, 0, xrefData.Length);
        Write(output, $"\nendstream\nendobj\nstartxref\n{xrefPosition}\n%%EOF\n");

        return output.ToArray();
    }

    private static byte[] AppendRevision(byte[] original, string newContent)
    {
        // A minimal incremental update: redefine the content stream and add a
        // second cross-reference section chained to the first through /Prev.
        string text = Encoding.Latin1.GetString(original);
        int previousXref = int.Parse(
            text[(text.LastIndexOf("startxref", StringComparison.Ordinal) + 9)..]
                .TrimStart()
                .Split('\n')[0]
                .Trim(),
            System.Globalization.CultureInfo.InvariantCulture);

        int size = int.Parse(
            text[(text.LastIndexOf("/Size ", StringComparison.Ordinal) + 6)..].Split(' ')[0],
            System.Globalization.CultureInfo.InvariantCulture);

        int root = int.Parse(
            text[(text.LastIndexOf("/Root ", StringComparison.Ordinal) + 6)..].Split(' ')[0],
            System.Globalization.CultureInfo.InvariantCulture);

        // The single-page fixture puts the content stream in the last object.
        int contentObject = size - 1;

        var output = new MemoryStream();
        output.Write(original, 0, original.Length);

        long objectOffset = output.Length;
        Write(output, $"{contentObject} 0 obj\n<< /Length {newContent.Length} >>\nstream\n{newContent}\nendstream\nendobj\n");

        long xref = output.Length;
        Write(output, $"xref\n{contentObject} 1\n{objectOffset:D10} 00000 n \n");
        Write(output, $"trailer\n<< /Size {size} /Root {root} 0 R /Prev {previousXref} >>\nstartxref\n{xref}\n%%EOF\n");

        return output.ToArray();
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var compressor = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            compressor.Write(data, 0, data.Length);
        return output.ToArray();
    }

    private static void Write(MemoryStream stream, string text)
    {
        byte[] bytes = PdfFileBuilder.Latin1(text);
        stream.Write(bytes, 0, bytes.Length);
    }
}
