using System;
using System.IO;
using System.Linq;

namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// ODT as a consumer of the shared metadata envelope (PDF roadmap §6.2): what
/// <c>meta.xml</c> carries into it and back out.
/// </summary>
public sealed class OdtMetadataTests
{
    private static readonly DocumentMetadata Stated = new(
        title: "Quarterly report",
        authors: ["Ada Lovelace"],
        subject: "Results",
        keywords: ["finance", "q3"],
        language: "en-GB",
        producer: "Broiler.Documents.Odt",
        creationDate: DocumentDate.WithOffset(
            new DateTimeOffset(2026, 9, 4, 8, 30, 0, TimeSpan.FromHours(2))),
        modificationDate: DocumentDate.WithoutOffset(new DateTime(2026, 9, 4, 11, 0, 0)));

    private static DocumentMetadata RoundTrip(DocumentMetadata metadata)
    {
        byte[] bytes = OdtDocumentCodec.WriteToArray(
            RichTextDocument.FromPlainText("body"),
            new DocumentWriteOptions(metadata: metadata));

        using var stream = new MemoryStream(bytes, writable: false);
        return new OdtDocumentCodec().Read(stream).Metadata;
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
        DocumentMetadata read = RoundTrip(Stated);

        Assert.True(read.CreationDate!.Value.HasUtcOffset);
        Assert.False(read.ModificationDate!.Value.HasUtcOffset);
    }

    [Fact]
    public void The_Creation_And_Modification_Dates_Are_Not_Swapped()
    {
        // ODF puts the creation time in meta:creation-date and the modification
        // time in dc:date, which is the opposite of what the element names
        // suggest to anyone reading dc:date as "the date". Reading the pair the
        // wrong way round populates both fields plausibly and gets both wrong,
        // so it is worth an assertion that would catch the swap.
        DocumentMetadata read = RoundTrip(Stated);

        Assert.Equal(new DateTime(2026, 9, 4, 8, 30, 0), read.CreationDate!.Value.Value.DateTime);
        Assert.Equal(new DateTime(2026, 9, 4, 11, 0, 0), read.ModificationDate!.Value.Value.DateTime);
    }

    [Fact]
    public void The_Author_Comes_From_Initial_Creator_Not_Dc_Creator()
    {
        // ODF's dc:creator is the last person to modify the document, not its
        // author. Reading it as the author would credit an editor.
        DocumentMetadata read = RoundTrip(Stated);

        Assert.Equal(["Ada Lovelace"], read.Authors);
    }

    [Fact]
    public void Authors_Past_The_First_Are_Reported_Rather_Than_Run_Together()
    {
        // meta:initial-creator holds one name. Joining several into it would
        // assert that one person is called "Ada Lovelace; Grace Hopper".
        using var stream = new MemoryStream();
        DocumentWriteResult result = new OdtDocumentCodec().Write(
            RichTextDocument.FromPlainText("body"),
            stream,
            new DocumentWriteOptions(metadata: new DocumentMetadata(
                authors: ["Ada Lovelace", "Grace Hopper"])));

        // Narrowed, not dropped: the first author does reach the file, and a
        // reader of the output cannot tell that from a document that only ever
        // had one author. That is exactly the case silence hides.
        DocumentDiagnostic narrowed = Assert.Single(
            result.Diagnostics,
            d => d.Code == DocumentDiagnosticCodes.MetadataNarrowed);
        Assert.Contains("Authors", narrowed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            result.Diagnostics,
            d => d.Code == DocumentDiagnosticCodes.MetadataDropped);

        stream.Position = 0;
        Assert.Equal(["Ada Lovelace"], new OdtDocumentCodec().Read(stream).Metadata.Authors);
    }

    [Fact]
    public void Reading_A_Document_Does_Not_Supply_The_Next_Write()
    {
        byte[] source = OdtDocumentCodec.WriteToArray(
            RichTextDocument.FromPlainText("body"),
            new DocumentWriteOptions(metadata: Stated));

        using var reading = new MemoryStream(source, writable: false);
        DocumentReadResult read = new OdtDocumentCodec().Read(reading);
        Assert.Equal("Quarterly report", read.Metadata.Title);

        byte[] rewritten = OdtDocumentCodec.WriteToArray(read.Document);
        using var rereading = new MemoryStream(rewritten, writable: false);
        DocumentMetadata rewrittenMetadata = new OdtDocumentCodec().Read(rereading).Metadata;

        Assert.Null(rewrittenMetadata.Title);
        Assert.Empty(rewrittenMetadata.Authors);
        Assert.Null(rewrittenMetadata.CreationDate);
    }

    [Fact]
    public void A_Write_Without_Metadata_Still_Names_This_Writer()
    {
        // The generator is the one field ODT states unasked: a package that names
        // no producer at all is less useful than one that names what made it, and
        // naming this writer asserts nothing about the document's author.
        byte[] bytes = OdtDocumentCodec.WriteToArray(RichTextDocument.FromPlainText("body"));

        using var stream = new MemoryStream(bytes, writable: false);
        DocumentMetadata read = new OdtDocumentCodec().Read(stream).Metadata;

        Assert.Equal(OdtMetadata.DefaultGenerator, read.Producer);
        Assert.Null(read.Title);
        Assert.Empty(read.Authors);
    }

    [Fact]
    public void A_Package_With_Metadata_Still_Reads_Its_Document()
    {
        byte[] bytes = OdtDocumentCodec.WriteToArray(
            RichTextDocument.FromPlainText("body"),
            new DocumentWriteOptions(metadata: Stated));

        using var stream = new MemoryStream(bytes, writable: false);
        DocumentReadResult read = new OdtDocumentCodec().Read(stream);

        Assert.Equal(DocumentResultStatus.Success, read.Status);
        Assert.Equal("body", read.Document.Paragraphs.Single().Text);
    }
}
