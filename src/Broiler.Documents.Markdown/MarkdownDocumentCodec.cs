using System;
using System.IO;
using System.Text;
using Broiler.Documents.Model;

namespace Broiler.Documents.Markdown;

/// <summary>
/// Markdown codec for a CommonMark-oriented rich-text subset. Markdown is
/// intentionally probed conservatively because plain text is valid Markdown.
/// </summary>
public sealed class MarkdownDocumentCodec : DocumentCodec
{
    private const string TextMarkdown = "text/markdown";

    public MarkdownDocumentCodec()
        : base(new DocumentFormatDescriptor(
            "Markdown",
            new[] { TextMarkdown, "text/x-markdown" },
            new[] { ".md", ".markdown" }))
    {
    }

    public override bool CanRead => true;

    public override bool CanWrite => true;

    public override DocumentProbeResult Probe(DocumentProbeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ReadOnlySpan<byte> span = request.Prefix.Span;
        int start = SkipBomAndWhitespace(span);
        string prefix = DecodeUtf8Prefix(span[start..]);

        if (StartsWithMarkdownBlock(prefix))
        {
            return DocumentProbeResult.Match(
                DocumentProbeConfidence.Medium,
                Descriptor.Name,
                TextMarkdown,
                start,
                "Matched a Markdown block marker.");
        }

        if (ContainsMarkdownInline(prefix))
        {
            return DocumentProbeResult.Match(
                DocumentProbeConfidence.Low,
                Descriptor.Name,
                TextMarkdown,
                diagnostic: "Matched Markdown inline syntax; plain text remains ambiguous.");
        }

        DocumentSourceHints hints = request.Hints;
        if (Descriptor.MatchesExtension(GetExtension(hints.FileName)) ||
            Descriptor.MatchesMimeType(hints.MimeType))
        {
            return DocumentProbeResult.Match(
                DocumentProbeConfidence.Low,
                Descriptor.Name,
                TextMarkdown,
                diagnostic: "Matched by filename/MIME hint; Markdown has no mandatory content signature.");
        }

        return DocumentProbeResult.NoMatch();
    }

    public override DocumentReadResult Read(Stream source, DocumentReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        DocumentReadOptions effective = options ?? DocumentReadOptions.Default;
        MarkdownReadInput input = ReadAllBytes(source, effective.Limits.MaxDocumentBytes);
        return MarkdownReader.Read(input.Bytes, effective, input.Truncated);
    }

    public override DocumentWriteResult Write(
        RichTextDocument document,
        Stream destination,
        DocumentWriteOptions? options = null) =>
        MarkdownWriter.Write(document, destination, options);

    public static byte[] WriteToArray(RichTextDocument document, DocumentWriteOptions? options = null) =>
        MarkdownWriter.WriteToArray(document, options);

    private static MarkdownReadInput ReadAllBytes(Stream source, long maxBytes)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        long total = 0;

        while (true)
        {
            int read = source.Read(chunk, 0, chunk.Length);
            if (read == 0)
                return new MarkdownReadInput(buffer.ToArray(), Truncated: false);

            if (total + read > maxBytes)
            {
                int allowed = (int)Math.Max(0, maxBytes - total);
                if (allowed > 0)
                    buffer.Write(chunk, 0, allowed);
                return new MarkdownReadInput(buffer.ToArray(), Truncated: true);
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

        while (i < span.Length && (span[i] == (byte)' ' || span[i] == (byte)'\t' || span[i] == (byte)'\r' || span[i] == (byte)'\n'))
            i++;

        return i;
    }

    private static string DecodeUtf8Prefix(ReadOnlySpan<byte> bytes)
    {
        int length = Math.Min(bytes.Length, 1024);
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
            .GetString(bytes[..length])
            .ToLowerInvariant();
    }

    private static bool StartsWithMarkdownBlock(string prefix)
    {
        if (prefix.Length == 0)
            return false;

        string firstLine = prefix.Split('\n', 2)[0].TrimEnd('\r');
        string trimmed = firstLine.TrimStart();
        if (trimmed.StartsWith("# ", StringComparison.Ordinal) ||
            trimmed.StartsWith("## ", StringComparison.Ordinal) ||
            trimmed.StartsWith("### ", StringComparison.Ordinal) ||
            trimmed.StartsWith("> ", StringComparison.Ordinal) ||
            trimmed.StartsWith("```", StringComparison.Ordinal) ||
            trimmed.StartsWith("~~~", StringComparison.Ordinal) ||
            trimmed.StartsWith("- ", StringComparison.Ordinal) ||
            trimmed.StartsWith("* ", StringComparison.Ordinal) ||
            trimmed.StartsWith("+ ", StringComparison.Ordinal))
        {
            return true;
        }

        return StartsWithOrderedList(trimmed);
    }

    private static bool StartsWithOrderedList(string line)
    {
        int i = 0;
        while (i < line.Length && char.IsDigit(line[i]))
            i++;

        return i > 0 && i + 1 < line.Length && (line[i] == '.' || line[i] == ')') && line[i + 1] == ' ';
    }

    private static bool ContainsMarkdownInline(string prefix) =>
        prefix.Contains("**", StringComparison.Ordinal) ||
        prefix.Contains("__", StringComparison.Ordinal) ||
        prefix.Contains("[", StringComparison.Ordinal) && prefix.Contains("](", StringComparison.Ordinal) ||
        prefix.Contains("`", StringComparison.Ordinal);

    private static string? GetExtension(string? fileName) =>
        string.IsNullOrEmpty(fileName) ? null : Path.GetExtension(fileName);

    private readonly record struct MarkdownReadInput(byte[] Bytes, bool Truncated);
}
