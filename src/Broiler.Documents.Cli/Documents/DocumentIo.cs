using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Broiler.Documents.Cli.Composition;
using Broiler.Documents.Cli.Infrastructure;
using Broiler.Documents.Model;

namespace Broiler.Documents.Cli.Documents;

/// <summary>A document that was read, together with how it was read and what the codec said about it.</summary>
public sealed class LoadedDocument
{
    public LoadedDocument(
        string source,
        DocumentCodec codec,
        DocumentProbeResult probe,
        DocumentReadResult result)
    {
        Source = source;
        Codec = codec;
        Probe = probe;
        Result = result;
    }

    /// <summary>The path this came from, or <c>-</c> for standard input.</summary>
    public string Source { get; }

    public DocumentCodec Codec { get; }

    public DocumentProbeResult Probe { get; }

    public DocumentReadResult Result { get; }

    public RichTextDocument Document => Result.Document;

    public string FormatName => Codec.Descriptor.Name;

    public IReadOnlyList<DocumentDiagnostic> Diagnostics => Result.Diagnostics;

    public DocumentResultStatus Status => Result.Status;
}

/// <summary>Raised when a document could not be read. Carries the exit code the caller should use.</summary>
public sealed class DocumentIoException : Exception
{
    public DocumentIoException(int exitCode, string message)
        : base(message)
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }
}

/// <summary>
/// Reading and writing documents through the catalog, with the file-level
/// concerns - paths, standard streams, format overrides, atomic replacement -
/// kept out of the commands.
/// </summary>
public static class DocumentIo
{
    /// <summary>The token that means standard input or standard output.</summary>
    public const string StandardStreamToken = "-";

    /// <summary>
    /// Reads a document, letting the catalog choose the codec unless
    /// <paramref name="formatOverride"/> names one.
    /// </summary>
    /// <remarks>
    /// The override exists because content probing is a judgement, not an oracle:
    /// a Markdown file with no Markdown syntax in it is indistinguishable from
    /// plain text, and a harness that knows what it wrote should be able to say
    /// so rather than argue with a heuristic. When an override is given the probe
    /// still runs and is still reported, so a mismatch between what the caller
    /// declared and what the bytes look like is visible rather than silent.
    /// </remarks>
    public static LoadedDocument Load(
        string source,
        DocumentCodecCatalog catalog,
        DocumentReadOptions options,
        string? formatOverride = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(options);

        byte[] bytes = ReadAllBytes(source, options.Limits.MaxDocumentBytes);
        var hints = new DocumentSourceHints(
            source == StandardStreamToken ? null : Path.GetFileName(source));

        using DocumentInput input = DocumentInput.FromBytes(bytes);

        if (string.IsNullOrEmpty(formatOverride))
        {
            DocumentCodecSelection selection = catalog.SelectAndRead(input, options, hints);
            if (selection.Match is null)
            {
                throw new DocumentIoException(
                    ExitCode.Read,
                    "No composed codec recognized " + Describe(source) + ". Known formats: " +
                    string.Join(", ", CodecComposition.FormatNames(catalog)) +
                    ". Pass --from to state the format explicitly.");
            }

            return new LoadedDocument(source, selection.Match.Codec, selection.Match.Result, selection.Result);
        }

        DocumentCodec codec = CodecComposition.Resolve(catalog, formatOverride)
            ?? throw new UsageException(
                "Unknown format \"" + formatOverride + "\". Known formats: " +
                string.Join(", ", CodecComposition.FormatNames(catalog)) + ".");

        if (!codec.CanRead)
            throw new DocumentIoException(ExitCode.Read, "The " + codec.Name + " codec does not implement reading.");

        DocumentProbeResult probe = codec.Probe(
            new DocumentProbeRequest(
                input.Peek(options.Limits.MaxProbeBytes),
                hints,
                options.Limits));

        DocumentReadResult result = codec.Read(new DocumentReadRequest(input, options));
        return new LoadedDocument(source, codec, probe, result);
    }

    /// <summary>Reads a document and fails the run when the result is unusable.</summary>
    public static LoadedDocument LoadOrThrow(
        string source,
        DocumentCodecCatalog catalog,
        DocumentReadOptions options,
        string? formatOverride = null)
    {
        LoadedDocument loaded = Load(source, catalog, options, formatOverride);
        if (loaded.Result.Status == DocumentResultStatus.Rejected)
        {
            string reason = loaded.Diagnostics.Count > 0
                ? string.Join("; ", loaded.Diagnostics.Select(diagnostic => diagnostic.Message))
                : "the codec produced no usable document.";
            throw new DocumentIoException(
                ExitCode.Read,
                "The " + loaded.FormatName + " codec rejected " + Describe(source) + ": " + reason);
        }

        return loaded;
    }

    /// <summary>
    /// Chooses the codec to write with: an explicit format name if one was
    /// given, otherwise the destination's file extension.
    /// </summary>
    public static DocumentCodec ResolveWriteCodec(
        DocumentCodecCatalog catalog,
        string destination,
        string? formatOverride)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        string token = formatOverride ?? Path.GetExtension(destination);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new UsageException(
                "Cannot tell what format to write: " + Describe(destination) +
                " has no file extension and --to was not given.");
        }

        DocumentCodec codec = CodecComposition.Resolve(catalog, token)
            ?? throw new UsageException(
                "Unknown output format \"" + token + "\". Known formats: " +
                string.Join(", ", CodecComposition.FormatNames(catalog)) + ".");

        if (!codec.CanWrite)
            throw new UsageException("The " + codec.Name + " codec does not implement writing.");

        return codec;
    }

    /// <summary>
    /// Writes a document, staging the whole output in memory before it replaces
    /// anything on disk.
    /// </summary>
    /// <remarks>
    /// Writing straight to the destination file would leave a truncated document
    /// in place when a codec stops half way, and the result would still be
    /// reported as a rejection with the file already destroyed. Staging keeps the
    /// existing file intact unless a complete replacement exists, which matters
    /// most in the loop this tool is built for: an overnight conversion sweep
    /// that fails on file 400 should not have eaten file 400.
    /// </remarks>
    public static DocumentWriteResult Save(
        RichTextDocument document,
        string destination,
        DocumentCodec codec,
        DocumentWriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(options);

        using var staging = new MemoryStream();
        DocumentWriteResult result = codec.Write(new DocumentWriteRequest(document, staging, options));

        if (result.Status == DocumentResultStatus.Rejected)
            return result;

        WriteAllBytes(destination, staging.ToArray());
        return result;
    }

    /// <summary>Reads a file, or standard input for <c>-</c>, refusing anything over the limit.</summary>
    public static byte[] ReadAllBytes(string source, long maxBytes)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source == StandardStreamToken)
        {
            using Stream input = Console.OpenStandardInput();
            return ReadBounded(input, maxBytes, "standard input");
        }

        if (!File.Exists(source))
            throw new DocumentIoException(ExitCode.Input, "File not found: " + source);

        try
        {
            var info = new FileInfo(source);
            if (info.Length > maxBytes)
            {
                throw new DocumentIoException(
                    ExitCode.Input,
                    Describe(source) + " is " + info.Length + " bytes, over the " + maxBytes +
                    " byte limit. Raise it with --max-bytes.");
            }

            return File.ReadAllBytes(source);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new DocumentIoException(ExitCode.Input, "Cannot read " + source + ": " + exception.Message);
        }
    }

    /// <summary>Writes a file, or standard output for <c>-</c>, creating the directory if needed.</summary>
    public static void WriteAllBytes(string destination, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(bytes);

        if (destination == StandardStreamToken)
        {
            using Stream output = Console.OpenStandardOutput();
            output.Write(bytes, 0, bytes.Length);
            return;
        }

        try
        {
            EnsureDirectory(destination);
            File.WriteAllBytes(destination, bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new DocumentIoException(ExitCode.Input, "Cannot write " + destination + ": " + exception.Message);
        }
    }

    /// <summary>Creates the directory a file is about to be written into.</summary>
    public static void EnsureDirectory(string filePath)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
    }

    private static string Describe(string source) =>
        source == StandardStreamToken ? "standard input" : "\"" + source + "\"";

    private static byte[] ReadBounded(Stream stream, long maxBytes, string what)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        long total = 0;

        while (true)
        {
            int read = stream.Read(chunk, 0, chunk.Length);
            if (read == 0)
                break;

            total += read;
            if (total > maxBytes)
            {
                throw new DocumentIoException(
                    ExitCode.Input,
                    what + " exceeded the " + maxBytes + " byte limit. Raise it with --max-bytes.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
