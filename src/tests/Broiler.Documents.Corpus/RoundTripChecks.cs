using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Broiler.Documents.Corpus;

/// <summary>What one round trip observed, and the checks it produced.</summary>
internal sealed record RoundTripReading(
    IReadOnlyList<RoundTripOutcome> Outcomes,
    IReadOnlyList<CheckResult> Results);

/// <summary>
/// The fidelity half of the suite: write each document out through every format
/// and read it straight back, then hold the result against the baseline.
/// </summary>
/// <remarks>
/// <para>
/// One <c>roundtrip</c> invocation covers every target, so the whole matrix -
/// twenty documents through five formats each - costs twenty processes rather
/// than a hundred. The document that went in is the reference, which is why this
/// needs no second implementation to compare against.
/// </para>
/// <para>
/// A difference is not a defect. <c>RichTextDocument</c> is a normalized model
/// and the roadmap is explicit that source-preserving round trips are not a
/// goal, so the interesting question is never "did anything change" but "did
/// anything change that nobody had decided on". That is the whole reason the
/// baseline separates a documented limitation from a suspected defect instead of
/// recording one undifferentiated list of noise.
/// </para>
/// </remarks>
internal static class RoundTripChecks
{
    private const string Group = "roundtrip";

    public static async Task<RoundTripReading> RunAsync(
        BroilerDocTool tool,
        CorpusDocument document,
        Baseline? baseline)
    {
        var arguments = new List<string> { "roundtrip", document.Path, "--json" };
        foreach (CorpusFormat format in CorpusFormat.All)
        {
            arguments.Add("--via");
            arguments.Add(format.Key);
        }

        ToolRun run = await tool.RunAsync(arguments, standardInput: null).ConfigureAwait(false);
        string label = document.Label;

        // 0 and 5 are both verdicts. Anything else means the run did not reach
        // one, and then there is nothing to compare against a baseline.
        if (run.ExitCode is not (0 or 5))
        {
            return new RoundTripReading([],
            [
                CheckResult.Fail(Group, "roundtrip/" + label,
                    "expected exit 0 or 5 - a verdict either way - but the tool exited " + run.ExitCode + ".",
                    run.CommandLine()),
            ]);
        }

        JsonElement? json = run.Json();
        if (json is null || !json.Value.TryGetProperty("results", out JsonElement results))
        {
            return new RoundTripReading([],
            [
                CheckResult.Fail(Group, "roundtrip/" + label,
                    "the --json payload carried no results array.", run.CommandLine()),
            ]);
        }

        var outcomes = new List<RoundTripOutcome>();
        var checks = new List<CheckResult>();

        foreach (JsonElement result in results.EnumerateArray())
        {
            string via = result.GetProperty("format").GetString() ?? "?";
            CorpusFormat? viaFormat = CorpusFormat.All.FirstOrDefault(format => format.ToolName == via);
            if (viaFormat is null)
            {
                checks.Add(CheckResult.Fail(Group, "roundtrip/" + label + "/" + via,
                    "the tool reported a hop through a format this runner does not know.", run.CommandLine()));
                continue;
            }

            RoundTripOutcome outcome = Read(document, viaFormat, result);
            outcomes.Add(outcome);
            checks.Add(Compare(outcome, baseline, run));
        }

        // A hop that never reported is not a pass. Without this a `roundtrip`
        // that silently stopped emitting one of its `--via` results would leave
        // the whole matrix green.
        string[] absent = CorpusFormat.All
            .Select(format => format.Key)
            .Except(outcomes.Select(outcome => outcome.Via), StringComparer.Ordinal)
            .ToArray();

        checks.Add(absent.Length == 0
            ? CheckResult.Pass(Group, "roundtrip/" + label + "/complete", run.Duration)
            : CheckResult.Fail(Group, "roundtrip/" + label + "/complete",
                "the tool was asked for every format but reported nothing for: " + string.Join(", ", absent) + ".",
                run.CommandLine()));

        return new RoundTripReading(outcomes, checks);
    }

    private static RoundTripOutcome Read(CorpusDocument document, CorpusFormat via, JsonElement result)
    {
        JsonElement comparison = result.GetProperty("comparison");

        string[] differences = comparison.GetProperty("differences").EnumerateArray()
            .Select(difference =>
                (difference.GetProperty("kind").GetString() ?? "?") + "@p" +
                Paragraph(difference) + ": " +
                (difference.GetProperty("detail").GetString() ?? string.Empty))
            .ToArray();

        // The read summaries are excluded and nothing else is. Every package
        // reader emits one at info severity on every document, so carrying them
        // would fill the baseline with rows saying only that a read happened.
        //
        // Excluding all of info was the first attempt and it was wrong. A codec
        // may report a real skip at info - the RTF reader says `rtf.embedded`
        // when it steps over a picture, which is the codec doing exactly what
        // this component asks of it - and a baseline that hid those made a
        // documented, tested, reported behaviour look like a silent loss. It was
        // classified as a defect on that basis.
        string[] diagnostics = Codes(result, "writeDiagnostics")
            .Concat(Codes(result, "readDiagnostics"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new RoundTripOutcome(
            document.Id,
            document.Format.Key,
            via.Key,
            comparison.GetProperty("equal").GetBoolean(),
            comparison.GetProperty("plainTextEqual").GetBoolean(),
            comparison.GetProperty("formatCodesEqual").GetBoolean(),
            differences,
            diagnostics);
    }

    private static string Paragraph(JsonElement difference) =>
        difference.TryGetProperty("leftParagraph", out JsonElement left) && left.ValueKind == JsonValueKind.Number
            ? left.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "?";

    private static IEnumerable<string> Codes(JsonElement result, string property) =>
        result.TryGetProperty(property, out JsonElement diagnostics)
            ? diagnostics.EnumerateArray()
                .Select(diagnostic => diagnostic.GetProperty("code").GetString() ?? "?")
                .Where(code => !code.EndsWith(".read.summary", StringComparison.Ordinal))
            : [];

    private static CheckResult Compare(RoundTripOutcome outcome, Baseline? baseline, ToolRun run)
    {
        string name = "roundtrip/" + outcome.Sample + "/" + outcome.Source + "/" + outcome.Via;
        bool clean = outcome.Equal && outcome.Diagnostics.Count == 0;
        BaselineRow? row = baseline?.Find(outcome);

        if (baseline is null)
            return CheckResult.Skip(Group, name, "running without a baseline, so nothing was compared.");

        if (row is null)
        {
            if (clean)
                return CheckResult.Pass(Group, name, run.Duration);

            return CheckResult.Fail(Group, name,
                "this round trip is not in the baseline, so it is expected to be lossless and silent, and it " +
                "was neither.\n" + Describe(outcome) +
                "\n      If it is a limitation somebody has decided on, record it: --update-baseline writes " +
                "the row, and the state and the why are then yours to fill in.",
                run.CommandLine());
        }

        var differences = new List<string>();

        string[] newDifferences = outcome.Differences.Except(row.Differences, StringComparer.Ordinal).ToArray();
        string[] goneDifferences = row.Differences.Except(outcome.Differences, StringComparer.Ordinal).ToArray();
        string[] newDiagnostics = outcome.Diagnostics.Except(row.Diagnostics, StringComparer.Ordinal).ToArray();
        string[] goneDiagnostics = row.Diagnostics.Except(outcome.Diagnostics, StringComparer.Ordinal).ToArray();

        if (newDifferences.Length > 0)
            differences.Add("new difference(s): " + string.Join("; ", newDifferences));
        if (goneDifferences.Length > 0)
            differences.Add("difference(s) the baseline expects and this run did not see: " +
                            string.Join("; ", goneDifferences));
        if (newDiagnostics.Length > 0)
            differences.Add("new diagnostic(s): " + string.Join(", ", newDiagnostics));
        if (goneDiagnostics.Length > 0)
            differences.Add("diagnostic(s) the baseline expects and this run did not see: " +
                            string.Join(", ", goneDiagnostics));

        if (differences.Count == 0)
            return CheckResult.Pass(Group, name, run.Duration);

        string closing = goneDifferences.Length > 0 || goneDiagnostics.Length > 0
            ? "\n      A loss that stopped happening is good news and still fails: update the baseline so the " +
              "improvement is recorded rather than absorbed."
            : string.Empty;

        return CheckResult.Fail(Group, name,
            "the round trip changed against the baseline (" + row.State + ").\n      " +
            string.Join("\n      ", differences) + closing,
            run.CommandLine());
    }

    private static string Describe(RoundTripOutcome outcome)
    {
        var lines = new List<string>();
        if (!outcome.PlainTextEqual)
            lines.Add("      the plain text itself changed, which is the strongest form of this failure");
        foreach (string difference in outcome.Differences)
            lines.Add("      " + difference);
        if (outcome.Diagnostics.Count > 0)
            lines.Add("      diagnostics: " + string.Join(", ", outcome.Diagnostics));

        return string.Join("\n", lines);
    }
}
