using Broiler.Documents.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Broiler.Documents.Cli.Composition;
using Broiler.Documents.Cli.Documents;
using Broiler.Documents.Cli.Infrastructure;
using Broiler.Documents.Cli.Rendering;

namespace Broiler.Documents.Cli.Commands;

/// <summary>The commands that answer "what is this file, and what is in it".</summary>
public static class InspectCommands
{
    public static CommandEntry Formats() => new(
        new CommandSpec(
            "formats",
            "List the document formats this tool composes.",
            "formats [--json]",
            Array.Empty<OptionSpec>(),
            new[] { "formats", "formats --json" },
            "The catalog is composed explicitly in code (ADR 0001/0003); nothing registers\n" +
            "itself. What this prints is therefore the complete and only set of formats\n" +
            "this build can read or write."),
        Run);

    public static CommandEntry Probe() => new(
        new CommandSpec(
            "probe",
            "Identify a file's format without fully reading it.",
            "probe <input> [--json]",
            DocumentOptions.Specs.Where(option => option.Name != "fail-on").ToArray(),
            new[] { "probe report.docx", "probe unknown.bin --json" },
            "Runs every composed codec's signature probe over the leading bytes and reports\n" +
            "each verdict, not just the winner. A file two codecs both claim is worth knowing\n" +
            "about."),
        RunProbe);

    public static CommandEntry Info() => new(
        new CommandSpec(
            "info",
            "Read a document and report its structure and diagnostics.",
            "info <input> [--json] [--verbose]",
            DocumentOptions.Specs,
            new[]
            {
                "info report.docx",
                "info report.docx --verbose",
                "info report.docx --fail-on warning",
            },
            "The diagnostics are the point. A codec that meets a construct it does not\n" +
            "implement returns a usable document and says what it dropped, so this is where a\n" +
            "gap is named before anything has to be inferred from a picture."),
        RunInfo);

    public static CommandEntry Version() => new(
        new CommandSpec(
            "version",
            "Report tool, component, and rendering environment versions.",
            "version [--json]",
            Array.Empty<OptionSpec>(),
            new[] { "version --json" },
            "Worth capturing at the head of any automated run. The font the renderer falls\n" +
            "back to is part of this, and it is the single most common reason two machines\n" +
            "produce different pixels from the same document."),
        RunVersion);

    private static int Run(CommandContext context)
    {
        DocumentCodecCatalog catalog = CodecComposition.CreateCatalog();
        var formats = new JsonArray();

        context.Report(string.Format(
            CultureInfo.InvariantCulture,
            "{0,-10} {1,-6} {2,-6} {3,-24} {4}",
            "FORMAT",
            "READ",
            "WRITE",
            "EXTENSIONS",
            "MIME TYPES"));

        foreach (DocumentCodec codec in catalog.Codecs)
        {
            DocumentFormatDescriptor descriptor = codec.Descriptor;
            context.Report(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-10} {1,-6} {2,-6} {3,-24} {4}",
                descriptor.Name,
                codec.CanRead ? "yes" : "no",
                codec.CanWrite ? "yes" : "no",
                string.Join(" ", descriptor.FileExtensions),
                string.Join(" ", descriptor.MimeTypes)));

            var extensions = new JsonArray();
            foreach (string extension in descriptor.FileExtensions)
                extensions.Add(extension);

            var mimeTypes = new JsonArray();
            foreach (string mimeType in descriptor.MimeTypes)
                mimeTypes.Add(mimeType);

            formats.Add(new JsonObject
            {
                ["name"] = descriptor.Name,
                ["canRead"] = codec.CanRead,
                ["canWrite"] = codec.CanWrite,
                ["fileExtensions"] = extensions,
                ["mimeTypes"] = mimeTypes,
            });
        }

        context.Report();
        context.Report("Rendering to an image is available for every format that can be read.");
        context.Report("PDF is not composed here: Broiler.Documents.Pdf stays out of every application");
        context.Report("catalog until its read-preview and write-preview gates pass.");

        context.Result["formats"] = formats;
        context.Result["pdfComposed"] = false;
        return ExitCode.Ok;
    }

    private static int RunProbe(CommandContext context)
    {
        string source = context.Line.RequirePositional(0, "input");
        context.Line.RequireNoExtraPositionals(1);

        DocumentCodecCatalog catalog = CodecComposition.CreateCatalog();
        DocumentReadOptions options = DocumentOptions.ReadOptionsFrom(context.Line);

        byte[] bytes = DocumentIo.ReadAllBytes(source, options.Limits.MaxDocumentBytes);
        var hints = new DocumentSourceHints(
            source == DocumentIo.StandardStreamToken ? null : System.IO.Path.GetFileName(source));

        int prefixLength = Math.Min(bytes.Length, options.Limits.MaxProbeBytes);
        var request = new DocumentProbeRequest(
            bytes.AsMemory(0, prefixLength),
            hints,
            options.Limits);

        var results = new JsonArray();
        DocumentCodec? best = null;
        DocumentProbeResult? bestResult = null;

        context.Report("probing " + source + " (" + bytes.Length + " bytes, " + prefixLength + " byte prefix)");
        context.Report();

        foreach (DocumentCodec codec in catalog.Codecs)
        {
            DocumentProbeResult result = codec.Probe(request);

            context.Report(string.Format(
                CultureInfo.InvariantCulture,
                "  {0,-10} {1,-8} {2}",
                codec.Descriptor.Name,
                result.Confidence,
                result.Diagnostic ?? string.Empty));

            results.Add(new JsonObject
            {
                ["format"] = codec.Descriptor.Name,
                ["confidence"] = result.Confidence.ToString().ToLowerInvariant(),
                ["confidenceValue"] = (int)result.Confidence,
                ["mimeType"] = result.MimeType,
                ["bytesConsumed"] = result.BytesConsumed,
                ["diagnostic"] = result.Diagnostic,
                ["match"] = result.IsMatch,
            });

            if (result.IsMatch && (bestResult is null || result.Confidence > bestResult.Confidence))
            {
                best = codec;
                bestResult = result;
            }
        }

        context.Report();
        context.Report(best is null
            ? "No composed codec recognized this content."
            : "Selected: " + best.Descriptor.Name + " (" + bestResult!.Confidence + ").");

        context.Result["source"] = source;
        context.Result["byteLength"] = bytes.Length;
        context.Result["selected"] = best?.Descriptor.Name;
        context.Result["probes"] = results;

        return best is null ? ExitCode.Read : ExitCode.Ok;
    }

    private static int RunInfo(CommandContext context)
    {
        string source = context.Line.RequirePositional(0, "input");
        context.Line.RequireNoExtraPositionals(1);

        DocumentCodecCatalog catalog = CodecComposition.CreateCatalog();
        DocumentReadOptions options = DocumentOptions.ReadOptionsFrom(context.Line);
        LoadedDocument loaded = DocumentIo.LoadOrThrow(source, catalog, options, context.Line.Get("from"));

        DocumentStatistics statistics = DocumentReport.Measure(loaded.Document);

        context.Report(source);
        context.Report("  format            " + loaded.FormatName + " (" + loaded.Probe.Confidence + " confidence)");
        context.Report("  status            " + loaded.Status);
        context.Report("  paragraphs        " + statistics.Paragraphs.ToString(CultureInfo.InvariantCulture) +
            " (" + statistics.EmptyParagraphs.ToString(CultureInfo.InvariantCulture) + " empty)");
        context.Report("  characters        " + statistics.Characters.ToString(CultureInfo.InvariantCulture));
        context.Report("  runs              " + statistics.Runs.ToString(CultureInfo.InvariantCulture));

        foreach (KeyValuePair<string, long> count in statistics.Counts().Skip(4))
        {
            if (count.Value > 0)
                context.Report("  " + count.Key.PadRight(18) + count.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (loaded.Document.PageGeometry is PageGeometry page)
        {
            context.Report(
                "  page              " +
                page.Width.ToString("0.#", CultureInfo.InvariantCulture) + " x " +
                page.Height.ToString("0.#", CultureInfo.InvariantCulture) + " pt, margins l/r/t/b " +
                page.MarginLeft.ToString("0.#", CultureInfo.InvariantCulture) + "/" +
                page.MarginRight.ToString("0.#", CultureInfo.InvariantCulture) + "/" +
                page.MarginTop.ToString("0.#", CultureInfo.InvariantCulture) + "/" +
                page.MarginBottom.ToString("0.#", CultureInfo.InvariantCulture));
        }

        if (statistics.FontFamilies.Count > 0)
            context.Report("  fonts             " + string.Join(", ", statistics.FontFamilies));

        context.Report();
        DocumentReport.Print(context, loaded.Diagnostics, "diagnostics");

        context.Result["source"] = source;
        context.Result["format"] = loaded.FormatName;
        context.Result["confidence"] = loaded.Probe.Confidence.ToString().ToLowerInvariant();
        context.Result["status"] = loaded.Status.ToString().ToLowerInvariant();
        context.Result["statistics"] = statistics.ToJson();
        context.Result["diagnostics"] = DocumentReport.ToJson(loaded.Diagnostics);

        return DocumentReport.ApplyFailOn(
            loaded.Diagnostics,
            DocumentOptions.FailOnFrom(context.Line),
            ExitCode.Ok);
    }

    private static int RunVersion(CommandContext context)
    {
        context.Line.RequireNoExtraPositionals(0);
        CodecComposition.RegisterImageCodecs();

        string tool = Describe(typeof(InspectCommands).Assembly);
        string documents = Describe(typeof(DocumentCodec).Assembly);
        string model = Describe(typeof(Broiler.Documents.Model.RichTextDocument).Assembly);
        string graphics = Describe(typeof(Broiler.Graphics.BColor).Assembly);
        string fallbackFont = FontResolution.DescribeHostFallback();

        context.Report(HelpText.ToolName + " " + tool);
        context.Report("  Broiler.Documents        " + documents);
        context.Report("  Broiler.Documents.Model  " + model);
        context.Report("  Broiler.Graphics         " + graphics);
        context.Report("  runtime                  " + Environment.Version);
        context.Report("  os                       " + Environment.OSVersion);
        context.Report("  fallback text font       " + fallbackFont);

        context.Result["tool"] = tool;
        context.Result["broilerDocuments"] = documents;
        context.Result["broilerDocumentsModel"] = model;
        context.Result["broilerGraphics"] = graphics;
        context.Result["runtime"] = Environment.Version.ToString();
        context.Result["os"] = Environment.OSVersion.ToString();
        context.Result["fallbackTextFont"] = fallbackFont;

        return ExitCode.Ok;
    }

    private static string Describe(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? assembly.GetName().Version?.ToString()
        ?? "unknown";
}
