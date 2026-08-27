using System;
using System.Collections.Generic;
using System.IO;
using Broiler.Documents.Model;

namespace Broiler.Documents.Docx;

/// <summary>
/// DOCX codec for Open XML WordprocessingML packages. The first implementation
/// handles the text, paragraph, list, hyperlink, and direct-formatting subset
/// represented by <see cref="RichTextDocument"/>.
/// </summary>
public sealed class DocxDocumentCodec : DocumentCodec
{
    private static readonly byte[] ZipLocalHeader = [0x50, 0x4B, 0x03, 0x04];

    public DocxDocumentCodec()
        : base(new DocumentFormatDescriptor(
            "DOCX",
            new[] { DocxNamespaces.PackageContentType },
            new[] { ".docx" }))
    {
    }

    public override bool CanRead => true;

    public override bool CanWrite => true;

    public override DocumentProbeResult Probe(DocumentProbeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ReadOnlySpan<byte> span = request.Prefix.Span;
        DocumentSourceHints hints = request.Hints;
        bool hasDocxHint = Descriptor.MatchesExtension(GetExtension(hints.FileName)) ||
            Descriptor.MatchesMimeType(hints.MimeType);

        if (StartsWith(span, ZipLocalHeader))
        {
            ZipPrefixInfo info = InspectZipPrefix(span);
            if (info.HasWordDocument)
            {
                return DocumentProbeResult.Match(
                    DocumentProbeConfidence.High,
                    Descriptor.Name,
                    DocxNamespaces.PackageContentType,
                    info.BytesConsumed,
                    "Matched a DOCX word/document.xml package part.");
            }

            if (hasDocxHint)
            {
                return DocumentProbeResult.Match(
                    DocumentProbeConfidence.High,
                    Descriptor.Name,
                    DocxNamespaces.PackageContentType,
                    4,
                    "Matched ZIP package signature with DOCX filename/MIME hint.");
            }

            return DocumentProbeResult.NoMatch("ZIP package signature was present, but no DOCX hint or WordprocessingML part was found.");
        }

        if (hasDocxHint)
        {
            return DocumentProbeResult.Match(
                DocumentProbeConfidence.Low,
                Descriptor.Name,
                DocxNamespaces.PackageContentType,
                diagnostic: "Matched by filename/MIME hint; no ZIP package signature was present.");
        }

        return DocumentProbeResult.NoMatch();
    }

    public override DocumentReadResult Read(Stream source, DocumentReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        DocumentReadOptions effective = options ?? DocumentReadOptions.Default;
        DocxReadInput input = ReadAllBytes(source, effective.Limits.MaxDocumentBytes);
        if (input.Truncated)
        {
            return new DocumentReadResult(
                RichTextDocument.Empty,
                new[]
                {
                    DocumentDiagnostic.Error(
                        "docx.limit.bytes",
                        "DOCX input exceeded MaxDocumentBytes and was not parsed."),
                },
                DocumentResultStatus.Rejected);
        }

        return DocxReader.Read(input.Bytes, effective);
    }

    public override DocumentWriteResult Write(
        RichTextDocument document,
        Stream destination,
        DocumentWriteOptions? options = null) =>
        DocxWriter.Write(document, destination, options);

    public static byte[] WriteToArray(RichTextDocument document, DocumentWriteOptions? options = null) =>
        DocxWriter.WriteToArray(document, options);

    private static DocxReadInput ReadAllBytes(Stream source, long maxBytes)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        long total = 0;

        while (true)
        {
            int read = source.Read(chunk, 0, chunk.Length);
            if (read == 0)
                return new DocxReadInput(buffer.ToArray(), Truncated: false);

            if (total + read > maxBytes)
            {
                int allowed = (int)Math.Max(0, maxBytes - total);
                if (allowed > 0)
                    buffer.Write(chunk, 0, allowed);
                return new DocxReadInput(buffer.ToArray(), Truncated: true);
            }

            buffer.Write(chunk, 0, read);
            total += read;
        }
    }

    private static ZipPrefixInfo InspectZipPrefix(ReadOnlySpan<byte> span)
    {
        bool hasWordDocument = false;
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
            if (name.Equals("word/document.xml", StringComparison.OrdinalIgnoreCase))
                hasWordDocument = true;

            consumed = nameEnd;
            uint compressedSize = ReadUInt32(span, offset + 18);
            if ((ReadUInt16(span, offset + 6) & 0x0008) != 0)
                break;

            long next = (long)nameOffset + fileNameLength + extraLength + compressedSize;
            if (next <= offset || next > int.MaxValue)
                break;
            offset = (int)next;
        }

        return new ZipPrefixInfo(hasWordDocument, consumed);
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

    private readonly record struct DocxReadInput(byte[] Bytes, bool Truncated);

    private readonly record struct ZipPrefixInfo(bool HasWordDocument, int BytesConsumed);
}
