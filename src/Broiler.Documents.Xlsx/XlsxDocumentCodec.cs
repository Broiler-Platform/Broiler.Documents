using System;
using System.IO;
using Broiler.Documents.Model;

namespace Broiler.Documents.Xlsx;

/// <summary>
/// Document codec for Open XML SpreadsheetML (.xlsx) spreadsheet workbooks.
/// </summary>
public sealed class XlsxDocumentCodec : DocumentCodec
{
    private static readonly byte[] ZipLocalHeader = [0x50, 0x4B, 0x03, 0x04];

    /// <summary>The canonical Open XML SpreadsheetML package MIME type.</summary>
    public const string PackageContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Initializes a new instance of <see cref="XlsxDocumentCodec"/>.</summary>
    public XlsxDocumentCodec() : base(new DocumentFormatDescriptor("XLSX", [PackageContentType], [".xlsx", ".xlsm", ".xltx"]))
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
        bool hasXlsxHint = Descriptor.MatchesExtension(GetExtension(hints.FileName)) || Descriptor.MatchesMimeType(hints.MimeType);

        if (StartsWith(span, ZipLocalHeader))
        {
            ZipPrefixInfo info = InspectZipPrefix(span);
            if (info.HasWorkbook)
            {
                return DocumentProbeResult.Match(
                    DocumentProbeConfidence.High,
                    Descriptor.Name,
                    PackageContentType,
                    info.BytesConsumed,
                    "Matched an XLSX xl/workbook.xml package part.");
            }

            if (hasXlsxHint)
            {
                return DocumentProbeResult.Match(
                    DocumentProbeConfidence.High,
                    Descriptor.Name,
                    PackageContentType,
                    4,
                    "Matched ZIP package signature with XLSX filename/MIME hint.");
            }

            return DocumentProbeResult.NoMatch("ZIP package signature was present, but no XLSX hint or SpreadsheetML part was found.");
        }

        if (hasXlsxHint)
        {
            return DocumentProbeResult.Match(
                DocumentProbeConfidence.Low,
                Descriptor.Name,
                PackageContentType,
                diagnostic: "Matched by filename/MIME hint; no ZIP package signature was present.");
        }

        return DocumentProbeResult.NoMatch();
    }

    /// <inheritdoc/>
    public override DocumentReadResult Read(Stream source, DocumentReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        DocumentReadOptions effective = options ?? DocumentReadOptions.Default;
        return XlsxReader.Read(source, effective);
    }

    /// <inheritdoc/>
    public override DocumentWriteResult Write(RichTextDocument document, Stream destination, DocumentWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);
        var request = new DocumentWriteRequest(document, destination, options ?? DocumentWriteOptions.Default);
        return XlsxWriter.Write(request);
    }

    private static ZipPrefixInfo InspectZipPrefix(ReadOnlySpan<byte> span)
    {
        bool hasWorkbook = false;
        int offset = 0;
        int consumed = 0;

        while (offset + 30 <= span.Length && StartsWith(span[offset..], ZipLocalHeader))
        {
            ushort fileNameLength = ReadUInt16(span, offset + 26);
            ushort extraLength = ReadUInt16(span, offset + 28);
            int nameOffset = offset + 30;
            int nameEnd = nameOffset + fileNameLength;
            if (nameEnd > span.Length)
                break;

            string name = DecodeAsciiName(span[nameOffset..nameEnd]);
            if (name.Equals("xl/workbook.xml", StringComparison.OrdinalIgnoreCase))
                hasWorkbook = true;

            consumed = nameEnd;
            uint compressedSize = ReadUInt32(span, offset + 18);
            if ((ReadUInt16(span, offset + 6) & 0x0008) != 0)
                break;

            long next = (long)nameOffset + fileNameLength + extraLength + compressedSize;
            if (next <= offset || next > int.MaxValue)
                break;
            offset = (int)next;
        }

        return new ZipPrefixInfo(hasWorkbook, consumed);
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

    private readonly record struct ZipPrefixInfo(bool HasWorkbook, int BytesConsumed);
}
