using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using Broiler.Documents.Corpus;

namespace Broiler.Documents.Office;

/// <summary>What the run produced, in the three forms somebody might want it.</summary>
internal static class OfficeReporting
{
    /// <summary>
    /// The human summary, and the last line CI reads.
    /// </summary>
    /// <remarks>
    /// The count line is deliberately in the same shape as the corpus suite's
    /// and the console runners in Graphics and Media - <c>N/M passed, K
    /// failed.</c> - because CI already greps for exactly that, and a second
    /// shape would need a second grep to go stale independently.
    /// </remarks>
    public static void WriteConsole(TextWriter output, IReadOnlyList<CheckResult> results, bool verbose)
    {
        CheckResult[] failed = results.Where(result => result.Outcome == CheckOutcome.Failed).ToArray();
        CheckResult[] skipped = results.Where(result => result.Outcome == CheckOutcome.Skipped).ToArray();
        int passed = results.Count - failed.Length - skipped.Length;

        if (failed.Length > 0)
        {
            output.WriteLine();
            output.WriteLine("failures");
            foreach (CheckResult result in failed)
            {
                output.WriteLine("  " + result.Name);
                output.WriteLine("    " + result.Detail);
                if (result.Command is not null)
                    output.WriteLine("    re-run: " + Shorten(result.Command));
            }
        }

        if (skipped.Length > 0)
        {
            output.WriteLine();
            output.WriteLine("skipped");
            foreach (IGrouping<string?, CheckResult> group in skipped
                         .GroupBy(result => result.Detail)
                         .OrderByDescending(group => group.Count()))
            {
                output.WriteLine("  " + group.Count().ToString(CultureInfo.InvariantCulture) + "  " + group.Key);
                if (verbose)
                {
                    foreach (CheckResult result in group)
                        output.WriteLine("       " + result.Name);
                }
            }
        }

        output.WriteLine();
        output.WriteLine("by group");
        foreach (IGrouping<string, CheckResult> group in results
                     .GroupBy(result => result.Group, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            int groupFailed = group.Count(result => result.Outcome == CheckOutcome.Failed);
            int groupSkipped = group.Count(result => result.Outcome == CheckOutcome.Skipped);
            output.WriteLine(
                "  " + group.Key.PadRight(12) +
                (group.Count() - groupFailed - groupSkipped).ToString(CultureInfo.InvariantCulture).PadLeft(5) +
                " passed" +
                (groupFailed > 0 ? ", " + groupFailed + " failed" : string.Empty) +
                (groupSkipped > 0 ? ", " + groupSkipped + " skipped" : string.Empty));
        }

        output.WriteLine();
        if (skipped.Length > 0)
            output.WriteLine(skipped.Length.ToString(CultureInfo.InvariantCulture) + " skipped.");

        output.WriteLine(
            passed.ToString(CultureInfo.InvariantCulture) + "/" +
            (passed + failed.Length).ToString(CultureInfo.InvariantCulture) + " passed, " +
            failed.Length.ToString(CultureInfo.InvariantCulture) + " failed.");
    }

    /// <summary>
    /// The machine-readable report.
    /// </summary>
    /// <remarks>
    /// It carries the readings as well as the verdicts, because the readings are
    /// what a person classifying a new baseline row actually needs and re-running
    /// the suite to get them back would take minutes.
    /// </remarks>
    public static void WriteJson(
        string path,
        IReadOnlyList<CheckResult> results,
        IReadOnlyList<SemanticReading> readings,
        IReadOnlyList<RenderRow> renders,
        RunHeader header)
    {
        var root = new JsonObject
        {
            ["tool"] = header.Tool,
            ["libreoffice"] = header.LibreOffice,
            ["rasterizer"] = header.Rasterizer,
            ["fontSet"] = header.FontSet,
            ["platform"] = header.Platform,
            ["dpi"] = header.Dpi,
            ["stampMatches"] = header.StampMatches,
            ["passed"] = results.Count(result => result.Outcome == CheckOutcome.Passed),
            ["failed"] = results.Count(result => result.Outcome == CheckOutcome.Failed),
            ["skipped"] = results.Count(result => result.Outcome == CheckOutcome.Skipped),
        };

        var checks = new JsonArray();
        foreach (CheckResult result in results)
        {
            checks.Add(new JsonObject
            {
                ["group"] = result.Group,
                ["name"] = result.Name,
                ["outcome"] = result.Outcome.ToString().ToLowerInvariant(),
                ["detail"] = result.Detail,
                ["command"] = result.Command,
            });
        }

        root["checks"] = checks;

        var reads = new JsonArray();
        foreach (SemanticReading reading in readings)
        {
            reads.Add(new JsonObject
            {
                ["seed"] = reading.Seed,
                ["via"] = reading.Via,
                ["check"] = reading.Check,
                ["differences"] = Array(reading.Differences),
                ["diagnostics"] = Array(reading.Diagnostics),
            });
        }

        root["reads"] = reads;

        var rendered = new JsonArray();
        foreach (RenderRow row in renders)
        {
            rendered.Add(new JsonObject
            {
                ["seed"] = row.Seed,
                ["via"] = row.Via,
                ["pageCount"] = new JsonObject
                {
                    ["broilerdoc"] = row.BroilerdocPages,
                    ["libreoffice"] = row.LibreOfficePages,
                },
                ["worstPage"] = row.WorstPage,
                ["bands"] = new JsonObject
                {
                    ["geometry"] = row.Bands.Geometry,
                    ["inkBox"] = row.Bands.InkBox,
                    ["inkProfile"] = row.Bands.InkProfile,
                    ["coverage"] = row.Bands.Coverage,
                    ["blurDiff"] = row.Bands.BlurDiff,
                },
                ["observed"] = new JsonObject
                {
                    ["inkBoxDeltaPx"] = row.Observed.InkBoxDeltaPx,
                    ["inkProfileSimilarity"] = row.Observed.InkProfileSimilarity,
                    ["coverageDelta"] = row.Observed.CoverageDelta,
                    ["blurDiffRatio"] = row.Observed.BlurDiffRatio,
                },
            });
        }

        root["renders"] = rendered;

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        File.WriteAllText(path, root.ToJsonString(options).ReplaceLineEndings("\n") + "\n");
    }

    /// <summary>The shape a CI test reporter reads.</summary>
    public static void WriteJUnit(string path, IReadOnlyList<CheckResult> results)
    {
        var settings = new XmlWriterSettings { Indent = true, NewLineChars = "\n" };
        using XmlWriter writer = XmlWriter.Create(path, settings);

        writer.WriteStartDocument();
        writer.WriteStartElement("testsuites");

        foreach (IGrouping<string, CheckResult> group in results
                     .GroupBy(result => result.Group, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            writer.WriteStartElement("testsuite");
            writer.WriteAttributeString("name", group.Key);
            writer.WriteAttributeString(
                "tests", group.Count().ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString(
                "failures",
                group.Count(result => result.Outcome == CheckOutcome.Failed)
                    .ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString(
                "skipped",
                group.Count(result => result.Outcome == CheckOutcome.Skipped)
                    .ToString(CultureInfo.InvariantCulture));

            foreach (CheckResult result in group)
            {
                writer.WriteStartElement("testcase");
                writer.WriteAttributeString("classname", result.Group);
                writer.WriteAttributeString("name", result.Name);
                writer.WriteAttributeString(
                    "time", result.Duration.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture));

                if (result.Outcome == CheckOutcome.Failed)
                {
                    writer.WriteStartElement("failure");
                    writer.WriteAttributeString("message", Single(result.Detail));
                    writer.WriteString(result.Detail ?? string.Empty);
                    writer.WriteEndElement();
                }
                else if (result.Outcome == CheckOutcome.Skipped)
                {
                    writer.WriteStartElement("skipped");
                    writer.WriteAttributeString("message", Single(result.Detail));
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    /// <summary>
    /// The re-run command with its font mappings folded away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pinned render carries one <c>--font-file</c> pair per face per family,
    /// which for the four families this corpus names is twenty-seven arguments
    /// and about four kilobytes. Printed in full it buried every failure message
    /// on the page under a wall of paths, and a report nobody can read is a
    /// report nobody reads.
    /// </para>
    /// <para>
    /// The full command survives in the JSON report, which is where a caller
    /// that actually wants to replay it should look. What is printed keeps the
    /// <c>--font-dir</c>, so the shortened form still renders with most of the
    /// pinned set by hand - and it says how many mappings it dropped rather than
    /// dropping them quietly.
    /// </para>
    /// </remarks>
    private static string Shorten(string command)
    {
        const string Flag = " --font-file ";
        int first = command.IndexOf(Flag, StringComparison.Ordinal);
        if (first < 0)
            return command;

        int count = 0;
        int index = first;
        while (index >= 0)
        {
            count++;
            index = command.IndexOf(Flag, index + Flag.Length, StringComparison.Ordinal);
        }

        // Everything after the first mapping is either another mapping or an
        // argument that followed them; the render command puts them last, so
        // cutting here loses nothing else.
        return command[..first] +
               " --font-file ... (" + count.ToString(CultureInfo.InvariantCulture) +
               " mappings elided; the full command is in the JSON report)";
    }

    private static JsonArray Array(IReadOnlyList<string> items)
    {
        var array = new JsonArray();
        foreach (string item in items)
            array.Add(item);

        return array;
    }

    private static string Single(string? text) =>
        (text ?? string.Empty).ReplaceLineEndings(" ").Trim();
}

/// <summary>The facts about a run that a reader needs before any of its numbers mean anything.</summary>
internal sealed record RunHeader(
    string Tool,
    string? LibreOffice,
    string? Rasterizer,
    string? FontSet,
    string Platform,
    int Dpi,
    bool StampMatches);
