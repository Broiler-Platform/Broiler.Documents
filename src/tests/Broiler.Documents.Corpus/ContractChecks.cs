using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Broiler.Documents.Corpus;

/// <summary>
/// The parts of the command line that are a contract rather than a codec: the
/// exit codes, the <c>--json</c> envelope, and the refusals.
/// </summary>
/// <remarks>
/// docs/cli.md states these as promises to an automated caller, and they are the
/// promises that are cheapest to break by accident - an option renamed, a
/// refusal downgraded to a warning, a JSON key dropped. None of them needs the
/// corpus, so they run first: if the envelope is wrong, every fidelity result
/// after it is being read out of the wrong shape.
/// </remarks>
internal static class ContractChecks
{
    private const string Group = "contract";

    public static async Task<IReadOnlyList<CheckResult>> RunAsync(
        BroilerDocTool tool,
        CorpusManifest manifest,
        CorpusWorkspace workspace)
    {
        var results = new List<CheckResult>();

        results.AddRange(await EnvelopeAsync(tool).ConfigureAwait(false));
        results.AddRange(await RefusalsAsync(tool, workspace).ConfigureAwait(false));
        results.AddRange(await MalformedAsync(tool, manifest, workspace).ConfigureAwait(false));

        return results;
    }

    private static async Task<IReadOnlyList<CheckResult>> EnvelopeAsync(BroilerDocTool tool)
    {
        var results = new List<CheckResult>();

        ToolRun formats = await tool.RunAsync("formats", "--json").ConfigureAwait(false);
        results.Add(CheckResult.ExpectExit(Group, "formats/exit", 0, formats));

        JsonElement? formatsJson = formats.Json();
        if (formatsJson is null)
        {
            results.Add(CheckResult.Fail(Group, "formats/json", "stdout was not one JSON object.", formats.CommandLine()));
        }
        else
        {
            JsonElement root = formatsJson.Value;

            var named = root.TryGetProperty("formats", out JsonElement list)
                ? list.EnumerateArray().Select(entry => entry.GetProperty("name").GetString()).ToHashSet(StringComparer.Ordinal)
                : [];

            string[] missing = CorpusFormat.All
                .Where(format => !named.Contains(format.ToolName))
                .Select(format => format.ToolName)
                .ToArray();

            results.Add(missing.Length == 0
                ? CheckResult.Pass(Group, "formats/composed")
                : CheckResult.Fail(Group, "formats/composed",
                    "the tool no longer composes: " + string.Join(", ", missing) + ".", formats.CommandLine()));

            // The one negative claim in the CLI guide that a change could quietly
            // reverse. Composing the PDF codec here would ship the capability the
            // roadmap's read-preview and write-preview gates exist to hold back,
            // from the surface an automated system would then depend on.
            bool pdf = root.TryGetProperty("pdfComposed", out JsonElement composed) && composed.GetBoolean();
            results.Add(!pdf
                ? CheckResult.Pass(Group, "formats/pdf-not-composed")
                : CheckResult.Fail(Group, "formats/pdf-not-composed",
                    "the tool reports the PDF codec as composed. docs/cli.md and the PDF support roadmap 4.1 " +
                    "say it must not be, until the read-preview and write-preview gates pass.",
                    formats.CommandLine()));
        }

        ToolRun version = await tool.RunAsync("version", "--json").ConfigureAwait(false);
        results.Add(CheckResult.ExpectExit(Group, "version/exit", 0, version));

        JsonElement? versionJson = version.Json();
        if (versionJson is null)
        {
            results.Add(CheckResult.Fail(Group, "version/json", "stdout was not one JSON object.", version.CommandLine()));
        }
        else
        {
            string[] required = ["tool", "broilerDocuments", "broilerDocumentsModel", "broilerGraphics", "runtime", "os"];
            string[] absent = required.Where(key => !versionJson.Value.TryGetProperty(key, out _)).ToArray();
            results.Add(absent.Length == 0
                ? CheckResult.Pass(Group, "version/keys")
                : CheckResult.Fail(Group, "version/keys",
                    "version --json no longer reports: " + string.Join(", ", absent) + ".", version.CommandLine()));
        }

        // Every command's --json output must carry its own exit code, because
        // docs/cli.md promises a caller never has to parse the human form. The
        // claim is about `--json` generally, so it is checked on a command that
        // succeeds and on one that reaches a negative verdict.
        foreach ((string name, string[] arguments) in new[]
                 {
                     ("formats", new[] { "formats", "--json" }),
                     ("probe-missing", ["probe", "no-such-file.docx", "--json"]),
                 })
        {
            ToolRun run = await tool.RunAsync(arguments, standardInput: null).ConfigureAwait(false);
            JsonElement? json = run.Json();
            if (json is null || !json.Value.TryGetProperty("exitCode", out JsonElement code))
            {
                results.Add(CheckResult.Fail(Group, "json-envelope/" + name,
                    "the --json payload carries no exitCode.", run.CommandLine()));
                continue;
            }

            results.Add(code.GetInt32() == run.ExitCode
                ? CheckResult.Pass(Group, "json-envelope/" + name)
                : CheckResult.Fail(Group, "json-envelope/" + name,
                    "the payload says exitCode " + code.GetInt32() + " but the process exited " + run.ExitCode + ".",
                    run.CommandLine()));
        }

        return results;
    }

    private static async Task<IReadOnlyList<CheckResult>> RefusalsAsync(BroilerDocTool tool, CorpusWorkspace workspace)
    {
        var results = new List<CheckResult>();

        results.Add(CheckResult.ExpectExit(Group, "usage/help", 0,
            await tool.RunAsync("--help").ConfigureAwait(false)));

        results.Add(CheckResult.ExpectExit(Group, "usage/unknown-command", 1,
            await tool.RunAsync("frobnicate").ConfigureAwait(false)));

        // The one docs/cli.md argues for by name: a harness that writes
        // --tolerence must fail loudly, because comparing at the default
        // tolerance would report a pass nobody earned.
        results.Add(CheckResult.ExpectExit(Group, "usage/misspelt-option", 1,
            await tool.RunAsync("compare", "a.docx", "b.docx", "--tolerence", "2").ConfigureAwait(false)));

        results.Add(CheckResult.ExpectExit(Group, "usage/missing-argument", 1,
            await tool.RunAsync("convert", "--out").ConfigureAwait(false)));

        CorpusDocument? sample = workspace.Documents.FirstOrDefault(document => document.Format.Key == "docx");
        if (sample is null)
            return results;

        // Exit 5 is a verdict, not an error: a comparison that finds a
        // difference ran successfully. A document compared with itself must
        // reach the other verdict, or every comparison in the suite is
        // meaningless.
        results.Add(CheckResult.ExpectExit(Group, "compare/self", 0,
            await tool.RunAsync("compare", sample.Path, sample.Path, "--quiet").ConfigureAwait(false)));

        return results;
    }

    private static async Task<IReadOnlyList<CheckResult>> MalformedAsync(
        BroilerDocTool tool,
        CorpusManifest manifest,
        CorpusWorkspace workspace)
    {
        var results = new List<CheckResult>();

        foreach (CorpusMalformed row in manifest.Malformed)
        {
            string path = workspace.MalformedPath(row.Id);
            ToolRun run = await tool.RunAsync("info", path, "--json").ConfigureAwait(false);

            results.Add(CheckResult.ExpectExit(Group, "malformed/" + row.Id, row.ExpectedExit, run));

            // Checked separately from the expected exit, and on every row rather
            // than only the failing ones. A run that produced the right exit for
            // the wrong reason is still wrong, and 70 is the reason docs/cli.md
            // defines as always a defect in the tool.
            results.Add(run.ExitCode != 70
                ? CheckResult.Pass(Group, "malformed/" + row.Id + "/no-crash")
                : CheckResult.Fail(Group, "malformed/" + row.Id + "/no-crash",
                    "the tool exited 70 on a malformed input, which it documents as always a defect in itself.",
                    run.CommandLine()));
        }

        return results;
    }
}
