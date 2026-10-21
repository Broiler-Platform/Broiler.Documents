using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Documents.Corpus;

namespace Broiler.Documents.Office;

/// <summary>
/// A sample document discovered recursively under LibreOffice's <c>sw/qa/extras</c>
/// tree (or a local mirror of it).
/// </summary>
/// <param name="Id">
/// Stable relative identifier with forward slashes, e.g.
/// <c>ooxmlexport/data/tdf12345.docx</c>.
/// </param>
/// <param name="Extension">Lower-case extension including the leading dot, e.g. <c>.docx</c>.</param>
/// <param name="Format">Canonical format family (<c>docx</c>, <c>odt</c>, <c>rtf</c>, <c>html</c>, <c>md</c>, <c>csv</c>, <c>pdf</c>).</param>
/// <param name="LocalPath">Where the file was materialised inside the temporary workspace.</param>
/// <param name="SourceUrl">The upstream URL the sample came from, when downloaded from GitHub.</param>
internal sealed record ExtrasSample(
    string Id,
    string Extension,
    string Format,
    string LocalPath,
    string? SourceUrl);

/// <summary>
/// The combined semantic and pixel evaluation of one <see cref="ExtrasSample"/>,
/// together with a deterministic severity score so the top <c>x</c> major
/// differences can be ranked and reported.
/// </summary>
/// <param name="CrashRegressionFamily">
/// The <see cref="CrashRegressions"/> family the sample belongs to, or null for
/// a document. A sample in a family was never compared with LibreOffice; the
/// only thing recorded about it is whether <c>broilerdoc</c> handled it.
/// </param>
/// <param name="RefusedByBoth">
/// True when LibreOffice produced nothing for the document - no text and no
/// pages - and <c>broilerdoc</c> refused it with a documented exit code. An
/// encrypted package opened without its password and a part that is not XML
/// are refused by both; that is agreement, not a difference.
/// </param>
internal sealed record ExtrasDifference(
    ExtrasSample Sample,
    bool BroilerReadFailed,
    int BroilerExitCode,
    string? BroilerError,
    bool LibreOfficeFailed,
    SemanticReading? Semantic,
    RenderRow? Render,
    string? DiffImagePath,
    double SeverityScore,
    string Summary,
    string? CrashRegressionFamily = null,
    bool RefusedByBoth = false)
{
    /// <summary>True for a crash-regression input, which is never ranked.</summary>
    public bool SetAside => CrashRegressionFamily is not null;

    /// <summary>
    /// True when either the semantic projection or the pixel rendering diverged
    /// between LibreOffice and <c>broilerdoc</c>, or when <c>broilerdoc</c>
    /// failed to read or render a document that LibreOffice handled. For a
    /// crash-regression input it is only the second: there was nothing to
    /// diverge from. A document both refused has none.
    /// </summary>
    public bool HasDifference =>
        !RefusedByBoth &&
        (BroilerReadFailed ||
        (Semantic is not null && (Semantic.Differences.Count > 0 || Semantic.Diagnostics.Count > 0)) ||
        (Render is not null && (!Bands.IsBest(Render.Bands) || Render.BroilerdocPages != Render.LibreOfficePages)));
}

/// <summary>
/// Discovers, downloads, renders and compares sample documents from
/// <c>https://github.com/LibreOffice/core/tree/master/sw/qa/extras</c>, then
/// ranks the most major differences and optionally files or updates a GitHub issue.
/// </summary>
internal static class ExtrasConformance
{
    /// <summary>The default upstream LibreOffice Writer QA extras URL.</summary>
    public const string DefaultExtrasUrl = "https://github.com/LibreOffice/core/tree/master/sw/qa/extras";

    /// <summary>Default number of major differences to include in the GitHub issue.</summary>
    public const int DefaultTopDifferences = 3;

    /// <summary>
    /// Every file extension supported across Broiler.Documents' codecs and
    /// templates, mapped to the canonical format family passed to <c>broilerdoc</c>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> SupportedExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".docx"] = "docx",
            [".dotx"] = "docx",
            [".odt"] = "odt",
            [".ott"] = "odt",
            [".fodt"] = "odt",
            [".rtf"] = "rtf",
            [".html"] = "html",
            [".htm"] = "html",
            [".md"] = "md",
            [".markdown"] = "md",
            [".csv"] = "csv",
            [".pdf"] = "pdf",
        };

    private const int BandHeight = 4;
    private const int ProfileSmoothingBands = 6;
    private const int BlurRadius = 3;
    private const int BlurDelta = 32;

    /// <summary>
    /// Resolves the canonical format family for a file path, or null when the
    /// file's extension is not one of the supported formats.
    /// </summary>
    public static string? FormatForExtension(string pathOrExtension)
    {
        string ext = pathOrExtension.StartsWith('.')
            ? pathOrExtension
            : Path.GetExtension(pathOrExtension);

        return SupportedExtensions.TryGetValue(ext, out string? format) ? format : null;
    }

    /// <summary>
    /// Parses a GitHub tree URL such as
    /// <c>https://github.com/LibreOffice/core/tree/master/sw/qa/extras</c>
    /// into its owner, repository, branch/ref, and subpath components.
    /// </summary>
    public static (string Owner, string Repo, string Branch, string Subpath) ParseGitHubTreeUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            throw new ArgumentException("Invalid extras URL '" + url + "'.", nameof(url));

        string[] segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 4 && string.Equals(segments[2], "tree", StringComparison.OrdinalIgnoreCase))
        {
            string owner = segments[0];
            string repo = segments[1];
            string branch = segments[3];
            string subpath = segments.Length > 4 ? string.Join("/", segments.Skip(4)) : string.Empty;
            return (owner, repo, branch, subpath);
        }

        if (segments.Length >= 2)
        {
            return (segments[0], segments[1], "master", "sw/qa/extras");
        }

        throw new ArgumentException(
            "Expected a GitHub tree URL of the form https://github.com/<owner>/<repo>/tree/<branch>/<path>, got '" +
            url + "'.",
            nameof(url));
    }

    /// <summary>
    /// Discovers and materialises all supported sample files into
    /// <paramref name="workspace"/>, either from a local directory
    /// (<paramref name="extrasDirectory"/>) or by downloading from
    /// <paramref name="extrasUrl"/>.
    /// </summary>
    public static async Task<IReadOnlyList<ExtrasSample>> AcquireSamplesAsync(
        string workspace,
        string? extrasUrl,
        string? extrasDirectory,
        string? only,
        int? maxFiles)
    {
        string downloadRoot = Path.Combine(workspace, "extras-src");
        Directory.CreateDirectory(downloadRoot);

        if (!string.IsNullOrWhiteSpace(extrasDirectory))
        {
            if (!Directory.Exists(extrasDirectory))
                throw new DirectoryNotFoundException("The directory named by --extras-dir does not exist: " + extrasDirectory);

            return DiscoverFromDirectory(extrasDirectory, sourceUrlPrefix: null, only, maxFiles);
        }

        string url = string.IsNullOrWhiteSpace(extrasUrl) ? DefaultExtrasUrl : extrasUrl!;
        (string owner, string repo, string branch, string subpath) = ParseGitHubTreeUrl(url);
        string rawPrefix = "https://github.com/" + owner + "/" + repo + "/blob/" + branch +
                           (string.IsNullOrEmpty(subpath) ? string.Empty : "/" + subpath);

        // Strategy 1: Git sparse checkout with blobless clone (--filter=blob:none --depth 1 --sparse).
        // When git is available and the caller has not capped to a tiny sample, sparse-checkout fetches
        // the subtree in a single packfile without GitHub REST API rate limits.
        // When --max-files is small or git sparse checkout fails, Strategy 2 uses the GitHub Trees API
        // and downloads only the matching supported files directly.
        if (maxFiles is null or > 50)
        {
            string? sparseDir = TryGitSparseCheckout(downloadRoot, owner, repo, branch, subpath);
            if (sparseDir is not null && Directory.Exists(sparseDir))
            {
                return DiscoverFromDirectory(sparseDir, rawPrefix, only, maxFiles);
            }
        }

        IReadOnlyList<ExtrasSample>? fromApi = await TryDownloadViaGitHubApiAsync(
            downloadRoot, owner, repo, branch, subpath, rawPrefix, only, maxFiles).ConfigureAwait(false);
        if (fromApi is not null)
            return fromApi;

        // Final fallback if API failed (e.g. rate limit when maxFiles <= 50): try git sparse checkout.
        string? fallbackDir = TryGitSparseCheckout(downloadRoot, owner, repo, branch, subpath);
        if (fallbackDir is not null && Directory.Exists(fallbackDir))
            return DiscoverFromDirectory(fallbackDir, rawPrefix, only, maxFiles);

        throw new IOException(
            "Could not download extras samples from " + url +
            " via GitHub API or git sparse-checkout.");
    }

    /// <summary>
    /// Recursively scans a local directory for all supported formats and returns
    /// them in deterministic ordinal order.
    /// </summary>
    public static IReadOnlyList<ExtrasSample> DiscoverFromDirectory(
        string rootDirectory,
        string? sourceUrlPrefix,
        string? only,
        int? maxFiles)
    {
        string fullRoot = Path.GetFullPath(rootDirectory);
        var samples = new List<ExtrasSample>();

        foreach (string file in Directory
                     .EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(fullRoot, file).Replace('\\', '/');
            if (relative.StartsWith(".git/", StringComparison.Ordinal) ||
                relative.Contains("/.git/", StringComparison.Ordinal))
            {
                continue;
            }

            string extension = Path.GetExtension(file).ToLowerInvariant();
            string? format = FormatForExtension(extension);
            if (format is null)
                continue;

            if (only is not null &&
                !relative.Contains(only, StringComparison.OrdinalIgnoreCase) &&
                !format.Contains(only, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? sourceUrl = sourceUrlPrefix is null
                ? null
                : sourceUrlPrefix.TrimEnd('/') + "/" + relative;

            samples.Add(new ExtrasSample(relative, extension, format, file, sourceUrl));
            if (maxFiles is { } cap && samples.Count >= cap)
                break;
        }

        return samples;
    }

    private static string? TryGitSparseCheckout(
        string downloadRoot, string owner, string repo, string branch, string subpath)
    {
        string repoDir = Path.Combine(downloadRoot, "git-sparse");
        try
        {
            if (Directory.Exists(repoDir))
                Directory.Delete(repoDir, recursive: true);

            Directory.CreateDirectory(repoDir);
            string remote = "https://github.com/" + owner + "/" + repo + ".git";

            if (!RunGit(repoDir, ["clone", "--filter=blob:none", "--no-checkout", "--depth", "1", "--sparse", "--branch", branch, remote, "."], TimeSpan.FromMinutes(3)))
                return null;

            if (!string.IsNullOrEmpty(subpath))
            {
                if (!RunGit(repoDir, ["sparse-checkout", "set", subpath], TimeSpan.FromMinutes(1)))
                    return null;
            }

            if (!RunGit(repoDir, ["checkout"], TimeSpan.FromMinutes(5)))
                return null;

            return string.IsNullOrEmpty(subpath)
                ? repoDir
                : Path.Combine(repoDir, subpath.Replace('/', Path.DirectorySeparatorChar));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool RunGit(string workingDirectory, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        var start = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string arg in arguments)
            start.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return false;

            _ = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                    // Best-effort termination.
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task<IReadOnlyList<ExtrasSample>?> TryDownloadViaGitHubApiAsync(
        string downloadRoot,
        string owner,
        string repo,
        string branch,
        string subpath,
        string blobPrefix,
        string? only,
        int? maxFiles)
    {
        using HttpClient client = CreateHttpClient();

        try
        {
            // Resolve the target tree SHA so we only traverse sw/qa/extras rather than the entire LibreOffice repo.
            string? treeSha = await ResolveSubtreeShaAsync(client, owner, repo, branch, subpath).ConfigureAwait(false);
            if (treeSha is null)
                return null;

            string treeUrl = "https://api.github.com/repos/" + owner + "/" + repo + "/git/trees/" + treeSha + "?recursive=1";
            using HttpResponseMessage response = await client.GetAsync(treeUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            string jsonText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(jsonText);
            if (!doc.RootElement.TryGetProperty("tree", out JsonElement tree) ||
                tree.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var candidates = new List<(string RelativePath, string Extension, string Format)>();
            foreach (JsonElement item in tree.EnumerateArray())
            {
                if (!item.TryGetProperty("type", out JsonElement typeProp) ||
                    !string.Equals(typeProp.GetString(), "blob", StringComparison.Ordinal))
                {
                    continue;
                }

                string? path = item.TryGetProperty("path", out JsonElement pathProp) ? pathProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                string ext = Path.GetExtension(path).ToLowerInvariant();
                string? format = FormatForExtension(ext);
                if (format is null)
                    continue;

                if (only is not null &&
                    !path.Contains(only, StringComparison.OrdinalIgnoreCase) &&
                    !format.Contains(only, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                candidates.Add((path, ext, format));
            }

            candidates.Sort((left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
            if (maxFiles is { } cap && candidates.Count > cap)
                candidates.RemoveRange(cap, candidates.Count - cap);

            string filesRoot = Path.Combine(downloadRoot, "api-files");
            Directory.CreateDirectory(filesRoot);

            int totalCandidates = candidates.Count;
            int downloadStep = totalCandidates > 0 ? Math.Max(1, (int)Math.Ceiling(totalCandidates / 100.0)) : 1;
            int downloadedCount = 0;

            var downloaded = new List<ExtrasSample>(totalCandidates);
            await Parallelism.ForEachAsync(candidates, 8, async candidate =>
            {
                string fullSubpath = string.IsNullOrEmpty(subpath)
                    ? candidate.RelativePath
                    : subpath.TrimEnd('/') + "/" + candidate.RelativePath;
                string rawUrl = "https://raw.githubusercontent.com/" + owner + "/" + repo + "/" + branch + "/" + fullSubpath;
                string localPath = Path.Combine(
                    filesRoot,
                    candidate.RelativePath.Replace('/', Path.DirectorySeparatorChar));

                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                byte[] bytes = await client.GetByteArrayAsync(rawUrl).ConfigureAwait(false);
                await File.WriteAllBytesAsync(localPath, bytes).ConfigureAwait(false);

                string sourceUrl = blobPrefix.TrimEnd('/') + "/" + candidate.RelativePath;
                lock (downloaded)
                {
                    downloaded.Add(new ExtrasSample(
                        candidate.RelativePath, candidate.Extension, candidate.Format, localPath, sourceUrl));
                }

                int current = Interlocked.Increment(ref downloadedCount);
                if (current % downloadStep == 0 || current == totalCandidates)
                {
                    int percent = (int)Math.Min(100, Math.Round(current * 100.0 / totalCandidates));
                    string utc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                    Console.WriteLine(
                        "download    [" + utc + "] " +
                        current.ToString(CultureInfo.InvariantCulture) + "/" +
                        totalCandidates.ToString(CultureInfo.InvariantCulture) +
                        " (" + percent.ToString(CultureInfo.InvariantCulture) + "%)");
                }
            }).ConfigureAwait(false);

            return downloaded
                .OrderBy(sample => sample.Id, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return null;
        }
    }

    private static async Task<string?> ResolveSubtreeShaAsync(
        HttpClient client, string owner, string repo, string branch, string subpath)
    {
        if (string.IsNullOrEmpty(subpath))
            return branch;

        string[] segments = subpath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string currentPath = string.Empty;
        string? lastSha = null;

        for (int i = 0; i < segments.Length; i++)
        {
            string parentPath = currentPath;
            currentPath = string.IsNullOrEmpty(currentPath) ? segments[i] : currentPath + "/" + segments[i];
            string contentsUrl = "https://api.github.com/repos/" + owner + "/" + repo + "/contents/" +
                                 parentPath + "?ref=" + Uri.EscapeDataString(branch);

            using HttpResponseMessage response = await client.GetAsync(contentsUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            lastSha = null;
            foreach (JsonElement entry in doc.RootElement.EnumerateArray())
            {
                string? name = entry.TryGetProperty("name", out JsonElement nameProp) ? nameProp.GetString() : null;
                if (string.Equals(name, segments[i], StringComparison.Ordinal))
                {
                    lastSha = entry.TryGetProperty("sha", out JsonElement shaProp) ? shaProp.GetString() : null;
                    break;
                }
            }

            if (lastSha is null)
                return null;
        }

        return lastSha;
    }

    /// <summary>
    /// Evaluates one <see cref="ExtrasSample"/> with LibreOffice headless and
    /// <c>broilerdoc</c>, returning check results, readings, render metrics,
    /// and a ranked <see cref="ExtrasDifference"/>.
    /// </summary>
    public static async Task<(IReadOnlyList<CheckResult> Results, SemanticReading? Reading, RenderRow? Render, ExtrasDifference Difference)> EvaluateSampleAsync(
        BroilerDocTool tool,
        LibreOfficeTool libreOffice,
        Rasterizer? rasterizer,
        ExtrasSample sample,
        string workspace,
        string axis,
        int dpi,
        int maxPages,
        IReadOnlyList<string> fontDirectories,
        IReadOnlyList<string> fontArguments,
        TimeSpan libreOfficeTimeout,
        Func<string> nextProfile)
    {
        var results = new List<CheckResult>();
        string safeKey = SanitizePath(sample.Id);
        string? inputSourcePath = sample.LocalPath;

        string[] fromArgs = FormatArguments(sample);

        bool broilerReadFailed = false;
        int broilerExitCode = 0;
        string? broilerError = null;
        bool libreOfficeFailed = false;
        bool libreOfficeAnswered = false;
        SemanticReading? reading = null;
        RenderRow? renderRow = null;
        string? diffImagePath = null;

        // 1. Semantic axis: compare LibreOffice plain-text extraction with broilerdoc dump --as text.
        if (axis is "semantic" or "all")
        {
            ToolRun info = await tool
                .RunAsync(["info", inputSourcePath, .. fromArgs, "--json", "--quiet"])
                .ConfigureAwait(false);

            IReadOnlyList<string> diagnostics = ExtractDiagnostics(info.Json());
            if (info.ExitCode != 0)
            {
                broilerReadFailed = true;
                broilerExitCode = info.ExitCode;
                broilerError = FirstNonEmptyLine(info.StandardError) ?? ("info exited " + info.ExitCode.ToString(CultureInfo.InvariantCulture));
                results.Add(CheckResult.Fail(
                    "read",
                    "read/" + sample.Id,
                    "broilerdoc info exited " + info.ExitCode.ToString(CultureInfo.InvariantCulture) + ": " + broilerError,
                    info.CommandLine()));
            }
            else
            {
                results.Add(CheckResult.Pass("read", "read/" + sample.Id, info.Duration));
            }

            string textDir = Path.Combine(workspace, "lo-text", safeKey);
            LibreOfficeRun extracted = libreOffice.Convert(
                sample.LocalPath,
                LibreOfficeFilters.Text,
                textDir,
                nextProfile(),
                libreOfficeTimeout,
                LibreOfficeTool.InputFilterFor(sample.Extension));

            if (extracted.TimedOut || extracted.ExitCode != 0 || extracted.OutputPath is null || !File.Exists(extracted.OutputPath))
            {
                // For formats like PDF or CSV where Writer's Text (encoded):UTF8 filter may not apply,
                // skip text extraction cleanly rather than blaming broilerdoc.
                libreOfficeFailed = true;
                results.Add(CheckResult.Skip(
                    "text",
                    "text/" + sample.Id,
                    "LibreOffice headless did not produce a plain-text projection for this sample."));
            }
            else
            {
                libreOfficeAnswered = true;
                ToolRun dumped = await tool
                    .RunAsync(["dump", inputSourcePath, .. fromArgs, "--as", "text", "--quiet"])
                    .ConfigureAwait(false);

                if (dumped.ExitCode != 0)
                {
                    broilerReadFailed = true;
                    broilerExitCode = dumped.ExitCode;
                    broilerError ??= FirstNonEmptyLine(dumped.StandardError) ?? ("dump exited " + dumped.ExitCode.ToString(CultureInfo.InvariantCulture));
                    results.Add(CheckResult.Fail(
                        "text",
                        "text/" + sample.Id,
                        "broilerdoc dump --as text exited " + dumped.ExitCode.ToString(CultureInfo.InvariantCulture) + ".",
                        dumped.CommandLine()));
                }
                else
                {
                    string theirs = new UTF8Encoding(false).GetString(File.ReadAllBytes(extracted.OutputPath));
                    string ours = dumped.StandardOutput;
                    IReadOnlyList<string> differences = TextProjection.Differences(ours, theirs, "broilerdoc", "libreoffice");
                    reading = new SemanticReading(sample.Id, sample.Format, "text", differences, diagnostics);

                    if (differences.Count == 0)
                    {
                        results.Add(CheckResult.Pass("text", "text/" + sample.Id, dumped.Duration));
                    }
                    else
                    {
                        results.Add(CheckResult.Fail(
                            "text",
                            "text/" + sample.Id,
                            differences.Count.ToString(CultureInfo.InvariantCulture) + " line difference(s) between broilerdoc and LibreOffice:\n      " +
                            string.Join("\n      ", differences.Take(8)),
                            dumped.CommandLine()));
                    }
                }
            }
        }

        // 2. Pixel axis: render with LibreOffice headless -> PDF -> pdftoppm, and render with broilerdoc -> BMP.
        if (axis is "pixel" or "all")
        {
            if (rasterizer is null)
            {
                results.Add(CheckResult.Skip(
                    "pixel",
                    "pixel/" + sample.Id,
                    "no rasteriser is available, so the two renderings could not be compared."));
            }
            else
            {
                string? pdfPath = null;
                if (string.Equals(sample.Extension, ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    // For a PDF sample, try LibreOffice headless PDF export first; if LibreOffice
                    // does not use a Writer filter on PDF, rasterise the PDF sample directly so the
                    // visual comparison against broilerdoc render still runs.
                    string loPdfDir = Path.Combine(workspace, "lo-pdf", safeKey);
                    LibreOfficeRun loPdf = libreOffice.Convert(
                        sample.LocalPath,
                        "pdf",
                        loPdfDir,
                        nextProfile(),
                        libreOfficeTimeout);
                    pdfPath = loPdf.ExitCode == 0 && loPdf.OutputPath is not null && File.Exists(loPdf.OutputPath)
                        ? loPdf.OutputPath
                        : sample.LocalPath;
                }
                else
                {
                    string loPdfDir = Path.Combine(workspace, "lo-pdf", safeKey);
                    LibreOfficeRun loPdf = libreOffice.Convert(
                        sample.LocalPath,
                        LibreOfficeFilters.Pdf,
                        loPdfDir,
                        nextProfile(),
                        libreOfficeTimeout,
                        LibreOfficeTool.InputFilterFor(sample.Extension));

                    if (loPdf.TimedOut || loPdf.ExitCode != 0 || loPdf.OutputPath is null || !File.Exists(loPdf.OutputPath))
                    {
                        // Retry with generic pdf filter (e.g. for .csv or Draw/Calc fallback)
                        loPdf = libreOffice.Convert(
                            sample.LocalPath,
                            "pdf",
                            loPdfDir,
                            nextProfile(),
                            libreOfficeTimeout,
                            LibreOfficeTool.InputFilterFor(sample.Extension));
                    }

                    if (!loPdf.TimedOut && loPdf.ExitCode == 0 && loPdf.OutputPath is not null && File.Exists(loPdf.OutputPath))
                    {
                        pdfPath = loPdf.OutputPath;
                    }
                    else
                    {
                        libreOfficeFailed = true;
                        results.Add(CheckResult.Skip(
                            "pixel",
                            "pixel/" + sample.Id,
                            "LibreOffice headless produced no PDF for this sample."));
                    }
                }

                if (pdfPath is not null)
                {
                    string loPxDir = Path.Combine(workspace, "lo-px", safeKey);
                    RasterRun raster = rasterizer.Render(
                        pdfPath,
                        Path.Combine(loPxDir, "page"),
                        dpi,
                        maxPages,
                        TimeSpan.FromSeconds(180));

                    if (raster.TimedOut || raster.ExitCode != 0 || raster.Pages.Count == 0)
                    {
                        libreOfficeFailed = true;
                        results.Add(CheckResult.Skip(
                            "pixel",
                            "pixel/" + sample.Id,
                            "pdftoppm produced no pages for LibreOffice's PDF output."));
                    }
                    else
                    {
                        libreOfficeAnswered = true;
                        string bdPxDir = Path.Combine(workspace, "bd-px", safeKey);
                        Directory.CreateDirectory(bdPxDir);

                        ToolRun rendered = await tool
                            .RunAsync([.. RenderArguments(sample, bdPxDir, dpi, maxPages, fontDirectories, fontArguments)])
                            .ConfigureAwait(false);
                        if (rendered.ExitCode != 0)
                        {
                            broilerReadFailed = true;
                            broilerExitCode = rendered.ExitCode;
                            broilerError ??= FirstNonEmptyLine(rendered.StandardError) ?? ("render exited " + rendered.ExitCode.ToString(CultureInfo.InvariantCulture));
                            results.Add(CheckResult.Fail(
                                "pixel",
                                "pixel/" + sample.Id,
                                "broilerdoc render exited " + rendered.ExitCode.ToString(CultureInfo.InvariantCulture) + ": " + broilerError,
                                rendered.CommandLine()));
                        }
                        else
                        {
                            string[] ourPages = [.. Directory
                                .GetFiles(bdPxDir, "page-*.bmp")
                                .OrderBy(Ordinal, Comparer<int>.Default)];

                            int theirPages = raster.Pages.Count;
                            int pages = Math.Min(theirPages, ourPages.Length);
                            if (pages == 0)
                            {
                                broilerReadFailed = ourPages.Length == 0;
                                results.Add(CheckResult.Fail(
                                    "pixel",
                                    "pixel/" + sample.Id,
                                    "broilerdoc rendered 0 pages while LibreOffice rendered " +
                                    theirPages.ToString(CultureInfo.InvariantCulture) + ".",
                                    rendered.CommandLine()));
                            }
                            else
                            {
                                var measurements = new List<(int Page, RenderBands Bands, RenderObserved Observed, int Severity, double Composite)>(pages);
                                for (int page = 0; page < pages; page++)
                                {
                                    RawImage theirs = RawImage.Read(raster.Pages[page]);
                                    RawImage ours = RawImage.Read(ourPages[page]);

                                    InkBox theirBox = InkProfile.Box(theirs);
                                    InkBox ourBox = InkProfile.Box(ours);
                                    int boxDelta = InkProfile.BoxDelta(theirBox, ourBox);

                                    double similarity = InkProfile.CosineSimilarity(
                                        InkProfile.Smooth(InkProfile.RowProfile(theirs, BandHeight), ProfileSmoothingBands),
                                        InkProfile.Smooth(InkProfile.RowProfile(ours, BandHeight), ProfileSmoothingBands));

                                    double coverage = Math.Abs(InkProfile.Coverage(theirs) - InkProfile.Coverage(ours));
                                    double blur = InkProfile.BlurredDifferenceRatio(theirs, ours, BlurRadius, BlurDelta);

                                    var pageBands = new RenderBands(
                                        Bands.Geometry(Math.Abs(theirs.Width - ours.Width), Math.Abs(theirs.Height - ours.Height)),
                                        Bands.InkBox(boxDelta),
                                        Bands.InkProfile(similarity),
                                        Bands.Coverage(coverage),
                                        Bands.BlurDiff(blur));

                                    var pageObserved = new RenderObserved(boxDelta, similarity, coverage, blur);
                                    int severity = Bands.Rank(pageBands);
                                    double composite = (severity * 100.0) + (blur * 100.0) + ((1.0 - similarity) * 50.0) + (coverage * 50.0);
                                    measurements.Add((page + 1, pageBands, pageObserved, severity, composite));
                                }

                                var worst = measurements
                                    .OrderByDescending(m => m.Severity)
                                    .ThenByDescending(m => m.Composite)
                                    .First();

                                renderRow = new RenderRow(
                                    sample.Id,
                                    sample.Format,
                                    ourPages.Length,
                                    theirPages,
                                    worst.Page,
                                    worst.Bands,
                                    worst.Observed,
                                    BaselineState.SuspectedDefect,
                                    null,
                                    string.Empty,
                                    null,
                                    null);

                                bool clean = Bands.IsBest(renderRow.Bands) &&
                                             renderRow.BroilerdocPages == renderRow.LibreOfficePages;

                                if (clean)
                                {
                                    results.Add(CheckResult.Pass("pixel", "pixel/" + sample.Id, rendered.Duration));
                                }
                                else
                                {
                                    results.Add(CheckResult.Fail(
                                        "pixel",
                                        "pixel/" + sample.Id,
                                        Bands.Describe(renderRow.Bands) +
                                        " (worst page " + renderRow.WorstPage.ToString(CultureInfo.InvariantCulture) +
                                        ", blur diff " + renderRow.Observed.BlurDiffRatio.ToString("0.0000", CultureInfo.InvariantCulture) +
                                        ", profile similarity " + renderRow.Observed.InkProfileSimilarity.ToString("0.000", CultureInfo.InvariantCulture) +
                                        ", pages: broilerdoc=" + renderRow.BroilerdocPages.ToString(CultureInfo.InvariantCulture) +
                                        " vs libreoffice=" + renderRow.LibreOfficePages.ToString(CultureInfo.InvariantCulture) + ")",
                                        rendered.CommandLine()));

                                    // Generate a heat-map diff image for the worst page.
                                    string diffDir = Path.Combine(workspace, "failures", safeKey);
                                    Directory.CreateDirectory(diffDir);
                                    RasterRun singlePng = rasterizer.RenderPagePng(
                                        pdfPath,
                                        Path.Combine(diffDir, "libreoffice"),
                                        worst.Page,
                                        dpi,
                                        TimeSpan.FromSeconds(60));

                                    if (singlePng.Pages.Count > 0)
                                    {
                                        string candidateDiff = Path.Combine(
                                            diffDir,
                                            "diff-p" + worst.Page.ToString(CultureInfo.InvariantCulture) + ".png");
                                        await tool.RunAsync(
                                            "compare", singlePng.Pages[0], ourPages[worst.Page - 1],
                                            "--mode", "image",
                                            "--diff", candidateDiff,
                                            "--diff-style", "heat",
                                            "--allow-size-difference",
                                            "--quiet").ConfigureAwait(false);

                                        if (File.Exists(candidateDiff))
                                            diffImagePath = candidateDiff;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        bool refusedByBoth = broilerReadFailed && !libreOfficeAnswered && IsHandledExit(broilerExitCode);
        double score = refusedByBoth
            ? 0.0
            : ComputeSeverityScore(broilerReadFailed, broilerExitCode, libreOfficeFailed, reading, renderRow);
        string summary = BuildSummary(sample, broilerReadFailed, broilerExitCode, broilerError, reading, renderRow);
        var difference = new ExtrasDifference(
            sample,
            broilerReadFailed,
            broilerExitCode,
            broilerError,
            libreOfficeFailed,
            reading,
            renderRow,
            diffImagePath,
            score,
            summary,
            RefusedByBoth: refusedByBoth);

        return (results, reading, renderRow, difference);
    }

    /// <summary>
    /// Checks a crash-regression input for the one thing it can tell this suite:
    /// whether <c>broilerdoc</c> handles it.
    /// </summary>
    /// <remarks>
    /// LibreOffice is not run. Its reading of a fuzzed file is not a reference,
    /// and the upstream test for each of these exists because LibreOffice once
    /// crashed or hung on it - a minute of conversion buys nothing worth
    /// comparing. <c>broilerdoc</c> is asked what the axis would have asked it
    /// on a document, and each run has to end in a documented exit code. Refusing
    /// a damaged file is an answer; exit 70, a crash or a hang is not.
    /// </remarks>
    public static async Task<(IReadOnlyList<CheckResult> Results, ExtrasDifference Difference)> EvaluateCrashRegressionAsync(
        BroilerDocTool tool,
        ExtrasSample sample,
        string family,
        string workspace,
        string axis,
        int dpi,
        int maxPages,
        IReadOnlyList<string> fontDirectories,
        IReadOnlyList<string> fontArguments)
    {
        var commands = new List<IReadOnlyList<string>>();
        if (axis is "semantic" or "all")
        {
            commands.Add(["info", sample.LocalPath, .. FormatArguments(sample), "--json", "--quiet"]);
            commands.Add(["dump", sample.LocalPath, .. FormatArguments(sample), "--as", "text", "--quiet"]);
        }

        if (axis is "pixel" or "all")
        {
            string bdPxDir = Path.Combine(workspace, "bd-px", SanitizePath(sample.Id));
            Directory.CreateDirectory(bdPxDir);
            commands.Add(RenderArguments(sample, bdPxDir, dpi, maxPages, fontDirectories, fontArguments));
        }

        var results = new List<CheckResult>();
        ToolRun? unhandled = null;
        TimeSpan duration = TimeSpan.Zero;
        foreach (IReadOnlyList<string> arguments in commands)
        {
            ToolRun run = await tool.RunAsync([.. arguments]).ConfigureAwait(false);
            duration += run.Duration;
            if (!IsHandledExit(run.ExitCode))
            {
                unhandled = run;
                break;
            }
        }

        string? error = null;
        if (unhandled is null)
        {
            results.Add(CheckResult.Pass("robust", "robust/" + sample.Id, duration));
        }
        else
        {
            error = FirstNonEmptyLine(unhandled.StandardError) ?? "no message";
            results.Add(CheckResult.Fail(
                "robust",
                "robust/" + sample.Id,
                "broilerdoc " + unhandled.Arguments[0] + " " + DescribeExit(unhandled.ExitCode) + ": " + error +
                ". A crash-regression input (" + family + ") may be refused, but only with a documented exit code.",
                unhandled.CommandLine()));
        }

        string reason = "a crash-regression input (" + family + "): LibreOffice's reading of a fuzzed file is " +
                        "not a reference, so it was not compared.";
        if (axis is "semantic" or "all")
            results.Add(CheckResult.Skip("text", "text/" + sample.Id, reason));
        if (axis is "pixel" or "all")
            results.Add(CheckResult.Skip("pixel", "pixel/" + sample.Id, reason));

        bool failed = unhandled is not null;
        int exitCode = unhandled?.ExitCode ?? 0;
        var difference = new ExtrasDifference(
            sample,
            failed,
            exitCode,
            error,
            LibreOfficeFailed: false,
            Semantic: null,
            Render: null,
            DiffImagePath: null,
            ComputeSeverityScore(failed, exitCode, libreOfficeFailed: false, semantic: null, render: null),
            failed
                ? "broilerdoc " + unhandled!.Arguments[0] + " " + DescribeExit(exitCode) + " (" + error + ")"
                : "handled",
            family);

        return (results, difference);
    }

    /// <summary>
    /// Whether an exit code is one <c>docs/guides/cli.md</c> documents as an
    /// answer. Everything else is not: 70 is defined there as always a defect,
    /// a signal or abort is a crash, and -1 is a run the harness killed for
    /// hanging.
    /// </summary>
    public static bool IsHandledExit(int exitCode) => exitCode is >= 0 and <= 6;

    private static string DescribeExit(int exitCode) => exitCode == -1
        ? "did not exit and was killed"
        : "exited " + exitCode.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The <c>--from</c> a sample is read with: its format when LibreOffice is
    /// told its filter too, and nothing otherwise.
    /// </summary>
    /// <remarks>
    /// LibreOffice picks the filter for a DOCX, ODT, RTF or PDF from the bytes,
    /// not the name, and the corpus relies on that: <c>tdf132596.docx</c> and
    /// three more are ODT packages saved under <c>.docx</c>. Stating
    /// <c>--from docx</c> for them made <c>broilerdoc</c> refuse a document
    /// LibreOffice reads. Where the runner does name LibreOffice's filter -
    /// HTML, and flat ODF - it names <c>broilerdoc</c>'s format as well, so the
    /// two are always asked the same question.
    /// </remarks>
    private static string[] FormatArguments(ExtrasSample sample) =>
        LibreOfficeTool.InputFilterFor(sample.Extension) is null ? [] : ["--from", sample.Format];

    /// <summary>
    /// The <c>render</c> invocation both paths use, so a crash-regression input
    /// is rendered exactly as a document would have been.
    /// </summary>
    private static List<string> RenderArguments(
        ExtrasSample sample,
        string outputDirectory,
        int dpi,
        int maxPages,
        IReadOnlyList<string> fontDirectories,
        IReadOnlyList<string> fontArguments)
    {
        var arguments = new List<string> { "render", sample.LocalPath };
        arguments.AddRange(FormatArguments(sample));
        arguments.AddRange(
        [
            "--out", Path.Combine(outputDirectory, "page-{page}.bmp"),
            "--image-format", "bmp",
            "--dpi", dpi.ToString(CultureInfo.InvariantCulture),
            "--max-pages", maxPages.ToString(CultureInfo.InvariantCulture),
            "--background", "#FFFFFF",
            "--json", "--quiet",
        ]);

        foreach (string directory in fontDirectories)
        {
            arguments.Add("--font-dir");
            arguments.Add(directory);
        }

        arguments.AddRange(fontArguments);
        return arguments;
    }

    /// <summary>
    /// The crash-regression inputs <c>broilerdoc</c> did not handle, in sample
    /// order. They are reported in full rather than ranked: there should be
    /// none, and each one is a defect.
    /// </summary>
    public static IReadOnlyList<ExtrasDifference> CrashRegressionFailures(IEnumerable<ExtrasDifference> differences) =>
        [.. differences
            .Where(diff => diff.SetAside && diff.BroilerReadFailed)
            .OrderBy(diff => diff.Sample.Id, StringComparer.Ordinal)];

    /// <summary>
    /// Computes a deterministic composite severity score so that the top <c>x</c>
    /// major differences can be ordered from most severe to least severe.
    /// </summary>
    public static double ComputeSeverityScore(
        bool broilerReadFailed,
        int broilerExitCode,
        bool libreOfficeFailed,
        SemanticReading? semantic,
        RenderRow? render)
    {
        if (libreOfficeFailed && !broilerReadFailed && semantic is null && render is null)
            return 0.0;

        double score = 0.0;

        // Unhandled defect (exit 70) or read/render failure on a valid LibreOffice sample is highest severity.
        if (broilerReadFailed)
            score += broilerExitCode == 70 ? 2000.0 : 1200.0;

        if (render is not null)
        {
            int bandRank = Bands.Rank(render.Bands);
            score += bandRank * 200.0;

            int pageDelta = Math.Abs(render.BroilerdocPages - render.LibreOfficePages);
            score += Math.Min(pageDelta, 20) * 40.0;

            if (render.Bands.Geometry == "off")
                score += 150.0;

            score += Math.Clamp(render.Observed.BlurDiffRatio, 0.0, 1.0) * 300.0;
            score += Math.Clamp(1.0 - render.Observed.InkProfileSimilarity, 0.0, 1.0) * 200.0;
            score += Math.Clamp(render.Observed.CoverageDelta, 0.0, 1.0) * 150.0;

            int cappedBox = render.Observed.InkBoxDeltaPx == int.MaxValue
                ? 500
                : Math.Min(render.Observed.InkBoxDeltaPx, 500);
            score += cappedBox * 0.2;
        }

        if (semantic is not null)
        {
            score += Math.Min(semantic.Differences.Count, 40) * 15.0;
            score += Math.Min(semantic.Diagnostics.Count, 10) * 5.0;
        }

        return Math.Round(score, 4);
    }

    /// <summary>
    /// Ranks the evaluated documents and returns the top <paramref name="topCount"/>
    /// major differences. Crash-regression inputs are not ranked: their failures,
    /// if any, are reported by <see cref="CrashRegressionFailures"/> instead.
    /// </summary>
    public static IReadOnlyList<ExtrasDifference> TopDifferences(
        IEnumerable<ExtrasDifference> differences,
        int topCount = DefaultTopDifferences)
    {
        int limit = Math.Max(1, topCount);
        return differences
            .Where(diff => !diff.SetAside && diff.HasDifference)
            .OrderByDescending(diff => diff.SeverityScore)
            .ThenBy(diff => diff.Sample.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    /// <summary>
    /// Formats a consolidated GitHub issue Markdown report describing the top
    /// <paramref name="topCount"/> major differences between LibreOffice headless
    /// and <c>broilerdoc</c>.
    /// </summary>
    public static string FormatIssueMarkdown(
        string extrasSource,
        RunHeader header,
        IReadOnlyList<ExtrasSample> allSamples,
        IReadOnlyList<ExtrasDifference> allDifferences,
        int topCount = DefaultTopDifferences)
    {
        IReadOnlyList<ExtrasDifference> top = TopDifferences(allDifferences, topCount);
        ExtrasDifference[] documents = [.. allDifferences.Where(diff => !diff.SetAside)];
        ExtrasDifference[] setAside = [.. allDifferences.Where(diff => diff.SetAside)];
        IReadOnlyList<ExtrasDifference> unhandled = CrashRegressionFailures(setAside);
        int totalWithDifferences = documents.Count(diff => diff.HasDifference);
        ExtrasDifference[] refusedByBoth = [.. documents
            .Where(diff => diff.RefusedByBoth)
            .OrderBy(diff => diff.Sample.Id, StringComparer.Ordinal)];

        var sb = new StringBuilder();
        sb.AppendLine("## Office Conformance Major Differences (`sw/qa/extras`)");
        sb.AppendLine();
        sb.AppendLine("Automated comparison of sample documents from [" + extrasSource + "](" + extrasSource + ") rendered with **LibreOffice headless** and **`broiler.cli` (`broilerdoc`)**.");
        sb.AppendLine();
        sb.AppendLine("### Run Summary");
        sb.AppendLine();
        sb.AppendLine("| Property | Value |");
        sb.AppendLine("| :--- | :--- |");
        sb.AppendLine("| **Corpus Source** | `" + extrasSource + "` |");
        sb.AppendLine("| **Supported Samples Evaluated** | " + allSamples.Count.ToString(CultureInfo.InvariantCulture) + " |");
        sb.AppendLine("| **Documents Compared With LibreOffice** | " + documents.Length.ToString(CultureInfo.InvariantCulture) + " |");
        sb.AppendLine("| **Crash-Regression Inputs Set Aside** | " + setAside.Length.ToString(CultureInfo.InvariantCulture) +
                      (unhandled.Count > 0
                          ? " (**" + unhandled.Count.ToString(CultureInfo.InvariantCulture) + " not handled**)"
                          : string.Empty) + " |");
        sb.AppendLine("| **Documents With Differences** | " + totalWithDifferences.ToString(CultureInfo.InvariantCulture) + " |");
        sb.AppendLine("| **Refused By Both** | " + refusedByBoth.Length.ToString(CultureInfo.InvariantCulture) + " |");
        sb.AppendLine("| **Top Major Differences Reported** | " + top.Count.ToString(CultureInfo.InvariantCulture) + " (requested top " + topCount.ToString(CultureInfo.InvariantCulture) + ") |");
        sb.AppendLine("| **LibreOffice** | `" + (header.LibreOffice ?? "none") + "` |");
        sb.AppendLine("| **Rasteriser** | `" + (header.Rasterizer ?? "none") + "` |");
        sb.AppendLine("| **Platform / DPI** | `" + header.Platform + "` @ `" + header.Dpi.ToString(CultureInfo.InvariantCulture) + "` dpi |");
        sb.AppendLine();

        // Breakdown by format
        sb.AppendLine("### Format Breakdown");
        sb.AppendLine();
        sb.AppendLine("| Format | Documents | With Differences | Set Aside |");
        sb.AppendLine("| :--- | ---: | ---: | ---: |");
        foreach (IGrouping<string, ExtrasDifference> group in allDifferences
                     .GroupBy(d => d.Sample.Format, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            int documentCount = group.Count(d => !d.SetAside);
            int diffCount = group.Count(d => !d.SetAside && d.HasDifference);
            int setAsideCount = group.Count(d => d.SetAside);
            sb.AppendLine(
                "| `" + group.Key + "` | " +
                documentCount.ToString(CultureInfo.InvariantCulture) + " | " +
                diffCount.ToString(CultureInfo.InvariantCulture) + " | " +
                setAsideCount.ToString(CultureInfo.InvariantCulture) + " |");
        }

        sb.AppendLine();

        if (setAside.Length > 0)
            AppendCrashRegressions(sb, setAside, unhandled);

        if (refusedByBoth.Length > 0)
            AppendRefusedByBoth(sb, refusedByBoth);

        if (top.Count == 0)
        {
            sb.AppendLine("No major differences were detected across the evaluated samples.");
            return sb.ToString().ReplaceLineEndings("\n");
        }

        sb.AppendLine("### Top " + top.Count.ToString(CultureInfo.InvariantCulture) + " Major Differences");
        sb.AppendLine();

        for (int i = 0; i < top.Count; i++)
        {
            ExtrasDifference diff = top[i];
            int rank = i + 1;
            string sampleLink = diff.Sample.SourceUrl is not null
                ? "[`" + diff.Sample.Id + "`](" + diff.Sample.SourceUrl + ")"
                : "`" + diff.Sample.Id + "`";

            sb.AppendLine("#### " + rank.ToString(CultureInfo.InvariantCulture) + ". " + sampleLink +
                          " (`" + diff.Sample.Extension + "`, severity `" +
                          diff.SeverityScore.ToString("0.00", CultureInfo.InvariantCulture) + "`)");
            sb.AppendLine();
            sb.AppendLine("- **Summary**: " + diff.Summary);

            if (diff.BroilerReadFailed)
            {
                sb.AppendLine("- **CLI Status**: Failed with exit code `" +
                              diff.BroilerExitCode.ToString(CultureInfo.InvariantCulture) + "`" +
                              (string.IsNullOrWhiteSpace(diff.BroilerError) ? string.Empty : " — `" + diff.BroilerError + "`"));
            }

            if (diff.Render is { } render)
            {
                sb.AppendLine("- **Pagination**: `broilerdoc` rendered **" +
                              render.BroilerdocPages.ToString(CultureInfo.InvariantCulture) +
                              "** page(s) vs LibreOffice **" +
                              render.LibreOfficePages.ToString(CultureInfo.InvariantCulture) +
                              "** page(s) (worst page: `" +
                              render.WorstPage.ToString(CultureInfo.InvariantCulture) + "`)");
                sb.AppendLine("- **Visual Bands**: `" + Bands.Describe(render.Bands) + "`");
                sb.AppendLine("- **Visual Metrics**: blur diff `" +
                              (render.Observed.BlurDiffRatio * 100.0).ToString("0.00", CultureInfo.InvariantCulture) +
                              "%`, ink profile similarity `" +
                              render.Observed.InkProfileSimilarity.ToString("0.000", CultureInfo.InvariantCulture) +
                              "`, coverage delta `" +
                              (render.Observed.CoverageDelta * 100.0).ToString("0.00", CultureInfo.InvariantCulture) +
                              "%`, ink box delta `" +
                              (render.Observed.InkBoxDeltaPx == int.MaxValue
                                  ? "blank-vs-inked"
                                  : render.Observed.InkBoxDeltaPx.ToString(CultureInfo.InvariantCulture) + " px") +
                              "`");
            }

            if (diff.Semantic is { } semantic && semantic.Differences.Count > 0)
            {
                sb.AppendLine("- **Semantic Text Differences** (" +
                              semantic.Differences.Count.ToString(CultureInfo.InvariantCulture) + " line(s)):");
                sb.AppendLine();
                sb.AppendLine("```diff");
                foreach (string line in semantic.Differences.Take(10))
                    sb.AppendLine(line);
                if (semantic.Differences.Count > 10)
                {
                    sb.AppendLine("... and " +
                                  (semantic.Differences.Count - 10).ToString(CultureInfo.InvariantCulture) +
                                  " more line(s)");
                }
                sb.AppendLine("```");
            }

            if (diff.Semantic is { Diagnostics.Count: > 0 } diagReading)
            {
                sb.AppendLine("- **Diagnostics**: `" + string.Join("`, `", diagReading.Diagnostics) + "`");
            }

            sb.AppendLine();
        }

        return sb.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>
    /// Says what was set aside and why, and lists every input <c>broilerdoc</c>
    /// did not handle. Placed above the ranking because a crash is a defect on
    /// any input, and the ranking is about something else.
    /// </summary>
    private static void AppendRefusedByBoth(StringBuilder sb, IReadOnlyList<ExtrasDifference> refused)
    {
        sb.AppendLine("### Refused By Both");
        sb.AppendLine();
        sb.AppendLine(
            refused.Count.ToString(CultureInfo.InvariantCulture) + " document(s) produced neither text nor pages in " +
            "LibreOffice and were refused by `broilerdoc` with a documented exit code - an encrypted package with " +
            "no password given, or a part that is not well-formed. They are not ranked.");
        sb.AppendLine();
        foreach (ExtrasDifference diff in refused)
        {
            string sampleLink = diff.Sample.SourceUrl is not null
                ? "[`" + diff.Sample.Id + "`](" + diff.Sample.SourceUrl + ")"
                : "`" + diff.Sample.Id + "`";
            sb.AppendLine("- " + sampleLink + ": " + (diff.BroilerError ?? diff.Summary));
        }

        sb.AppendLine();
    }

    private static void AppendCrashRegressions(
        StringBuilder sb,
        IReadOnlyList<ExtrasDifference> setAside,
        IReadOnlyList<ExtrasDifference> unhandled)
    {
        string families = string.Join(", ", setAside
            .GroupBy(diff => diff.CrashRegressionFamily!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => "`" + group.Key + "` " + group.Count().ToString(CultureInfo.InvariantCulture)));

        sb.AppendLine("### Crash-Regression Inputs");
        sb.AppendLine();
        sb.AppendLine(
            setAside.Count.ToString(CultureInfo.InvariantCulture) + " sample(s) are fuzzer output that LibreOffice " +
            "keeps as crash regressions (" + families + "; families from " +
            "`tests/office/extras-crash-regressions.json`). They are not compared with LibreOffice, whose " +
            "reading of a fuzzed file is not a reference, and they are not ranked. `broilerdoc` still has to " +
            "handle each one with a documented exit code.");
        sb.AppendLine();

        if (unhandled.Count == 0)
        {
            sb.AppendLine("All " + setAside.Count.ToString(CultureInfo.InvariantCulture) + " were handled.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("**" + unhandled.Count.ToString(CultureInfo.InvariantCulture) + " were not handled:**");
        sb.AppendLine();
        foreach (ExtrasDifference diff in unhandled)
        {
            string sampleLink = diff.Sample.SourceUrl is not null
                ? "[`" + diff.Sample.Id + "`](" + diff.Sample.SourceUrl + ")"
                : "`" + diff.Sample.Id + "`";
            sb.AppendLine("- " + sampleLink + " (`" + diff.CrashRegressionFamily + "`): " + diff.Summary);
        }

        sb.AppendLine();
    }

    /// <summary>
    /// Creates or updates a consolidated GitHub issue with the top <paramref name="topCount"/>
    /// major differences. Uses the GitHub REST API when <c>GITHUB_TOKEN</c> or
    /// <c>GH_TOKEN</c> is set, falling back to the <c>gh</c> CLI if available.
    /// </summary>
    public static async Task<string?> CreateOrUpdateGitHubIssueAsync(
        string? explicitRepo,
        string issueTitle,
        string issueBody,
        int topCount)
    {
        _ = topCount;
        string? repo = explicitRepo;
        if (string.IsNullOrWhiteSpace(repo))
            repo = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");

        string? token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
            token = Environment.GetEnvironmentVariable("GH_TOKEN");

        if (!string.IsNullOrWhiteSpace(repo) && !string.IsNullOrWhiteSpace(token))
        {
            using HttpClient client = CreateHttpClient(token);

            // Check whether an open issue with the same title already exists so recurring CI runs update it rather than spamming duplicates.
            string listUrl = "https://api.github.com/repos/" + repo + "/issues?state=open&per_page=100";
            using HttpResponseMessage listResp = await client.GetAsync(listUrl).ConfigureAwait(false);
            int? existingNumber = null;

            if (listResp.IsSuccessStatusCode)
            {
                string listJson = await listResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using JsonDocument doc = JsonDocument.Parse(listJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement issue in doc.RootElement.EnumerateArray())
                    {
                        string? title = issue.TryGetProperty("title", out JsonElement t) ? t.GetString() : null;
                        if (string.Equals(title, issueTitle, StringComparison.Ordinal) &&
                            issue.TryGetProperty("number", out JsonElement num) &&
                            num.ValueKind == JsonValueKind.Number)
                        {
                            existingNumber = num.GetInt32();
                            break;
                        }
                    }
                }
            }

            var payload = new JsonObject
            {
                ["title"] = issueTitle,
                ["body"] = issueBody,
            };

            string endpoint = existingNumber is { } number
                ? "https://api.github.com/repos/" + repo + "/issues/" + number.ToString(CultureInfo.InvariantCulture)
                : "https://api.github.com/repos/" + repo + "/issues";

            HttpMethod method = existingNumber.HasValue ? HttpMethod.Patch : HttpMethod.Post;
            using var request = new HttpRequestMessage(method, endpoint)
            {
                Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
            };

            using HttpResponseMessage issueResp = await client.SendAsync(request).ConfigureAwait(false);
            string respBody = await issueResp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!issueResp.IsSuccessStatusCode)
            {
                throw new IOException(
                    "GitHub API returned " + ((int)issueResp.StatusCode).ToString(CultureInfo.InvariantCulture) +
                    " when creating/updating issue in " + repo + ": " + respBody);
            }

            using JsonDocument created = JsonDocument.Parse(respBody);
            return created.RootElement.TryGetProperty("html_url", out JsonElement htmlUrl)
                ? htmlUrl.GetString()
                : endpoint;
        }

        // Fallback to `gh issue create` CLI if available.
        return TryCreateIssueWithGhCli(repo, issueTitle, issueBody);
    }

    private static string? TryCreateIssueWithGhCli(string? repo, string title, string body)
    {
        string bodyFile = Path.Combine(Path.GetTempPath(), "broilerdoc-issue-" + Guid.NewGuid().ToString("N") + ".md");
        try
        {
            File.WriteAllText(bodyFile, body, new UTF8Encoding(false));
            var start = new ProcessStartInfo
            {
                FileName = "gh",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("issue");
            start.ArgumentList.Add("create");
            start.ArgumentList.Add("--title");
            start.ArgumentList.Add(title);
            start.ArgumentList.Add("--body-file");
            start.ArgumentList.Add(bodyFile);
            if (!string.IsNullOrWhiteSpace(repo))
            {
                start.ArgumentList.Add("--repo");
                start.ArgumentList.Add(repo);
            }

            using var process = Process.Start(start);
            if (process is null)
                return null;

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(30_000);

            if (process.ExitCode != 0)
            {
                throw new IOException("gh issue create exited " + process.ExitCode.ToString(CultureInfo.InvariantCulture) + ": " + stderr.Trim());
            }

            return FirstNonEmptyLine(stdout);
        }
        finally
        {
            try
            {
                if (File.Exists(bodyFile))
                    File.Delete(bodyFile);
            }
            catch (IOException)
            {
            }
        }
    }

    private static HttpClient CreateHttpClient(string? token = null)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BroilerDocumentsOfficeConformance", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        string? auth = token
                       ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                       ?? Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(auth))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth);

        return client;
    }

    private static string BuildSummary(
        ExtrasSample sample,
        bool broilerReadFailed,
        int broilerExitCode,
        string? broilerError,
        SemanticReading? reading,
        RenderRow? render)
    {
        var parts = new List<string>();
        if (broilerReadFailed)
        {
            parts.Add("broilerdoc exited " + broilerExitCode.ToString(CultureInfo.InvariantCulture) +
                      (string.IsNullOrWhiteSpace(broilerError) ? string.Empty : " (" + broilerError + ")"));
        }

        if (render is not null)
        {
            if (render.BroilerdocPages != render.LibreOfficePages)
            {
                parts.Add("page count mismatch (" +
                          render.BroilerdocPages.ToString(CultureInfo.InvariantCulture) + " vs " +
                          render.LibreOfficePages.ToString(CultureInfo.InvariantCulture) + ")");
            }

            if (!Bands.IsBest(render.Bands))
            {
                parts.Add(Bands.Describe(render.Bands) +
                          " (blur diff " + (render.Observed.BlurDiffRatio * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "%)");
            }
        }

        if (reading is not null && reading.Differences.Count > 0)
        {
            parts.Add(reading.Differences.Count.ToString(CultureInfo.InvariantCulture) + " text difference(s)");
        }

        return parts.Count == 0
            ? "clean (" + sample.Format + ")"
            : string.Join("; ", parts);
    }

    private static IReadOnlyList<string> ExtractDiagnostics(JsonElement? json)
    {
        if (json is not { } element || !element.TryGetProperty("diagnostics", out JsonElement array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var codes = new SortedSet<string>(StringComparer.Ordinal);
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

        return codes.ToArray();
    }

    private static string SanitizePath(string id)
    {
        var sb = new StringBuilder(id.Length);
        foreach (char c in id)
        {
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        }

        return sb.ToString();
    }

    private static int Ordinal(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        int dash = name.LastIndexOf('-');
        return dash >= 0 && int.TryParse(
            name[(dash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : 0;
    }

    private static string? FirstNonEmptyLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length > 0)
                return trimmed;
        }

        return null;
    }
}
