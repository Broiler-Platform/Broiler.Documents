namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Covers the Info-and-XMP reconciliation the roadmap specifies in §6.2: XMP wins
/// for a field it supplies, Info is the fallback, and a disagreement is reported
/// rather than resolved in silence.
/// </summary>
/// <remarks>
/// Both halves of the boundary are covered, as
/// <see href="../../../docs/pdf-extension-points.md">extension points §5.6</see>
/// requires — the path where a packet is present and read, and the paths where
/// there is none or it cannot be used. The second is the one that would rot
/// unnoticed: a reader that quietly stopped falling back to Info would still pass
/// every test that only exercises a good packet.
/// </remarks>
public sealed class PdfXmpMetadataTests
{
    private static PdfReadResult Read(byte[] pdf, PdfReadOptions? options = null)
    {
        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec().ReadPdf(stream, options);
    }

    private static DocumentDiagnostic Only(PdfReadResult result, string code) =>
        Assert.Single(result.Diagnostics.Where(d => d.Code == code));

    // ---- XMP supplies the normalized fields -----------------------------------

    [Fact]
    public void An_Xmp_Packet_Supplies_Metadata_With_No_Info_Dictionary_At_All()
    {
        PdfReadResult result = Read(Document(
            Packet(
                """
                <dc:title><rdf:Alt><rdf:li xml:lang="x-default">Only In XMP</rdf:li></rdf:Alt></dc:title>
                <dc:creator><rdf:Seq><rdf:li>Ada Lovelace</rdf:li><rdf:li>Grace Hopper</rdf:li></rdf:Seq></dc:creator>
                <dc:subject><rdf:Bag><rdf:li>alpha</rdf:li><rdf:li>beta</rdf:li></rdf:Bag></dc:subject>
                <xmp:CreatorTool>Broiler.Writer</xmp:CreatorTool>
                <pdf:Producer>Broiler.Documents.Pdf</pdf:Producer>
                """),
            info: null));

        PdfDocumentMetadata metadata = result.Metadata;
        Assert.Equal("Only In XMP", metadata.Title);
        Assert.Equal(["Ada Lovelace", "Grace Hopper"], metadata.Authors);
        Assert.Equal(["alpha", "beta"], metadata.Keywords);
        Assert.Equal("Broiler.Writer", metadata.CreatorApplication);
        Assert.Equal("Broiler.Documents.Pdf", metadata.Producer);
    }

    [Fact]
    public void Xmp_Wins_For_A_Field_Both_Sources_Supply()
    {
        PdfReadResult result = Read(Document(
            Packet("<dc:title>From XMP</dc:title>"),
            info: "/Title (From Info)"));

        Assert.Equal("From XMP", result.Metadata.Title);
    }

    [Fact]
    public void Info_Is_The_Fallback_For_A_Field_Xmp_Does_Not_Mention()
    {
        PdfReadResult result = Read(Document(
            Packet("<dc:title>From XMP</dc:title>"),
            info: "/Title (From Info) /Producer (Only In Info) /Subject (Also Only In Info)"));

        Assert.Equal("From XMP", result.Metadata.Title);
        Assert.Equal("Only In Info", result.Metadata.Producer);
        Assert.Equal("Also Only In Info", result.Metadata.Subject);
    }

    [Fact]
    public void A_Timestamp_Keeps_Whether_Xmp_Stated_An_Offset()
    {
        PdfReadResult zoned = Read(Document(
            Packet("<xmp:CreateDate>2026-09-01T09:30:00Z</xmp:CreateDate>"),
            info: null));

        PdfReadResult local = Read(Document(
            Packet("<xmp:CreateDate>2026-09-01T09:30:00</xmp:CreateDate>"),
            info: null));

        Assert.True(zoned.Metadata.CreationDate?.HasUtcOffset);
        Assert.False(local.Metadata.CreationDate?.HasUtcOffset);
    }

    // ---- disagreement ---------------------------------------------------------

    [Fact]
    public void A_Disagreement_Is_Reported_By_Field_Name_And_Never_By_Value()
    {
        PdfReadResult result = Read(Document(
            Packet(
                """
                <dc:title>Redacted Merger Terms</dc:title>
                <pdf:Producer>Producer From XMP</pdf:Producer>
                """),
            info: "/Title (Board Pack Confidential) /Producer (Producer From Info)"));

        DocumentDiagnostic conflict = Only(result, PdfDiagnosticCodes.MetadataConflict);

        Assert.Contains("title", conflict.Message, StringComparison.Ordinal);
        Assert.Contains("producer", conflict.Message, StringComparison.Ordinal);

        // The reason the code exists at all: a caller has to be told the two
        // sources disagree without the diagnostic itself becoming a place the
        // values leak to (ADR 0009).
        Assert.All(
            result.Diagnostics,
            d =>
            {
                Assert.DoesNotContain("Redacted Merger Terms", d.Message, StringComparison.Ordinal);
                Assert.DoesNotContain("Board Pack Confidential", d.Message, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void Two_Sources_That_Agree_Are_Not_A_Disagreement()
    {
        PdfReadResult result = Read(Document(
            Packet("<dc:title>The Same Title</dc:title>"),
            info: "/Title (The Same Title)"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.MetadataConflict);
    }

    [Fact]
    public void Silence_From_Xmp_Is_Not_A_Disagreement()
    {
        PdfReadResult result = Read(Document(
            Packet("<dc:title>From XMP</dc:title>"),
            info: "/Producer (Only In Info)"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.MetadataConflict);
    }

    // ---- the packet is read, and then dropped ---------------------------------

    [Fact]
    public void Reading_A_Packet_Is_Reported_Without_Making_The_Read_Partial()
    {
        PdfReadResult result = Read(Document(Packet("<dc:title>Fine</dc:title>"), info: null));

        DocumentDiagnostic raw = Only(result, PdfDiagnosticCodes.MetadataRawDropped);

        // Consuming a packet is not skipping a construct. Before IP-004 cleared,
        // every document carrying XMP came back Partial for that reason alone.
        Assert.Equal(DocumentDiagnosticSeverity.Info, raw.Severity);
        Assert.Equal(DocumentResultStatus.Success, result.Status);
    }

    [Fact]
    public void The_Raw_Packet_Is_Read_For_The_Allowlist_And_Then_Dropped()
    {
        DocumentDiagnostic raw = Only(
            Read(Document(
                Packet(
                    """
                    <dc:title>Kept</dc:title>
                    <dc:rights><rdf:Alt><rdf:li xml:lang="x-default">All rights reserved</rdf:li></rdf:Alt></dc:rights>
                    <dc:format>application/pdf</dc:format>
                    """),
                info: null)),
            PdfDiagnosticCodes.MetadataRawDropped);

        Assert.Contains("raw packet was then dropped", raw.Message, StringComparison.Ordinal);
        Assert.Contains("1 normalized field came from it", raw.Message, StringComparison.Ordinal);
        Assert.Contains("2 properties outside the allowlist were ignored", raw.Message, StringComparison.Ordinal);
    }

    // ---- the paths where there is no usable packet ----------------------------

    [Fact]
    public void A_Document_With_No_Packet_Reports_Nothing_About_Xmp()
    {
        PdfReadResult result = Read(Document(xmp: null, info: "/Title (Info Only)"));

        Assert.Equal("Info Only", result.Metadata.Title);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.MetadataRawDropped);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.MetadataXmpUnusable);
    }

    [Fact]
    public void A_Malformed_Packet_Falls_Back_To_Info_And_Says_Why()
    {
        PdfReadResult result = Read(Document(
            "<x:xmpmeta><rdf:RDF></x:xmpmeta>",
            info: "/Title (From Info)"));

        Assert.Equal("From Info", result.Metadata.Title);

        DocumentDiagnostic unusable = Only(result, PdfDiagnosticCodes.MetadataXmpUnusable);
        Assert.Contains("not well-formed RDF/XML", unusable.Message, StringComparison.Ordinal);
        Assert.Contains("came from Info alone", unusable.Message, StringComparison.Ordinal);
        Assert.Equal(DocumentDiagnosticSeverity.Warning, unusable.Severity);
    }

    [Fact]
    public void A_Packet_Over_The_Byte_Ceiling_Is_Refused_Without_Parsing()
    {
        PdfReadResult result = Read(
            Document(Packet("<dc:title>Would Have Been Read</dc:title>"), info: "/Title (From Info)"),
            new PdfReadOptions(pdfLimits: new PdfLimits(maxXmpBytes: 64)));

        Assert.Equal("From Info", result.Metadata.Title);
        Assert.Contains(
            "larger than the XMP byte ceiling",
            Only(result, PdfDiagnosticCodes.MetadataXmpUnusable).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_Metadata_Entry_That_Is_Not_A_Stream_Is_Reported_As_Unusable()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int content = builder.AddStream(string.Empty, PdfFileBuilder.ShowText("Body"));
        int notAStream = builder.AddObject("<< /Type /Metadata >>");

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R /Metadata {notAStream} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, Page(pages, font, content));

        DocumentDiagnostic unusable = Only(Read(builder.Build(catalog)), PdfDiagnosticCodes.MetadataXmpUnusable);

        Assert.Contains("is not a stream", unusable.Message, StringComparison.Ordinal);
    }

    // ---- fixtures -------------------------------------------------------------

    private static string Page(int pages, int font, int content) =>
        $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
        $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>";

    /// <summary>One page of text, optionally carrying an XMP packet and an Info dictionary.</summary>
    private static byte[] Document(string? xmp, string? info)
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int content = builder.AddStream(string.Empty, PdfFileBuilder.ShowText("Body"));

        string metadata = xmp is null
            ? string.Empty
            : $" /Metadata {builder.AddStream("/Type /Metadata /Subtype /XML", xmp)} 0 R";

        string trailer = info is null
            ? string.Empty
            : $"/Info {builder.AddObject($"<< {info} >>")} 0 R";

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R{metadata} >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, Page(pages, font, content));

        return builder.Build(catalog, trailer.Length == 0 ? null : trailer);
    }

    /// <summary>A packet whose single Description holds <paramref name="properties"/>.</summary>
    private static string Packet(string properties) =>
        $"""
        <?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="{XmpReader.RdfNamespace}">
            <rdf:Description rdf:about=""
                xmlns:dc="{XmpReader.DublinCoreNamespace}"
                xmlns:xmp="{XmpReader.XmpBasicNamespace}"
                xmlns:pdf="{XmpReader.AdobePdfNamespace}">
              {properties}
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        <?xpacket end="w"?>
        """;
}
