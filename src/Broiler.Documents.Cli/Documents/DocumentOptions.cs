using System;
using Broiler.Documents.Cli.Infrastructure;

namespace Broiler.Documents.Cli.Documents;

/// <summary>
/// Builds the codec read and write options from the shared options every command
/// that touches a document accepts.
/// </summary>
/// <remarks>
/// The limits are exposed rather than hidden because they are a deliberate part
/// of the contract: the defaults refuse a 64 MB document and a million
/// paragraphs, and a harness feeding a deliberately hostile corpus needs to be
/// able to say so on the command line instead of rebuilding the tool.
/// </remarks>
public static class DocumentOptions
{
    /// <summary>The options every command that reads or writes a document shares.</summary>
    public static OptionSpec[] Specs { get; } =
    {
        OptionSpec.Value("from", "format", "Read as this format instead of probing the content."),
        OptionSpec.Value(
            "max-bytes",
            "n",
            "Largest document accepted, in bytes.",
            DocumentLimits.DefaultMaxDocumentBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        OptionSpec.Value(
            "max-paragraphs",
            "n",
            "Largest paragraph count accepted.",
            DocumentLimits.DefaultMaxParagraphCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        OptionSpec.Value(
            "code-page",
            "n",
            "Fallback code page for RTF documents that declare none.",
            DocumentReadOptions.Windows1252CodePage.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        OptionSpec.Flag(
            "decode-embedded",
            "Ask codecs to decode embedded binary objects. Codecs that cannot report it rather than skipping silently."),
        OptionSpec.Value(
            "fail-on",
            "severity",
            "Exit " + ExitCode.Diagnostics + " when a diagnostic reaches this severity: info, warning, error, or never.",
            "never"),
    };

    /// <summary>The write-side options, added on top of <see cref="Specs"/> by commands that write.</summary>
    public static OptionSpec[] WriteSpecs { get; } =
    {
        OptionSpec.Value("to", "format", "Write this format instead of inferring it from the output extension."),
        OptionSpec.Flag("raw-unicode", "Ask writers to emit non-ASCII characters directly rather than escaping them."),
    };

    public static DocumentReadOptions ReadOptionsFrom(CommandLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var limits = new DocumentLimits(
            maxDocumentBytes: PositiveLong(line, "max-bytes", DocumentLimits.DefaultMaxDocumentBytes),
            maxParagraphCount: PositiveInt(line, "max-paragraphs", DocumentLimits.DefaultMaxParagraphCount));

        int codePage = PositiveInt(line, "code-page", DocumentReadOptions.Windows1252CodePage);
        // The CLI operates on files the user named on their own command line, so
        // reading their pictures and writing them back out is what was asked for.
        // A host with a different relationship to its input picks a different
        // policy; this one is stated rather than inherited.
        return new DocumentReadOptions(
            limits,
            codePage,
            line.Has("decode-embedded"),
            DocumentResourcePolicy.AllowOwnDocuments);
    }

    /// <summary>
    /// The write options for a run, carrying the decisions the read made about
    /// the document's resources.
    /// </summary>
    /// <remarks>
    /// <paramref name="resources"/> is the context the read returned. Passing it
    /// is what lets a picture reach the output: a write given no context permits
    /// nothing, which is the documented behaviour for a conversion whose caller
    /// recorded no origin for what it is about to redistribute.
    /// </remarks>
    public static DocumentWriteOptions WriteOptionsFrom(
        CommandLine line,
        DocumentConversionContext? resources = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new DocumentWriteOptions(asciiOnly: !line.Has("raw-unicode"), resources: resources);
    }

    /// <summary>
    /// The severity at which <c>--fail-on</c> turns a completed run into a
    /// non-zero exit, or null when the caller did not ask for one.
    /// </summary>
    public static DocumentDiagnosticSeverity? FailOnFrom(CommandLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        string value = line.Get("fail-on", "never")!;
        return value.ToLowerInvariant() switch
        {
            "never" or "none" or "" => null,
            "info" => DocumentDiagnosticSeverity.Info,
            "warning" or "warn" => DocumentDiagnosticSeverity.Warning,
            "error" => DocumentDiagnosticSeverity.Error,
            _ => throw new UsageException(
                "--fail-on expects never, info, warning, or error, not \"" + value + "\"."),
        };
    }

    private static int PositiveInt(CommandLine line, string name, int fallback)
    {
        int value = line.GetInt32(name, fallback);
        if (value <= 0)
            throw new UsageException("--" + name + " must be greater than zero.");
        return value;
    }

    private static long PositiveLong(CommandLine line, string name, long fallback)
    {
        double value = line.GetDouble(name, fallback);
        if (value <= 0 || value > long.MaxValue)
            throw new UsageException("--" + name + " must be greater than zero.");
        return (long)value;
    }
}
