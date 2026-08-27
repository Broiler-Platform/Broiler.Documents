using System;
using System.IO;
using Broiler.Documents.Model;

namespace Broiler.Documents.Rtf;

/// <summary>
/// The RTF codec. Phase 1 delivers format identification (<see cref="Probe"/>) and
/// tokenization (<see cref="RtfTokenizer"/>); the semantic reader (RTF → model)
/// arrives in Phase 2 and the writer (model → RTF) in Phase 3, so
/// <see cref="CanRead"/>/<see cref="CanWrite"/> are currently <see langword="false"/>.
/// </summary>
public sealed class RtfDocumentCodec : DocumentCodec
{
    private static readonly byte[] Signature = "{\\rtf"u8.ToArray();
    private const string ApplicationRtf = "application/rtf";

    public RtfDocumentCodec()
        : base(new DocumentFormatDescriptor(
            "RTF",
            new[] { ApplicationRtf, "text/rtf" },
            new[] { ".rtf" }))
    {
    }

    public override bool CanRead => true;

    public override bool CanWrite => true;

    public override DocumentProbeResult Probe(DocumentProbeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ReadOnlySpan<byte> span = request.Prefix.Span;

        int start = SkipBomAndWhitespace(span);
        if (StartsWith(span, start, Signature))
        {
            int after = start + Signature.Length;
            DocumentProbeConfidence confidence = after < span.Length && span[after] == (byte)'1'
                ? DocumentProbeConfidence.Certain
                : DocumentProbeConfidence.High;
            return DocumentProbeResult.Match(confidence, Descriptor.Name, ApplicationRtf, after);
        }

        // No content signature: fall back to advisory filename/MIME hints only.
        DocumentSourceHints hints = request.Hints;
        if (Descriptor.MatchesExtension(GetExtension(hints.FileName)) ||
            Descriptor.MatchesMimeType(hints.MimeType))
        {
            return DocumentProbeResult.Match(
                DocumentProbeConfidence.Low,
                Descriptor.Name,
                ApplicationRtf,
                diagnostic: "Matched by filename/MIME hint; no RTF content signature was present.");
        }

        return DocumentProbeResult.NoMatch();
    }

    public override DocumentReadResult Read(Stream source, DocumentReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        DocumentReadOptions effective = options ?? DocumentReadOptions.Default;
        byte[] bytes = ReadAllBytes(source, effective.Limits.MaxDocumentBytes);
        return RtfReader.Read(bytes, effective);
    }

    public override DocumentWriteResult Write(
        RichTextDocument document,
        Stream destination,
        DocumentWriteOptions? options = null) =>
        RtfWriter.Write(document, destination, options);

    /// <summary>Tokenize an RTF byte buffer (see <see cref="RtfTokenizer"/>).</summary>
    public static RtfTokenizeResult Tokenize(ReadOnlyMemory<byte> content, DocumentLimits? limits = null) =>
        RtfTokenizer.Tokenize(content, limits);

    // Reads the stream into memory, bounded a little past MaxDocumentBytes so the
    // tokenizer sees the overflow and reports truncation (ADR 0004).
    private static byte[] ReadAllBytes(Stream source, long maxBytes)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        long total = 0;
        int read;
        while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
        {
            buffer.Write(chunk, 0, read);
            total += read;
            if (total > maxBytes)
                break;
        }

        return buffer.ToArray();
    }

    private static int SkipBomAndWhitespace(ReadOnlySpan<byte> span)
    {
        int i = 0;
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
            i = 3;

        while (i < span.Length && IsWhitespace(span[i]))
            i++;

        return i;
    }

    private static bool IsWhitespace(byte b) =>
        b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n';

    private static bool StartsWith(ReadOnlySpan<byte> span, int offset, ReadOnlySpan<byte> pattern)
    {
        if (offset < 0 || offset + pattern.Length > span.Length)
            return false;
        return span.Slice(offset, pattern.Length).SequenceEqual(pattern);
    }

    private static string? GetExtension(string? fileName) =>
        string.IsNullOrEmpty(fileName) ? null : Path.GetExtension(fileName);
}
