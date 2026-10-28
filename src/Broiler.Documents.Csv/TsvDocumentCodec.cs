using System;
using System.IO;
using System.Text;
using System.Threading;
using Broiler.Documents.Model;

namespace Broiler.Documents.Csv;

/// <summary>
/// The TSV codec: tab-separated values, read into a table in the rich-text
/// model and written back out from one (ADR 0016).
/// </summary>
/// <remarks>
/// Per the IANA <c>text/tab-separated-values</c> registration, fields are
/// separated by tabs, records by line breaks, and quotes are treated as plain text
/// rather than quoting or escaping syntax.
/// </remarks>
public sealed class TsvDocumentCodec : DocumentCodec
{
    private const string TextTsv = "text/tab-separated-values";

    public TsvDocumentCodec() : base(new DocumentFormatDescriptor("TSV", [TextTsv], [".tsv", ".tab"])) { }

    public override bool CanRead => true;

    public override bool CanWrite => true;

    public override DocumentProbeResult Probe(DocumentProbeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        DocumentSourceHints hints = request.Hints;
        bool hinted = Descriptor.MatchesExtension(GetExtension(hints.FileName)) ||
            Descriptor.MatchesMimeType(WithoutParameters(hints.MimeType));
        if (!hinted)
            return DocumentProbeResult.NoMatch();

        if (PrefixText(request.Prefix.Span) is not string text)
            return DocumentProbeResult.NoMatch("Named as TSV, but the content is not text.");

        return CsvSniffer.LooksTabularWith(WithoutPartialLastLine(text), '\t')
            ? DocumentProbeResult.Match(
                DocumentProbeConfidence.High,
                Descriptor.Name,
                TextTsv,
                diagnostic: "Named as TSV, and the content reads as a tab-separated grid.")
            : DocumentProbeResult.Match(
                DocumentProbeConfidence.Medium,
                Descriptor.Name,
                TextTsv,
                diagnostic: "Named as TSV; TSV has no content signature, and this prefix does not read as a tab-separated grid.");
    }

    public override DocumentReadResult Read(Stream source, DocumentReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= CsvReadOptions.Default;
        if (!TryValidateOptions(options, Name, out CsvReadOptions? _, out DocumentReadResult? rejection))
            return rejection!;

        return ReadCore(source, CsvReadOptions.From(options), CancellationToken.None);
    }

    public override DocumentReadResult Read(DocumentReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryValidateOptions(request.Options, Name, out CsvReadOptions? _, out DocumentReadResult? rejection))
            return rejection!;

        if (request.CancellationToken.IsCancellationRequested)
            return DocumentReadResult.Rejected(DocumentDiagnosticCodes.Cancelled, "The read was cancelled before it began.");

        CsvReadOptions baseOptions = CsvReadOptions.From(request.Options);
        bool hasHeader = baseOptions.HasHeaderRow;
        if (MediaTypeHeader.TryGetParameter(request.Hints.MimeType, "header", out string? headerVal))
        {
            if (string.Equals(headerVal, "present", StringComparison.OrdinalIgnoreCase))
                hasHeader = true;
        }

        Encoding? explicitEncoding = null;
        if (MediaTypeHeader.TryGetParameter(request.Hints.MimeType, "charset", out string? charsetVal))
        {
            explicitEncoding = MediaTypeHeader.ResolveEncoding(charsetVal);
        }

        CsvReadOptions effective = new(baseOptions.Limits, '\t', hasHeader);

        try
        {
            using Stream source = request.Input.OpenStream();
            return ReadCore(source, effective, request.CancellationToken, explicitEncoding);
        }
        catch (OperationCanceledException)
        {
            return DocumentReadResult.Rejected(DocumentDiagnosticCodes.Cancelled,
                "The read was cancelled before it produced a usable document.");
        }
    }

    public override DocumentWriteResult Write(
        RichTextDocument document,
        Stream destination,
        DocumentWriteOptions? options = null)
    {
        options ??= CsvWriteOptions.Default;
        if (!TryValidateOptions(options, Name, out CsvWriteOptions? _, out DocumentWriteResult? rejection))
            return rejection!;

        return TsvWriter.Write(document, destination, CsvWriteOptions.From(options));
    }

    public override DocumentWriteResult Write(DocumentWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryValidateOptions(request.Options, Name, out CsvWriteOptions? _, out DocumentWriteResult? rejection))
            return rejection!;

        if (request.CancellationToken.IsCancellationRequested)
        {
            return DocumentWriteResult.Rejected(DocumentDiagnosticCodes.Cancelled,
                "The write was cancelled before any byte reached the destination.");
        }

        if (request.RequireAtomic && !request.IsStaged)
        {
            return DocumentWriteResult.Rejected(
                DocumentDiagnosticCodes.CommitAtomicRequired,
                "The write request requires atomic commit, but the destination has no staging capability.");
        }

        try
        {
            return TsvWriter.Write(
                request.Document,
                request.Destination,
                CsvWriteOptions.From(request.Options),
                request.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            return DocumentWriteResult.Rejected(DocumentDiagnosticCodes.Cancelled,
                "The write was cancelled before any byte reached the destination.");
        }
    }

    private static TsvReadResult ReadCore(
        Stream source,
        CsvReadOptions options,
        CancellationToken cancellationToken,
        Encoding? explicitEncoding = null)
    {
        (byte[] bytes, bool truncated) = ReadAllBytes(source, options.Limits.MaxDocumentBytes, cancellationToken);
        return TsvReader.Read(bytes, truncated, options, cancellationToken, explicitEncoding);
    }

    private static (byte[] Bytes, bool Truncated) ReadAllBytes(Stream source, long maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        long total = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int read = source.Read(chunk, 0, chunk.Length);
            if (read == 0)
                return (buffer.ToArray(), false);

            if (total + read > maxBytes)
            {
                int allowed = (int)Math.Max(0, maxBytes - total);
                buffer.Write(chunk, 0, allowed);
                return (buffer.ToArray(), true);
            }

            buffer.Write(chunk, 0, read);
            total += read;
        }
    }

    private static string? PrefixText(ReadOnlySpan<byte> prefix)
    {
        if (CsvTextDecoder.ByteOrderMark(prefix) is (Encoding encoding, int length))
            return CsvTextDecoder.Lenient(encoding).GetString(prefix[length..]);

        if (prefix.Contains((byte)0))
            return null;

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetString(prefix);
    }

    private static string WithoutPartialLastLine(string text)
    {
        int last = text.LastIndexOfAny(['\r', '\n']);
        return last < 0 ? text : text[..(last + 1)];
    }

    private static string? WithoutParameters(string? mimeType)
    {
        if (mimeType is null)
            return null;

        int semicolon = mimeType.IndexOf(';', StringComparison.Ordinal);
        return semicolon < 0 ? mimeType : mimeType[..semicolon];
    }

    private static string? GetExtension(string? fileName) =>
        string.IsNullOrEmpty(fileName) ? null : Path.GetExtension(fileName);
}
