using System;
using System.IO;
using System.Text;
using System.Threading;
using Broiler.Documents.Model;

namespace Broiler.Documents.Csv;

/// <summary>
/// The CSV codec: comma-separated values, read into a table in the rich-text
/// model and written back out from one (ADR 0016).
/// </summary>
/// <remarks>
/// <para>
/// CSV has no signature. Any text with a comma in it is a CSV file of some
/// shape, so content alone would claim every plain-text document - including
/// prose another codec should have had. The probe therefore matches only when
/// the caller's file name or media type says CSV, and uses the content to say
/// how sure it is: a prefix that reads as a grid is a high-confidence match, one
/// that does not is a medium one, and one with NUL bytes in it is not text and
/// not a match. A caller with no hint names the format instead.
/// </para>
/// <para>
/// The confidence for a hinted grid is high on purpose. A spreadsheet's first
/// column often starts with something Markdown reads as a block marker - a
/// numbered "1. Quarter" row - and a file the caller named <c>.csv</c> should not
/// lose to that.
/// </para>
/// </remarks>
public sealed class CsvDocumentCodec : DocumentCodec
{
    private const string TextCsv = "text/csv";

    public CsvDocumentCodec() : base(new DocumentFormatDescriptor("CSV", [TextCsv], [".csv"])) { }

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
            return DocumentProbeResult.NoMatch("Named as CSV, but the content is not text.");

        return CsvSniffer.LooksTabular(WithoutPartialLastLine(text))
            ? DocumentProbeResult.Match(
                DocumentProbeConfidence.High,
                Descriptor.Name,
                TextCsv,
                diagnostic: "Named as CSV, and the content reads as a grid.")
            : DocumentProbeResult.Match(
                DocumentProbeConfidence.Medium,
                Descriptor.Name,
                TextCsv,
                diagnostic: "Named as CSV; CSV has no content signature, and this prefix does not read as a grid.");
    }

    public override DocumentReadResult Read(Stream source, DocumentReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= CsvReadOptions.Default;
        if (!TryValidateOptions(options, Name, out CsvReadOptions? _, out DocumentReadResult? rejection))
            return rejection!;

        return ReadCore(source, CsvReadOptions.From(options), CancellationToken.None);
    }

    /// <summary>
    /// Reads through the request contract, taking hints and cancellation from the request.
    /// </summary>
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

        CsvReadOptions effective = new(baseOptions.Limits, baseOptions.Delimiter, hasHeader);

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

    /// <summary>Reads a CSV file returning a format-specific <see cref="CsvReadResult"/>.</summary>
    public CsvReadResult ReadCsv(
        Stream source,
        CsvReadOptions? options = null,
        CancellationToken cancellationToken = default,
        Encoding? explicitEncoding = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= CsvReadOptions.Default;
        if (!TryValidateOptions(options, Name, out CsvReadOptions? _, out DocumentReadResult? rejection))
            return (rejection as CsvReadResult) ?? new CsvReadResult(RichTextDocument.Empty, ',', null, rejection?.Diagnostics, DocumentResultStatus.Rejected);

        return ReadCore(source, CsvReadOptions.From(options), cancellationToken, explicitEncoding);
    }

    public override DocumentWriteResult Write(
        RichTextDocument document,
        Stream destination,
        DocumentWriteOptions? options = null)
    {
        options ??= CsvWriteOptions.Default;
        if (!TryValidateOptions(options, Name, out CsvWriteOptions? _, out DocumentWriteResult? rejection))
            return rejection!;

        return CsvWriter.Write(document, destination, CsvWriteOptions.From(options));
    }

    /// <summary>Writes through the request contract, taking cancellation from the request.</summary>
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
            return CsvWriter.Write(
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

    /// <summary>Writes <paramref name="document"/> to a new byte array.</summary>
    public static byte[] WriteToArray(RichTextDocument document, CsvWriteOptions? options = null)
    {
        using var stream = new MemoryStream();
        CsvWriter.Write(document, stream, options ?? CsvWriteOptions.Default);
        return stream.ToArray();
    }

    private static CsvReadResult ReadCore(
        Stream source,
        CsvReadOptions options,
        CancellationToken cancellationToken,
        Encoding? explicitEncoding = null)
    {
        (byte[] bytes, bool truncated) = ReadAllBytes(source, options.Limits.MaxDocumentBytes, cancellationToken);
        return CsvReader.Read(bytes, truncated, options, cancellationToken, explicitEncoding);
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

    /// <summary>The prefix as text, or null when it holds bytes no text file would.</summary>
    private static string? PrefixText(ReadOnlySpan<byte> prefix)
    {
        if (CsvTextDecoder.ByteOrderMark(prefix) is (Encoding encoding, int length))
            return CsvTextDecoder.Lenient(encoding).GetString(prefix[length..]);

        if (prefix.Contains((byte)0))
            return null;

        // Only the structure is looked at, and every delimiter, quote and line
        // break is ASCII, so a lenient decode serves whatever the encoding.
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetString(prefix);
    }

    /// <summary>
    /// The text without what follows its last line break. A prefix usually ends
    /// part way through a record, and a cut record would count as a short row.
    /// </summary>
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
