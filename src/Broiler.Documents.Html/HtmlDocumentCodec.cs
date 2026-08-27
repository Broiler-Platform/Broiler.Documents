using System;
using System.IO;
using System.Text;
using Broiler.Documents.Model;

namespace Broiler.Documents.Html;

/// <summary>
/// HTML codec over the Broiler rich-text model. The first subset is deliberately
/// document-fragment shaped: paragraphs, line breaks, common inline formatting,
/// links, and the model's paragraph styling fields.
/// </summary>
public sealed class HtmlDocumentCodec : DocumentCodec
{
    private const string TextHtml = "text/html";

    private static readonly string[] StrongMarkers =
    [
        "<!doctype html",
        "<html",
        "<head",
        "<body",
    ];

    private static readonly string[] FragmentMarkers =
    [
        "<p",
        "<div",
        "<span",
        "<a",
        "<strong",
        "<b",
        "<em",
        "<i",
        "<ul",
        "<ol",
        "<li",
        "<h1",
        "<h2",
        "<h3",
        "<br",
        "<section",
        "<article",
    ];

    public HtmlDocumentCodec()
        : base(new DocumentFormatDescriptor(
            "HTML",
            new[] { TextHtml, "application/xhtml+xml" },
            new[] { ".html", ".htm" }))
    {
    }

    public override bool CanRead => true;

    public override bool CanWrite => true;

    public override DocumentProbeResult Probe(DocumentProbeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ReadOnlySpan<byte> span = request.Prefix.Span;
        int start = SkipBomAndWhitespace(span);
        string prefix = DecodeAsciiPrefix(span[start..]);

        if (StartsWithAny(prefix, StrongMarkers))
        {
            return DocumentProbeResult.Match(
                DocumentProbeConfidence.High,
                Descriptor.Name,
                TextHtml,
                start);
        }

        if (StartsWithAny(prefix, FragmentMarkers))
        {
            return DocumentProbeResult.Match(
                DocumentProbeConfidence.Medium,
                Descriptor.Name,
                TextHtml,
                start,
                "Matched a common HTML fragment element.");
        }

        DocumentSourceHints hints = request.Hints;
        if (Descriptor.MatchesExtension(GetExtension(hints.FileName)) ||
            Descriptor.MatchesMimeType(hints.MimeType))
        {
            return DocumentProbeResult.Match(
                DocumentProbeConfidence.Low,
                Descriptor.Name,
                TextHtml,
                diagnostic: "Matched by filename/MIME hint; no HTML content signature was present.");
        }

        return DocumentProbeResult.NoMatch();
    }

    public override DocumentReadResult Read(Stream source, DocumentReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        DocumentReadOptions effective = options ?? DocumentReadOptions.Default;
        HtmlReadInput input = ReadAllBytes(source, effective.Limits.MaxDocumentBytes);
        return HtmlReader.Read(input.Bytes, effective, input.Truncated);
    }

    public override DocumentWriteResult Write(
        RichTextDocument document,
        Stream destination,
        DocumentWriteOptions? options = null) =>
        HtmlWriter.Write(document, destination, options);

    /// <summary>Serialize to a byte array (convenience over the stream overload).</summary>
    public static byte[] WriteToArray(RichTextDocument document, DocumentWriteOptions? options = null) =>
        HtmlWriter.WriteToArray(document, options);

    private static HtmlReadInput ReadAllBytes(Stream source, long maxBytes)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        long total = 0;

        while (true)
        {
            int read = source.Read(chunk, 0, chunk.Length);
            if (read == 0)
                return new HtmlReadInput(buffer.ToArray(), Truncated: false);

            if (total + read > maxBytes)
            {
                int allowed = (int)Math.Max(0, maxBytes - total);
                if (allowed > 0)
                    buffer.Write(chunk, 0, allowed);
                return new HtmlReadInput(buffer.ToArray(), Truncated: true);
            }

            buffer.Write(chunk, 0, read);
            total += read;
        }
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

    private static string DecodeAsciiPrefix(ReadOnlySpan<byte> bytes)
    {
        int length = Math.Min(bytes.Length, 512);
        char[] chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            byte value = bytes[i];
            chars[i] = char.ToLowerInvariant(value <= 0x7F ? (char)value : '?');
        }

        return new string(chars);
    }

    private static bool StartsWithAny(string value, string[] markers)
    {
        foreach (string marker in markers)
        {
            if (value.StartsWith(marker, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string? GetExtension(string? fileName) =>
        string.IsNullOrEmpty(fileName) ? null : Path.GetExtension(fileName);

    private readonly record struct HtmlReadInput(byte[] Bytes, bool Truncated);
}
