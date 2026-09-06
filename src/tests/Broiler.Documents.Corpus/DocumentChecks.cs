using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Broiler.Documents.Corpus;

/// <summary>
/// What every materialised document must satisfy on its own, before anything is
/// compared with anything.
/// </summary>
internal static class DocumentChecks
{
    private const string Group = "document";

    public static async Task<IReadOnlyList<CheckResult>> RunAsync(
        BroilerDocTool tool,
        CorpusDocument document,
        string scratch)
    {
        var results = new List<CheckResult>();
        string label = document.Label;

        results.AddRange(await ProbeAsync(tool, document, label).ConfigureAwait(false));
        results.AddRange(await InfoAsync(tool, document, label).ConfigureAwait(false));
        results.AddRange(await DumpAsync(tool, document, label).ConfigureAwait(false));
        results.AddRange(await WriteDeterminismAsync(tool, document, label, scratch).ConfigureAwait(false));
        results.AddRange(await StandardStreamsAsync(tool, document, label).ConfigureAwait(false));
        results.AddRange(await EmittedContentAsync(document, label).ConfigureAwait(false));

        return results;
    }

    private static async Task<IReadOnlyList<CheckResult>> ProbeAsync(
        BroilerDocTool tool, CorpusDocument document, string label)
    {
        var results = new List<CheckResult>();
        ToolRun run = await tool.RunAsync("probe", document.Path, "--json").ConfigureAwait(false);
        results.Add(CheckResult.ExpectExit(Group, "probe/" + label, 0, run));

        JsonElement? json = run.Json();
        if (json is null)
        {
            results.Add(CheckResult.Fail(Group, "probe/" + label + "/selected",
                "stdout was not one JSON object.", run.CommandLine()));
            return results;
        }

        string? selected = json.Value.TryGetProperty("selected", out JsonElement value) ? value.GetString() : null;

        // Markdown is the honest exception and the manifest does not need to
        // carry it as an expectation. markdown-conformance.md is explicit that
        // plain text is valid Markdown, so probing is conservative on purpose
        // and a Markdown file can be selected as something else without anything
        // being wrong. Every other format has a signature it must be recognised
        // by.
        if (document.Format.Key == "markdown")
        {
            results.Add(selected is not null
                ? CheckResult.Pass(Group, "probe/" + label + "/selected")
                : CheckResult.Fail(Group, "probe/" + label + "/selected",
                    "no codec claimed the file at all.", run.CommandLine()));
            return results;
        }

        results.Add(selected == document.Format.ToolName
            ? CheckResult.Pass(Group, "probe/" + label + "/selected", run.Duration)
            : CheckResult.Fail(Group, "probe/" + label + "/selected",
                "expected the probe to select " + document.Format.ToolName + " but it selected " +
                (selected ?? "nothing") + ".", run.CommandLine()));

        return results;
    }

    private static async Task<IReadOnlyList<CheckResult>> InfoAsync(
        BroilerDocTool tool, CorpusDocument document, string label)
    {
        var results = new List<CheckResult>();
        ToolRun run = await tool.RunAsync("info", document.Path, "--json").ConfigureAwait(false);
        results.Add(CheckResult.ExpectExit(Group, "info/" + label, 0, run));

        JsonElement? json = run.Json();
        if (json is null)
        {
            results.Add(CheckResult.Fail(Group, "info/" + label + "/shape",
                "stdout was not one JSON object.", run.CommandLine()));
            return results;
        }

        if (!json.Value.TryGetProperty("statistics", out JsonElement statistics) ||
            !statistics.TryGetProperty("paragraphs", out JsonElement paragraphs))
        {
            results.Add(CheckResult.Fail(Group, "info/" + label + "/shape",
                "info --json no longer reports statistics.paragraphs.", run.CommandLine()));
            return results;
        }

        // The floor, and only the floor. An exact paragraph count belongs in the
        // baseline, because a format that cannot express an empty paragraph
        // legitimately reports fewer than the sample has - that is a documented
        // limitation, not an info defect.
        results.Add(paragraphs.GetInt32() > 0
            ? CheckResult.Pass(Group, "info/" + label + "/shape", run.Duration)
            : CheckResult.Fail(Group, "info/" + label + "/shape",
                "the document read back as zero paragraphs.", run.CommandLine()));

        return results;
    }

    private static async Task<IReadOnlyList<CheckResult>> DumpAsync(
        BroilerDocTool tool, CorpusDocument document, string label)
    {
        var results = new List<CheckResult>();

        // Four projections, four code paths. `json` and `codes` are the two the
        // CLI guide names as diffable, so they are the two whose determinism is
        // asserted rather than only their exit code.
        foreach (string form in new[] { "text", "json", "codes", "outline" })
        {
            ToolRun run = await tool.RunAsync("dump", document.Path, "--as", form).ConfigureAwait(false);
            string name = "dump/" + label + "/" + form;

            if (run.ExitCode != 0)
            {
                results.Add(CheckResult.ExpectExit(Group, name, 0, run));
                continue;
            }

            if (string.IsNullOrWhiteSpace(run.StandardOutput))
            {
                results.Add(CheckResult.Fail(Group, name, "the dump was empty.", run.CommandLine()));
                continue;
            }

            results.Add(CheckResult.Pass(Group, name, run.Duration));

            if (form is not ("json" or "codes"))
                continue;

            ToolRun again = await tool.RunAsync("dump", document.Path, "--as", form).ConfigureAwait(false);
            results.Add(string.Equals(run.StandardOutput, again.StandardOutput, StringComparison.Ordinal)
                ? CheckResult.Pass(Group, name + "/deterministic")
                : CheckResult.Fail(Group, name + "/deterministic",
                    "two dumps of the same document differed, so a diff between two of them means nothing. " +
                    "docs/cli.md states that two dumps of equal documents are byte-identical.",
                    run.CommandLine()));
        }

        return results;
    }

    private static async Task<IReadOnlyList<CheckResult>> WriteDeterminismAsync(
        BroilerDocTool tool, CorpusDocument document, string label, string scratch)
    {
        // A writer that embeds a timestamp, a temporary path or a hash-ordered
        // set produces a different file every run. Nothing else in this suite
        // would notice - every comparison here goes through the model, and the
        // model would be identical - but a consumer diffing two exports would
        // see a change that is not one, and the ZIP-based formats are where this
        // goes wrong most easily.
        if (document.Sample is not { } sample)
        {
            return
            [
                CheckResult.Skip(Group, "write-deterministic/" + label,
                    "the document is authored in the manifest rather than written by the tool, so there is " +
                    "no write to repeat."),
            ];
        }

        string second = Path.Combine(scratch, "determinism-" + sample.Id + document.Format.Extension);

        var arguments = new List<string>
        {
            "new", "--out", second, "--text", sample.Text(), "--quiet",
        };

        foreach (string operation in sample.Operations)
        {
            // A sample whose operations name an asset cannot be rewritten here
            // without the workspace's substitution, and the asset path is a
            // temporary directory that legitimately differs. Skipping it is
            // honest; pretending it passed is not.
            if (operation.Contains("${asset:", StringComparison.Ordinal))
            {
                return
                [
                    CheckResult.Skip(Group, "write-deterministic/" + label,
                        "the sample embeds a generated asset, whose path is a temporary directory, so two " +
                        "writes are not expected to be byte-identical."),
                ];
            }

            arguments.Add("--op");
            arguments.Add(operation);
        }

        ToolRun run = await tool.RunAsync(arguments, standardInput: null).ConfigureAwait(false);
        if (run.ExitCode != 0)
            return [CheckResult.ExpectExit(Group, "write-deterministic/" + label, 0, run)];

        byte[] first = await File.ReadAllBytesAsync(document.Path).ConfigureAwait(false);
        byte[] repeat = await File.ReadAllBytesAsync(second).ConfigureAwait(false);

        return
        [
            first.AsSpan().SequenceEqual(repeat)
                ? CheckResult.Pass(Group, "write-deterministic/" + label, run.Duration)
                : CheckResult.Fail(Group, "write-deterministic/" + label,
                    "writing the same document twice produced different bytes (" + first.Length + " and " +
                    repeat.Length + "). A reproducible export is what makes a byte diff between two runs " +
                    "mean something.", run.CommandLine()),
        ];
    }

    private static async Task<IReadOnlyList<CheckResult>> StandardStreamsAsync(
        BroilerDocTool tool, CorpusDocument document, string label)
    {
        // The `-` path, which docs/cli.md documents and no in-process test can
        // reach: it is the process's own stdin and stdout, not a writer the
        // harness passed in.
        byte[] input = await File.ReadAllBytesAsync(document.Path).ConfigureAwait(false);

        ToolRun run = await tool.RunAsync(
                ["convert", "-", "--out", "-", "--from", document.Format.Key, "--to", "markdown", "--quiet"],
                input)
            .ConfigureAwait(false);

        string name = "stdio/" + label;
        if (run.ExitCode != 0)
            return [CheckResult.ExpectExit(Group, name, 0, run)];

        return
        [
            !string.IsNullOrWhiteSpace(run.StandardOutput)
                ? CheckResult.Pass(Group, name, run.Duration)
                : CheckResult.Fail(Group, name,
                    "the tool read the document from stdin, exited 0, and wrote nothing to stdout.",
                    run.CommandLine()),
        ];
    }

    /// <summary>
    /// What the writer actually put in the file, for the samples that declare
    /// something which must not be there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The only check here that reads a written document as bytes rather than
    /// through the model, and it exists because some things are invisible from
    /// the model side. A URI scheme outside the allow-list is the case that
    /// prompted it: every reader in this component refuses such a scheme, so a
    /// document that wrote the link and one that dropped it both read back
    /// without it, both round trip equal, and the comparison cannot tell a writer
    /// that refused from a writer that did not.
    /// </para>
    /// <para>
    /// A literal substring test, and deliberately not more. Something cleverer -
    /// parsing each format's link syntax, say - would be a second implementation
    /// of five writers living in the test suite, and would be wrong in its own
    /// ways. The limit is stated in the schema so a reader knows what a pass is
    /// worth: a writer that percent-encoded the string would pass this.
    /// </para>
    /// </remarks>
    private static async Task<IReadOnlyList<CheckResult>> EmittedContentAsync(
        CorpusDocument document, string label)
    {
        if (document.Sample is not { MustNotAppear.Count: > 0 } sample)
            return [];

        var results = new List<CheckResult>();
        string[] parts;
        try
        {
            parts = await ReadTextPartsAsync(document.Path).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            return
            [
                CheckResult.Fail(Group, "emitted/" + label,
                    "the written document could not be read back as bytes: " + exception.Message),
            ];
        }

        foreach (CorpusForbidden forbidden in sample.MustNotAppear)
        {
            bool present = parts.Any(part =>
                part.Contains(forbidden.Text, StringComparison.OrdinalIgnoreCase));
            bool expected = forbidden.EmittedBy.Contains(document.Format.Key, StringComparer.Ordinal);
            string name = "emitted/" + label + "/" + forbidden.Text;

            if (present == expected)
            {
                results.Add(CheckResult.Pass(Group, name));
                continue;
            }

            results.Add(present
                ? CheckResult.Fail(Group, name,
                    "the writer put \"" + forbidden.Text + "\" in the file, and the corpus does not " +
                    "record this format as one that does. No round trip can see it: every reader here " +
                    "refuses the same thing, so the document reads back without it either way.")
                : CheckResult.Fail(Group, name,
                    "the corpus records this format as writing \"" + forbidden.Text + "\" and it no " +
                    "longer does. That is a fix, and it fails until the row is updated: take " +
                    document.Format.Key + " off this entry's emittedBy in tests/corpus/corpus.json."));
        }

        return results;
    }

    /// <summary>
    /// The document as text: one entry for a flat file, one per package entry for
    /// a ZIP. A package stores its parts compressed, so a scan of the raw file
    /// would find nothing and report a clean pass over a document it never read.
    /// </summary>
    private static async Task<string[]> ReadTextPartsAsync(string path)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        bool package = bytes.Length >= 2 && bytes[0] == 'P' && bytes[1] == 'K';

        if (!package)
            return [Encoding.UTF8.GetString(bytes)];

        var parts = new List<string>();
        using var buffer = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            using Stream stream = entry.Open();
            using var text = new StreamReader(stream, Encoding.UTF8);
            parts.Add(await text.ReadToEndAsync().ConfigureAwait(false));
        }

        return parts.ToArray();
    }
}
