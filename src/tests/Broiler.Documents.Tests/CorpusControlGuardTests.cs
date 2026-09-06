using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Broiler.Documents.Tests;

/// <summary>
/// Binds the corpus suite's data files to the things they claim.
/// </summary>
/// <remarks>
/// <para>
/// The corpus suite is a console runner, so nothing in <c>dotnet test</c>
/// executes it and nothing here tries to. What these guards check is the part
/// that is a control rather than a program: that a baseline row was classified
/// by somebody rather than by being written down, that a row citing a document
/// cites one that exists, and that the register which says it gates external
/// documents is actually empty.
/// </para>
/// <para>
/// The model is <see cref="PdfTestControlGuardTests"/>, which makes the same
/// demand of the tool manifest. A control nothing checks is a document that was
/// accurate on the day it was written.
/// </para>
/// </remarks>
public sealed class CorpusControlGuardTests
{
    private const string Corpus = "tests/corpus/corpus.json";
    private const string BaselineFile = "tests/corpus/corpus-baseline.json";
    private const string Register = "tests/corpus/external-sources.json";
    private const string Runner = "src/tests/Broiler.Documents.Corpus";

    private static JsonDocument Load(string relative) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(PdfGuardRoots.Component, relative)));

    [Fact(Timeout = 600000)]
    public void No_Baseline_Row_Is_Classified_Merely_By_Being_Written_Down()
    {
        // A regenerated row starts as a suspected defect on purpose, and that
        // state needs nobody. The other two are claims - "a document says this is
        // a limitation", "somebody looked at this and accepted it" - and a claim
        // names who made it and when, or it is not one.
        using JsonDocument baseline = Load(BaselineFile);
        foreach (JsonElement row in baseline.RootElement.GetProperty("roundTrips").EnumerateArray())
        {
            string state = row.GetProperty("state").GetString() ?? string.Empty;
            Assert.Contains(state, new[] { "documented", "suspected-defect", "accepted" });

            if (state == "suspected-defect")
                continue;

            string key = Key(row);
            Assert.False(
                string.IsNullOrWhiteSpace(row.GetProperty("reviewer").GetString()),
                key + " is " + state + " and names no reviewer.");
            Assert.False(
                string.IsNullOrWhiteSpace(row.GetProperty("decided").GetString()),
                key + " is " + state + " and records no date.");
            Assert.False(
                string.IsNullOrWhiteSpace(row.GetProperty("why").GetString()),
                key + " is " + state + " and says nothing about why.");
        }
    }

    [Fact(Timeout = 600000)]
    public void Every_Documented_Row_Cites_A_Document_That_Exists()
    {
        // A citation nobody can follow is the same as none, and this is the one
        // half of the classification a machine can check. Whether the cited
        // document actually says what the row claims is a reader's job.
        using JsonDocument baseline = Load(BaselineFile);
        string[] missing = baseline.RootElement.GetProperty("roundTrips").EnumerateArray()
            .Where(row => (row.GetProperty("state").GetString() ?? string.Empty) == "documented")
            .Select(row => (Key: Key(row), Reference: row.GetProperty("reference").GetString()))
            .Where(row => row.Reference is null ||
                          !File.Exists(Path.Combine(
                              PdfGuardRoots.Component,
                              row.Reference.Replace('/', Path.DirectorySeparatorChar))))
            .Select(row => row.Key + " -> " + (row.Reference ?? "(none)"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "Documented baseline rows citing a document that is not there: " + string.Join(", ", missing));
    }

    [Fact(Timeout = 600000)]
    public void Every_Baseline_Row_Names_A_Document_The_Corpus_Actually_Has()
    {
        // A renamed sample leaves its rows behind, and rows for a document
        // nothing produces can never fail - they would sit in the file looking
        // like coverage.
        using JsonDocument corpus = Load(Corpus);
        var known = corpus.RootElement.GetProperty("samples").EnumerateArray()
            .Concat(corpus.RootElement.GetProperty("authored").EnumerateArray())
            .Select(entry => entry.GetProperty("id").GetString() ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);

        using JsonDocument baseline = Load(BaselineFile);
        string[] orphaned = baseline.RootElement.GetProperty("roundTrips").EnumerateArray()
            .Select(row => row.GetProperty("sample").GetString() ?? string.Empty)
            .Where(sample => !known.Contains(sample))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            orphaned.Length == 0,
            "The baseline holds rows for documents the corpus does not define: " + string.Join(", ", orphaned));
    }

    [Fact(Timeout = 600000)]
    public void The_External_Register_Is_Closed_And_Says_So()
    {
        // The register is the door an outside document would have to come
        // through, and every format register in this component rejects
        // third-party document artifacts by default, per artifact (ADR 0013). A
        // row appearing here without those registers moving first is the thing
        // this guard exists to notice.
        using JsonDocument register = Load(Register);

        Assert.Equal("off", register.RootElement.GetProperty("policy").GetProperty("default").GetString());

        JsonElement sources = register.RootElement.GetProperty("sources");
        Assert.True(
            sources.GetArrayLength() == 0,
            "tests/corpus/external-sources.json has grown a row. That is not forbidden, but it needs " +
            "the format's own IP register to have moved first, and this guard updated to say so.");
    }

    [Fact(Timeout = 600000)]
    public void The_Corpus_Commits_No_Document_Of_Its_Own()
    {
        // The corpus materialises .docx, .odt, .rtf and .html, and
        // FormatClaimGuardTests walks the whole tree for exactly those. This
        // narrows that to the corpus directories, so a stray file left by a run
        // is reported against the thing that produced it rather than as a
        // mysterious failure elsewhere.
        string[] extensions = [".rtf", ".docx", ".dotx", ".html", ".htm", ".odt", ".ott"];

        string[] strays =
        [
            .. new[] { "tests/corpus", Runner }
                .Select(relative => Path.Combine(PdfGuardRoots.Component, relative))
                .Where(Directory.Exists)
                .SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                .Where(path => !PdfGuardRoots.IsBuildOutput(path))
                .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .Select(path => Path.GetRelativePath(PdfGuardRoots.Component, path))
                .Order(StringComparer.Ordinal),
        ];

        Assert.True(
            strays.Length == 0,
            "A corpus run materialised inside the repository, or a document was committed: " +
            string.Join(", ", strays) + ". The runner writes to a temporary directory and refuses a " +
            "workspace under the component root; --keep leaves it where it is rather than moving it here.");
    }

    [Fact(Timeout = 600000)]
    public void Every_Project_In_The_Tree_Is_In_The_Solution()
    {
        // The test-count guard in CI is a floor, so a project nobody registered
        // does not fail anything - it simply never builds and never runs, which
        // is the quietest way for a suite to stop existing. This is what noticed
        // the corpus runner itself.
        string solution = File.ReadAllText(Path.Combine(PdfGuardRoots.Component, "Broiler.Documents.slnx"));

        string[] unregistered = Directory
            .EnumerateFiles(Path.Combine(PdfGuardRoots.Component, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !PdfGuardRoots.IsBuildOutput(path))
            .Select(path => Path.GetRelativePath(PdfGuardRoots.Component, path).Replace('\\', '/'))
            .Where(path => !solution.Contains("\"" + path + "\"", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unregistered.Length == 0,
            "Projects on disk that Broiler.Documents.slnx does not list, so nothing builds or runs them: " +
            string.Join(", ", unregistered));
    }

    private static string Key(JsonElement row) =>
        (row.GetProperty("sample").GetString() ?? "?") + "/" +
        (row.GetProperty("source").GetString() ?? "?") + "/" +
        (row.GetProperty("via").GetString() ?? "?");
}
