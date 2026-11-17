using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Broiler.Documents.Corpus;

namespace Broiler.Documents.Office;

/// <summary>Normalized same-format round trips, independent of the rendering toolchain.</summary>
internal static class ReSaveConformance
{
    public static string DefaultFailedPath(string root) =>
        Path.Combine(root, "tests", "office", "resave-failed-documents.txt");

    public static async Task<int> RunAsync(Program.Options options, BroilerDocTool tool,
        string workspace, string root)
    {
        string trackedPath = options.FailedDocumentsPath ?? DefaultFailedPath(root);
        var prior = FailedDocuments.Load(trackedPath);
        IReadOnlySet<string>? selected = options.Incremental ? prior :
            ExtrasSelection.Resolve(options.SampleList, options.SingleSample);
        if (options.Incremental)
        {
            IEnumerable<string> ids = prior;
            if (options.Only is not null)
                ids = ids.Where(id => id.Contains(options.Only, StringComparison.OrdinalIgnoreCase));
            if (options.MaxFiles is int limit) ids = ids.Take(limit);
            selected = ids.ToHashSet(StringComparer.Ordinal);
        }

        IReadOnlyList<ExtrasSample> samples = selected is { Count: 0 } ? [] :
            await ExtrasConformance.AcquireSamplesAsync(workspace, options.ExtrasUrl, options.ExtrasDirectory,
                options.Incremental ? null : options.Only, options.MaxFiles, selected).ConfigureAwait(false);
        ExtrasSelection.RequireAll(selected, samples);
        Console.WriteLine("corpus      " + samples.Count + " re-save sample(s)");
        Console.WriteLine("axis        normalized round-trip fidelity" + (options.ReSaveRender
            ? " + images (" + options.Threshold.ToString(CultureInfo.InvariantCulture) + "% matching pixels)" : string.Empty));
        Console.WriteLine("workspace   " + workspace);

        var crashes = CrashRegressions.Load(options.CrashRegressions ??
            Path.Combine(root, "tests", "office", "extras-crash-regressions.json"));
        var known = ReSaveRejections.Load(Path.Combine(root, "tests", "office", "resave-known-rejections.json"));
        ToolRun catalogRun = await tool.RunAsync(["formats", "--json", "--quiet"]).ConfigureAwait(false);
        JsonElement? catalog = catalogRun.Json();
        if (catalogRun.ExitCode != 0 || catalog is null || !catalog.Value.TryGetProperty("formats", out JsonElement formats))
            throw new InvalidDataException("Could not read the tool's format capabilities: " + catalogRun.StandardError);
        var writable = formats.EnumerateArray().Where(f => f.GetProperty("canWrite").GetBoolean())
            .Select(f => f.GetProperty("name").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = new ConcurrentBag<ReSaveReading>();
        await Parallelism.ForEachAsync(samples, options.Jobs, async sample =>
        {
            string? family = crashes.FamilyOf(sample);
            if (family is not null)
            {
                var robust = await ExtrasConformance.EvaluateCrashRegressionAsync(tool, sample, family,
                    workspace, "semantic", options.Dpi, options.MaxPages, [], []).ConfigureAwait(false);
                CheckResult check = robust.Results.Single(r => r.Group == "robust") with
                {
                    Group = "resave", Name = "resave/" + sample.Id,
                    Detail = robust.Difference.BroilerReadFailed ? robust.Results[0].Detail :
                        "Crash-regression input: read/dump handled with documented exits; fidelity not compared."
                };
                rows.Add(new ReSaveReading(sample.Id, "crash-regression", check));
                return;
            }
            rows.Add(await EvaluateAsync(sample, Path.Combine(workspace, "resave", sample.Id),
                writable, known, arguments => tool.RunAsync(arguments), options.ReSaveRender, options.Threshold,
                options.Dpi, options.MaxPages, options.FontDirectories, options.Keep).ConfigureAwait(false));
        }).ConfigureAwait(false);
        ReSaveReading[] readings = rows.OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();
        CheckResult[] results = readings.Select(r => r.Check).ToArray();
        var header = new RunHeader(tool.Description, null, null, options.FontSet,
            System.Runtime.InteropServices.RuntimeInformation.OSDescription, options.ReSaveRender ? options.Dpi : 0, false);
        if (options.JsonReport is not null)
        {
            OfficeReporting.WriteJson(options.JsonReport, results, [], [], header, threshold: options.Threshold);
            var json = JsonNode.Parse(File.ReadAllText(options.JsonReport))!.AsObject();
            json["comparisonPolicy"] = options.ReSaveRender ? "normalized-roundtrip-render-v1" : "normalized-roundtrip-v1";
            json["renderEnabled"] = options.ReSaveRender;
            json["renderThreshold"] = options.Threshold;
            json["resaves"] = JsonSerializer.SerializeToNode(readings,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            File.WriteAllText(options.JsonReport, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        if (options.JUnitReport is not null)
            OfficeReporting.WriteJUnit(options.JUnitReport, results);
        if (options.IssueReport is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.IssueReport))!);
            File.WriteAllText(options.IssueReport, Markdown(readings, options.ReSaveRender, options.Threshold));
        }

        string[] failures = results.Where(r => r.Outcome == CheckOutcome.Failed)
            .Select(r => r.Name["resave/".Length..]).ToArray();
        if (options.Incremental || (options.Only is null && options.MaxFiles is null && selected is null))
            FailedDocuments.Save(trackedPath, !options.Incremental ? failures :
                FailedDocuments.UpdateTracked(prior, samples.Select(s => s.Id), failures));
        OfficeReporting.WriteConsole(Console.Out, results, options.Verbose);
        return OfficeExitCodes.ForFailures(failures.Length);
    }

    internal static async Task<ReSaveReading> EvaluateAsync(ExtrasSample sample, string outputDirectory,
        IReadOnlySet<string> writable, ReSaveRejections known, Func<string[], Task<ToolRun>> run,
        bool render = true, double threshold = 80, int dpi = 144, int maxPages = 40,
        IReadOnlyList<string>? fontDirectories = null, bool keepImages = false)
    {
        string name = "resave/" + sample.Id;
        ToolRun info = await run(["info", sample.LocalPath, "--json", "--quiet"]).ConfigureAwait(false);
        if (info.ExitCode != 0)
        {
            string? reason = known.ReasonFor(sample, info.ExitCode);
            return reason is not null
                ? new(sample.Id, "known-rejection", CheckResult.Skip("resave", name, reason) with { Command = info.CommandLine() }, Info: info.Json())
                : new(sample.Id, "read-failure", CheckResult.ExpectExit("resave", name, 0, info), Info: info.Json());
        }
        JsonElement? source = info.Json();
        if (source is null || !source.Value.TryGetProperty("format", out JsonElement formatValue) ||
            formatValue.ValueKind != JsonValueKind.String)
            return new(sample.Id, "invalid-report", CheckResult.Fail("resave", name,
                "info succeeded without a detected format in its JSON report.", info.CommandLine()));
        string format = formatValue.GetString()!;
        if (!writable.Contains(format))
            return new(sample.Id, "unsupported-write", CheckResult.Skip("resave", name,
                "Detected " + format + ": the composed codec cannot write this format.") with { Command = info.CommandLine() }, format, Info: source);

        Directory.CreateDirectory(outputDirectory);
        // The CLI picks its canonical extension and reads the serialized file back.
        var arguments = new List<string> { "roundtrip", sample.LocalPath, "--via", format,
            "--keep", outputDirectory, "--json", "--quiet" };
        if (render)
        {
            arguments.AddRange(["--render", "--max-different-ratio", ((100 - threshold) / 100).ToString("R", CultureInfo.InvariantCulture),
                "--dpi", dpi.ToString(CultureInfo.InvariantCulture), "--max-pages", maxPages.ToString(CultureInfo.InvariantCulture)]);
            foreach (string directory in fontDirectories ?? []) arguments.AddRange(["--font-dir", directory]);
            if (keepImages)
                arguments.AddRange(["--render-out", Path.Combine(outputDirectory, "{side}-{page}.png"),
                    "--diff", Path.Combine(outputDirectory, "diff-{page}.png")]);
        }
        ToolRun trip = await run([.. arguments]).ConfigureAwait(false);
        return EvaluateRoundtrip(sample, format, source, trip, render, threshold);
    }

    internal static ReSaveReading EvaluateRoundtrip(ExtrasSample sample, string format, JsonElement? source, ToolRun run, bool render = true, double threshold = 80)
    {
        string name = "resave/" + sample.Id;
        JsonElement? report = run.Json();
        if (run.ExitCode is not (0 or 5))
            return new(sample.Id, "roundtrip-failure", CheckResult.ExpectExit("resave", name, 0, run), format, Info: source, Roundtrip: report);
        try
        {
            if (report is null) throw new InvalidDataException("no JSON report");
            JsonElement result = report.Value.GetProperty("results")[0];
            if (report.Value.GetProperty("results").GetArrayLength() != 1 ||
                !string.Equals(report.Value.GetProperty("sourceFormat").GetString(), format, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(result.GetProperty("format").GetString(), format, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("roundtrip reported a different format or result count");
            if ((result.TryGetProperty("writeStatus", out JsonElement writeStatus) && writeStatus.GetString() == "rejected") ||
                (result.TryGetProperty("readStatus", out JsonElement readStatus) && readStatus.GetString() == "rejected"))
                return new(sample.Id, "roundtrip-failure", CheckResult.Fail("resave", name,
                    "The codec rejected its write or could not read its own output. See roundtrip diagnostics in JSON.", run.CommandLine()), format, Info: source, Roundtrip: report);
            if (result.GetProperty("writeStatus").GetString() is not ("success" or "partial") ||
                result.GetProperty("readStatus").GetString() is not ("success" or "partial"))
                throw new InvalidDataException("unknown write/read status");
            string kept = result.GetProperty("keptAt").GetString() ?? throw new InvalidDataException("no saved output path");
            if (!File.Exists(kept)) throw new InvalidDataException("roundtrip produced no saved output file");
            // Byte identity is informational and never controls the fidelity verdict.
            using var input = File.OpenRead(sample.LocalPath);
            using var output = File.OpenRead(kept);
            bool bytesEqual = input.Length == output.Length && SHA256.HashData(input).AsSpan().SequenceEqual(SHA256.HashData(output));
            JsonElement comparison = result.GetProperty("comparison");
            bool modelEqual = comparison.GetProperty("equal").GetBoolean() && comparison.GetProperty("plainTextEqual").GetBoolean();
            (bool Equal, double? Similarity, string Detail) pixels = render
                ? EvaluateRender(result.GetProperty("render"), threshold) : (true, null, "image comparison disabled");
            bool equal = modelEqual && pixels.Equal;
            bool reportedEqual = result.GetProperty("equal").GetBoolean();
            if (reportedEqual != equal || (run.ExitCode == 0) != equal)
                throw new InvalidDataException("roundtrip exit and comparison verdict disagree");
            string detail = !modelEqual ? "Normalized model changed: " + comparison.GetProperty("differenceCount").GetInt32() +
                " structural difference(s); plain text equal: " + comparison.GetProperty("plainTextEqual").GetBoolean() +
                ".\n" + comparison.GetProperty("differences").ToString() : "Normalized model equal.";
            if (render) detail += "\nImages: " + pixels.Detail;
            CheckResult check = new("resave", name, equal ? CheckOutcome.Passed : CheckOutcome.Failed,
                detail, run.CommandLine(), run.Duration);
            string category = !modelEqual ? "model-difference" : !pixels.Equal ? "render-difference" : "model-equal";
            return new(sample.Id, category, check, format, bytesEqual, source, report, pixels.Similarity);
        }
        catch (Exception ex) when (ex is InvalidDataException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            return new(sample.Id, "invalid-report", CheckResult.Fail("resave", name,
                "Invalid roundtrip report: " + ex.Message, run.CommandLine()), format, Info: source, Roundtrip: report);
        }
    }

    internal static (bool Equal, double? Similarity, string Detail) EvaluateRender(JsonElement render, double threshold)
    {
        int before = render.GetProperty("beforePageCount").GetInt32();
        int after = render.GetProperty("afterPageCount").GetInt32();
        JsonElement pages = render.GetProperty("pages");
        if (before < 1 || after < 1 || pages.GetArrayLength() != Math.Min(before, after))
            throw new InvalidDataException("missing or incomplete rendered pages");
        bool truncated = render.GetProperty("beforeTruncated").GetBoolean() || render.GetProperty("afterTruncated").GetBoolean();
        bool equal = before == after && !truncated;
        double minimum = 100;
        int worst = 1;
        bool sizeMismatch = false;
        foreach (JsonElement page in pages.EnumerateArray())
        {
            long compared = page.GetProperty("comparedPixels").GetInt64();
            long differing = page.GetProperty("differingPixels").GetInt64();
            if (compared <= 0 || differing < 0 || differing > compared)
                throw new InvalidDataException("invalid rendered pixel counts");
            double ratio = (double)differing / compared;
            double similarity = 100 * (1 - ratio);
            if (similarity < minimum) { minimum = similarity; worst = page.GetProperty("page").GetInt32(); }
            sizeMismatch |= page.GetProperty("sizeDiffers").GetBoolean();
            equal &= !page.GetProperty("sizeDiffers").GetBoolean() && ratio <= (100 - threshold) / 100;
        }
        return (equal, minimum, $"worst page {worst}: {minimum.ToString("F2", CultureInfo.InvariantCulture)}% matching pixels; " +
            $"required {threshold.ToString(CultureInfo.InvariantCulture)}%; pages {before} vs {after}" +
            (sizeMismatch ? "; page image dimensions differ" : string.Empty) +
            (truncated ? "; rendering truncated (increase --max-pages)." : "."));
    }

    internal static string Markdown(IReadOnlyList<ReSaveReading> readings, bool render = true, double threshold = 80)
    {
        var text = new StringBuilder("# Office re-save conformance\n\n");
        text.AppendLine("Comparison: **normalized round-trip fidelity**. Byte identity is informational.");
        text.AppendLine(render ? $"Images: enabled; every page must have at least {threshold.ToString(CultureInfo.InvariantCulture)}% matching pixels, equal page counts/sizes, and no truncation."
            : "Images: disabled (model-only comparison).");
        text.AppendLine($"\nSamples: {readings.Count}; passed: {readings.Count(r => r.Check.Outcome == CheckOutcome.Passed)}; failed: {readings.Count(r => r.Check.Outcome == CheckOutcome.Failed)}; skipped: {readings.Count(r => r.Check.Outcome == CheckOutcome.Skipped)}.");
        foreach (var group in readings.GroupBy(r => r.Category).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            text.AppendLine($"\n## {group.Key}: {group.Count()}\n");
            foreach (ReSaveReading row in group.Take(3))
            {
                // Bound individual excerpts; complete diagnostics remain in JSON/JUnit/logs.
                string detail = row.Check.Detail ?? row.Format ?? "handled";
                if (detail.Length > 900) detail = detail[..900] + " ...";
                text.AppendLine($"- `{row.Id}`: {detail.ReplaceLineEndings(" ")}");
            }
        }
        text.AppendLine("\nDownload the workflow artifact for every sample, full diagnostics, and reproduction commands.");
        return text.ToString();
    }
}

internal sealed record ReSaveReading(string Id, string Category, CheckResult Check, string? Format = null,
    bool? ByteIdentical = null, JsonElement? Info = null, JsonElement? Roundtrip = null, double? MinimumPageSimilarity = null);
