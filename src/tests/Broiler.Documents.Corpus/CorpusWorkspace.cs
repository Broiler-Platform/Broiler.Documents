using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broiler.Documents.Corpus;

/// <summary>One materialised document: which corpus entry, which format, where.</summary>
/// <param name="Sample">
/// The generated sample this came from, or null when the document was authored
/// in the manifest rather than produced by the tool. The distinction is kept
/// because a few checks - rewriting the document to compare bytes, for one -
/// only mean something for a document the tool wrote.
/// </param>
internal sealed record CorpusDocument(string Id, CorpusFormat Format, string Path, CorpusSample? Sample)
{
    public string Label => Id + "/" + Format.Key;
}

/// <summary>
/// The corpus on disk, in a directory outside the repository.
/// </summary>
/// <remarks>
/// <para>
/// Documents arrive two ways, and the difference is the point. Samples are
/// written by the tool under test from a recipe, which costs nothing and
/// reaches only the constructs one of this component's own writers emits -
/// so the readers are shown exactly the shapes those writers already chose.
/// Authored documents are markup the manifest states outright, which is how
/// the corpus reaches a nested list, a table, a stylesheet destination or a
/// setext heading that no writer here produces.
/// </para>
/// <para>
/// Neither is a document somebody outside this project wrote, and neither
/// pretends to be. That is what tests/corpus/external-sources.json exists for,
/// and it is empty.
/// </para>
/// <para>
/// The directory is deliberately not inside the repository.
/// <c>FormatClaimGuardTests.No_Document_Of_A_Supported_Format_Is_Committed</c>
/// walks the whole tree for exactly the extensions this corpus writes, so a run
/// that materialised in place would make that guard fail - and it would be
/// right to.
/// </para>
/// </remarks>
internal sealed class CorpusWorkspace
{
    private readonly BroilerDocTool _tool;
    private readonly CorpusManifest _manifest;
    private readonly Dictionary<string, string> _assetPaths = new(StringComparer.Ordinal);
    private readonly List<CorpusDocument> _documents = [];
    private readonly Dictionary<string, string> _malformedPaths = new(StringComparer.Ordinal);

    public CorpusWorkspace(BroilerDocTool tool, CorpusManifest manifest, string directory)
    {
        _tool = tool;
        _manifest = manifest;
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
    }

    /// <summary>Where the corpus was written. Kept when <c>--keep</c> is given.</summary>
    public string Directory { get; }

    public IReadOnlyList<CorpusDocument> Documents => _documents;

    public string MalformedPath(string id) => _malformedPaths[id];

    /// <summary>
    /// Takes documents that arrived some other way - today only the external
    /// register's - into the set the checks run over, so a fetched document is
    /// treated exactly like an authored one from here on.
    /// </summary>
    public void Adopt(IEnumerable<CorpusDocument> documents) => _documents.AddRange(documents);

    /// <summary>
    /// Writes every asset, sample and malformed input, returning the checks that
    /// materialisation itself is. A sample that will not write is reported as a
    /// failed check rather than thrown, so one bad row does not take the rest of
    /// the suite down with it.
    /// </summary>
    public async Task<IReadOnlyList<CheckResult>> MaterialiseAsync()
    {
        var results = new List<CheckResult>();

        foreach (CorpusAsset asset in _manifest.Assets)
            results.AddRange(await WriteAssetAsync(asset).ConfigureAwait(false));

        foreach (CorpusSample sample in _manifest.Samples)
        {
            foreach (CorpusFormat format in CorpusFormat.All)
                results.Add(await WriteSampleAsync(sample, format).ConfigureAwait(false));
        }

        foreach (CorpusAuthored authored in _manifest.Authored)
            results.Add(WriteAuthored(authored));

        foreach (CorpusMalformed row in _manifest.Malformed)
            results.Add(WriteMalformed(row));

        return results;
    }

    private async Task<IReadOnlyList<CheckResult>> WriteAssetAsync(CorpusAsset asset)
    {
        var results = new List<CheckResult>();
        string seed = Path.Combine(Directory, "asset-" + asset.Id + asset.SourceFormat.Extension);
        string png = Path.Combine(Directory, "asset-" + asset.Id + ".png");

        ToolRun write = await _tool.RunAsync("new", "--out", seed, "--text", asset.Text, "--quiet")
            .ConfigureAwait(false);
        results.Add(CheckResult.ExpectExit("asset", "asset/" + asset.Id + "/seed", 0, write));
        if (write.ExitCode != 0)
            return results;

        ToolRun render = await _tool.RunAsync(
                "render", seed, "--out", png, "--continuous",
                "--page-size", asset.PageSize,
                "--margin", asset.MarginPoints.ToString(CultureInfo.InvariantCulture),
                "--quiet")
            .ConfigureAwait(false);
        results.Add(CheckResult.ExpectExit("asset", "asset/" + asset.Id + "/render", 0, render));

        if (render.ExitCode == 0 && File.Exists(png))
            _assetPaths[asset.Id] = png;
        else
        {
            results.Add(CheckResult.Fail(
                "asset", "asset/" + asset.Id + "/exists",
                "the render reported success but wrote no file at " + png + ".", render.CommandLine()));
        }

        return results;
    }

    private async Task<CheckResult> WriteSampleAsync(CorpusSample sample, CorpusFormat format)
    {
        string name = "materialise/" + sample.Id + "/" + format.Key;
        string path = Path.Combine(Directory, sample.Id + format.Extension);

        if (sample.AssetId is not null && !_assetPaths.ContainsKey(sample.AssetId))
        {
            return CheckResult.Skip(
                "materialise", name,
                "the asset " + sample.AssetId + " was not produced, so this sample cannot be written.");
        }

        var arguments = new List<string> { "new", "--out", path, "--text", sample.Text(), "--quiet" };
        foreach (string operation in sample.Operations)
        {
            arguments.Add("--op");
            arguments.Add(Substitute(operation));
        }

        ToolRun run = await _tool.RunAsync(arguments, standardInput: null).ConfigureAwait(false);
        if (run.ExitCode != 0)
            return CheckResult.ExpectExit("materialise", name, 0, run);

        if (!File.Exists(path) || new FileInfo(path).Length == 0)
        {
            return CheckResult.Fail(
                "materialise", name,
                "the tool exited 0 but wrote no bytes to " + Path.GetFileName(path) + ".", run.CommandLine());
        }

        _documents.Add(new CorpusDocument(sample.Id, format, path, sample));
        return CheckResult.Pass("materialise", name, run.Duration);
    }

    /// <summary>
    /// Writes a document the manifest states rather than one the tool produced.
    /// A package is zipped here, which is the only place this runner knows
    /// anything about a document format - and it is container knowledge, not
    /// format knowledge: which entry is stored and which is deflated, and
    /// nothing about what is inside them.
    /// </summary>
    private CheckResult WriteAuthored(CorpusAuthored authored)
    {
        string name = "materialise/" + authored.Id + "/" + authored.Format.Key;
        string path = Path.Combine(Directory, authored.Id + authored.Format.Extension);

        try
        {
            if (authored.Kind == "package")
            {
                using FileStream file = File.Create(path);
                using var archive = new ZipArchive(file, ZipArchiveMode.Create);
                foreach (CorpusPart part in authored.Parts)
                {
                    // The order is the manifest's, because for OpenDocument it
                    // is normative: the mimetype entry comes first and is
                    // stored, and a package that deflates it is one a probe is
                    // entitled to refuse.
                    ZipArchiveEntry entry = archive.CreateEntry(
                        part.Path,
                        part.Stored ? CompressionLevel.NoCompression : CompressionLevel.Optimal);

                    using Stream stream = entry.Open();
                    byte[] bytes = new UTF8Encoding(false).GetBytes(part.Content);
                    stream.Write(bytes, 0, bytes.Length);
                }
            }
            else
            {
                // Written without newline translation. The manifest states the
                // bytes, and a Windows runner that turned every \n into \r\n
                // would be testing a different document from the one a Linux
                // runner tested.
                File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(authored.Content ?? string.Empty));
            }
        }
        catch (IOException exception)
        {
            return CheckResult.Fail("materialise", name, "could not be written: " + exception.Message);
        }

        _documents.Add(new CorpusDocument(authored.Id, authored.Format, path, Sample: null));
        return CheckResult.Pass("materialise", name);
    }

    /// <summary>
    /// Replaces the manifest's <c>${asset:ID}</c> tokens. Nothing else in an
    /// operation is rewritten: the edit language has escaping rules of its own
    /// and a runner that also escaped would corrupt every operation carrying a
    /// colon or a backslash.
    /// </summary>
    private string Substitute(string operation)
    {
        foreach ((string id, string path) in _assetPaths)
            operation = operation.Replace("${asset:" + id + "}", path, StringComparison.Ordinal);

        return operation;
    }

    private CheckResult WriteMalformed(CorpusMalformed row)
    {
        string path = Path.Combine(Directory, "malformed-" + row.Id + row.Extension);
        _malformedPaths[row.Id] = path;

        try
        {
            switch (row.Kind)
            {
                case "absent":
                    if (File.Exists(path))
                        File.Delete(path);
                    break;

                case "empty":
                    File.WriteAllBytes(path, []);
                    break;

                case "literal":
                    File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(row.Content ?? string.Empty));
                    break;

                case "repeat":
                    var text = new StringBuilder(row.Prefix ?? string.Empty);
                    for (int index = 0; index < row.Count; index++)
                        text.Append(row.Unit);
                    text.Append(row.Suffix);
                    File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(text.ToString()));
                    break;

                case "truncate":
                    CorpusDocument? source = _documents.FirstOrDefault(document =>
                        document.Id == row.Of && document.Format.Key == row.Format);
                    if (source is null)
                    {
                        return CheckResult.Skip(
                            "malformed", "malformed/" + row.Id,
                            "the document it truncates, " + row.Of + " as " + row.Format + ", was not written.");
                    }

                    byte[] bytes = File.ReadAllBytes(source.Path);
                    File.WriteAllBytes(path, bytes[..Math.Min(row.Bytes, bytes.Length)]);
                    break;

                default:
                    return CheckResult.Fail(
                        "malformed", "malformed/" + row.Id,
                        "the manifest asks for a kind this runner does not build: " + row.Kind + ".");
            }
        }
        catch (IOException exception)
        {
            return CheckResult.Fail("malformed", "malformed/" + row.Id, "could not be written: " + exception.Message);
        }

        return CheckResult.Pass("malformed", "malformed/" + row.Id + "/write");
    }
}
