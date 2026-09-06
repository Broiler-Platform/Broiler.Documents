using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Documents.Corpus;

internal sealed record ExternalSource(
    string Id,
    string Url,
    CorpusFormat Format,
    string Licence,
    string Decision,
    string? Sha256,
    long SizeBytes);

/// <summary>
/// The gate a document written outside this repository has to pass to reach the
/// corpus.
/// </summary>
/// <remarks>
/// <para>
/// It ships closed, and the register it reads has no rows. That is the honest
/// answer to "fetch or create": this component's registers reject third-party
/// document artifacts by default, per artifact, and downloading one at test time
/// is the same act as committing one with the evidence deleted. So the corpus is
/// authored, and this exists so that the day somebody decides otherwise, the
/// decision is a row with a reviewer and a digest rather than a URL somebody
/// added to a script.
/// </para>
/// <para>
/// It is written rather than left as prose deliberately. A register describing an
/// enforcement path nothing implements is the failure mode the PDF test-control
/// guards exist to catch elsewhere in this repository, and it would be a poor
/// joke to reproduce it in the file that cites them.
/// </para>
/// </remarks>
internal static class ExternalSources
{
    public const string RelativePath = "tests/corpus/external-sources.json";
    private const string Group = "external";

    public static IReadOnlyList<ExternalSource> Load(string repositoryRoot)
    {
        string path = Path.Combine(repositoryRoot, RelativePath.Replace('/', Path.DirectorySeparatorChar));
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));

        return document.RootElement.GetProperty("sources").EnumerateArray().Select(source => new ExternalSource(
            source.GetProperty("id").GetString() ?? "?",
            source.GetProperty("url").GetString() ?? string.Empty,
            CorpusFormat.ByKey(source.GetProperty("format").GetString() ?? "rtf"),
            source.GetProperty("licence").GetString() ?? string.Empty,
            source.GetProperty("decision").GetString() ?? "pending",
            source.TryGetProperty("sha256", out JsonElement digest) ? digest.GetString() : null,
            source.TryGetProperty("sizeBytes", out JsonElement size) && size.ValueKind == JsonValueKind.Number
                ? size.GetInt64()
                : 0)).ToArray();
    }

    /// <summary>
    /// Fetches every approved row into the workspace and returns the documents
    /// that arrived, alongside a check per row.
    /// </summary>
    /// <remarks>
    /// A row that is not approved produces a skip naming its state, never a
    /// silent omission: the point of a register is that a reader can see what it
    /// declined and why, and a suite that quietly ignored a pending row would
    /// make the register decorative.
    /// </remarks>
    public static async Task<(IReadOnlyList<CorpusDocument> Documents, IReadOnlyList<CheckResult> Results)>
        FetchAsync(string repositoryRoot, string directory)
    {
        var documents = new List<CorpusDocument>();
        var results = new List<CheckResult>();
        IReadOnlyList<ExternalSource> sources = Load(repositoryRoot);

        if (sources.Count == 0)
        {
            results.Add(CheckResult.Skip(Group, "external/register",
                "--fetch was given and " + RelativePath + " holds no rows, so nothing was fetched. " +
                "That is the shipped state: this component's format registers reject third-party " +
                "document artifacts by default, per artifact."));
            return (documents, results);
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        foreach (ExternalSource source in sources)
        {
            string name = "external/" + source.Id;

            if (source.Decision != "approved")
            {
                results.Add(CheckResult.Skip(Group, name,
                    "the row is " + source.Decision + ", so it was not fetched. Only an approved row is."));
                continue;
            }

            if (source.Sha256 is null)
            {
                // Refused rather than fetched. An approved row with no digest
                // makes a green run evidence about whatever the server served
                // that morning, which is not what the approval was for.
                results.Add(CheckResult.Fail(Group, name,
                    "the row is approved but pins no sha256. An unpinned fetch is not reproducible, " +
                    "and the register's own policy requires the digest."));
                continue;
            }

            byte[] bytes;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                bytes = await client.GetByteArrayAsync(source.Url, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                results.Add(CheckResult.Fail(Group, name, "could not be fetched: " + exception.Message));
                continue;
            }

            string actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (!string.Equals(actual, source.Sha256, StringComparison.Ordinal))
            {
                results.Add(CheckResult.Fail(Group, name,
                    "the bytes fetched are not the bytes approved.\n" +
                    "      approved " + source.Sha256 + "\n" +
                    "      received " + actual + " (" +
                    bytes.Length.ToString(CultureInfo.InvariantCulture) + " bytes)"));
                continue;
            }

            string path = Path.Combine(directory, "external-" + source.Id + source.Format.Extension);
            await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
            documents.Add(new CorpusDocument(source.Id, source.Format, path, Sample: null));
            results.Add(CheckResult.Pass(Group, name));
        }

        return (documents, results);
    }
}
