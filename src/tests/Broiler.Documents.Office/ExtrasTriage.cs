using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Broiler.Documents.Corpus;

namespace Broiler.Documents.Office;

internal sealed record TriageEnvironment(string? LibreOffice, string? Rasterizer, string? FontSet,
    string Platform, int Dpi, double Threshold, string Axis, int MaxPages, double ReferenceTimeoutSeconds);
internal sealed record TriageCheck(string Group, string Name, string Outcome, double DurationSeconds);
internal sealed record TriageRender(int BroilerPages, int ReferencePages, int WorstPage, RenderObserved Observed);
internal sealed record TriageCase(string Id, string Format, string? SourceUrl, string Sha256, string Outcome,
    double SeverityScore, string[] Symptoms, string[] Diagnostics, TriageCheck[] Checks, TriageRender? Render);
internal sealed record TriageCluster(string Observation, string[] Samples);
internal sealed record TriageChange(string Id, string Status, bool Comparable, string[] FixedChecks,
    string[] RegressedChecks, bool DiagnosticsChanged, bool MeasurementsChanged, bool WorstPageChanged, bool MetricsComparable, TriageRender? BeforeRender = null, TriageRender? AfterRender = null);
internal sealed record TriageComparison(bool EnvironmentMatches, string[] ChangedEnvironmentFields,
    string[] NotRun, TriageChange[] Changes);
internal sealed record TriageReport(int SchemaVersion, string Tool, TriageEnvironment Environment,
    TriageCase[] Cases, TriageCluster[] Clusters, TriageComparison? Comparison);

/// <summary>Observations, not automatic root-cause guesses or baseline classifications.</summary>
internal static class ExtrasTriage
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static TriageReport Create(IEnumerable<ExtrasDifference> differences, RunHeader header,
        string axis, double threshold, int maxPages, TimeSpan timeout)
    {
        var cases = differences.OrderBy(d => d.Sample.Id, StringComparer.Ordinal).Select(d =>
        {
            using FileStream input = File.OpenRead(d.Sample.LocalPath);
            string hash = Convert.ToHexStringLower(SHA256.HashData(input));
            string[] diagnostics = d.Semantic?.Diagnostics.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() ?? [];
            var symptoms = new List<string>();
            if (d.SetAside) symptoms.Add("crash-regression");
            else if (d.RefusedByBoth) symptoms.Add("refused-by-both");
            else
            {
                if (d.BroilerReadFailed) symptoms.Add("broilerdoc-failure");
                if (d.LibreOfficeFailed) symptoms.Add("reference-incomplete");
                if (d.Semantic?.Differences.Count > 0) symptoms.Add("text-mismatch");
                if (d.Render is { } render)
                {
                    if (render.BroilerdocPages != render.LibreOfficePages) symptoms.Add("pagination-mismatch");
                    if (d.Checks.Any(c => c.Group == "pixel" && c.Outcome == CheckOutcome.Failed)) symptoms.Add("visual-mismatch");
                    else if (!Bands.IsBest(render.Bands)) symptoms.Add("visual-observation-within-threshold");
                }
                if (d.Checks.Any(c => c.Outcome == CheckOutcome.Skipped)) symptoms.Add("incomplete-checks");
            }
            string outcome = d.RefusedByBoth ? "refused-by-both" : d.SetAside ? "crash-regression" :
                d.HasDifference ? "failed" : d.Checks.Any(c => c.Outcome == CheckOutcome.Skipped) ? "partial" : "passed";
            return new TriageCase(d.Sample.Id, d.Sample.Format, d.Sample.SourceUrl, hash, outcome, d.SeverityScore,
                symptoms.ToArray(), diagnostics, d.Checks.OrderBy(c => c.Name, StringComparer.Ordinal)
                    .Select(c => new TriageCheck(c.Group, c.Name, c.Outcome.ToString().ToLowerInvariant(), c.Duration.TotalSeconds)).ToArray(),
                d.Render is null ? null : new TriageRender(d.Render.BroilerdocPages, d.Render.LibreOfficePages, d.Render.WorstPage, d.Render.Observed));
        }).ToArray();
        var clusters = cases.SelectMany(c => c.Symptoms.Concat(c.Diagnostics.Select(code => "diagnostic:" + code))
                .Select(observation => (observation, c.Id)))
            .GroupBy(entry => entry.observation, StringComparer.Ordinal)
            .Select(group => new TriageCluster(group.Key, group.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()))
            .OrderByDescending(group => group.Samples.Length).ThenBy(group => group.Observation, StringComparer.Ordinal).ToArray();
        return new TriageReport(1, header.Tool, new TriageEnvironment(header.LibreOffice, header.Rasterizer, header.FontSet,
            header.Platform, header.Dpi, threshold, axis, maxPages, timeout.TotalSeconds), cases, clusters, null);
    }

    public static TriageReport Load(string path)
    {
        try
        {
            TriageReport? report = JsonSerializer.Deserialize<TriageReport>(File.ReadAllText(path), JsonOptions);
            if (report is null || report.SchemaVersion != 1 || report.Environment is null || report.Cases is null ||
                report.Cases.Any(c => c is null || string.IsNullOrWhiteSpace(c.Id) || c.Checks is null || c.Diagnostics is null ||
                    c.Sha256 is null || c.Sha256.Length != 64 || !c.Sha256.All(Uri.IsHexDigit) ||
                    c.Checks.Any(check => check is null || string.IsNullOrWhiteSpace(check.Name) ||
                        check.Outcome is not ("passed" or "failed" or "skipped")) ||
                    c.Checks.Select(check => check.Name).Distinct(StringComparer.Ordinal).Count() != c.Checks.Length) ||
                report.Cases.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != report.Cases.Length)
                throw new InvalidDataException("Invalid or unsupported triage report: " + path);
            return report;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Could not read triage report: " + path, exception);
        }
    }

    public static TriageComparison Compare(TriageReport before, TriageReport after)
    {
        var oldEnvironment = JsonSerializer.SerializeToElement(before.Environment, JsonOptions);
        var newEnvironment = JsonSerializer.SerializeToElement(after.Environment, JsonOptions);
        string[] environmentChanges = oldEnvironment.EnumerateObject()
            .Where(p => p.Value.GetRawText() != newEnvironment.GetProperty(p.Name).GetRawText()).Select(p => p.Name).ToArray();
        var old = before.Cases.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var current = after.Cases.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var changes = new List<TriageChange>();
        foreach (TriageCase sample in after.Cases)
        {
            if (!old.TryGetValue(sample.Id, out TriageCase? prior))
            {
                changes.Add(new TriageChange(sample.Id, "new-sample", false, [], [], false, false, false, false));
                continue;
            }
            bool sameInput = sample.Sha256 == prior.Sha256;
            bool policyComparable = prior.Outcome is not ("refused-by-both" or "crash-regression") &&
                sample.Outcome is not ("refused-by-both" or "crash-regression");
            bool comparable = sameInput && environmentChanges.Length == 0 && policyComparable;
            var previousChecks = prior.Checks.ToDictionary(c => c.Name, c => c.Outcome, StringComparer.Ordinal);
            string[] fixedChecks = comparable ? sample.Checks.Where(c => c.Outcome == "passed" && previousChecks.GetValueOrDefault(c.Name) == "failed").Select(c => c.Name).ToArray() : [];
            string[] regressed = comparable ? sample.Checks.Where(c => c.Outcome == "failed" && previousChecks.GetValueOrDefault(c.Name) == "passed").Select(c => c.Name).ToArray() : [];
            bool coverageChanged = prior.Checks.Length != sample.Checks.Length || sample.Checks.Any(c =>
                !previousChecks.TryGetValue(c.Name, out string? status) || (status == "skipped") != (c.Outcome == "skipped"));
            bool measurementsChanged = !Equals(prior.Render, sample.Render);
            bool diagnosticsChanged = !prior.Diagnostics.SequenceEqual(sample.Diagnostics, StringComparer.Ordinal);
            bool worstPageChanged = prior.Render is not null && sample.Render is not null && prior.Render.WorstPage != sample.Render.WorstPage;
            bool metricsComparable = comparable && prior.Render is not null && sample.Render is not null && !worstPageChanged &&
                prior.Render.BroilerPages == sample.Render.BroilerPages && prior.Render.ReferencePages == sample.Render.ReferencePages;
            string status = !sameInput ? "input-changed" : environmentChanges.Length > 0 ? "environment-changed" :
                !policyComparable ? "separate-policy-group" :
                fixedChecks.Length > 0 && regressed.Length > 0 ? "mixed" : regressed.Length > 0 ? "regressed-checks" :
                fixedChecks.Length > 0 ? "fixed-checks" : coverageChanged ? "coverage-changed" :
                measurementsChanged ? "observations-changed" : diagnosticsChanged ? "diagnostics-changed" : "unchanged";
            if (status != "unchanged")
                changes.Add(new TriageChange(sample.Id, status, comparable, fixedChecks, regressed, diagnosticsChanged,
                    measurementsChanged, worstPageChanged, metricsComparable, prior.Render, sample.Render));
        }
        return new TriageComparison(environmentChanges.Length == 0, environmentChanges,
            old.Keys.Where(id => !current.Contains(id)).Order(StringComparer.Ordinal).ToArray(), changes.ToArray());
    }

    public static void Write(string directory, IReadOnlyList<ExtrasDifference> differences, RunHeader header,
        string axis, double threshold, int maxPages, TimeSpan timeout, int topCount, string? previousPath)
    {
        TriageReport report = Create(differences, header, axis, threshold, maxPages, timeout);
        if (previousPath is not null)
            report = report with { Comparison = Compare(Load(previousPath), report) };
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "triage.json"), JsonSerializer.Serialize(report, JsonOptions) + "\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "triage.md"), Markdown(report), new UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(directory, "all-samples.txt"), report.Cases.Select(c => c.Id), new UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(directory, "top-samples.txt"), ExtrasConformance.TopDifferences(differences, topCount).Select(d => d.Sample.Id), new UTF8Encoding(false));
    }

    internal static string Markdown(TriageReport report)
    {
        static string Cell(string value) => value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ").Replace("`", "'", StringComparison.Ordinal);
        var text = new StringBuilder("# Office triage\n\n");
        text.AppendLine("Observations are not root-cause classifications. Passing the pixel threshold does not establish full conformance or native export fidelity.");
        text.AppendLine().AppendLine($"Samples: {report.Cases.Length}; DPI: {report.Environment.Dpi}; threshold: {report.Environment.Threshold}%.");
        text.AppendLine("Use `top-samples.txt` for ranked failures, `all-samples.txt` for the complete control group, and `--sample-list` to replay them.");
        if (report.Environment.FontSet is null)
            text.AppendLine("Font set is unpinned; matching environment labels do not establish font equality.");
        text.AppendLine().AppendLine("## Observation groups\n").AppendLine("| Observation | Count | Examples |\n| --- | ---: | --- |");
        foreach (TriageCluster cluster in report.Clusters)
            text.AppendLine($"| {Cell(cluster.Observation)} | {cluster.Samples.Length} | {string.Join(", ", cluster.Samples.Take(5).Select(Cell))} |");
        if (report.Comparison is { } comparison)
        {
            text.AppendLine().AppendLine("## Previous run\n");
            if (!comparison.EnvironmentMatches)
                text.AppendLine("Environment differs: " + string.Join(", ", comparison.ChangedEnvironmentFields) + ". Check fixes/regressions are not inferred across it.");
            text.AppendLine($"Not run from previous selection: {comparison.NotRun.Length}. These are not counted as fixed.");
            text.AppendLine().AppendLine("| Sample | Observation | Fixed checks | Regressed checks | Worst-page metrics |\n| --- | --- | ---: | ---: | --- |");
            foreach (TriageChange change in comparison.Changes)
                text.AppendLine($"| {Cell(change.Id)} | {change.Status} | {change.FixedChecks.Length} | {change.RegressedChecks.Length} | {(change.WorstPageChanged ? "page changed; not comparable" : change.MetricsComparable ? "same page and page counts" : "not comparable / unavailable")} |");
        }
        return text.ToString().ReplaceLineEndings("\n");
    }
}
