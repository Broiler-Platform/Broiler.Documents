using System.IO;
using Broiler.Documents.Csv;
using Broiler.Documents.Model;

namespace Broiler.Documents.Cli.Composition;

/// <summary>
/// Applies this command's CSV settings only to CSV. Catalog probing and other
/// codecs still receive shared options; request limits and cancellation survive.
/// Explicit typed options take precedence, including the roundtrip reread dialect.
/// </summary>
internal sealed class ConfiguredCsvCodec(CsvReadOptions read, CsvWriteOptions write)
    : DocumentCodec(new CsvDocumentCodec().Descriptor)
{
    private readonly CsvDocumentCodec _inner = new();

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override DocumentProbeResult Probe(DocumentProbeRequest request) => _inner.Probe(request);

    public override DocumentReadResult Read(Stream source, DocumentReadOptions? options = null) =>
        _inner.Read(source, ReadOptions(options));

    public override DocumentReadResult Read(DocumentReadRequest request) =>
        _inner.Read(new DocumentReadRequest(request.Input, ReadOptions(request.Options), request.CancellationToken, request.Hints));

    public override DocumentWriteResult Write(RichTextDocument document, Stream destination, DocumentWriteOptions? options = null) =>
        _inner.Write(document, destination, WriteOptions(options));

    public override DocumentWriteResult Write(DocumentWriteRequest request) =>
        _inner.Write(new DocumentWriteRequest(request.Document, request.Destination,
            WriteOptions(request.Options), request.CancellationToken, request.RequireAtomic, request.IsStaged));

    private DocumentReadOptions ReadOptions(DocumentReadOptions? options) =>
        options is null || options.GetType() == typeof(DocumentReadOptions)
            ? new CsvReadOptions(options?.Limits, read.Delimiter, read.HasHeaderRow)
            : options;

    private DocumentWriteOptions WriteOptions(DocumentWriteOptions? options) =>
        options is null || options.GetType() == typeof(DocumentWriteOptions)
            ? new CsvWriteOptions(write.Delimiter, write.ByteOrderMark, write.EscapeFormulas, options?.Metadata)
            : options;
}
