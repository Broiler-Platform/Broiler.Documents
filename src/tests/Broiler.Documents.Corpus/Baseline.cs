using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Broiler.Documents.Corpus;

/// <summary>
/// What a round trip through one format did to one sample, reduced to the
/// things worth comparing across runs.
/// </summary>
/// <remarks>
/// Absolute paths, byte counts and durations are deliberately not part of this.
/// A baseline that carried a temporary directory would differ on every run, and
/// one that carried a byte count would fail whenever a writer changed something
/// that made no difference to the document.
/// </remarks>
internal sealed record RoundTripOutcome(
    string Sample,
    string Source,
    string Via,
    bool Equal,
    bool PlainTextEqual,
    bool FormatCodesEqual,
    IReadOnlyList<string> Differences,
    IReadOnlyList<string> Diagnostics)
{
    public string Key => Sample + "/" + Source + "/" + Via;
}

/// <summary>Why a recorded difference is in the baseline.</summary>
internal static class BaselineState
{
    /// <summary>A limitation one of the conformance documents already states.</summary>
    public const string Documented = "documented";

    /// <summary>
    /// A difference nobody decided on. The CLI guide calls this out as the thing
    /// round-tripping is actually good for, and the distinction is kept in the
    /// data rather than in a comment so the runner can report the two counts
    /// separately and a reviewer can see the second list shrink.
    /// </summary>
    public const string SuspectedDefect = "suspected-defect";

    /// <summary>
    /// Looked at, understood, and accepted as the normalized model's own
    /// behaviour rather than a format limitation or a defect.
    /// </summary>
    public const string Accepted = "accepted";
}

internal sealed record BaselineRow(
    string Sample,
    string Source,
    string Via,
    IReadOnlyList<string> Differences,
    IReadOnlyList<string> Diagnostics,
    string State,
    string? Reference,
    string Why,
    string? Reviewer,
    string? Decided)
{
    public string Key => Sample + "/" + Source + "/" + Via;
}

/// <summary>
/// The expected shape of the corpus's losses, as
/// <c>tests/corpus/corpus-baseline.json</c> records it.
/// </summary>
/// <remarks>
/// <para>
/// It records only what is <em>not</em> lossless. Everything absent from it is
/// asserted to round trip without a difference and without a warning, so a new
/// loss fails by not being listed rather than by being listed differently. That
/// way round the file stays readable, and the check that matters - "nothing new
/// broke" - does not depend on somebody having written a row for the case that
/// broke.
/// </para>
/// <para>
/// A row that stops reproducing also fails. That reads as harsh for a fix, and
/// it is deliberate: the point of separating <c>documented</c> from
/// <c>suspected-defect</c> is to watch the second list shrink, and a shrink
/// nobody records is a shrink nobody can show.
/// </para>
/// </remarks>
internal sealed class Baseline
{
    public const string RelativePath = "tests/corpus/corpus-baseline.json";

    private readonly Dictionary<string, BaselineRow> _rows;

    private Baseline(IEnumerable<BaselineRow> rows) =>
        _rows = rows.ToDictionary(row => row.Key, StringComparer.Ordinal);

    public IReadOnlyCollection<BaselineRow> Rows => _rows.Values;

    public static string PathIn(string repositoryRoot) =>
        Path.Combine(repositoryRoot, RelativePath.Replace('/', Path.DirectorySeparatorChar));

    public static Baseline Load(string repositoryRoot)
    {
        string path = PathIn(repositoryRoot);
        if (!File.Exists(path))
            throw new FileNotFoundException("No baseline at " + path + ". Write one with --update-baseline.");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        int version = root.GetProperty("schemaVersion").GetInt32();
        if (version != 1)
        {
            throw new InvalidDataException(
                "corpus-baseline.json declares schemaVersion " +
                version.ToString(CultureInfo.InvariantCulture) + ", which this runner cannot read.");
        }

        return new Baseline(root.GetProperty("roundTrips").EnumerateArray().Select(row => new BaselineRow(
            row.GetProperty("sample").GetString() ?? string.Empty,
            row.GetProperty("source").GetString() ?? string.Empty,
            row.GetProperty("via").GetString() ?? string.Empty,
            Strings(row, "differences"),
            Strings(row, "diagnostics"),
            row.GetProperty("state").GetString() ?? BaselineState.SuspectedDefect,
            row.TryGetProperty("reference", out JsonElement reference) ? reference.GetString() : null,
            row.GetProperty("why").GetString() ?? string.Empty,
            row.TryGetProperty("reviewer", out JsonElement reviewer) ? reviewer.GetString() : null,
            row.TryGetProperty("decided", out JsonElement decided) ? decided.GetString() : null)));
    }

    public BaselineRow? Find(RoundTripOutcome outcome) =>
        _rows.TryGetValue(outcome.Key, out BaselineRow? row) ? row : null;

    /// <summary>Rows that no outcome in this run reached.</summary>
    public IEnumerable<BaselineRow> NotReproduced(IEnumerable<RoundTripOutcome> outcomes)
    {
        var seen = outcomes.Select(outcome => outcome.Key).ToHashSet(StringComparer.Ordinal);
        return _rows.Values.Where(row => !seen.Contains(row.Key));
    }

    /// <summary>
    /// Writes a baseline from what a run actually observed, preserving the
    /// classification and prose of any row that is already there.
    /// </summary>
    /// <remarks>
    /// The carry-over is the point. Regenerating a baseline is a mechanical act
    /// and the state and the why are not: throwing away a reviewer's judgement
    /// every time somebody adds a sample would make the classification decay to
    /// whatever the last regeneration guessed.
    /// </remarks>
    public static void Write(
        string repositoryRoot,
        IReadOnlyList<RoundTripOutcome> outcomes,
        Baseline? existing,
        string today)
    {
        var rows = new JsonArray();
        foreach (RoundTripOutcome outcome in outcomes
                     .Where(outcome => !outcome.Equal || outcome.Diagnostics.Count > 0)
                     .OrderBy(outcome => outcome.Sample, StringComparer.Ordinal)
                     .ThenBy(outcome => outcome.Source, StringComparer.Ordinal)
                     .ThenBy(outcome => outcome.Via, StringComparer.Ordinal))
        {
            BaselineRow? previous = existing?.Find(outcome);
            rows.Add(new JsonObject
            {
                ["sample"] = outcome.Sample,
                ["source"] = outcome.Source,
                ["via"] = outcome.Via,
                ["differences"] = new JsonArray(outcome.Differences.Select(text => (JsonNode)text!).ToArray()),
                ["diagnostics"] = new JsonArray(outcome.Diagnostics.Select(text => (JsonNode)text!).ToArray()),
                ["state"] = previous?.State ?? BaselineState.SuspectedDefect,
                ["reference"] = previous?.Reference,
                ["why"] = previous?.Why ?? "Not yet classified. A row regenerated into this file starts as a " +
                                           "suspected defect on purpose: an unreviewed difference should read as " +
                                           "one until somebody says otherwise.",
                ["reviewer"] = previous?.Reviewer,
                ["decided"] = previous?.Decided,
            });
        }

        var document = new JsonObject
        {
            ["$schema"] = "corpus-baseline.schema.json",
            ["schemaVersion"] = 1,
            ["updated"] = today,
            ["policy"] = new JsonObject
            {
                ["records"] = "Only the round trips that lose something or warn about something. Everything " +
                              "this corpus can do that is absent from the list below is asserted to round trip " +
                              "clean, so a new loss fails by not being listed.",
                ["reproduce"] = "A row that stops reproducing fails too. That is deliberate: a fix should " +
                                "arrive with this file updated, so the shrinking of the suspected-defect list " +
                                "is a thing somebody can point at.",
                ["states"] = "documented - a limitation one of the conformance documents states. " +
                             "suspected-defect - a difference nobody decided on, which is what the CLI guide " +
                             "says round-tripping is actually good for. " +
                             "accepted - the normalized model's own behaviour, looked at and accepted.",
                ["reviewer"] = "A row is not classified by being written down. documented and accepted both " +
                               "need a reviewer and a date, and a documented row needs a reference to the " +
                               "document that states the limitation. CorpusBaselineGuardTests enforces it, " +
                               "the way PdfTestControlGuardTests does for the tool manifest.",
                ["regenerate"] = "dotnet run --project src/tests/Broiler.Documents.Corpus -- --update-baseline",
            },
            ["roundTrips"] = rows,
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            // The corpus carries non-Latin text and characters every format has
            // to escape. Left to the default encoder they would land in this
            // file as \uXXXX and a reviewer could not read the row they are
            // being asked to classify.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        // Written with LF whichever host regenerated it. The indenting writer
        // takes its newline from the environment, and .gitattributes normalises
        // nothing but shell scripts, so a Windows regeneration would otherwise
        // commit a whole-file diff against a Linux one and neither would be
        // wrong.
        File.WriteAllText(
            PathIn(repositoryRoot),
            document.ToJsonString(options).ReplaceLineEndings("\n") + "\n");
    }

    private static IReadOnlyList<string> Strings(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement array)
            ? array.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray()
            : [];
}
