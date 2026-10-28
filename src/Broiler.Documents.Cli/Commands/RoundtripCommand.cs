using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Broiler.Documents.Cli.Comparison;
using Broiler.Documents.Cli.Composition;
using Broiler.Documents.Cli.Documents;
using Broiler.Documents.Cli.Infrastructure;
using Broiler.Documents.Cli.Rendering;
using Broiler.Documents.Csv;
using Broiler.Documents.Model;

namespace Broiler.Documents.Cli.Commands;

/// <summary>
/// Writes a document out and reads it straight back, then reports what did not
/// survive the trip.
/// </summary>
/// <remarks>
/// <para>
/// This is the shortest path from a corpus to a list of codec gaps, and it needs
/// no reference implementation to compare against: the document that went in
/// <em>is</em> the reference. Anything the model held before the write and does
/// not hold after the read was lost by that format's writer, its reader, or
/// both, and the report says which paragraph and which property.
/// </para>
/// <para>
/// A difference is not automatically a defect. <c>RichTextDocument</c> is a
/// normalized model, not a source-preserving one, and the roadmap is explicit
/// that source-preserving round trips are not a goal. What this command is good
/// for is the difference nobody decided on: a bold run that quietly stops being
/// bold is a gap, whereas Markdown having no way to express a highlight colour
/// is a documented limitation the conformance documents already name.
/// </para>
/// </remarks>
public static class RoundtripCommand
{
    public static CommandEntry Create() => new(
        new CommandSpec(
            "roundtrip",
            "Write a document to a format, read it back, and report what changed.",
            "roundtrip <input> --via <format>... [--sheet <name|index>] [--table <index>] [--dialect <preset>] [--delimiter <sep>] [--render] [--keep <directory>]",
            [
                OptionSpec.Many("via", "format", "A format to round-trip through. Give more than one to test several."),
                OptionSpec.Value("keep", "directory", "Keep the intermediate files instead of working in memory."),
                OptionSpec.Flag("render", "Also render both sides and compare the pixels."),
                OptionSpec.Value("diff", "path", "Write a difference image. {via} and {page} are replaced."),
                OptionSpec.Value("diff-style", "style", "overlay, mask, or heat.", "overlay"),
                OptionSpec.Value("tolerance", "n", "Per-channel difference that still counts as equal, 0-255.", "0"),
                OptionSpec.Value("max-different-ratio", "r", "Differing pixel fraction allowed, 0-1.", "0"),
                OptionSpec.Flag("ignore-whitespace", "Collapse runs of whitespace before comparing text."),
                OptionSpec.Flag("ignore-inline-style", "Compare text and paragraph structure only."),
                OptionSpec.Flag("ignore-paragraph-style", "Compare text and run formatting only."),
                OptionSpec.Flag("ignore-table-layout", "Compare tabular data and structure only, ignoring cell widths, padding, and borders."),
                OptionSpec.Value("numeric-tolerance", "delta", "Floating-point tolerance for cell value comparisons."),
                OptionSpec.Value("max-differences", "n", "Stop listing differences after this many per format.", "50"),
                .. DocumentOptions.RoutingSpecs,
                .. RenderPipeline.Specs,
                .. DocumentOptions.Specs,
                .. DocumentOptions.WriteSpecs,
            ],
            [
                "roundtrip report.docx --via docx",
                "roundtrip report.docx --via docx --via rtf --via html --via markdown --json",
                "roundtrip sheet.csv --sheet 0 --via csv --dialect tsv",
                "roundtrip report.docx --table 0 --via csv --ignore-table-layout",
                "roundtrip report.docx --via rtf --render --diff rtf-diff.png",
                "roundtrip report.docx --via docx --keep ./artifacts",
            ],
            "Exit 0 when every format round-tripped without a difference, 5 when any did not.\n" +
            "\n" +
            "A difference is not automatically a defect: the document model is normalized, and\n" +
            "source-preserving round trips are not a goal. What this finds cheaply is the\n" +
            "difference nobody decided on - and the diagnostics printed alongside usually say\n" +
            "which of the two it is.\n\n" +
            "Use --sheet or --table to isolate a specific spreadsheet sheet or table for roundtrip testing.\n" +
            "Use --dialect or --delimiter to route CSV/TSV dialect presets across roundtrip hops."),
        Run);

    private static int Run(CommandContext context)
    {
        string source = context.Line.RequirePositional(0, "input");
        context.Line.RequireNoExtraPositionals(1);

        IReadOnlyList<string> formats = context.Line.GetAll("via");
        if (formats.Count == 0)
            throw new UsageException("Give at least one --via format to round-trip through.");

        DocumentCodecCatalog catalog = DocumentOptions.CreateCatalogFrom(context.Line);
        DocumentReadOptions readOptions = DocumentOptions.ReadOptionsFrom(context.Line);
        LoadedDocument original = DocumentIo.LoadOrThrow(source, catalog, readOptions, context.Line.Get("from"));

        // The decisions the first read made travel with the document through
        // every hop, so a picture that may be written once may be written again.
        DocumentWriteOptions writeOptions =
            DocumentOptions.WriteOptionsFrom(context.Line, original.Result.Resources);

        context.Report("source  " + source + "  (" + original.FormatName + ", " + original.Status + ")");
        DocumentReport.Print(context, original.Diagnostics, "read diagnostics");

        int sheetIndex = DocumentOptions.ResolveSheetIndex(original.Document, context.Line);
        LoadedDocument documentToTest = original;
        if (sheetIndex >= 0)
        {
            RichTextDocument isolated = DocumentOptions.ExtractSheet(original.Document, sheetIndex);
            documentToTest = original.WithDocument(isolated);
            context.Report($"selected sheet/table {sheetIndex} ({isolated.ParagraphCount} paragraphs)");
            context.Result["selectedSheet"] = sheetIndex;
        }

        string? dialectName = context.Line.Get("dialect") ?? context.Line.Get("csv-dialect");
        if (!string.IsNullOrEmpty(dialectName))
            context.Result["dialect"] = dialectName;

        double? numTolerance = null;
        if (context.Line.Has("numeric-tolerance"))
        {
            string? tolStr = context.Line.Get("numeric-tolerance");
            if (!double.TryParse(tolStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double nt) || nt < 0)
                throw new UsageException("The --numeric-tolerance option requires a non-negative number.");
            numTolerance = nt;
        }

        var options = new ComparisonOptions
        {
            IgnoreWhitespace = context.Line.Has("ignore-whitespace"),
            IgnoreInlineStyle = context.Line.Has("ignore-inline-style"),
            IgnoreParagraphStyle = context.Line.Has("ignore-paragraph-style"),
            IgnoreTableLayout = context.Line.Has("ignore-table-layout"),
            NumericTolerance = numTolerance,
            MaxDifferences = Math.Max(1, context.Line.GetInt32("max-differences", 50)),
        };

        var results = new JsonArray();
        var diagnostics = new List<DocumentDiagnostic>(original.Diagnostics);
        bool allEqual = true;

        foreach (string format in formats)
        {
            (bool equal, JsonObject json, IReadOnlyList<DocumentDiagnostic> reported) = RoundtripOne(
                context, catalog, documentToTest, format, readOptions, writeOptions, options, source);

            allEqual &= equal;
            diagnostics.AddRange(reported);
            results.Add(json);
        }

        context.Result["source"] = source;
        context.Result["sourceFormat"] = original.FormatName;
        context.Result["sourceDiagnostics"] = DocumentReport.ToJson(original.Diagnostics);
        context.Result["results"] = results;
        context.Result["equal"] = allEqual;

        context.Report();
        context.Report(allEqual
            ? "verdict: every format round-tripped without a difference."
            : "verdict: DIFFERENT - see the differences listed above.");

        if (!allEqual)
            return ExitCode.Different;

        // Every format survived the trip, which is not the same as every codec
        // having been happy about it: --fail-on promotes what they said.
        return DocumentReport.ApplyFailOn(
            diagnostics,
            DocumentOptions.FailOnFrom(context.Line),
            ExitCode.Ok);
    }

    private static (bool Equal, JsonObject Json, IReadOnlyList<DocumentDiagnostic> Diagnostics) RoundtripOne(
        CommandContext context,
        DocumentCodecCatalog catalog,
        LoadedDocument original,
        string format,
        DocumentReadOptions readOptions,
        DocumentWriteOptions writeOptions,
        ComparisonOptions comparisonOptions,
        string source)
    {
        DocumentCodec codec = CodecComposition.Resolve(catalog, format)
            ?? throw new UsageException(
                "Unknown format \"" + format + "\". Known formats: " +
                string.Join(", ", CodecComposition.FormatNames(catalog)) + ".");

        // A format this tool cannot both write and read back is not one to
        // round-trip through, any more than an unknown one is. Reported as a
        // "different" verdict, it read as a round trip that had run and lost
        // content - which is what PDF, composed to read only, would have said.
        if (!codec.CanWrite || !codec.CanRead)
        {
            throw new UsageException(
                "Cannot round-trip through " + codec.Descriptor.Name + ": this tool " +
                (codec.CanRead ? "reads it but does not write it." : "writes it but does not read it."));
        }

        bool isTsv = format.Equals("tsv", StringComparison.OrdinalIgnoreCase) || format.Equals(".tsv", StringComparison.OrdinalIgnoreCase);
        bool isCsv = codec.Descriptor.MatchesExtension(".csv");
        string viaDisplayName = isTsv ? "TSV" : codec.Descriptor.Name;

        context.Report();
        context.Report("via " + viaDisplayName);

        using var staging = new MemoryStream();
        DocumentWriteOptions effectiveWriteOptions = writeOptions;
        if (isCsv)
        {
            CsvWriteOptions baseCsv = DocumentOptions.CsvWriteOptionsFrom(context.Line);
            char delim = isTsv ? '\t' : baseCsv.Delimiter;
            effectiveWriteOptions = new CsvWriteOptions(delim, baseCsv.ByteOrderMark, baseCsv.EscapeFormulas, writeOptions.Metadata);
        }

        DocumentWriteResult written = codec.Write(
            new DocumentWriteRequest(original.Document, staging, effectiveWriteOptions));

        DocumentReport.Print(context, written.Diagnostics, "  write diagnostics");

        if (written.Status == DocumentResultStatus.Rejected)
        {
            context.Fail("the " + viaDisplayName + " codec rejected the write.");
            return (false, new JsonObject
            {
                ["format"] = viaDisplayName,
                ["equal"] = false,
                ["writeStatus"] = written.Status.ToString().ToLowerInvariant(),
                ["writeDiagnostics"] = DocumentReport.ToJson(written.Diagnostics),
            }, written.Diagnostics);
        }

        byte[] bytes = staging.ToArray();
        string? keptPath = KeepIfAsked(context, codec, bytes, source, isTsv);

        using DocumentInput input = DocumentInput.FromBytes(bytes);
        // The intermediate file uses the output dialect, independently of the
        // original input delimiter (which may even be a different format).
        char rereadDelimiter = isTsv
            ? '\t'
            : (effectiveWriteOptions as CsvWriteOptions)?.Delimiter
                ?? DocumentOptions.CsvWriteOptionsFrom(context.Line).Delimiter;

        DocumentReadOptions rereadOptions = isCsv
            ? new Broiler.Documents.Csv.CsvReadOptions(readOptions.Limits,
                rereadDelimiter, context.Line.Has("csv-header"))
            : readOptions;
        DocumentReadResult reread = codec.Read(new DocumentReadRequest(input, rereadOptions));

        DocumentReport.Print(context, reread.Diagnostics, "  read diagnostics");

        if (reread.Status == DocumentResultStatus.Rejected)
        {
            // The codec could not read back what it had just written. That is
            // never a normalization difference and never a documented limitation.
            context.Fail("the " + viaDisplayName + " codec could not read back its own output.");
            return (false, new JsonObject
            {
                ["format"] = viaDisplayName,
                ["equal"] = false,
                ["byteLength"] = bytes.Length,
                ["readStatus"] = reread.Status.ToString().ToLowerInvariant(),
                ["writeDiagnostics"] = DocumentReport.ToJson(written.Diagnostics),
                ["readDiagnostics"] = DocumentReport.ToJson(reread.Diagnostics),
            }, written.Diagnostics.Concat(reread.Diagnostics).ToArray());
        }

        DocumentComparison comparison = DocumentComparison.Compare(
            original.Document, reread.Document, comparisonOptions);

        context.Report(
            $"  {bytes.Length} bytes, plain text {(comparison.TextEqual ? "same" : "DIFFERENT")}, format codes {(comparison.FormatCodesEqual ? "same" : "DIFFERENT")}, {comparison.Differences.Count} structural difference(s)");

        foreach (DocumentDifference difference in comparison.Differences)
            context.Report("    " + difference);

        var json = new JsonObject
        {
            ["format"] = codec.Descriptor.Name,
            ["byteLength"] = bytes.Length,
            ["keptAt"] = keptPath,
            ["writeStatus"] = written.Status.ToString().ToLowerInvariant(),
            ["readStatus"] = reread.Status.ToString().ToLowerInvariant(),
            ["writeDiagnostics"] = DocumentReport.ToJson(written.Diagnostics),
            ["readDiagnostics"] = DocumentReport.ToJson(reread.Diagnostics),
            ["comparison"] = comparison.ToJson(),
        };

        bool equal = comparison.Equal;

        if (context.Line.Has("render"))
        {
            (bool pixelsEqual, JsonObject render) = CompareRendered(
                context, original.Document, reread.Document, codec.Descriptor.Name);
            equal &= pixelsEqual;
            json["render"] = render;
        }

        json["equal"] = equal;
        return (equal, json, written.Diagnostics.Concat(reread.Diagnostics).ToArray());
    }

    private static (bool Equal, JsonObject Json) CompareRendered(
        CommandContext context,
        RichTextDocument left,
        RichTextDocument right,
        string formatName)
    {
        RenderPipeline pipeline = RenderPipeline.Create(context.Line);
        using RenderOutcome before = pipeline.Render(left);
        using RenderOutcome after = pipeline.Render(right);

        int tolerance = context.Line.GetInt32("tolerance", 0);
        if (tolerance is < 0 or > 255)
            throw new UsageException("--tolerance must be between 0 and 255.");

        double maxRatio = context.Line.GetDouble("max-different-ratio", 0);
        DiffStyle? style = context.Line.Has("diff") ? ParseDiffStyle(context.Line) : null;

        bool equal = before.Pages.Count == after.Pages.Count;
        var pages = new JsonArray();

        context.Report("  render          " + before.Pages.Count + " vs " + after.Pages.Count + " page(s)");

        for (int i = 0; i < Math.Min(before.Pages.Count, after.Pages.Count); i++)
        {
            ImageComparison comparison = ImageComparison.Compare(
                before.Pages[i], after.Pages[i], tolerance, style);

            try
            {
                bool passes = comparison.Passes(0, maxRatio, requireSameSize: true);
                equal &= passes;

                context.Report($"  page {i + 1,-11} {comparison.DifferingPixels} differing pixel(s), max delta {comparison.MaxChannelDelta}{(passes ? string.Empty : "   DIFFERENT")}");

                JsonObject json = comparison.ToJson();
                json["page"] = i + 1;
                json["passes"] = passes;

                if (comparison.Diff is not null)
                {
                    string path = context.Line.Require("diff")
                        .Replace("{via}", formatName.ToLowerInvariant(), StringComparison.Ordinal)
                        .Replace("{page}", (i + 1).ToString("D3", CultureInfo.InvariantCulture), StringComparison.Ordinal);

                    DocumentIo.WriteAllBytes(path, DocumentRasterizer.EncodePng(comparison.Diff));
                    context.Report("  diff            " + path);
                    json["diffPath"] = path;
                }

                pages.Add(json);
            }
            finally
            {
                comparison.Diff?.Dispose();
            }
        }

        return (equal, new JsonObject
        {
            ["beforePageCount"] = before.Pages.Count,
            ["afterPageCount"] = after.Pages.Count,
            ["pages"] = pages,
        });
    }

    private static string? KeepIfAsked(
        CommandContext context,
        DocumentCodec codec,
        byte[] bytes,
        string source,
        bool isTsv = false)
    {
        if (!context.Line.Has("keep"))
            return null;

        string directory = context.Line.Require("keep");
        string stem = source == DocumentIo.StandardStreamToken
            ? "document"
            : Path.GetFileNameWithoutExtension(source);

        string formatName = isTsv ? "tsv" : codec.Descriptor.Name.ToLowerInvariant();
        string extension = isTsv ? ".tsv" : (codec.Descriptor.FileExtensions.Count > 0 ? codec.Descriptor.FileExtensions[0] : ".bin");

        string path = Path.Combine(directory, stem + "-roundtrip-" + formatName + extension);
        DocumentIo.WriteAllBytes(path, bytes);
        context.Report("  kept            " + path);
        return path;
    }

    private static DiffStyle ParseDiffStyle(CommandLine line)
    {
        string value = line.Get("diff-style", "overlay")!;
        return value.ToLowerInvariant() switch
        {
            "overlay" => DiffStyle.Overlay,
            "mask" => DiffStyle.Mask,
            "heat" => DiffStyle.Heat,
            _ => throw new UsageException("--diff-style expects overlay, mask, or heat, not \"" + value + "\"."),
        };
    }
}
