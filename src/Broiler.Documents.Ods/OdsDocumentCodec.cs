using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Broiler.Documents.Model;

namespace Broiler.Documents.Ods;

/// <summary>
/// Document codec for OASIS OpenDocument Spreadsheet (.ods) packages.
/// </summary>
public sealed class OdsDocumentCodec : DocumentCodec
{
    private static readonly byte[] ZipLocalHeader = [0x50, 0x4B, 0x03, 0x04];

    /// <summary>The canonical OpenDocument Spreadsheet MIME type.</summary>
    public const string PackageMediaType = "application/vnd.oasis.opendocument.spreadsheet";

    /// <summary>Initializes a new instance of <see cref="OdsDocumentCodec"/>.</summary>
    public OdsDocumentCodec() : base(new DocumentFormatDescriptor("ODS", [PackageMediaType], [".ods", ".ots"]))
    {
    }

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanWrite => true;

    /// <inheritdoc/>
    public override DocumentProbeResult Probe(DocumentProbeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ReadOnlySpan<byte> span = request.Prefix.Span;
        DocumentSourceHints hints = request.Hints;
        string? extension = GetExtension(hints.FileName);
        bool hasOdsHint = Descriptor.MatchesExtension(extension) || Descriptor.MatchesMimeType(hints.MimeType);

        if (!StartsWith(span, ZipLocalHeader))
        {
            if (hasOdsHint)
            {
                return DocumentProbeResult.Match(
                    DocumentProbeConfidence.Low,
                    Descriptor.Name,
                    PackageMediaType,
                    diagnostic: "Matched by filename/MIME hint; no ZIP package signature was present.");
            }

            return DocumentProbeResult.NoMatch();
        }

        // Check if mimetype entry is first entry in ZIP prefix
        ZipPrefixInfo info = InspectZipPrefix(span);
        if (info.MediaType is not null)
        {
            if (info.MediaType.Equals(PackageMediaType, StringComparison.Ordinal))
            {
                return DocumentProbeResult.Match(
                    DocumentProbeConfidence.Certain,
                    Descriptor.Name,
                    PackageMediaType,
                    info.BytesConsumed,
                    "Matched the standard uncompressed ODF mimetype entry.");
            }

            return DocumentProbeResult.NoMatch($"ZIP package mimetype was '{info.MediaType}', not ODS.");
        }

        if (hasOdsHint)
        {
            return DocumentProbeResult.Match(
                DocumentProbeConfidence.High,
                Descriptor.Name,
                PackageMediaType,
                4,
                "Matched ZIP package signature with ODS filename/MIME hint.");
        }

        return DocumentProbeResult.NoMatch("ZIP signature was present, but no ODS hint or mimetype entry was found.");
    }

    /// <inheritdoc/>
    public override DocumentReadResult Read(Stream source, DocumentReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        DocumentReadOptions effective = options ?? DocumentReadOptions.Default;
        return OdsReader.Read(source, effective);
    }

    /// <inheritdoc/>
    public override DocumentWriteResult Write(RichTextDocument document, Stream destination, DocumentWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);
        var request = new DocumentWriteRequest(document, destination, options ?? DocumentWriteOptions.Default);
        return OdsWriter.Write(request);
    }

    private static ZipPrefixInfo InspectZipPrefix(ReadOnlySpan<byte> span)
    {
        int offset = 0;
        string? mediaType = null;
        int consumed = 0;

        while (offset + 30 <= span.Length && StartsWith(span[offset..], ZipLocalHeader))
        {
            ushort method = ReadUInt16(span, offset + 8);
            uint uncompressedSize = ReadUInt32(span, offset + 22);
            ushort fileNameLength = ReadUInt16(span, offset + 26);
            ushort extraLength = ReadUInt16(span, offset + 28);
            int nameOffset = offset + 30;
            int nameEnd = nameOffset + fileNameLength;
            if (nameEnd > span.Length)
                break;

            string name = DecodeAsciiName(span[nameOffset..nameEnd]);
            if (name.Equals("mimetype", StringComparison.OrdinalIgnoreCase))
            {
                int dataOffset = nameEnd + extraLength;
                int dataEnd = dataOffset + (int)uncompressedSize;
                if (method == 0 && dataEnd <= span.Length)
                {
                    mediaType = Encoding.ASCII.GetString(span[dataOffset..dataEnd]).Trim();
                    consumed = dataEnd;
                }
                break;
            }

            consumed = nameEnd;
            uint compressedSize = ReadUInt32(span, offset + 18);
            if ((ReadUInt16(span, offset + 6) & 0x0008) != 0)
                break;

            long next = (long)nameOffset + fileNameLength + extraLength + compressedSize;
            if (next <= offset || next > int.MaxValue)
                break;
            offset = (int)next;
        }

        return new ZipPrefixInfo(mediaType, consumed);
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

    private static string? GetExtension(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        int dot = fileName.LastIndexOf('.');
        return dot >= 0 ? fileName[dot..] : null;
    }

    private readonly record struct ZipPrefixInfo(string? MediaType, int BytesConsumed);
}
