using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Broiler.Documents.Corpus;

namespace Broiler.Documents.Office;

/// <summary>One check's worth of disagreement, in the shape a baseline row stores.</summary>
internal sealed record SemanticReading(
    string Seed,
    string Via,
    string Check,
    IReadOnlyList<string> Differences,
    IReadOnlyList<string> Diagnostics)
{
    public string Key => Seed + "/" + Via + "/" + Check;
}

/// <summary>
/// What broilerdoc makes of a document LibreOffice wrote.
/// </summary>
/// <remarks>
/// <para>
/// Four questions, in ascending order of how much they prove. Does broilerdoc
/// identify the format? Does it read the file at all? Does its plain-text
/// projection agree with LibreOffice's own? And does a document broilerdoc
/// writes still say the same thing when LibreOffice reads it back?
/// </para>
/// <para>
/// The third is the headline. It asks LibreOffice to read back a file
/// LibreOffice itself produced moments earlier, which is the strongest
/// available statement of what that file is supposed to contain, and it
/// compares that with what this component made of the same bytes. A
/// disagreement means one of the two readers dropped, duplicated, reordered or
/// invented content, and no formatting difference can produce one.
/// </para>
/// <para>
/// The fourth is the direction nothing else in this repository tests. It makes
/// LibreOffice a reader-oracle for Broiler's writers: the corpus suite can tell
/// you that an export reads back correctly <em>here</em>, and cannot tell you
/// whether anybody else can read it.
/// </para>
/// <para>
/// What none of them prove: conformance. LibreOffice is not a specification.
/// For DOCX the reference is Word, for ODT the OASIS standard, for RTF
/// Microsoft's own. Where the two disagree this component may be right and
/// LibreOffice wrong, which is what a <c>documented</c> baseline row citing a
/// conformance document is for.
/// </para>
/// </remarks>
internal static class SemanticChecks
{
    private const string Group = "semantic";

    public static async Task<SemanticOutcome> RunAsync(
        BroilerDocTool tool,
        OfficeWorkspace space,
        OfficeDocument document,
        OfficeBaseline? baseline,
        bool comparing,
        string notComparing)
    {
        var results = new List<CheckResult>();
        var readings = new List<SemanticReading>();
        string id = document.Seed.Id;
        string via = document.Via;

        // probe - the format is what LibreOffice was asked to write. A wrong
        // answer here is not a nuance: every later check would be measuring a
        // codec nobody meant to exercise.
        ToolRun probe = await tool.RunAsync(["probe", document.Path, "--json", "--quiet"]).ConfigureAwait(false);
        string expected = LibreOfficeFilters.BroilerdocFormat(via);
        string? selected = Text(probe.Json(), "selected");
        results.Add(string.Equals(selected, expected, StringComparison.Ordinal)
            ? CheckResult.Pass("probe", "probe/" + id + "/" + via, probe.Duration)
            : CheckResult.Fail("probe", "probe/" + id + "/" + via,
                "probe selected " + (selected ?? "nothing") + " where " + expected + " was written.",
                probe.CommandLine()));

        // read - exit code first, because exit 70 is documented as always a
        // defect in the tool and deserves to be said in those words.
        ToolRun info = await tool
            .RunAsync(["info", document.Path, "--json", "--quiet"])
            .ConfigureAwait(false);

        if (info.ExitCode != 0)
        {
            string detail = "info exited " + info.ExitCode.ToString(CultureInfo.InvariantCulture) +
                            " on a document LibreOffice wrote and reads back itself.";
            if (info.ExitCode == 70)
                detail += " Exit 70 is documented as always a defect in the tool.";

            results.Add(CheckResult.Fail("read", "read/" + id + "/" + via, detail, info.CommandLine()));
        }
        else
        {
            results.Add(CheckResult.Pass("read", "read/" + id + "/" + via, info.Duration));
        }

        IReadOnlyList<string> diagnostics = Diagnostics(info.Json());

        // expect - the floor the seed states about itself. It is deliberately
        // coarse: a paragraph count is answered differently by each target for a
        // list item or a table cell, so the manifest states the number every
        // target has to reach and no more. It exists because a seed that lost
        // most of its content still reads, still probes, and still agrees with
        // LibreOffice about the little that survived - all three checks above
        // pass on a document that is nearly empty, and nothing else would say so.
        int? paragraphs = Statistic(info.Json(), "paragraphs");
        if (info.ExitCode != 0)
        {
            results.Add(CheckResult.Skip("read", "expect/" + id + "/" + via,
                "the document could not be read, so there was nothing to hold the seed's " +
                "expectation against."));
        }
        else if (paragraphs is null)
        {
            results.Add(CheckResult.Fail("read", "expect/" + id + "/" + via,
                "info reported no paragraph count, so the seed's expectation could not be checked.",
                info.CommandLine()));
        }
        else if (paragraphs < document.Seed.Expect.MinParagraphs)
        {
            results.Add(CheckResult.Fail("read", "expect/" + id + "/" + via,
                "the seed states at least " +
                document.Seed.Expect.MinParagraphs.ToString(CultureInfo.InvariantCulture) +
                " paragraph(s) and this document read back " +
                paragraphs.Value.ToString(CultureInfo.InvariantCulture) + ".",
                info.CommandLine()));
        }
        else
        {
            results.Add(CheckResult.Pass("read", "expect/" + id + "/" + via));
        }

        // text - the headline check.
        LibreOfficeRun extracted = space.ExtractText(document);
        CheckResult extraction = OfficeWorkspace.Validate(
            "text", "text/" + id + "/" + via + "/extract", extracted, "Text (encoded):UTF8", 1);

        if (extraction.Outcome != CheckOutcome.Passed)
        {
            results.Add(extraction);
            results.Add(CheckResult.Skip("text", "text/" + id + "/" + via,
                "LibreOffice could not produce its own text projection, so there was nothing to " +
                "compare against."));
        }
        else
        {
            ToolRun dumped = await tool
                .RunAsync(["dump", document.Path, "--as", "text", "--quiet"])
                .ConfigureAwait(false);

            if (dumped.ExitCode != 0)
            {
                results.Add(CheckResult.Fail("text", "text/" + id + "/" + via,
                    "dump --as text exited " + dumped.ExitCode.ToString(CultureInfo.InvariantCulture) + ".",
                    dumped.CommandLine()));
            }
            else
            {
                string theirs = Read(extracted.OutputPath!);
                string ours = dumped.StandardOutput;
                IReadOnlyList<string> differences = Compare(document.Seed, ours, theirs, "broilerdoc", "libreoffice");
                var reading = new SemanticReading(id, via, "text", differences, diagnostics);
                readings.Add(reading);
                results.Add(Hold(reading, baseline, comparing, notComparing, dumped.CommandLine()));
            }
        }

        // interop - the reverse direction. ODT is the vehicle because it is the
        // format both implementations treat as native, so a difference found
        // here is least likely to be about the container.
        string ourDirectory = space.Area("interop", id, via);
        string ourPath = Path.Combine(ourDirectory, id + ".odt");
        ToolRun converted = await tool
            .RunAsync(["convert", document.Path, "--out", ourPath, "--quiet"])
            .ConfigureAwait(false);

        if (converted.ExitCode != 0 || !File.Exists(ourPath))
        {
            results.Add(CheckResult.Fail("interop", "interop/" + id + "/" + via,
                "convert to ODT exited " + converted.ExitCode.ToString(CultureInfo.InvariantCulture) +
                " on a document LibreOffice wrote.",
                converted.CommandLine()));
        }
        else if (extraction.Outcome != CheckOutcome.Passed)
        {
            results.Add(CheckResult.Skip("interop", "interop/" + id + "/" + via,
                "there is no LibreOffice text projection of the original to compare ours against."));
        }
        else
        {
            LibreOfficeRun back = space.ExtractTextOfOurs(document, ourPath);
            CheckResult reread = OfficeWorkspace.Validate(
                "interop", "interop/" + id + "/" + via + "/extract", back, "Text (encoded):UTF8", 1);

            if (reread.Outcome != CheckOutcome.Passed)
            {
                results.Add(reread);
            }
            else
            {
                string theirsOfOurs = Read(back.OutputPath!);
                string theirsOfTheirs = Read(extracted.OutputPath!);
                IReadOnlyList<string> differences = Compare(
                    document.Seed, theirsOfTheirs, theirsOfOurs, "libreoffice-of-its-own", "libreoffice-of-ours");
                var reading = new SemanticReading(id, via, "interop", differences, []);
                readings.Add(reading);
                results.Add(Hold(reading, baseline, comparing, notComparing, converted.CommandLine()));
            }
        }

        // roundtrip - the existing discipline, applied to foreign input. The
        // corpus suite round-trips documents this component wrote; the new
        // information is whether the same holds for one it did not.
        ToolRun trip = await tool
            .RunAsync(["roundtrip", document.Path, "--via", via, "--json", "--quiet"])
            .ConfigureAwait(false);

        if (trip.ExitCode is not (0 or 5))
        {
            results.Add(CheckResult.Fail("roundtrip", "roundtrip/" + id + "/" + via,
                "roundtrip exited " + trip.ExitCode.ToString(CultureInfo.InvariantCulture) +
                ", which is neither the positive verdict nor the negative one.",
                trip.CommandLine()));
        }
        else if (!HasResults(trip.Json()))
        {
            // Absence of a payload is not agreement. Both readers of the JSON
            // below answer an empty list when the object is missing or does not
            // parse, so a --quiet regression that swallowed stdout, or a banner
            // printed ahead of the object, would have produced a clean round
            // trip of a document nothing looked at. The probe check above
            // already refuses to read a null payload as an answer; this is the
            // same refusal.
            results.Add(CheckResult.Fail("roundtrip", "roundtrip/" + id + "/" + via,
                "roundtrip exited " + trip.ExitCode.ToString(CultureInfo.InvariantCulture) +
                " and produced no JSON object carrying a non-empty 'results' array, so there is " +
                "nothing to compare and no evidence the round trip happened.",
                trip.CommandLine()));
        }
        else
        {
            var reading = new SemanticReading(
                id, via, "roundtrip", RoundTripDifferences(trip.Json()), RoundTripDiagnostics(trip.Json()));
            readings.Add(reading);
            results.Add(Hold(reading, baseline, comparing, notComparing, trip.CommandLine()));
        }

        return new SemanticOutcome(results, readings);
    }

    /// <summary>
    /// Holds one reading against the committed baseline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule is the corpus suite's, and it is the right way round: the file
    /// records only what is not clean, so <em>a new loss fails by not being
    /// listed</em>. Everything absent is asserted to be lossless and silent,
    /// which keeps the file readable and keeps the check that matters from
    /// depending on somebody having written a row for the case that broke.
    /// </para>
    /// <para>
    /// A row that stops reproducing fails too, including when it stops because
    /// the difference was fixed. That reads as harsh and it is deliberate: the
    /// point of separating a documented limitation from a suspected defect is to
    /// watch the second list shrink, and a shrink nobody records is a shrink
    /// nobody can show.
    /// </para>
    /// </remarks>
    private static CheckResult Hold(
        SemanticReading reading, OfficeBaseline? baseline, bool comparing, string notComparing,
        string command)
    {
        string name = reading.Check + "/" + reading.Seed + "/" + reading.Via;
        bool clean = reading.Differences.Count == 0 && reading.Diagnostics.Count == 0;

        if (!comparing)
        {
            return CheckResult.Skip(reading.Check, name, notComparing);
        }

        ReadRow? row = baseline?.FindRead(reading.Seed, reading.Via, reading.Check);

        if (row is null)
        {
            return clean
                ? CheckResult.Pass(reading.Check, name)
                : CheckResult.Fail(reading.Check, name,
                    "no baseline row, so this was expected to be lossless and silent, and it was " +
                    "neither." + Listing(reading), command);
        }

        if (Same(row.Differences, reading.Differences) && Same(row.Diagnostics, reading.Diagnostics))
            return CheckResult.Pass(reading.Check, name);

        if (clean)
        {
            return CheckResult.Fail(reading.Check, name,
                "the baseline records a difference here and this run found none. A difference that " +
                "stopped happening is good news and still fails: update the baseline so the " +
                "improvement is recorded rather than absorbed.", command);
        }

        return CheckResult.Fail(reading.Check, name,
            "the difference is not the one the baseline records." +
            Listing(reading) +
            "\n      recorded: " + Join(row.Differences) +
            (row.Diagnostics.Count > 0 ? "\n      recorded diagnostics: " + Join(row.Diagnostics) : string.Empty),
            command);
    }

    private static string Listing(SemanticReading reading)
    {
        var text = new StringBuilder();
        foreach (string difference in reading.Differences)
            text.Append("\n      ").Append(difference);

        if (reading.Diagnostics.Count > 0)
            text.Append("\n      diagnostics: ").Append(Join(reading.Diagnostics));

        return text.ToString();
    }

    private static string Join(IReadOnlyList<string> items) =>
        items.Count == 0 ? "(none)" : string.Join(", ", items);

    /// <summary>
    /// Whether two difference lists say the same thing.
    /// </summary>
    /// <remarks>
    /// Compared as sets, because the two sides are ordered differently on
    /// purpose and neither ordering is wrong. A run produces its lines in
    /// document order, which is what a person reading a failure wants;
    /// <see cref="OfficeBaseline.Write"/> sorts them before committing, which is
    /// what makes two versions of the file diff cleanly. Comparing them as
    /// sequences made every row fail against a baseline holding exactly the same
    /// lines, with a message listing both and no visible difference between
    /// them - the worst kind of failure to be handed.
    /// </remarks>
    private static bool Same(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.Count == right.Count &&
        left.OrderBy(line => line, StringComparer.Ordinal)
            .SequenceEqual(right.OrderBy(line => line, StringComparer.Ordinal), StringComparer.Ordinal);

    private static IReadOnlyList<string> Compare(
        OfficeSeed seed, string left, string right, string leftLabel, string rightLabel)
    {
        foreach (TextIgnore ignore in seed.TextIgnore)
        {
            left = ignore.Apply(left);
            right = ignore.Apply(right);
        }

        return TextProjection.Differences(left, right, leftLabel, rightLabel);
    }

    private static string Read(string path)
    {
        // The bytes rather than a decoded string, because LibreOffice writes a
        // UTF-8 BOM here and the canonicaliser is the one place that should
        // know about it.
        byte[] bytes = File.ReadAllBytes(path);
        return new UTF8Encoding(false).GetString(bytes);
    }

    /// <summary>One number out of the <c>statistics</c> block, or null when absent.</summary>
    private static int? Statistic(JsonElement? json, string property)
    {
        if (json is not { } element ||
            !element.TryGetProperty("statistics", out JsonElement statistics) ||
            !statistics.TryGetProperty(property, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return value.GetInt32();
    }

    private static string? Text(JsonElement? json, string property) =>
        json is { } element && element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// The diagnostic codes a read produced, minus the per-format read summary.
    /// </summary>
    /// <remarks>
    /// Only the summaries are excluded, and the corpus suite's experience says
    /// why the wider filter is wrong. Filtering every <c>info</c> diagnostic
    /// kept the file short and also hid a codec doing precisely what this
    /// component asks of it - reporting that it skipped something - which made a
    /// correct codec look silent and put a defect in the baseline that was never
    /// there.
    /// </remarks>
    private static IReadOnlyList<string> Diagnostics(JsonElement? json)
    {
        if (json is not { } element || !element.TryGetProperty("diagnostics", out JsonElement array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var codes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (JsonElement diagnostic in array.EnumerateArray())
        {
            if (!diagnostic.TryGetProperty("code", out JsonElement code) ||
                code.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string? value = code.GetString();
            if (value is not null && !value.EndsWith(".read.summary", StringComparison.Ordinal))
                codes.Add(value);
        }

        return codes.ToArray();
    }

    /// <summary>
    /// Whether the tool actually reported on at least one round trip.
    /// </summary>
    /// <remarks>
    /// An empty <c>results</c> array is refused as well as a missing one: a run
    /// that round-tripped zero formats reached no verdict, and reading that as
    /// "nothing differed" is the same mistake in a smaller hat.
    /// </remarks>
    private static bool HasResults(JsonElement? json) =>
        json is { } element &&
        element.TryGetProperty("results", out JsonElement results) &&
        results.ValueKind == JsonValueKind.Array &&
        results.GetArrayLength() > 0;

    private static IReadOnlyList<string> RoundTripDifferences(JsonElement? json)
    {
        if (json is not { } element || !element.TryGetProperty("results", out JsonElement results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var lines = new SortedSet<string>(StringComparer.Ordinal);
        foreach (JsonElement result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("comparison", out JsonElement comparison))
                continue;

            if (comparison.TryGetProperty("plainTextEqual", out JsonElement plain) &&
                plain.ValueKind == JsonValueKind.False)
            {
                lines.Add("plain text differs");
            }

            if (!comparison.TryGetProperty("differences", out JsonElement differences) ||
                differences.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement difference in differences.EnumerateArray())
            {
                string? text = Spell(difference);
                if (!string.IsNullOrWhiteSpace(text))
                    lines.Add(text!.Trim());
            }
        }

        return lines.Take(20).ToArray();
    }

    /// <summary>
    /// One structural difference on one line, in the shape the corpus baseline
    /// already uses.
    /// </summary>
    /// <remarks>
    /// <c>kind@pN: detail</c>, and the same spelling on purpose. The two files
    /// are read by the same people looking for the same kind of thing, and a
    /// second shape would mean a reader who learned one had to learn the other.
    /// Serialising the JSON object instead - which is what this did first - put
    /// a five-line blob into a field whose whole value is being one comparable
    /// line.
    /// </remarks>
    private static string? Spell(JsonElement difference)
    {
        if (difference.ValueKind == JsonValueKind.String)
            return difference.GetString();

        if (difference.ValueKind != JsonValueKind.Object)
            return difference.ToString();

        string kind = difference.TryGetProperty("kind", out JsonElement kindValue) &&
                      kindValue.ValueKind == JsonValueKind.String
            ? kindValue.GetString() ?? "difference"
            : "difference";

        string where = difference.TryGetProperty("leftParagraph", out JsonElement paragraph) &&
                       paragraph.ValueKind == JsonValueKind.Number
            ? "@p" + paragraph.GetInt32().ToString(CultureInfo.InvariantCulture)
            : string.Empty;

        string detail = difference.TryGetProperty("detail", out JsonElement detailValue) &&
                        detailValue.ValueKind == JsonValueKind.String
            ? detailValue.GetString() ?? string.Empty
            : string.Empty;

        return detail.Length == 0 ? kind + where : kind + where + ": " + detail;
    }

    private static IReadOnlyList<string> RoundTripDiagnostics(JsonElement? json)
    {
        if (json is not { } element || !element.TryGetProperty("results", out JsonElement results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var codes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (JsonElement result in results.EnumerateArray())
        {
            foreach (string property in new[] { "writeDiagnostics", "readDiagnostics" })
            {
                if (!result.TryGetProperty(property, out JsonElement array) ||
                    array.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement diagnostic in array.EnumerateArray())
                {
                    if (diagnostic.TryGetProperty("code", out JsonElement code) &&
                        code.ValueKind == JsonValueKind.String)
                    {
                        string? value = code.GetString();
                        if (value is not null && !value.EndsWith(".read.summary", StringComparison.Ordinal))
                            codes.Add(value);
                    }
                }
            }
        }

        return codes.ToArray();
    }
}

/// <summary>Everything one document's semantic pass produced.</summary>
internal sealed record SemanticOutcome(
    IReadOnlyList<CheckResult> Results,
    IReadOnlyList<SemanticReading> Readings);
