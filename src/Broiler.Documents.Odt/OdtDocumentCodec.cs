using System;
using System.IO;
using System.Text;
using Broiler.Documents.Model;

namespace Broiler.Documents.Odt;

/// <summary>
/// ODT codec for OASIS OpenDocument text packages (ODF 1.0 through 1.3,
/// ISO/IEC 26300). It handles the text, paragraph, list, table, hyperlink,
/// style, and picture subset represented by <see cref="RichTextDocument"/>.
/// </summary>
public sealed class OdtDocumentCodec : DocumentCodec
{
    private static readonly byte[] ZipLocalHeader = [0x50, 0x4B, 0x03, 0x04];

    public OdtDocumentCodec()
        : base(new DocumentFormatDescriptor(
            "ODT",
            new[] { OdtNamespaces.PackageMediaType },
            new[] { ".odt" }))
    {
    }

    public override bool CanRead => true;

    public override bool CanWrite => true;

    /// <summary>
    /// Judges a byte prefix. ODF makes this unusually cheap: the first entry of
    /// the package is a stored, uncompressed <c>mimetype</c> holding the media
    /// type in plain text, which is exactly what part 2 section 3.3 put it there
    /// for. The filename and MIME hints only matter for a package that does not
    /// have it.
    /// </summary>
    public override DocumentProbeResult Probe(DocumentProbeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ReadOnlySpan<byte> span = request.Prefix.Span;
        DocumentSourceHints hints = request.Hints;
        bool hasOdtHint = Descriptor.MatchesExtension(GetExtension(hints.FileName)) ||
            Descriptor.MatchesMimeType(hints.MimeType);

        if (!StartsWith(span, ZipLocalHeader))
        {
            if (hasOdtHint)
            {
                return DocumentProbeResult.Match(
                    DocumentProbeConfidence.Low,
                    Descriptor.Name,
                    OdtNamespaces.PackageMediaType,
                    diagnostic: "Matched by filename/MIME hint; no ZIP package signature was present.");
            }

            return DocumentProbeResult.NoMatch();
        }

        ZipPrefixInfo info = InspectZipPrefix(span);

        if (info.MediaType is not null)
        {
            if (info.MediaType.Equals(OdtNamespaces.PackageMediaType, StringComparison.Ordinal))
            {
                return DocumentProbeResult.Match(
                    DocumentProbeConfidence.Certain,
                    Descriptor.Name,
                    OdtNamespaces.PackageMediaType,
                    info.BytesConsumed,
                    "Matched the OpenDocument text mimetype entry.");
            }

            if (info.MediaType.Equals(OdtNamespaces.TemplateMediaType, StringComparison.Ordinal))
            {
                return DocumentProbeResult.Match(
                    DocumentProbeConfidence.High,
                    Descriptor.Name,
                    OdtNamespaces.TemplateMediaType,
                    info.BytesConsumed,
                    "Matched an OpenDocument text template, which holds the same body a document does.");
            }

            // Another OpenDocument type: a spreadsheet, a presentation, a drawing.
            // The package says what it is, so there is nothing to claim here.
            return DocumentProbeResult.NoMatch(
                "The package declared a non-text OpenDocument media type.");
        }

        if (info.HasContentPart && info.HasManifest)
        {
            return DocumentProbeResult.Match(
                DocumentProbeConfidence.High,
                Descriptor.Name,
                OdtNamespaces.PackageMediaType,
                info.BytesConsumed,
                "Matched an OpenDocument package layout with no mimetype entry.");
        }

        if (hasOdtHint)
        {
            return DocumentProbeResult.Match(
                DocumentProbeConfidence.High,
                Descriptor.Name,
                OdtNamespaces.PackageMediaType,
                4,
                "Matched ZIP package signature with ODT filename/MIME hint.");
        }

        return DocumentProbeResult.NoMatch(
            "ZIP package signature was present, but no ODT hint or OpenDocument package evidence was found.");
    }

    public override DocumentReadResult Read(Stream source, DocumentReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        DocumentReadOptions effective = options ?? DocumentReadOptions.Default;
        OdtReadInput input = ReadAllBytes(source, effective.Limits.MaxDocumentBytes);
        if (input.Truncated)
        {
            return new DocumentReadResult(
                RichTextDocument.Empty,
                new[]
                {
                    DocumentDiagnostic.Error(
                        "odt.limit.bytes",
                        "ODT input exceeded MaxDocumentBytes and was not parsed."),
                },
                DocumentResultStatus.Rejected);
        }

        return OdtReader.Read(input.Bytes, effective);
    }

    public override DocumentWriteResult Write(
        RichTextDocument document,
        Stream destination,
        DocumentWriteOptions? options = null) =>
        OdtWriter.Write(document, destination, options);

    public static byte[] WriteToArray(RichTextDocument document, DocumentWriteOptions? options = null) =>
        OdtWriter.WriteToArray(document, options);

    private static OdtReadInput ReadAllBytes(Stream source, long maxBytes)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        long total = 0;

        while (true)
        {
            int read = source.Read(chunk, 0, chunk.Length);
            if (read == 0)
                return new OdtReadInput(buffer.ToArray(), Truncated: false);

            if (total + read > maxBytes)
            {
                int allowed = (int)Math.Max(0, maxBytes - total);
                if (allowed > 0)
                    buffer.Write(chunk, 0, allowed);
                return new OdtReadInput(buffer.ToArray(), Truncated: true);
            }

            buffer.Write(chunk, 0, read);
            total += read;
        }
    }

    /// <summary>
    /// Walks the local file headers in the prefix, reading the media type out of
    /// a leading stored <c>mimetype</c> entry and noting the package parts that
    /// identify an OpenDocument package without one.
    /// </summary>
    private static ZipPrefixInfo InspectZipPrefix(ReadOnlySpan<byte> span)
    {
        string? mediaType = null;
        bool hasContentPart = false;
        bool hasManifest = false;
        int offset = 0;
        int consumed = 0;
        bool first = true;

        while (offset + 30 <= span.Length && StartsWith(span[offset..], ZipLocalHeader))
        {
            ushort flags = ReadUInt16(span, offset + 6);
            ushort method = ReadUInt16(span, offset + 8);
            uint compressedSize = ReadUInt32(span, offset + 18);
            ushort fileNameLength = ReadUInt16(span, offset + 26);
            ushort extraLength = ReadUInt16(span, offset + 28);
            int nameOffset = offset + 30;
            int nameEnd = nameOffset + fileNameLength;
            if (nameEnd > span.Length)
                break;

            string name = DecodeAsciiName(span[nameOffset..nameEnd]);
            consumed = nameEnd;

            if (name.Equals(OdtNamespaces.ContentPart, StringComparison.OrdinalIgnoreCase))
                hasContentPart = true;
            else if (name.Equals(OdtNamespaces.ManifestPart, StringComparison.OrdinalIgnoreCase))
                hasManifest = true;

            int dataOffset = nameEnd + extraLength;
            if (first &&
                name.Equals(OdtNamespaces.MimeTypePart, StringComparison.Ordinal) &&
                method == 0 &&
                compressedSize is > 0 and <= 128 &&
                dataOffset + (int)compressedSize <= span.Length)
            {
                mediaType = Encoding.ASCII.GetString(span.Slice(dataOffset, (int)compressedSize)).Trim();
                consumed = dataOffset + (int)compressedSize;
            }

            first = false;

            // Bit 3 puts the sizes in a trailing data descriptor, so the next
            // header cannot be found by arithmetic. The prefix has given up what
            // it can by this point.
            if ((flags & 0x0008) != 0)
                break;

            long next = (long)dataOffset + compressedSize;
            if (next <= offset || next > int.MaxValue)
                break;
            offset = (int)next;
        }

        return new ZipPrefixInfo(mediaType, hasContentPart, hasManifest, consumed);
    }

    private static bool StartsWith(ReadOnlySpan<byte> span, ReadOnlySpan<byte> pattern) =>
        span.Length >= pattern.Length && span[..pattern.Length].SequenceEqual(pattern);

    private static ushort ReadUInt16(ReadOnlySpan<byte> span, int offset) =>
        offset + 2 <= span.Length
            ? (ushort)(span[offset] | (span[offset + 1] << 8))
            : (ushort)0;

    private static uint ReadUInt32(ReadOnlySpan<byte> span, int offset) =>
        offset + 4 <= span.Length
            ? (uint)(span[offset] | (span[offset + 1] << 8) | (span[offset + 2] << 16) | (span[offset + 3] << 24))
            : 0;

    private static string DecodeAsciiName(ReadOnlySpan<byte> bytes)
    {
        char[] chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            byte value = bytes[i];
            chars[i] = value <= 0x7F ? (char)value : '?';
        }

        return new string(chars).Replace('\\', '/');
    }

    private static string? GetExtension(string? fileName) =>
        string.IsNullOrEmpty(fileName) ? null : Path.GetExtension(fileName);

    private readonly record struct OdtReadInput(byte[] Bytes, bool Truncated);

    private readonly record struct ZipPrefixInfo(
        string? MediaType,
        bool HasContentPart,
        bool HasManifest,
        int BytesConsumed);
}
