using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;

namespace Broiler.Documents.Corpus;

/// <summary>What the run produced, in the three forms somebody might want it.</summary>
internal static class Reporting
{
    /// <summary>
    /// The human summary, and the last line CI reads.
    /// </summary>
    /// <remarks>
    /// The count line is deliberately in the same shape as the console-runner
    /// suites in Graphics and Media - <c>N/M passed, K failed.</c> - because CI
    /// already greps for exactly that and a second shape would need a second
    /// grep to go stale independently. Skips go on their own line so the count
    /// line stays anchored.
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
                    output.WriteLine("    re-run: " + result.Command);
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
    /// The machine-readable report. Carries the round-trip readings as well as
    /// the checks, so a failing CI run can be diagnosed from the artifact
    /// without re-running anything.
    /// </summary>
    public static void WriteJson(
        string path,
        IReadOnlyList<CheckResult> results,
        IReadOnlyList<RoundTripOutcome> outcomes,
        string toolDescription)
    {
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

        var trips = new JsonArray();
        foreach (RoundTripOutcome outcome in outcomes)
        {
            trips.Add(new JsonObject
            {
                ["sample"] = outcome.Sample,
                ["source"] = outcome.Source,
                ["via"] = outcome.Via,
                ["equal"] = outcome.Equal,
                ["plainTextEqual"] = outcome.PlainTextEqual,
                ["formatCodesEqual"] = outcome.FormatCodesEqual,
                ["differences"] = new JsonArray(outcome.Differences.Select(text => (JsonNode)text!).ToArray()),
                ["diagnostics"] = new JsonArray(outcome.Diagnostics.Select(text => (JsonNode)text!).ToArray()),
            });
        }

        var document = new JsonObject
        {
            ["tool"] = toolDescription,
            ["passed"] = results.Count(result => result.Outcome == CheckOutcome.Passed),
            ["failed"] = results.Count(result => result.Outcome == CheckOutcome.Failed),
            ["skipped"] = results.Count(result => result.Outcome == CheckOutcome.Skipped),
            ["checks"] = checks,
            ["roundTrips"] = trips,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, document.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) + "\n");
    }

    /// <summary>
    /// JUnit XML, so a CI front end lists the failures next to every other
    /// suite's rather than making a reader open a log.
    /// </summary>
    public static void WriteJUnit(string path, IReadOnlyList<CheckResult> results)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
        using XmlWriter writer = XmlWriter.Create(path, settings);

        writer.WriteStartElement("testsuites");
        foreach (IGrouping<string, CheckResult> group in results
                     .GroupBy(result => result.Group, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            writer.WriteStartElement("testsuite");
            writer.WriteAttributeString("name", "corpus." + group.Key);
            writer.WriteAttributeString("tests", group.Count().ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("failures",
                group.Count(result => result.Outcome == CheckOutcome.Failed).ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("skipped",
                group.Count(result => result.Outcome == CheckOutcome.Skipped).ToString(CultureInfo.InvariantCulture));

            foreach (CheckResult result in group)
            {
                writer.WriteStartElement("testcase");
                writer.WriteAttributeString("classname", "corpus." + result.Group);
                writer.WriteAttributeString("name", result.Name);
                writer.WriteAttributeString("time",
                    result.Duration.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture));

                switch (result.Outcome)
                {
                    case CheckOutcome.Failed:
                        writer.WriteStartElement("failure");
                        writer.WriteAttributeString("message", Sanitize(result.Detail ?? "failed"));
                        writer.WriteString(Sanitize(
                            (result.Detail ?? string.Empty) +
                            (result.Command is null ? string.Empty : "\nre-run: " + result.Command)));
                        writer.WriteEndElement();
                        break;

                    case CheckOutcome.Skipped:
                        writer.WriteStartElement("skipped");
                        writer.WriteAttributeString("message", Sanitize(result.Detail ?? "skipped"));
                        writer.WriteEndElement();
                        break;
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    /// <summary>
    /// Drops the characters XML cannot carry.
    /// </summary>
    /// <remarks>
    /// The corpus holds characters chosen to be awkward - a U+2028 soft break,
    /// every metacharacter each format has to escape - and a failure message
    /// quotes the text it failed on. One character XML cannot express would
    /// otherwise take out the whole report rather than the one check.
    /// U+2028 itself is a legal XML character and survives; the writer escapes
    /// it.
    /// </remarks>
    private static string Sanitize(string text)
    {
        var clean = new StringBuilder(text.Length);
        foreach (char character in text)
        {
            clean.Append(XmlConvert.IsXmlChar(character) ? character : '\uFFFD');
        }

        return clean.ToString();
    }
}
