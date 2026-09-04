using System;
using System.IO;
using System.Threading;
using Broiler.Documents.Model;
using Broiler.Documents.Pdf.Writing;

namespace Broiler.Documents.Pdf;

/// <summary>
/// The PDF document codec: logical text import from ISO 32000-1 files, and
/// deterministic export to new PDF 1.7 files.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this codec claims.</b> Import is <em>logical extraction</em>: it
/// recovers text, basic styling, and admitted links from the constructs listed in
/// the feature matrix. It is not a renderer, a sanitizer, a redaction engine, or
/// an archival converter, and a successful read is not evidence that anything was
/// removed from a document. Export writes new files from the rich-text model; it
/// is not a layout-preserving round trip, and it never edits an input in place.
/// </para>
/// <para>
/// <b>What the base build carries.</b> Only what this repository implements
/// itself: PDF syntax and object stores, classic and stream cross-references,
/// object streams, the Flate, ASCIIHex, ASCII85, and RunLength filters, simple
/// and composite font mappings through encodings and <c>ToUnicode</c>, and a
/// writer that uses the fourteen standard font names with no embedded program.
/// There is no third-party runtime dependency and no bundled font, glyph list, or
/// codec asset.
/// </para>
/// <para>
/// <b>What arrives later, and how.</b> LZW, DCT/JPEG, CCITT, JPX, JBIG2,
/// embedded font programs, image extraction, and encryption are each detected and
/// skipped with their own stable diagnostic, and each becomes available by
/// composing a reviewed implementation into <see cref="PdfCodecServices"/>. The
/// parser, interpreter, and writer do not change when one arrives; only the
/// service graph does.
/// </para>
/// <para>
/// <b>Delivery state.</b> The package is not published. It is registered — for
/// opening only — by the Windows and Linux Writer composition roots, which is the
/// read-preview integration candidate the roadmap allows so integration checks
/// can run; no other application registers it, no head registers it for saving,
/// and the shared Writer core does not reference it, so no head acquires it
/// transitively. Advertising a PDF capability to users still waits on the preview
/// gates in the PDF support roadmap and on the clearance rows in the IP/licensing
/// register.
/// </para>
/// </remarks>
public sealed class PdfDocumentCodec : DocumentCodec
{
    private const string ApplicationPdf = "application/pdf";
    private static readonly byte[] Signature = "%PDF-"u8.ToArray();

    private readonly PdfCodecServices _services;

    /// <summary>Creates a codec with the base service graph.</summary>
    public PdfDocumentCodec()
        : this(PdfCodecServices.Base)
    {
    }

    /// <summary>Creates a codec over an explicitly composed service graph.</summary>
    public PdfDocumentCodec(PdfCodecServices services)
        : base(new DocumentFormatDescriptor("PDF", [ApplicationPdf], [".pdf"]))
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>The services this instance was composed with.</summary>
    public PdfCodecServices Services => _services;

    public override bool CanRead => true;

    public override bool CanWrite => true;

    public override DocumentProbeResult Probe(DocumentProbeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ReadOnlySpan<byte> span = request.Prefix.Span;

        // The header need not be at byte zero: a file with a preamble is still a
        // PDF, and the reader resolves offsets relative to wherever it sits.
        int limit = Math.Min(span.Length, 1024);
        for (int i = 0; i + Signature.Length <= limit; i++)
        {
            if (!span[i..].StartsWith(Signature))
                continue;

            DocumentProbeConfidence confidence = i == 0
                ? DocumentProbeConfidence.Certain
                : DocumentProbeConfidence.High;
            string? note = i == 0
                ? null
                : "The PDF header is preceded by other bytes; offsets are resolved relative to the header.";
            return DocumentProbeResult.Match(confidence, Descriptor.Name, ApplicationPdf, i + Signature.Length, note);
        }

        DocumentSourceHints hints = request.Hints;
        if (Descriptor.MatchesExtension(GetExtension(hints.FileName)) ||
            Descriptor.MatchesMimeType(hints.MimeType))
        {
            return DocumentProbeResult.Match(
                DocumentProbeConfidence.Low,
                Descriptor.Name,
                ApplicationPdf,
                diagnostic: "Matched by filename/MIME hint; no PDF header was present in the probed prefix.");
        }

        return DocumentProbeResult.NoMatch();
    }

    /// <summary>
    /// Reads a PDF. Malformed-but-recoverable input produces diagnostics rather
    /// than exceptions; input that cannot yield a usable document produces a
    /// result whose <see cref="PdfReadResult.Status"/> is
    /// <see cref="DocumentResultStatus.Rejected"/> and an empty document that no host
    /// may present.
    /// </summary>
    public override DocumentReadResult Read(Stream source, DocumentReadOptions? options = null) =>
        ReadPdf(source, AsPdfOptions(options));

    /// <summary>
    /// Reads through the request contract, taking cancellation from the request —
    /// its single owner — and probing and reading through the same replayable
    /// input.
    /// </summary>
    public override DocumentReadResult Read(DocumentReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryValidateOptions(request.Options, Name, out PdfReadOptions? typed, out DocumentReadResult? rejection))
            return rejection!;

        PdfReadOptions effective = typed ?? new PdfReadOptions(request.Options.Limits);

        byte[] bytes;
        try
        {
            bytes = request.Input.Materialize(effective.PdfLimits.MaxInputBytes).ToArray();
        }
        catch (DocumentException e)
        {
            return RejectedRead(effective, DocumentDiagnosticCodes.InputTooLarge, e.Message);
        }

        return PdfReader.Read(bytes, effective, _services, request.CancellationToken);
    }

    /// <summary>The typed read, returning the PDF-specific result.</summary>
    public PdfReadResult ReadPdf(
        Stream source,
        PdfReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        PdfReadOptions effective = options ?? PdfReadOptions.Default;

        byte[] bytes;
        try
        {
            bytes = PdfReader.ReadAllBytes(source, effective.PdfLimits.MaxInputBytes);
        }
        catch (PdfLimitExceededException e)
        {
            var sink = new PdfDiagnosticSink(effective.PdfLimits.MaxDiagnostics);
            sink.Error(PdfDiagnosticCodes.Limit, e.Message);
            return new PdfReadResult(
                RichTextDocument.Empty,
                DocumentResultStatus.Rejected,
                DocumentMetadata.Empty,
                Structure.PdfVersion.Unknown,
                0,
                Array.Empty<Structure.PdfExtensionDeclaration>(),
                sink.Build());
        }

        return PdfReader.Read(bytes, effective, _services, cancellationToken);
    }

    /// <summary>
    /// Writes a new PDF 1.7 file. Nothing is written to <paramref name="destination"/>
    /// until layout, policy, and limits have all passed, so a rejected write leaves
    /// the destination untouched.
    /// </summary>
    public override DocumentWriteResult Write(
        RichTextDocument document,
        Stream destination,
        DocumentWriteOptions? options = null) =>
        WritePdf(document, destination, AsPdfOptions(options));

    /// <summary>Writes through the request contract, taking cancellation from the request.</summary>
    public override DocumentWriteResult Write(DocumentWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryValidateOptions(request.Options, Name, out PdfWriteOptions? typed, out DocumentWriteResult? rejection))
            return rejection!;

        return PdfWriter.Write(
            request.Document,
            request.Destination,
            typed ?? PdfWriteOptions.Default,
            _services,
            request.CancellationToken);
    }

    /// <summary>The typed write, returning the PDF-specific result.</summary>
    public PdfWriteResult WritePdf(
        RichTextDocument document,
        Stream destination,
        PdfWriteOptions? options = null,
        CancellationToken cancellationToken = default) =>
        PdfWriter.Write(document, destination, options ?? PdfWriteOptions.Default, _services, cancellationToken);

    private static PdfReadResult RejectedRead(PdfReadOptions options, string code, string message)
    {
        var sink = new PdfDiagnosticSink(options.PdfLimits.MaxDiagnostics);
        sink.Error(code, message);
        return new PdfReadResult(
            RichTextDocument.Empty,
            DocumentResultStatus.Rejected,
            DocumentMetadata.Empty,
            Structure.PdfVersion.Unknown,
            0,
            Array.Empty<Structure.PdfExtensionDeclaration>(),
            sink.Build());
    }

    // A codec validates the option object it was handed rather than downcasting
    // opportunistically: a caller that passes plain DocumentReadOptions gets the
    // shared settings honoured and PDF defaults for the rest, and a caller that
    // passes PdfReadOptions gets exactly those (PDF roadmap §6.1).
    private static PdfReadOptions AsPdfOptions(DocumentReadOptions? options) => options switch
    {
        null => PdfReadOptions.Default,
        PdfReadOptions typed => typed,
        _ => new PdfReadOptions(options.Limits),
    };

    private static PdfWriteOptions AsPdfOptions(DocumentWriteOptions? options) => options switch
    {
        null => PdfWriteOptions.Default,
        PdfWriteOptions typed => typed,
        _ => PdfWriteOptions.Default,
    };

    private static string? GetExtension(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;
        int dot = fileName.LastIndexOf('.');
        return dot < 0 ? null : fileName[dot..];
    }
}
