using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Broiler.Documents.Corpus;

namespace Broiler.Documents.Office;

/// <summary>Strict byte fidelity checks, independent of the rendering toolchain.</summary>
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
        Console.WriteLine("axis        strict byte fidelity");
        Console.WriteLine("workspace   " + workspace);

        var log = new RunLog();
        await Parallelism.ForEachAsync(samples, options.Jobs, async sample =>
        {
            string output = Path.Combine(workspace, "resave", sample.Id);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            // A reused --workspace must never let a stale output pass a check.
            File.Delete(output);
            ToolRun run = await tool.RunAsync(["convert", sample.LocalPath, "--from", sample.Format,
                "--out", output, "--to", sample.Format, "--quiet"]).ConfigureAwait(false);
            log.Add(Evaluate(sample, output, run));
        }).ConfigureAwait(false);

        CheckResult[] results = log.Results.OrderBy(r => r.Name, StringComparer.Ordinal).ToArray();
        var header = new RunHeader(tool.Description, null, null, null, System.Runtime.InteropServices.RuntimeInformation.OSDescription, 0, false);
        if (options.JsonReport is not null)
            OfficeReporting.WriteJson(options.JsonReport, results, [], [], header);
        if (options.JUnitReport is not null)
            OfficeReporting.WriteJUnit(options.JUnitReport, results);
        if (options.IssueReport is not null)
        {
            var markdown = new StringBuilder("# Office re-save conformance\n\n");
            markdown.AppendLine("Comparison: **strict byte identity**. Serialization changes count as failures even when content is preserved.");
            markdown.AppendLine($"\nSamples: {results.Length}; passed: {results.Count(r => r.Outcome == CheckOutcome.Passed)}; failed: {results.Count(r => r.Outcome == CheckOutcome.Failed)}.");
            markdown.AppendLine();
            using var writer = new StringWriter();
            OfficeReporting.WriteConsole(writer, results, options.Verbose);
            markdown.AppendLine("```text").Append(writer).AppendLine("```");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.IssueReport))!);
            File.WriteAllText(options.IssueReport, markdown.ToString());
        }

        string[] failures = results.Where(r => r.Outcome == CheckOutcome.Failed)
            .Select(r => r.Name["resave/".Length..]).ToArray();
        if (options.Incremental || (options.Only is null && options.MaxFiles is null && selected is null))
            FailedDocuments.Save(trackedPath, FailedDocuments.UpdateTracked(prior, samples.Select(s => s.Id), failures));
        OfficeReporting.WriteConsole(Console.Out, results, options.Verbose);
        return OfficeExitCodes.ForFailures(failures.Length);
    }

    internal static CheckResult Evaluate(ExtrasSample sample, string output, ToolRun run)
    {
        string name = "resave/" + sample.Id;
        if (run.ExitCode != 0)
            return CheckResult.ExpectExit("resave", name, 0, run);
        if (!File.Exists(output))
            return CheckResult.Fail("resave", name, "convert exited 0 but produced no output file.", run.CommandLine());
        using var original = File.OpenRead(sample.LocalPath);
        using var saved = File.OpenRead(output);
        if (original.Length != saved.Length || !EqualBytes(original, saved))
            return CheckResult.Fail("resave", name,
                $"not byte-identical (input {original.Length} bytes; output {saved.Length} bytes).", run.CommandLine());
        return CheckResult.Pass("resave", name, run.Duration);
    }

    private static bool EqualBytes(Stream left, Stream right)
    {
        byte[] a = new byte[81920], b = new byte[81920];
        int count;
        while ((count = left.Read(a)) > 0)
            if (right.ReadAtLeast(b, count, throwOnEndOfStream: false) != count ||
                !a.AsSpan(0, count).SequenceEqual(b.AsSpan(0, count))) return false;
        return true;
    }
}
