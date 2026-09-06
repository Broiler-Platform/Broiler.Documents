using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Broiler.Documents.Tests;

/// <summary>
/// Binds the office conformance suite's data files to the things they claim.
/// </summary>
/// <remarks>
/// <para>
/// The office suite is a console runner that needs LibreOffice, so nothing in
/// <c>dotnet test</c> executes it and nothing here tries to. These guards check
/// the part that is a control rather than a program, and they are the half of
/// the discipline that runs on every pull request: that a tool was approved by
/// somebody rather than by being written down, that a baseline row was
/// classified the same way, that a citation points at a file that exists, and
/// that no seed names a font the pinned set does not hold.
/// </para>
/// <para>
/// The model is <see cref="CorpusControlGuardTests"/> and, behind it,
/// <see cref="PdfTestControlGuardTests"/>. A control nothing checks is a
/// document that was accurate on the day it was written.
/// </para>
/// </remarks>
public sealed class OfficeControlGuardTests
{
    private const string Corpus = "tests/office/office-corpus.json";
    private const string BaselineFile = "tests/office/office-baseline.json";
    private const string Tools = "tests/office/tools/manifest.json";
    private const string Runner = "src/tests/Broiler.Documents.Office";

    private static readonly string[] DocumentExtensions =
        [".docx", ".dotx", ".odt", ".ott", ".fodt", ".rtf", ".html", ".htm"];

    /// <summary>The markup properties a seed may carry, exactly one of which it must.</summary>
    private static readonly string[] SeedLanguages = ["html", "fodt"];

    private static JsonDocument Load(string relative) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(PdfGuardRoots.Component, relative)));

    [Fact(Timeout = 600000)]
    public void Both_Office_Controls_And_Their_Schemas_Parse()
    {
        // The cheapest guard in the file and the one most likely to fire. A
        // control file that does not parse fails the runner at the point where
        // it is least informative - after provisioning, minutes in - and here it
        // fails in milliseconds with the file named.
        foreach (string relative in new[]
                 {
                     Corpus, BaselineFile, Tools,
                     "tests/office/office-corpus.schema.json",
                     "tests/office/office-baseline.schema.json",
                     "tests/office/tools/manifest.schema.json",
                 })
        {
            string path = Path.Combine(PdfGuardRoots.Component, relative);
            Assert.True(File.Exists(path), relative + " is missing.");

            Exception? failure = Record.Exception(() => JsonDocument.Parse(File.ReadAllText(path)).Dispose());
            Assert.True(failure is null, relative + " does not parse: " + failure?.Message);
        }
    }

    [Fact(Timeout = 600000)]
    public void No_Tool_Is_Approved_Merely_By_Being_Written_Down()
    {
        // The rule this component already operates for its PDF oracles, applied
        // to the two this suite drives. `pending` is the default and needs
        // nobody; `approved` is a claim, and a claim names who made it and when
        // or it is not one.
        using JsonDocument manifest = Load(Tools);
        foreach (JsonElement tool in manifest.RootElement.GetProperty("tools").EnumerateArray())
        {
            string id = tool.GetProperty("id").GetString() ?? "?";
            JsonElement review = tool.GetProperty("review");
            string state = review.GetProperty("state").GetString() ?? string.Empty;

            Assert.Contains(state, new[] { "pending", "approved", "rejected" });

            if (state != "approved")
                continue;

            Assert.False(
                string.IsNullOrWhiteSpace(review.GetProperty("reviewer").GetString()),
                id + " is approved and names no reviewer.");
            Assert.False(
                string.IsNullOrWhiteSpace(review.GetProperty("decided").GetString()),
                id + " is approved and records no date.");
            Assert.False(
                string.IsNullOrWhiteSpace(review.GetProperty("why").GetString()),
                id + " is approved and says nothing about why.");
        }
    }

    [Fact(Timeout = 600000)]
    public void No_Registered_Tool_Is_A_Product_Reference()
    {
        // The other half of the register's promise, and the one a reader is
        // most likely to doubt. A tool that ends up referenced by a shipping
        // project is no longer an independent oracle, and its licence has
        // stopped being a question about CI.
        using JsonDocument manifest = Load(Tools);
        string[] names = manifest.RootElement.GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetProperty("identity").GetProperty("name").GetString() ?? string.Empty)
            .Where(name => name.Length > 0)
            .ToArray();

        foreach (string project in Directory.EnumerateFiles(
                     Path.Combine(PdfGuardRoots.Component, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            if (PdfGuardRoots.IsBuildOutput(project) ||
                project.Contains("Broiler.Documents.Tests", StringComparison.Ordinal) ||
                project.Contains("Broiler.Documents.Office", StringComparison.Ordinal))
            {
                continue;
            }

            string text = File.ReadAllText(project);
            foreach (string name in names)
            {
                Assert.False(
                    text.Contains(name, StringComparison.OrdinalIgnoreCase),
                    Path.GetFileName(project) + " references '" + name + "', which is registered as an " +
                    "external oracle and may not become a product dependency.");
            }
        }
    }

    [Fact(Timeout = 600000)]
    public void No_Office_Baseline_Row_Is_Classified_Merely_By_Being_Written_Down()
    {
        using JsonDocument baseline = Load(BaselineFile);
        foreach (string section in new[] { "reads", "renders" })
        {
            foreach (JsonElement row in baseline.RootElement.GetProperty(section).EnumerateArray())
            {
                string state = row.GetProperty("state").GetString() ?? string.Empty;
                Assert.Contains(state, new[] { "documented", "suspected-defect", "accepted" });

                if (state == "suspected-defect")
                    continue;

                string key = section + " " + Key(row);
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
    }

    [Fact(Timeout = 600000)]
    public void Every_Documented_Office_Row_Cites_A_Document_That_Exists()
    {
        // A citation nobody can follow is the same as none, and this is the one
        // half of the classification a machine can check. Whether the cited
        // document actually says what the row claims is a reader's job.
        using JsonDocument baseline = Load(BaselineFile);
        foreach (string section in new[] { "reads", "renders" })
        {
            foreach (JsonElement row in baseline.RootElement.GetProperty(section).EnumerateArray())
            {
                if (row.GetProperty("state").GetString() != "documented")
                    continue;

                string? reference = row.GetProperty("reference").GetString();
                Assert.False(string.IsNullOrWhiteSpace(reference),
                    Key(row) + " is documented and cites nothing.");
                Assert.True(
                    File.Exists(Path.Combine(PdfGuardRoots.Component, reference!)),
                    Key(row) + " cites " + reference + ", which is not in the tree.");
            }
        }
    }

    [Fact(Timeout = 600000)]
    public void Every_Office_Baseline_Row_Names_A_Seed_The_Manifest_Has()
    {
        // A row for a seed nobody has any more is a row that can never
        // reproduce, and the runner fails a row that does not reproduce. That
        // failure would be correct and useless: it names the symptom in a run
        // that takes minutes rather than the cause in a test that takes none.
        using JsonDocument corpus = Load(Corpus);
        using JsonDocument baseline = Load(BaselineFile);

        var seeds = corpus.RootElement.GetProperty("seeds").EnumerateArray()
            .Select(seed => seed.GetProperty("id").GetString() ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);

        foreach (string section in new[] { "reads", "renders" })
        {
            foreach (JsonElement row in baseline.RootElement.GetProperty(section).EnumerateArray())
            {
                string seed = row.GetProperty("seed").GetString() ?? string.Empty;
                Assert.True(seeds.Contains(seed),
                    "The baseline holds a row for the seed '" + seed + "', which office-corpus.json " +
                    "does not have.");
            }
        }
    }

    [Fact(Timeout = 600000)]
    public void Every_Seed_Names_Only_Pinned_Fonts()
    {
        // The single most valuable guard here, because the failure it prevents
        // is silent. LibreOffice substitutes a family it does not have without a
        // word - exit 0, empty stderr - so a seed asking for a font the CI image
        // lacks produces a run that measures the machine's font set and reports
        // it as a difference in this component.
        using JsonDocument corpus = Load(Corpus);
        var pinned = corpus.RootElement.GetProperty("fonts").EnumerateArray()
            .Select(font => font.GetString() ?? string.Empty)
            .ToArray();

        foreach (JsonElement seed in corpus.RootElement.GetProperty("seeds").EnumerateArray())
        {
            string id = seed.GetProperty("id").GetString() ?? "?";
            string markup = Markup(seed);

            Assert.True(
                pinned.Any(font => markup.Contains(font, StringComparison.OrdinalIgnoreCase)),
                "The seed '" + id + "' names none of the pinned families, so LibreOffice will pick " +
                "one from the host and the comparison will measure the machine.");
        }
    }

    [Fact(Timeout = 600000)]
    public void Every_Seed_Is_Written_In_Exactly_One_Language()
    {
        // The corpus holds two markup languages since a letterhead's constructs
        // turned out to be unstateable in HTML, and the loader picks the seed's
        // by looking for the property. So the shape it cannot tolerate is a seed
        // carrying both: the loader would refuse it minutes into a provisioned
        // run, and a seed carrying neither would materialise an empty document
        // and pass every check that is not about a construct.
        using JsonDocument corpus = Load(Corpus);

        foreach (JsonElement seed in corpus.RootElement.GetProperty("seeds").EnumerateArray())
        {
            string id = seed.GetProperty("id").GetString() ?? "?";
            string[] present = SeedLanguages
                .Where(language => seed.TryGetProperty(language, out JsonElement value) &&
                                   value.ValueKind == JsonValueKind.String &&
                                   !string.IsNullOrWhiteSpace(value.GetString()))
                .ToArray();

            Assert.True(present.Length == 1,
                "The seed '" + id + "' carries " +
                (present.Length == 0
                    ? "no markup at all"
                    : "markup in both " + string.Join(" and ", present)) +
                ". A seed is written in exactly one of " + string.Join(", ", SeedLanguages) + ".");
        }
    }

    [Fact(Timeout = 600000)]
    public void The_Schema_The_Runner_And_This_Guard_Agree_About_The_Seed_Languages()
    {
        // Three places name the seed languages and none of them can see the
        // other two: the schema's oneOf decides what a seed may carry, the
        // runner's family-spelling table decides what it can check the fonts of,
        // and the list at the top of this file decides what these guards look
        // for. Drift between the first two is the dangerous one - a language the
        // schema admitted and the runner had no spelling for would be refused at
        // load, minutes into a provisioned run, with the seed blamed.
        //
        // The runner's side is read out of the source rather than referenced,
        // the same compromise The_Schema_And_The_Runner_Agree_About_The_Band_Vocabulary
        // makes and for the same reason: this project deliberately does not link
        // against the console runner.
        using JsonDocument schema = Load("tests/office/office-corpus.schema.json");
        string runner = File.ReadAllText(Path.Combine(
            PdfGuardRoots.Component, "src/tests/Broiler.Documents.Office/OfficeManifest.cs"));

        string[] declared = Regex
            .Matches(runner, "\\[\"([a-z]+)\"\\] = \\(")
            .Select(match => match.Groups[1].Value)
            .ToArray();
        Assert.True(declared.Length > 0,
            "OfficeManifest.cs no longer declares a family-spelling table, so this guard cannot " +
            "check what the runner accepts.");

        string[] permitted = schema.RootElement
            .GetProperty("$defs").GetProperty("seed").GetProperty("oneOf").EnumerateArray()
            .Select(branch => branch.GetProperty("required").EnumerateArray().First().GetString() ?? string.Empty)
            .ToArray();

        Assert.True(
            declared.Order(StringComparer.Ordinal).SequenceEqual(
                permitted.Order(StringComparer.Ordinal), StringComparer.Ordinal),
            "The seed languages have drifted. OfficeManifest.cs can materialise [" +
            string.Join(", ", declared) + "] and office-corpus.schema.json permits [" +
            string.Join(", ", permitted) + "].");

        Assert.True(
            declared.Order(StringComparer.Ordinal).SequenceEqual(
                SeedLanguages.Order(StringComparer.Ordinal), StringComparer.Ordinal),
            "This guard looks for [" + string.Join(", ", SeedLanguages) + "] and the runner " +
            "materialises [" + string.Join(", ", declared) + "], so the checks above are reading " +
            "less than the corpus can hold.");
    }

    /// <summary>Whichever of the two markup properties a seed carries.</summary>
    private static string Markup(JsonElement seed)
    {
        foreach (string language in SeedLanguages)
        {
            if (seed.TryGetProperty(language, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    [Fact(Timeout = 600000)]
    public void The_Office_Suite_Commits_No_Document_Of_Its_Own()
    {
        // A narrowing of FormatClaimGuardTests, and it exists to put the blame
        // in the right place. That guard walks the whole tree, so a workspace
        // somebody pointed inside the repository fails on whoever runs the tests
        // next rather than on whoever caused it. This one names the suite.
        foreach (string relative in new[] { "tests/office", Runner })
        {
            string directory = Path.Combine(PdfGuardRoots.Component, relative);
            if (!Directory.Exists(directory))
                continue;

            string[] documents = Directory
                .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Where(path => !PdfGuardRoots.IsBuildOutput(path))
                .Where(path => DocumentExtensions.Contains(
                    Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .Select(path => Path.GetRelativePath(PdfGuardRoots.Component, path).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.True(documents.Length == 0,
                "The office suite writes documents into a temporary workspace and commits none. " +
                "These are in the tree: " + string.Join(", ", documents));
        }
    }

    [Fact(Timeout = 600000)]
    public void The_Baseline_Stamp_Says_Which_Toolchain_Produced_It()
    {
        // A number measured against an unrecorded LibreOffice is not evidence,
        // and the file has to be able to say "nothing has been measured yet"
        // without that reading as an omission. Either every field of the stamp
        // is null and both sections are empty, or the stamp is filled in.
        using JsonDocument baseline = Load(BaselineFile);
        JsonElement stamp = baseline.RootElement.GetProperty("producedWith");

        bool unmeasured =
            stamp.GetProperty("libreoffice").ValueKind == JsonValueKind.Null &&
            baseline.RootElement.GetProperty("reads").GetArrayLength() == 0 &&
            baseline.RootElement.GetProperty("renders").GetArrayLength() == 0;

        if (unmeasured)
            return;

        Assert.False(
            string.IsNullOrWhiteSpace(stamp.GetProperty("libreoffice").GetString()),
            "The baseline holds rows and does not say which LibreOffice produced them.");
        Assert.False(
            string.IsNullOrWhiteSpace(stamp.GetProperty("platform").GetString()),
            "The baseline holds rows and does not say which platform produced them.");
    }

    [Fact(Timeout = 600000)]
    public void The_Schema_And_The_Runner_Agree_About_The_Band_Vocabulary()
    {
        // Both office-baseline.schema.json and the Bands class in the runner
        // cite this guard as the thing that stops the two drifting, and until
        // now it did not exist - which is the failure mode a control file is
        // supposed to prevent, occurring in the control file's own paperwork.
        //
        // The runner's side is read out of the source rather than referenced,
        // because Broiler.Documents.Office is a console runner this test project
        // deliberately does not link against. That is weaker than a compile-time
        // check and stronger than nothing: a renamed band changes the source
        // line, and the test says which side moved.
        using JsonDocument schema = Load("tests/office/office-baseline.schema.json");
        string runner = File.ReadAllText(Path.Combine(
            PdfGuardRoots.Component, "src/tests/Broiler.Documents.Office/OfficeBaseline.cs"));

        foreach ((string metric, string field) in new[]
                 {
                     ("GeometryBands", "geometry"),
                     ("InkBoxBands", "inkBox"),
                     ("InkProfileBands", "inkProfile"),
                     ("CoverageBands", "coverage"),
                     ("BlurDiffBands", "blurDiff"),
                 })
        {
            string[] declared = Vocabulary(runner, metric);
            Assert.True(declared.Length > 0,
                "OfficeBaseline.cs no longer declares " + metric + ", so this guard cannot check it.");

            string[] permitted = SchemaEnum(schema, field);
            Assert.True(permitted.Length > 0,
                "office-baseline.schema.json declares no enum for the band '" + field + "'.");

            Assert.True(
                declared.SequenceEqual(permitted, StringComparer.Ordinal),
                "The band vocabulary for '" + field + "' has drifted. The runner declares [" +
                string.Join(", ", declared) + "] and the schema permits [" +
                string.Join(", ", permitted) + "].");
        }
    }

    /// <summary>The band words a Bands list declares, in the order it declares them.</summary>
    private static string[] Vocabulary(string source, string member)
    {
        int at = source.IndexOf("> " + member + " =", StringComparison.Ordinal);
        if (at < 0)
            return [];

        int open = source.IndexOf('[', at);
        int close = source.IndexOf(']', open + 1);
        if (open < 0 || close < 0)
            return [];

        return source[(open + 1)..close]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => word.Trim('"'))
            .ToArray();
    }

    /// <summary>The enum a band field is constrained to, wherever the schema states it.</summary>
    private static string[] SchemaEnum(JsonDocument schema, string field)
    {
        var found = new List<string>();
        Walk(schema.RootElement, field, found);
        return found.ToArray();
    }

    private static void Walk(JsonElement node, string field, List<string> found)
    {
        if (found.Count > 0)
            return;

        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in node.EnumerateObject())
            {
                if (property.NameEquals(field) &&
                    property.Value.ValueKind == JsonValueKind.Object &&
                    property.Value.TryGetProperty("enum", out JsonElement words) &&
                    words.ValueKind == JsonValueKind.Array)
                {
                    found.AddRange(words.EnumerateArray().Select(word => word.GetString() ?? string.Empty));
                    return;
                }

                Walk(property.Value, field, found);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in node.EnumerateArray())
                Walk(item, field, found);
        }
    }

    private static string Key(JsonElement row) =>
        (row.GetProperty("seed").GetString() ?? "?") + "/" +
        (row.GetProperty("via").GetString() ?? "?") +
        (row.TryGetProperty("check", out JsonElement check)
            ? "/" + (check.GetString() ?? "?")
            : string.Empty);
}
