using System;
using System.IO;
using System.Linq;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// DOCX as the non-PDF consumer of the shared metadata envelope (PDF roadmap
/// §6.2): what <c>docProps/core.xml</c> and <c>docProps/app.xml</c> carry into it
/// and back out.
/// </summary>
public sealed class DocxMetadataTests
{
    private static readonly DocumentMetadata Stated = new(
        title: "Quarterly report",
        authors: ["Ada Lovelace", "Grace Hopper"],
        subject: "Results",
        keywords: ["finance", "q3"],
        language: "en-GB",
        producer: "Broiler.Documents.Docx",
        creationDate: DocumentDate.WithOffset(
            new DateTimeOffset(2026, 9, 4, 8, 30, 0, TimeSpan.FromHours(2))),
        modificationDate: DocumentDate.WithoutOffset(new DateTime(2026, 9, 4, 11, 0, 0)));

    private static DocumentMetadata RoundTrip(DocumentMetadata metadata)
    {
        byte[] bytes = DocxDocumentCodec.WriteToArray(
            RichTextDocument.FromPlainText("body"),
            new DocumentWriteOptions(metadata: metadata));

        using var stream = new MemoryStream(bytes, writable: false);
        return new DocxDocumentCodec().Read(stream).Metadata;
    }

    [Fact]
    public void Every_Field_The_Format_States_Survives_A_Round_Trip()
    {
        DocumentMetadata read = RoundTrip(Stated);

        Assert.Equal(Stated.Title, read.Title);
        Assert.Equal(Stated.Authors, read.Authors);
        Assert.Equal(Stated.Subject, read.Subject);
        Assert.Equal(Stated.Keywords, read.Keywords);
        Assert.Equal(Stated.Language, read.Language);
        Assert.Equal(Stated.Producer, read.Producer);
        Assert.Equal(Stated.CreationDate, read.CreationDate);
        Assert.Equal(Stated.ModificationDate, read.ModificationDate);
    }

    [Fact]
    public void A_Zone_Less_Timestamp_Comes_Back_Zone_Less()
    {
        // The round trip above already asserts equality, and DocumentDate's
        // equality includes the flag — but this is the property the whole
        // timestamp type exists for, so it is stated where a reader will find it.
        DocumentMetadata read = RoundTrip(Stated);

        Assert.True(read.CreationDate!.Value.HasUtcOffset);
        Assert.False(read.ModificationDate!.Value.HasUtcOffset);
    }

    [Fact]
    public void A_Document_Written_Without_Metadata_States_None()
    {
        byte[] bytes = DocxDocumentCodec.WriteToArray(RichTextDocument.FromPlainText("body"));

        using var stream = new MemoryStream(bytes, writable: false);
        Assert.True(new DocxDocumentCodec().Read(stream).Metadata.IsEmpty);
    }

    [Fact]
    public void Reading_A_Document_Does_Not_Supply_The_Next_Write()
    {
        // §6.2's transfer rule, end to end: a document read with metadata and
        // written straight back out, without the caller passing it, carries none.
        byte[] source = DocxDocumentCodec.WriteToArray(
            RichTextDocument.FromPlainText("body"),
            new DocumentWriteOptions(metadata: Stated));

        using var reading = new MemoryStream(source, writable: false);
        DocumentReadResult read = new DocxDocumentCodec().Read(reading);
        Assert.Equal("Quarterly report", read.Metadata.Title);

        byte[] rewritten = DocxDocumentCodec.WriteToArray(read.Document);
        using var rereading = new MemoryStream(rewritten, writable: false);
        Assert.True(new DocxDocumentCodec().Read(rereading).Metadata.IsEmpty);
    }

    [Fact]
    public void What_OOXML_Cannot_State_Is_Reported_Rather_Than_Folded_In()
    {
        // OOXML names one producing application and no separate authoring one.
        // Writing CreatorApplication into the Application element would assert
        // something the caller did not say.
        var stream = new MemoryStream();
        DocumentWriteResult result = new DocxDocumentCodec().Write(
            RichTextDocument.FromPlainText("body"),
            stream,
            new DocumentWriteOptions(metadata: new DocumentMetadata(
                title: "T",
                creatorApplication: "Some Editor",
                producer: "Broiler")));

        DocumentDiagnostic dropped = Assert.Single(
            result.Diagnostics,
            d => d.Code == DocumentDiagnosticCodes.MetadataDropped);
        Assert.Contains("CreatorApplication", dropped.Message, StringComparison.Ordinal);

        stream.Position = 0;
        DocumentMetadata read = new DocxDocumentCodec().Read(stream).Metadata;
        Assert.Equal("Broiler", read.Producer);
        Assert.Null(read.CreatorApplication);
    }

    [Fact]
    public void A_Write_Reports_The_Fields_That_Reached_The_Package()
    {
        using var stream = new MemoryStream();
        DocumentWriteResult result = new DocxDocumentCodec().Write(
            RichTextDocument.FromPlainText("body"),
            stream,
            new DocumentWriteOptions(metadata: Stated));

        DocumentDiagnostic emitted = Assert.Single(
            result.Diagnostics,
            d => d.Code == DocumentDiagnosticCodes.MetadataEmitted);
        Assert.Contains("Title", emitted.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            result.Diagnostics,
            d => d.Code == DocumentDiagnosticCodes.MetadataDropped);
    }

    [Fact]
    public void A_Package_Without_Property_Parts_Still_Reads()
    {
        // The properties are optional in OPC, and a package that omits them is
        // not malformed. The document has to read either way.
        byte[] bytes = DocxDocumentCodec.WriteToArray(RichTextDocument.FromPlainText("body"));

        using var stream = new MemoryStream(bytes, writable: false);
        DocumentReadResult read = new DocxDocumentCodec().Read(stream);

        Assert.Equal(DocumentResultStatus.Success, read.Status);
        Assert.True(read.Metadata.IsEmpty);
        Assert.Equal("body", read.Document.Paragraphs.Single().Text);
    }
}
