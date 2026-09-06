using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Broiler.Documents.Corpus;

/// <summary>One of the five formats the tool composes.</summary>
internal sealed record CorpusFormat(string Key, string ToolName, string Extension)
{
    /// <summary>
    /// The formats in the order <c>formats</c> reports them. The order is not
    /// load-bearing anywhere, but keeping it makes a report read alongside the
    /// tool's own output without the reader re-sorting either in their head.
    /// </summary>
    public static readonly IReadOnlyList<CorpusFormat> All =
    [
        new("docx", "DOCX", ".docx"),
        new("odt", "ODT", ".odt"),
        new("rtf", "RTF", ".rtf"),
        new("html", "HTML", ".html"),
        new("markdown", "Markdown", ".md"),
    ];

    public static CorpusFormat ByKey(string key) =>
        All.FirstOrDefault(format => format.Key == key)
        ?? throw new InvalidDataException("The corpus names a format this tool does not compose: " + key);
}

internal sealed record CorpusAsset(string Id, string Text, CorpusFormat SourceFormat, string PageSize, double MarginPoints);

internal sealed record CorpusSample(
    string Id,
    string Title,
    IReadOnlyList<string> Lines,
    IReadOnlyList<string> Operations,
    string? AssetId,
    IReadOnlyList<CorpusForbidden> MustNotAppear)
{
    /// <summary>
    /// The document text as <c>new --text</c> takes it: one paragraph per line,
    /// joined by newlines, and otherwise untouched. <c>--text</c> applies no
    /// escape language - that belongs to the <c>--op</c> values - so a backslash
    /// in the manifest is a backslash in the document and must not be rewritten
    /// on the way through.
    /// </summary>
    public string Text() => string.Join('\n', Lines);
}

/// <summary>
/// A string that must not reach a written document, and the formats that write
/// it anyway.
/// </summary>
/// <remarks>
/// The second half is what keeps the row honest. A format in
/// <see cref="EmittedBy"/> is asserted to <em>still</em> emit the string, so a
/// writer that gets fixed fails the suite until somebody takes it off the list -
/// the same bargain the round-trip baseline makes, for the same reason: a
/// recorded defect that stops reproducing is news, and news nobody records is
/// news nobody can show.
/// </remarks>
internal sealed record CorpusForbidden(string Text, IReadOnlyList<string> EmittedBy);

internal sealed record CorpusPart(string Path, bool Stored, string Content);

/// <summary>
/// A document written by hand in the manifest rather than produced by the tool.
/// </summary>
/// <remarks>
/// These exist because of the one weakness a generated corpus cannot argue its
/// way out of: it can only contain constructs one of this component's own
/// writers emits, so it presents the readers with exactly the shapes those
/// writers already chose. An authored document is the cheapest honest answer -
/// markup nobody here produced, with a provenance that is still just "somebody
/// wrote it in this file".
/// </remarks>
internal sealed record CorpusAuthored(
    string Id,
    string Title,
    CorpusFormat Format,
    string Kind,
    string? Content,
    IReadOnlyList<CorpusPart> Parts);

internal sealed record CorpusMalformed(
    string Id,
    string Title,
    string Kind,
    string Extension,
    int ExpectedExit,
    string? Content,
    string? Of,
    string? Format,
    int Bytes,
    string? Prefix,
    string? Unit,
    int Count,
    string? Suffix);

/// <summary>The corpus, as <c>tests/corpus/corpus.json</c> states it.</summary>
internal sealed record CorpusManifest(
    IReadOnlyList<CorpusAsset> Assets,
    IReadOnlyList<CorpusSample> Samples,
    IReadOnlyList<CorpusAuthored> Authored,
    IReadOnlyList<CorpusMalformed> Malformed)
{
    public const string RelativePath = "tests/corpus/corpus.json";

    public static CorpusManifest Load(string repositoryRoot)
    {
        string path = Path.Combine(repositoryRoot, RelativePath.Replace('/', Path.DirectorySeparatorChar));
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        int version = root.GetProperty("schemaVersion").GetInt32();
        if (version != 1)
        {
            throw new InvalidDataException(
                "corpus.json declares schemaVersion " +
                version.ToString(CultureInfo.InvariantCulture) +
                ", which this runner does not know how to read.");
        }

        var assets = root.GetProperty("assets").EnumerateArray().Select(asset =>
        {
            JsonElement recipe = asset.GetProperty("recipe");
            return new CorpusAsset(
                Text(asset, "id"),
                Text(recipe, "text"),
                CorpusFormat.ByKey(Text(recipe, "sourceFormat")),
                Text(recipe, "pageSize"),
                recipe.GetProperty("marginPoints").GetDouble());
        }).ToArray();

        var samples = root.GetProperty("samples").EnumerateArray().Select(sample => new CorpusSample(
            Text(sample, "id"),
            Text(sample, "title"),
            sample.GetProperty("lines").EnumerateArray().Select(line => line.GetString() ?? string.Empty).ToArray(),
            sample.GetProperty("operations").EnumerateArray().Select(op => op.GetString() ?? string.Empty).ToArray(),
            sample.TryGetProperty("asset", out JsonElement assetId) ? assetId.GetString() : null,
            sample.TryGetProperty("mustNotAppear", out JsonElement forbidden)
                ? forbidden.EnumerateArray().Select(item => new CorpusForbidden(
                    Text(item, "text"),
                    item.GetProperty("emittedBy").EnumerateArray()
                        .Select(format => format.GetString() ?? string.Empty).ToArray())).ToArray()
                : [])).ToArray();

        var authored = root.GetProperty("authored").EnumerateArray().Select(entry => new CorpusAuthored(
            Text(entry, "id"),
            Text(entry, "title"),
            CorpusFormat.ByKey(Text(entry, "format")),
            Text(entry, "kind"),
            Optional(entry, "content"),
            entry.TryGetProperty("parts", out JsonElement parts)
                ? parts.EnumerateArray().Select(part => new CorpusPart(
                    Text(part, "path"),
                    part.GetProperty("stored").GetBoolean(),
                    Text(part, "content"))).ToArray()
                : [])).ToArray();

        var malformed = root.GetProperty("malformed").EnumerateArray().Select(row => new CorpusMalformed(
            Text(row, "id"),
            Text(row, "title"),
            Text(row, "kind"),
            Text(row, "extension"),
            row.GetProperty("expectedExit").GetInt32(),
            Optional(row, "content"),
            Optional(row, "of"),
            Optional(row, "format"),
            row.TryGetProperty("bytes", out JsonElement bytes) ? bytes.GetInt32() : 0,
            Optional(row, "prefix"),
            Optional(row, "unit"),
            row.TryGetProperty("count", out JsonElement count) ? count.GetInt32() : 0,
            Optional(row, "suffix"))).ToArray();

        var manifest = new CorpusManifest(assets, samples, authored, malformed);
        manifest.Validate();
        return manifest;
    }

    /// <summary>
    /// The checks the JSON schema cannot make, because they are about one part
    /// of the file agreeing with another. A schema validator would pass a sample
    /// naming an asset that is not declared, and the failure would then arrive
    /// as an unreadable tool invocation several hundred checks later.
    /// </summary>
    private void Validate()
    {
        var problems = new List<string>();
        var assetIds = Assets.Select(asset => asset.Id).ToHashSet(StringComparer.Ordinal);
        var sampleIds = Samples.Select(sample => sample.Id).ToHashSet(StringComparer.Ordinal);

        foreach (CorpusSample sample in Samples)
        {
            foreach (string operation in sample.Operations)
            {
                int token = operation.IndexOf("${asset:", StringComparison.Ordinal);
                if (token < 0)
                    continue;

                if (sample.AssetId is null)
                    problems.Add(sample.Id + " uses an ${asset:...} token but declares no asset.");
                else if (!assetIds.Contains(sample.AssetId))
                    problems.Add(sample.Id + " names an asset that is not declared: " + sample.AssetId + ".");
            }
        }

        foreach (CorpusMalformed row in Malformed)
        {
            if (row.Kind != "truncate")
                continue;

            if (row.Of is null || !sampleIds.Contains(row.Of))
                problems.Add(row.Id + " truncates a sample that is not declared: " + (row.Of ?? "(none)") + ".");
        }

        foreach (CorpusAuthored authored in Authored)
        {
            bool package = authored.Kind == "package";
            if (package == (authored.Parts.Count > 0))
                continue;

            problems.Add(authored.Id + " is a " + authored.Kind +
                         " but carries " + (package ? "no parts" : "parts, which only a package has") + ".");
        }

        // Ids reach the report and the baseline, where two rows with the same
        // name would silently overwrite one another rather than fail.
        foreach (string duplicate in Samples.Select(sample => sample.Id)
                     .Concat(Authored.Select(authored => authored.Id))
                     .GroupBy(id => id, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1).Select(group => group.Key))
        {
            problems.Add("Two documents share the id " + duplicate + ".");
        }

        if (problems.Count > 0)
            throw new InvalidDataException("corpus.json does not hang together:\n  " + string.Join("\n  ", problems));
    }

    private static string Text(JsonElement element, string name) =>
        element.GetProperty(name).GetString()
        ?? throw new InvalidDataException("corpus.json has a null where " + name + " must be a string.");

    private static string? Optional(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) ? value.GetString() : null;
}
