using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Broiler.Documents.Office;

/// <summary>Why a recorded difference is in the baseline.</summary>
/// <remarks>
/// An enum rather than three string constants because this suite reports the
/// three counts separately and a typo in a state would otherwise become a
/// fourth, silent category that nothing counts and nobody reviews. The wire
/// spelling is not the member name - <c>suspected-defect</c> carries a hyphen -
/// so the mapping is written out once, in
/// <see cref="OfficeBaseline.Spell(BaselineState)"/>, rather than guessed at
/// each call site.
/// </remarks>
internal enum BaselineState
{
    /// <summary>A limitation one of the conformance documents states.</summary>
    Documented,

    /// <summary>
    /// A difference nobody decided on. This is the state a regenerated row
    /// starts in, and it is the reason the suite exists: comparing against
    /// another implementation is good for finding differences, not for judging
    /// them, and the judging is a separate act by a person.
    /// </summary>
    SuspectedDefect,

    /// <summary>
    /// Looked at, understood, and accepted as this component's own behaviour
    /// rather than a format limitation or a defect.
    /// </summary>
    Accepted,
}

/// <summary>
/// One check whose result differs from what LibreOffice made of the same file.
/// </summary>
/// <remarks>
/// The check kind is part of the identity rather than a field inside a
/// per-document row. A seed can be read perfectly and still disagree about a
/// probe, and folding the two together would mean that fixing one of them
/// rewrote the other's prose.
/// </remarks>
internal sealed record ReadRow(
    string Seed,
    string Via,
    string Check,
    IReadOnlyList<string> Differences,
    IReadOnlyList<string> Diagnostics,
    BaselineState State,
    string? Reference,
    string Why,
    string? Reviewer,
    string? Decided)
{
    /// <summary>
    /// The identity of the row, prefixed with its family so that a read and a
    /// render of the same seed through the same format cannot collide in the
    /// one set of keys the run reports against.
    /// </summary>
    public string Key => KeyFor(Seed, Via, Check);

    /// <summary>
    /// The same key, for a caller holding the three parts rather than a row.
    /// </summary>
    /// <remarks>
    /// It exists because the second spelling of this string was written by hand
    /// and got the separator wrong, which made every committed row report as one
    /// the run never reached - a failure that reads as "the seed was renamed"
    /// and is really "two pieces of code disagreed about a colon". One
    /// definition, and the caller that reports against it uses this.
    /// </remarks>
    public static string KeyFor(string seed, string via, string check) =>
        "read/" + seed + "/" + via + "/" + check;
}

/// <summary>
/// The verdict of a rasterized comparison, one band per axis.
/// </summary>
/// <remarks>
/// Strings rather than five more enums, because these words are the schema's
/// vocabulary and the guard compares the two lists directly; see
/// <see cref="Bands"/>, which owns both the words and the thresholds that
/// produce them.
/// </remarks>
internal sealed record RenderBands(
    string Geometry,
    string InkBox,
    string InkProfile,
    string Coverage,
    string BlurDiff);

/// <summary>
/// The numbers the bands were derived from.
/// </summary>
/// <remarks>
/// They are recorded for a reader - so a row that says <c>loose</c> can be read
/// as barely loose or as hopelessly loose - and for hysteresis, so that a run
/// sitting on a threshold does not flap between two bands from one build to the
/// next. They are never the verdict. An expectation stated in these numbers
/// would fail on an antialiasing change nobody can see, and the band exists
/// precisely so that it does not.
/// </remarks>
internal sealed record RenderObserved(
    int InkBoxDeltaPx,
    double InkProfileSimilarity,
    double CoverageDelta,
    double BlurDiffRatio);

/// <summary>One rasterized page comparison that did not come out clean.</summary>
internal sealed record RenderRow(
    string Seed,
    string Via,
    int BroilerdocPages,
    int LibreOfficePages,
    int WorstPage,
    RenderBands Bands,
    RenderObserved Observed,
    BaselineState State,
    string? Reference,
    string Why,
    string? Reviewer,
    string? Decided)
{
    /// <summary>The identity of the row. See <see cref="ReadRow.Key"/>.</summary>
    public string Key => KeyFor(Seed, Via);

    /// <summary>The same key, for a caller holding the two parts rather than a row.</summary>
    public static string KeyFor(string seed, string via) => "render/" + seed + "/" + via;
}

/// <summary>
/// What produced a set of rows, pinned so that a run can tell whether they are
/// about the machine it is standing on.
/// </summary>
/// <remarks>
/// <para>
/// The corpus baseline needs nothing like this, because both sides of a round
/// trip are code in this repository. Here the other side is a program that is
/// not, and whose layout moves between releases and with whatever fonts it can
/// find. A row measured on another build is a true statement about another
/// machine, and reading it as a statement about this one is how a suite comes
/// to fail for reasons that have nothing to do with the change under test.
/// </para>
/// <para>
/// So a mismatch is a skip with the mismatch named, never a failure and never a
/// quiet pass. That is the same answer this repository gives for a missing
/// external tool, and for the same reason: the run has not learned that the
/// component is wrong, it has learned that it cannot tell.
/// </para>
/// </remarks>
internal sealed record ToolchainStamp(
    string? LibreOffice,
    string? Poppler,
    string? FontSet,
    string? Platform,
    int Dpi)
{
    /// <summary>
    /// The density the ink-box thresholds in <see cref="Bands"/> are stated at.
    /// It lives next to the stamp rather than only in the file, because a file
    /// that omitted the field would otherwise be compared at whatever density
    /// the run happened to use, and the bands would silently mean something
    /// else.
    /// </summary>
    public const int DefaultDpi = 144;

    /// <summary>
    /// The shipped stamp: nothing pinned. Every comparison against a baseline
    /// carrying this one is a skip, which is the intended reading of a file
    /// that has not been measured yet.
    /// </summary>
    public static ToolchainStamp Unpinned { get; } = new(null, null, null, null, DefaultDpi);

    /// <summary>
    /// Null when the two agree; otherwise the mismatch, named, for the skip
    /// reason.
    /// </summary>
    /// <remarks>
    /// Every mismatched field is named rather than only the first, because the
    /// usual case for a fresh checkout is that all of them are unpinned, and a
    /// skip reason that mentioned LibreOffice alone would send a reader off to
    /// install one version when what the file wants is a decision about the
    /// whole toolchain.
    /// </remarks>
    public string? Mismatch(ToolchainStamp observed)
    {
        var named = new List<string>();
        Compare(named, "libreoffice", LibreOffice, observed.LibreOffice);
        Compare(named, "poppler", Poppler, observed.Poppler);
        Compare(named, "fontSet", FontSet, observed.FontSet);
        Compare(named, "platform", Platform, observed.Platform);

        if (Dpi != observed.Dpi)
        {
            named.Add(
                "dpi: the rows were measured at " + Dpi.ToString(CultureInfo.InvariantCulture) +
                " and this run rasterized at " + observed.Dpi.ToString(CultureInfo.InvariantCulture) +
                ", which moves every ink-box band without anything having changed");
        }

        return named.Count == 0 ? null : string.Join("; ", named);
    }

    private static void Compare(List<string> named, string field, string? recorded, string? observed)
    {
        if (recorded is null)
        {
            // Not a defect in the run, and worth saying plainly: the shipped
            // file pins nothing on purpose, so this is the message a first
            // checkout gets, and it should read as "nobody has measured this
            // yet" rather than as a broken machine.
            named.Add(
                observed is null
                    ? field + ": the baseline pins nothing and this run could not tell what it has"
                    : field + ": the baseline pins nothing, and this run has " + observed);
            return;
        }

        if (observed is null)
        {
            named.Add(field + ": the rows were produced with " + recorded +
                      " and this run could not tell what it has");
            return;
        }

        if (!string.Equals(recorded, observed, StringComparison.Ordinal))
            named.Add(field + ": the rows were produced with " + recorded + " and this run has " + observed);
    }
}

/// <summary>
/// The expected shape of this component's disagreements with LibreOffice, as
/// <c>tests/office/office-baseline.json</c> records it.
/// </summary>
/// <remarks>
/// <para>
/// It records only what is <em>not</em> clean. Everything absent from it is
/// asserted to agree, so a new difference fails by not being listed rather than
/// by being listed differently. That way round the file stays readable, and the
/// check that matters - "nothing new broke" - does not depend on somebody
/// having written a row for the case that broke.
/// </para>
/// <para>
/// A row that stops reproducing also fails, and so does a render that comes
/// back better than the band recorded for it. That reads as harsh for a fix,
/// and it is deliberate: the point of separating <c>documented</c> from
/// <c>suspected-defect</c> is to watch the second list shrink, and a shrink
/// nobody records is a shrink nobody can show.
/// </para>
/// <para>
/// The one thing this baseline has that the corpus baseline does not is a
/// toolchain stamp, and it changes what a missing match means. A run that
/// cannot match the stamp does not fail these rows and does not pass them: it
/// skips, naming the mismatch, because it has learned nothing about the
/// component either way.
/// </para>
/// </remarks>
internal sealed class OfficeBaseline
{
    public const string RelativePath = "tests/office/office-baseline.json";

    /// <summary>
    /// The most differences one row records. A document that needs more than
    /// forty lines to describe is a defect nobody needs the fortieth line to
    /// see, and letting one such row run to hundreds would cost every other row
    /// its readability - which is the only property that makes this file worth
    /// reviewing rather than regenerating.
    /// </summary>
    public const int MaxDifferences = 40;

    private const string NotYetClassified =
        "Not yet classified. A row regenerated into this file starts as a suspected defect on " +
        "purpose: an unreviewed difference should read as one until somebody says otherwise.";

    private readonly Dictionary<string, ReadRow> _reads;
    private readonly Dictionary<string, RenderRow> _renders;

    private OfficeBaseline(ToolchainStamp stamp, IEnumerable<ReadRow> reads, IEnumerable<RenderRow> renders)
    {
        Stamp = stamp;
        _reads = reads.ToDictionary(row => row.Key, StringComparer.Ordinal);
        _renders = renders.ToDictionary(row => row.Key, StringComparer.Ordinal);
    }

    /// <summary>What the committed rows were measured with.</summary>
    public ToolchainStamp Stamp { get; }

    public IReadOnlyCollection<ReadRow> Reads => _reads.Values;

    public IReadOnlyCollection<RenderRow> Renders => _renders.Values;

    public static string PathIn(string repositoryRoot) =>
        Path.Combine(repositoryRoot, RelativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Reads the committed baseline.</summary>
    /// <remarks>
    /// This throws where the rest of the suite skips, and the difference is
    /// deliberate. A missing LibreOffice is an expected condition on a machine
    /// that never claimed to have one; a missing or unreadable
    /// <c>office-baseline.json</c> is a checkout that is not the repository,
    /// and a suite that shrugged and carried on would report a clean run
    /// against no expectations at all.
    /// </remarks>
    public static OfficeBaseline Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("No baseline at " + path + ". Write one with --update-baseline.");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        int version = root.GetProperty("schemaVersion").GetInt32();
        if (version != 1)
        {
            throw new InvalidDataException(
                "office-baseline.json declares schemaVersion " +
                version.ToString(CultureInfo.InvariantCulture) + ", which this runner cannot read.");
        }

        return new OfficeBaseline(
            ReadStamp(root),
            Rows(root, "reads").Select(row => new ReadRow(
                Text(row, "seed"),
                Text(row, "via"),
                Text(row, "check"),
                Strings(row, "differences"),
                Strings(row, "diagnostics"),
                ParseState(Optional(row, "state")),
                Optional(row, "reference"),
                Text(row, "why"),
                Optional(row, "reviewer"),
                Optional(row, "decided"))),
            Rows(root, "renders").Select(row => new RenderRow(
                Text(row, "seed"),
                Text(row, "via"),
                Count(row, "pageCount", "broilerdoc"),
                Count(row, "pageCount", "libreoffice"),
                row.TryGetProperty("worstPage", out JsonElement worst) ? worst.GetInt32() : 0,
                ReadBands(row),
                ReadObserved(row),
                ParseState(Optional(row, "state")),
                Optional(row, "reference"),
                Text(row, "why"),
                Optional(row, "reviewer"),
                Optional(row, "decided"))));
    }

    public ReadRow? FindRead(string seed, string via, string check) =>
        _reads.TryGetValue("read/" + seed + "/" + via + "/" + check, out ReadRow? row) ? row : null;

    public RenderRow? FindRender(string seed, string via) =>
        _renders.TryGetValue("render/" + seed + "/" + via, out RenderRow? row) ? row : null;

    /// <summary>
    /// Rows the run never reached. A row that stops reproducing fails.
    /// </summary>
    /// <remarks>
    /// Returned as keys rather than rows because the caller's next act is to
    /// print them, and sorted because an unordered report of what disappeared
    /// makes two runs of the same failure look like two different failures.
    /// </remarks>
    public IReadOnlyList<string> NotReproduced(IEnumerable<string> reachedKeys)
    {
        var reached = reachedKeys.ToHashSet(StringComparer.Ordinal);
        return _reads.Keys
            .Concat(_renders.Keys)
            .Where(key => !reached.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Writes a baseline from what a run actually observed, preserving the
    /// classification and prose of any row that is already there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The carry-over is the point. Regenerating a baseline is a mechanical act
    /// and the state and the why are not: throwing away a reviewer's judgement
    /// every time somebody adds a seed would make the classification decay to
    /// whatever the last regeneration guessed. So the measured half of a row
    /// comes from the run and the classified half comes from
    /// <paramref name="previous"/>, and the classification carried on the
    /// incoming rows is not consulted at all - the run measures, and a person
    /// classifies.
    /// </para>
    /// <para>
    /// Nothing here ever fills in <c>decided</c>. The date is available and
    /// stamping it would be one line, which is exactly why it is worth saying
    /// no in writing: a date in that field is a person's claim about when they
    /// looked at the row, and a program that wrote it would turn the whole
    /// reviewer quartet into decoration. The file's own <c>updated</c> field is
    /// a different thing - a fact about when the file was regenerated - and
    /// that one the program is entitled to.
    /// </para>
    /// </remarks>
    public static void Write(
        string path,
        ToolchainStamp stamp,
        IEnumerable<ReadRow> reads,
        IEnumerable<RenderRow> renders,
        OfficeBaseline? previous)
    {
        var readRows = new JsonArray();
        foreach (ReadRow row in reads
                     .OrderBy(row => row.Seed, StringComparer.Ordinal)
                     .ThenBy(row => row.Via, StringComparer.Ordinal)
                     .ThenBy(row => row.Check, StringComparer.Ordinal))
        {
            ReadRow? kept = previous?.FindRead(row.Seed, row.Via, row.Check);
            var json = new JsonObject
            {
                ["seed"] = row.Seed,
                ["via"] = row.Via,
                ["check"] = row.Check,
                ["differences"] = Items(Capped(row.Differences)),
                ["diagnostics"] = Items(Codes(row.Diagnostics)),
            };
            Classify(json, kept?.State, kept?.Reference, kept?.Why, kept?.Reviewer, kept?.Decided);
            readRows.Add(json);
        }

        var renderRows = new JsonArray();
        foreach (RenderRow row in renders
                     .OrderBy(row => row.Seed, StringComparer.Ordinal)
                     .ThenBy(row => row.Via, StringComparer.Ordinal))
        {
            RenderRow? kept = previous?.FindRender(row.Seed, row.Via);
            var json = new JsonObject
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
            };
            Classify(json, kept?.State, kept?.Reference, kept?.Why, kept?.Reviewer, kept?.Decided);
            renderRows.Add(json);
        }

        var document = new JsonObject
        {
            ["$schema"] = "office-baseline.schema.json",
            ["schemaVersion"] = 1,
            ["updated"] = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["producedWith"] = new JsonObject
            {
                ["libreoffice"] = stamp.LibreOffice,
                ["poppler"] = stamp.Poppler,
                ["fontSet"] = stamp.FontSet,
                ["platform"] = stamp.Platform,
                ["dpi"] = stamp.Dpi,
            },
            // The policy block is regenerated rather than carried over, so that
            // this file and the committed one cannot drift apart without
            // somebody editing the code that states the policy. It reads as
            // duplication and it is the cheaper half of the trade: prose that
            // survives regeneration only because nothing rewrote it is prose
            // nobody has to keep true.
            ["policy"] = new JsonObject
            {
                ["records"] = "Only the reads that differ from what LibreOffice made of the same file, " +
                              "and only the renders that do not land in the clean band. Everything this " +
                              "suite exercises that is absent from the two lists below is asserted to " +
                              "agree, so a new difference fails by not being listed.",
                ["reproduce"] = "A row that stops reproducing fails too. That is deliberate: a fix should " +
                                "arrive with this file updated, so the shrinking of the suspected-defect " +
                                "list is a thing somebody can point at. A render that comes back better " +
                                "than the band recorded here fails for the same reason - an improvement " +
                                "nobody records is an improvement nobody can show.",
                ["states"] = "documented - a limitation one of the conformance documents states. " +
                             "suspected-defect - a difference nobody decided on, which is what comparing " +
                             "against another implementation is actually good for. accepted - this " +
                             "component's own behaviour, looked at and accepted.",
                ["reviewer"] = "A row is not classified by being written down. documented and accepted " +
                               "both need a reviewer and a date, and a documented row needs a reference " +
                               "to the conformance document that states the limitation, which the guard " +
                               "checks is really there. This is the same demand CorpusControlGuardTests " +
                               "makes of the corpus baseline and PdfTestControlGuardTests makes of the " +
                               "tool manifest.",
                ["toolchain"] = "producedWith pins the LibreOffice build, the PDF text extractor, the " +
                                "font set, the platform and the dpi these rows were measured on. A run " +
                                "whose own stamp does not match it does not compare against these rows: " +
                                "it skips, and the skip names the mismatch. LibreOffice's layout moves " +
                                "between releases and with whatever fonts it can find, so a row measured " +
                                "on another machine is a claim about that machine, and quietly reading " +
                                "it as a claim about this one is how a suite comes to fail for reasons " +
                                "that have nothing to do with the change under test.",
                ["empty"] = "Both lists ship empty and every producedWith field is null, because nothing " +
                            "has been measured here against an approved, pinned toolchain yet. That is " +
                            "the shipped state on purpose, and it is not the same as having no opinion. " +
                            "An empty file says plainly that nothing has been measured; a file generated " +
                            "against whatever LibreOffice happened to be installed that morning looks " +
                            "like evidence, and would be worse than no baseline for exactly that reason. " +
                            "tests/corpus/external-sources.json ships with no rows on the same argument.",
                ["bands"] = "A render row records bands, not numbers. The numbers under observed are " +
                            "there for a reader, and for hysteresis when a run sits on a threshold; the " +
                            "verdict is the band. Rasterized comparison numbers move with antialiasing, " +
                            "hinting and the version of the font that got installed, so a file that " +
                            "recorded them as the expectation would fail on a change nobody can see and " +
                            "pass on one anybody could.",
                ["regenerate"] = "dotnet run --project src/tests/Broiler.Documents.Office -- --update-baseline",
            },
            ["reads"] = readRows,
            ["renders"] = renderRows,
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            // The seeds carry non-Latin text and characters every format has to
            // escape. Left to the default encoder they would land in this file
            // as \uXXXX and a reviewer could not read the row they are being
            // asked to classify.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        // Written with LF whichever host regenerated it. The indenting writer
        // takes its newline from the environment, and a Windows regeneration
        // would otherwise commit a whole-file diff against a Linux one and
        // neither would be wrong.
        File.WriteAllText(path, document.ToJsonString(options).ReplaceLineEndings("\n") + "\n");
    }

    /// <summary>The wire spelling of a state.</summary>
    /// <remarks>
    /// Written out rather than derived from the member name, because
    /// <c>suspected-defect</c> is hyphenated and a lowercasing convention that
    /// happened to work for two of the three values would fail silently on the
    /// one that matters most.
    /// </remarks>
    public static string Spell(BaselineState state) => state switch
    {
        BaselineState.Documented => "documented",
        BaselineState.Accepted => "accepted",
        _ => "suspected-defect",
    };

    /// <summary>
    /// The state a spelling names, defaulting to
    /// <see cref="BaselineState.SuspectedDefect"/>.
    /// </summary>
    /// <remarks>
    /// An unrecognised state reads as unreviewed rather than as documented or
    /// accepted. The schema rejects it long before this, so the only question
    /// is which way to fall when something has gone wrong anyway, and falling
    /// towards "nobody has decided this" is the only direction that cannot
    /// launder a difference into an approved one.
    /// </remarks>
    public static BaselineState ParseState(string? text) => text switch
    {
        "documented" => BaselineState.Documented,
        "accepted" => BaselineState.Accepted,
        _ => BaselineState.SuspectedDefect,
    };

    private static void Classify(
        JsonObject row,
        BaselineState? state,
        string? reference,
        string? why,
        string? reviewer,
        string? decided)
    {
        row["state"] = Spell(state ?? BaselineState.SuspectedDefect);
        row["reference"] = reference;
        row["why"] = why ?? NotYetClassified;
        row["reviewer"] = reviewer;
        row["decided"] = decided;
    }

    private static ToolchainStamp ReadStamp(JsonElement root)
    {
        if (!root.TryGetProperty("producedWith", out JsonElement stamp))
            return ToolchainStamp.Unpinned;

        return new ToolchainStamp(
            Optional(stamp, "libreoffice"),
            Optional(stamp, "poppler"),
            Optional(stamp, "fontSet"),
            Optional(stamp, "platform"),
            stamp.TryGetProperty("dpi", out JsonElement dpi) && dpi.ValueKind == JsonValueKind.Number
                ? dpi.GetInt32()
                : ToolchainStamp.DefaultDpi);
    }

    private static RenderBands ReadBands(JsonElement row)
    {
        // A row with no bands at all is a malformed file rather than a clean
        // render - the file records only what is not clean - so it is read as
        // the worst of each scale. That way a file somebody hand-edited into
        // nonsense cannot make a run pass.
        if (!row.TryGetProperty("bands", out JsonElement bands))
            return new RenderBands("off", "wild", "poor", "poor", "severe");

        return new RenderBands(
            Optional(bands, "geometry") ?? "off",
            Optional(bands, "inkBox") ?? "wild",
            Optional(bands, "inkProfile") ?? "poor",
            Optional(bands, "coverage") ?? "poor",
            Optional(bands, "blurDiff") ?? "severe");
    }

    private static RenderObserved ReadObserved(JsonElement row)
    {
        if (!row.TryGetProperty("observed", out JsonElement observed))
            return new RenderObserved(0, 0, 0, 0);

        return new RenderObserved(
            Number(observed, "inkBoxDeltaPx") is double ink ? (int)ink : 0,
            Number(observed, "inkProfileSimilarity") ?? 0,
            Number(observed, "coverageDelta") ?? 0,
            Number(observed, "blurDiffRatio") ?? 0);
    }

    private static IEnumerable<JsonElement> Rows(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray()
            : [];

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) ? value.GetString() ?? string.Empty : string.Empty;

    private static string? Optional(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) ? value.GetString() : null;

    private static int Count(JsonElement row, string group, string name) =>
        row.TryGetProperty(group, out JsonElement counts) &&
        counts.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static IReadOnlyList<string> Strings(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray()
            : [];

    private static string[] Sorted(IEnumerable<string> values) =>
        values.OrderBy(value => value, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// The diagnostic codes a row records: a set, sorted.
    /// </summary>
    /// <remarks>
    /// De-duplicated because a code reported twice is the same fact about the
    /// same check reported twice, and a row that recorded the repetition would
    /// stop matching the moment a codec reported it once - which is a diff
    /// about the codec's internals rather than about what it did to the
    /// document.
    /// </remarks>
    private static string[] Codes(IEnumerable<string> values) =>
        Sorted(values.Distinct(StringComparer.Ordinal));

    /// <summary>
    /// The differences a row records: sorted, so that a reordering inside the
    /// tool is not a diff here, and cut at <see cref="MaxDifferences"/> with
    /// the cut declared in the last entry rather than performed in silence.
    /// </summary>
    private static string[] Capped(IEnumerable<string> differences)
    {
        string[] all = Sorted(differences);
        if (all.Length <= MaxDifferences)
            return all;

        return
        [
            .. all.Take(MaxDifferences - 1),
            "... and " + (all.Length - (MaxDifferences - 1)).ToString(CultureInfo.InvariantCulture) +
            " more, not recorded: a row this size is a defect, not a baseline.",
        ];
    }

    private static JsonArray Items(IEnumerable<string> values) =>
        new(values.Select(value => (JsonNode)value!).ToArray());
}

/// <summary>
/// The bands a render comparison is reported in, and the thresholds that
/// produce them.
/// </summary>
/// <remarks>
/// <para>
/// The words and the numbers live together here for one reason: the schema
/// spells the same words as enums, and a guard compares the two lists. Renaming
/// a band in the classifier without updating the schema then fails a test
/// instead of quietly producing a file whose vocabulary nothing validates.
/// </para>
/// <para>
/// The thresholds are named constants for the same reason a band is not a
/// number. A magic 0.97 in a comparison says nothing about what it is being
/// asked; <c>InkProfileClose</c> at least says which side of which decision it
/// belongs to, and it can be cited from a conformance document.
/// </para>
/// </remarks>
internal static class Bands
{
    /// <summary>
    /// How far the two page boxes may differ and still count as the same page.
    /// Two pixels rather than zero because the two rasterizers round a page
    /// size to whole pixels independently, and a suite that called that a
    /// geometry failure would call every row a geometry failure.
    /// </summary>
    public const int GeometryTolerancePx = 2;

    public const int InkBoxTightPx = 8;
    public const int InkBoxNearPx = 32;
    public const int InkBoxLoosePx = 128;

    public const double InkProfileExactAbove = 0.995;
    public const double InkProfileCloseAbove = 0.97;
    public const double InkProfileFairAbove = 0.90;

    public const double CoverageExactWithin = 0.002;
    public const double CoverageCloseWithin = 0.01;
    public const double CoverageFairWithin = 0.05;

    public const double BlurCleanWithin = 0.005;
    public const double BlurLightWithin = 0.05;
    public const double BlurHeavyWithin = 0.20;

    /// <summary>Every scale, worst last. The order is the severity.</summary>
    public static readonly IReadOnlyList<string> GeometryBands = ["match", "off"];

    public static readonly IReadOnlyList<string> InkBoxBands = ["tight", "near", "loose", "wild"];

    public static readonly IReadOnlyList<string> InkProfileBands = ["exact", "close", "fair", "poor"];

    public static readonly IReadOnlyList<string> CoverageBands = ["exact", "close", "fair", "poor"];

    public static readonly IReadOnlyList<string> BlurDiffBands = ["clean", "light", "heavy", "severe"];

    private static readonly Dictionary<string, int> Severity = new(StringComparer.Ordinal);

    static Bands()
    {
        // Built from the lists rather than written out again, so that the rank
        // of a band cannot disagree with the order it is declared in. The two
        // similarity scales share their words and share their ranks, which is
        // why this assigns rather than adds.
        IReadOnlyList<string>[] scales =
            [GeometryBands, InkBoxBands, InkProfileBands, CoverageBands, BlurDiffBands];

        foreach (IReadOnlyList<string> scale in scales)
            for (int index = 0; index < scale.Count; index++)
                Severity[scale[index]] = index;
    }

    /// <summary>
    /// Whether the two rasterizations agree about the page box.
    /// </summary>
    /// <remarks>
    /// Binary, and checked first, because every other band is meaningless when
    /// this one says off: comparing ink on pages of different sizes measures
    /// the paper rather than the layout.
    /// </remarks>
    public static string Geometry(double widthDeltaPx, double heightDeltaPx) =>
        Math.Abs(widthDeltaPx) <= GeometryTolerancePx && Math.Abs(heightDeltaPx) <= GeometryTolerancePx
            ? "match"
            : "off";

    /// <summary>
    /// How far the bounding box of the drawn ink moved, by its worst edge.
    /// </summary>
    /// <remarks>
    /// The thresholds are pixels at <see cref="ToolchainStamp.DefaultDpi"/>, so
    /// tight is about a millimetre and a half and loose is about two
    /// centimetres. That is why the dpi is part of the toolchain stamp: the
    /// same page compared at another density lands in another band without
    /// anything having changed.
    /// </remarks>
    public static string InkBox(int maxEdgeDeltaPx) => maxEdgeDeltaPx switch
    {
        <= InkBoxTightPx => "tight",
        <= InkBoxNearPx => "near",
        <= InkBoxLoosePx => "loose",
        _ => "wild",
    };

    /// <summary>
    /// How similar the two row-by-row ink profiles are, by cosine similarity.
    /// </summary>
    /// <remarks>
    /// This is the axis that notices a line breaking somewhere else, which is
    /// the difference this suite exists to find. Cosine rather than a
    /// difference of sums because it is insensitive to how dark a rasterizer
    /// draws and sensitive to where it puts things, which is the way round this
    /// comparison wants.
    /// </remarks>
    public static string InkProfile(double cosineSimilarity) => cosineSimilarity switch
    {
        >= InkProfileExactAbove => "exact",
        >= InkProfileCloseAbove => "close",
        >= InkProfileFairAbove => "fair",
        _ => "poor",
    };

    /// <summary>
    /// How much ink each side put on the page, compared as an absolute
    /// difference of covered fraction.
    /// </summary>
    /// <remarks>
    /// It catches the whole missing element that happened to leave the bounding
    /// box alone - a dropped table rule, an image that did not draw - which no
    /// box comparison can see.
    /// </remarks>
    public static string Coverage(double absoluteDelta) => Math.Abs(absoluteDelta) switch
    {
        <= CoverageExactWithin => "exact",
        <= CoverageCloseWithin => "close",
        <= CoverageFairWithin => "fair",
        _ => "poor",
    };

    /// <summary>
    /// How much survives a blurred difference of the two pages, as a fraction.
    /// </summary>
    /// <remarks>
    /// Blurred because the unblurred difference of two independently hinted
    /// rasterizations is never empty. An exact pixel comparison would report a
    /// failure on every row, and a check that fails on everything reports
    /// nothing.
    /// </remarks>
    public static string BlurDiff(double ratio) => Math.Abs(ratio) switch
    {
        <= BlurCleanWithin => "clean",
        <= BlurLightWithin => "light",
        <= BlurHeavyWithin => "heavy",
        _ => "severe",
    };

    /// <summary>
    /// The severity of a band within its own scale, from 0 for the best.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It exists so that "the run came out worse than the row records" is a
    /// comparison of two integers rather than a table of special cases, one per
    /// pair of band names, which is where the fifth axis would have started
    /// disagreeing with the first.
    /// </para>
    /// <para>
    /// Better than recorded fails too, and the ordering makes that as easy to
    /// write as the other direction. It is not an oversight: an improvement
    /// nobody records is an improvement nobody can show, and a suite that
    /// accepted silent improvements would let a row that has quietly stopped
    /// measuring anything look exactly like a fix.
    /// </para>
    /// <para>
    /// An unknown word throws, and this is the one place in the suite that
    /// does. It is not an expected condition: it means the committed file and
    /// this classifier disagree about the vocabulary, and guessing a rank would
    /// turn that disagreement into a pass or into a failure that reads as a
    /// rendering change.
    /// </para>
    /// </remarks>
    public static int Rank(string band)
    {
        if (Severity.TryGetValue(band, out int rank))
            return rank;

        throw new ArgumentOutOfRangeException(
            nameof(band),
            band,
            "Not a band this classifier knows. The baseline and the code disagree about the " +
            "vocabulary, which is what the schema's enums exist to catch before a run gets here.");
    }

    /// <summary>
    /// How bad a whole row is, as the worst of its five bands.
    /// </summary>
    /// <remarks>
    /// The worst rather than the sum, because the five metrics are not
    /// commensurable and adding them would let four comfortable numbers hide one
    /// that is not. A page whose ink profile has collapsed is a finding whatever
    /// its coverage says.
    /// </remarks>
    public static int Rank(RenderBands bands) => Math.Max(
        Math.Max(Rank(bands.Geometry), Rank(bands.InkBox)),
        Math.Max(Rank(bands.InkProfile), Math.Max(Rank(bands.Coverage), Rank(bands.BlurDiff))));

    /// <summary>True when every band is the best its metric has.</summary>
    /// <remarks>
    /// This is what "clean" means for a render, and it is the condition under
    /// which no baseline row is needed. It is deliberately the same shape as the
    /// semantic axis's "no differences and no diagnostics": absence from the
    /// baseline is an assertion, so what counts as clean has to be stated
    /// somewhere a reader can find.
    /// </remarks>
    public static bool IsBest(RenderBands bands) => Rank(bands) == 0;

    /// <summary>
    /// Whether two sets of bands are the same set, metric by metric.
    /// </summary>
    /// <remarks>
    /// Not by <see cref="Rank(RenderBands)"/>. Collapsing five bands to their
    /// worst is the right way to decide which page of a document to record, and
    /// the wrong way to decide whether a row still reproduces: a run whose ink
    /// box got worse while its blurred difference got better keeps the same
    /// collapsed rank and passes, having changed in two ways that each deserved
    /// a look. The metrics are not commensurable, so they are compared
    /// individually.
    /// </remarks>
    public static bool Same(RenderBands left, RenderBands right) =>
        string.Equals(left.Geometry, right.Geometry, StringComparison.Ordinal) &&
        string.Equals(left.InkBox, right.InkBox, StringComparison.Ordinal) &&
        string.Equals(left.InkProfile, right.InkProfile, StringComparison.Ordinal) &&
        string.Equals(left.Coverage, right.Coverage, StringComparison.Ordinal) &&
        string.Equals(left.BlurDiff, right.BlurDiff, StringComparison.Ordinal);

    /// <summary>The five bands on one line, for a failure message.</summary>
    public static string Describe(RenderBands bands) =>
        "geometry " + bands.Geometry +
        ", ink box " + bands.InkBox +
        ", profile " + bands.InkProfile +
        ", coverage " + bands.Coverage +
        ", blurred " + bands.BlurDiff;
}
