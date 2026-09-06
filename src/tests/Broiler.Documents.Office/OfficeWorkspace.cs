using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Documents.Corpus;

namespace Broiler.Documents.Office;

/// <summary>One document LibreOffice manufactured, and how it got there.</summary>
/// <param name="Seed">The seed it came from.</param>
/// <param name="Via">The format LibreOffice wrote, which is the axis a failure is reported along.</param>
/// <param name="Path">Where it landed in the workspace.</param>
/// <param name="Run">The conversion that produced it, kept so a failure can name the command.</param>
internal sealed record OfficeDocument(OfficeSeed Seed, string Via, string Path, LibreOfficeRun Run)
{
    /// <summary>The key a baseline row is found by.</summary>
    public string Key => Seed.Id + "/" + Via;
}

/// <summary>
/// Where the documents under test come from.
/// </summary>
/// <remarks>
/// <para>
/// The trick this whole suite turns on is here. Nothing under test is committed
/// and nothing is downloaded: a seed is a fragment of authored HTML held as a
/// string in <c>tests/office/office-corpus.json</c>, and LibreOffice is what
/// turns it into a <c>.docx</c>, an <c>.odt</c> or an <c>.rtf</c>. So the file
/// broilerdoc is asked to read is markup no Broiler writer produced, which is
/// exactly the gap <c>docs/corpus-suite.md</c> says the existing corpus cannot
/// close - a corpus written by this component's own writers "can only contain
/// constructs those writers emit".
/// </para>
/// <para>
/// The documents live for one run in a temporary directory and are never near
/// the repository.
/// <c>FormatClaimGuardTests.No_Document_Of_A_Supported_Format_Is_Committed</c>
/// walks the tree for exactly the extensions written here, and it would be
/// right to fail on one.
/// </para>
/// <para>
/// Conversions are not batched, and that is a decision rather than an omission.
/// Ten documents in one <c>soffice</c> invocation cost about a fifth of ten
/// invocations, but one unreadable document takes the whole batch with it and
/// the per-document exit codes are lost. Correct attribution of a failure to a
/// document is worth more than the wall clock on a nightly job. The place to
/// revisit it is a corpus several times this size.
/// </para>
/// </remarks>
internal sealed class OfficeWorkspace
{
    private readonly LibreOfficeTool _libreOffice;
    private readonly OfficeManifest _manifest;
    private readonly TimeSpan _timeout;
    private readonly List<OfficeDocument> _documents = [];
    private readonly object _gate = new();
    private int _profileSlot = -1;

    public OfficeWorkspace(
        LibreOfficeTool libreOffice, OfficeManifest manifest, string directory, TimeSpan timeout)
    {
        _libreOffice = libreOffice;
        _manifest = manifest;
        _timeout = timeout;
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
    }

    /// <summary>Where the run materialised. Kept when <c>--keep</c> is given.</summary>
    public string Directory { get; }

    public IReadOnlyList<OfficeDocument> Documents
    {
        get
        {
            lock (_gate)
                return _documents.ToArray();
        }
    }

    /// <summary>
    /// A profile directory nobody else is using.
    /// </summary>
    /// <remarks>
    /// One per concurrent job, and the reason is measured rather than
    /// defensive. Three jobs sharing a profile lose exactly one per round, which
    /// one varies, and the loser exits 1 with completely empty stdout and
    /// stderr - a signature indistinguishable, from the outside, from a codec
    /// that produced nothing. <c>--nolockcheck</c> does not help. An interlocked
    /// counter is enough here because a slot is never returned: a directory
    /// costs about half a megabyte and the run discards the workspace anyway.
    /// </remarks>
    private string NextProfile()
    {
        int slot = Interlocked.Increment(ref _profileSlot);
        return Path.Combine(Directory, "profile", slot.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Writes every seed, then asks LibreOffice for one document per target
    /// format.
    /// </summary>
    /// <remarks>
    /// The seed is written as bytes rather than with <c>WriteAllText</c> on
    /// purpose. A Windows leg that turned the manifest's
    /// newlines into CRLF would be feeding LibreOffice a different document from
    /// the one the Linux leg fed it, and the two legs would disagree about a
    /// document neither of them got wrong.
    /// </remarks>
    public async Task<IReadOnlyList<CheckResult>> MaterialiseAsync(int jobs, string? only)
    {
        var results = new List<CheckResult>();
        string seedDirectory = Path.Combine(Directory, "seed");
        System.IO.Directory.CreateDirectory(seedDirectory);

        foreach (OfficeSeed seed in _manifest.Seeds)
        {
            string path = Path.Combine(seedDirectory, seed.Id + ".html");
            File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(seed.Html));
        }

        // The filter is applied here rather than only to the checks, because
        // producing a document costs a LibreOffice invocation and --only exists
        // to make a single case fast to iterate on. Filtering afterwards would
        // have left the slowest part of the run doing the whole corpus.
        var work = new List<(OfficeSeed Seed, string Via)>();
        foreach (OfficeSeed seed in _manifest.Seeds)
        {
            foreach (string via in _manifest.Targets)
            {
                if (only is null || (seed.Id + "/" + via).Contains(only, StringComparison.Ordinal))
                    work.Add((seed, via));
            }
        }

        await Parallelism.ForEachAsync(work, jobs, item =>
        {
            (OfficeSeed seed, string via) = item;
            string source = Path.Combine(seedDirectory, seed.Id + ".html");
            string target = Path.Combine(Directory, "docs", seed.Id, via);

            LibreOfficeRun run = _libreOffice.Convert(
                source,
                LibreOfficeFilters.For(via),
                target,
                NextProfile(),
                _timeout,
                LibreOfficeTool.InputFilterFor(".html"));

            CheckResult result = Validate("produce", "produce/" + seed.Id + "/" + via, run,
                LibreOfficeFilters.NameOf(via), MinimumSize(via));

            lock (_gate)
            {
                results.Add(result);
                if (result.Outcome == CheckOutcome.Passed && run.OutputPath is not null)
                    _documents.Add(new OfficeDocument(seed, via, run.OutputPath, run));
            }

            return Task.CompletedTask;
        }).ConfigureAwait(false);

        return results;
    }

    /// <summary>
    /// Asks LibreOffice for its own plain-text reading of a document it wrote.
    /// </summary>
    /// <remarks>
    /// This is the oracle the whole semantic axis rests on, and it is worth
    /// being clear about why it is trustworthy. It asks LibreOffice to read back
    /// a file LibreOffice itself produced moments earlier, so it is the
    /// strongest available statement of what that file is supposed to contain.
    /// A disagreement with broilerdoc's own text projection means one of the two
    /// readers dropped, duplicated, reordered or invented content.
    /// </remarks>
    public LibreOfficeRun ExtractText(OfficeDocument document)
    {
        string target = Path.Combine(Directory, "text", document.Seed.Id, document.Via);
        return _libreOffice.Convert(
            document.Path,
            LibreOfficeFilters.Text,
            target,
            NextProfile(),
            _timeout,
            LibreOfficeTool.InputFilterFor(Path.GetExtension(document.Path)));
    }

    /// <summary>LibreOffice's PDF of a document, which is the pixel axis's left-hand side.</summary>
    public LibreOfficeRun ExportPdf(OfficeDocument document)
    {
        string target = Path.Combine(Directory, "pdf", document.Seed.Id, document.Via);
        return _libreOffice.Convert(
            document.Path,
            LibreOfficeFilters.Pdf,
            target,
            NextProfile(),
            _timeout,
            LibreOfficeTool.InputFilterFor(Path.GetExtension(document.Path)));
    }

    /// <summary>
    /// LibreOffice's plain-text reading of a document <em>broilerdoc</em> wrote.
    /// </summary>
    /// <remarks>
    /// The reverse direction, and the one no other suite here performs. It makes
    /// LibreOffice a reader-oracle for Broiler's writers: if our export of a
    /// document says something different to LibreOffice than the document we
    /// were given, one of the two is wrong and the difference is worth a name.
    /// </remarks>
    public LibreOfficeRun ExtractTextOfOurs(OfficeDocument document, string ourPath)
    {
        string target = Path.Combine(Directory, "interop-text", document.Seed.Id, document.Via);
        return _libreOffice.Convert(
            ourPath,
            LibreOfficeFilters.Text,
            target,
            NextProfile(),
            _timeout,
            LibreOfficeTool.InputFilterFor(Path.GetExtension(ourPath)));
    }

    /// <summary>A directory inside the workspace for one purpose, created on demand.</summary>
    public string Area(string name, params string[] parts)
    {
        string[] all = new string[parts.Length + 2];
        all[0] = Directory;
        all[1] = name;
        Array.Copy(parts, 0, all, 2, parts.Length);
        string path = Path.Combine(all);
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// The three-part validation every LibreOffice invocation is held to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// None of the three is sufficient alone, and the third is the one that
    /// matters. Given bytes it cannot parse, LibreOffice does not fail: it falls
    /// back to the plain-text importer, converts happily, exits 0 and leaves a
    /// plausible file behind. For a suite whose headline value is catching
    /// content that went missing quietly, being taken in by exactly that failure
    /// mode would be a poor joke. So the filter LibreOffice actually chose is
    /// parsed out of its own stdout and compared with the one that was asked
    /// for.
    /// </para>
    /// <para>
    /// A run that failed with nothing on either stream is reported as suspected
    /// profile contention rather than as a bad document. That signature was
    /// measured, and blaming a codec for a symptom known to belong to the
    /// harness is how a suite loses its reader.
    /// </para>
    /// </remarks>
    public static CheckResult Validate(
        string group, string name, LibreOfficeRun run, string expectedFilter, long minimumSize)
    {
        if (run.TimedOut)
        {
            return CheckResult.Fail(group, name,
                "LibreOffice did not finish inside the timeout and was killed with its process tree.",
                run.CommandLine());
        }

        if (run.LooksLikeProfileContention)
        {
            return CheckResult.Fail(group, name,
                "LibreOffice exited " + run.ExitCode.ToString(CultureInfo.InvariantCulture) +
                " with nothing on stdout and nothing on stderr. That is the measured signature of " +
                "profile contention rather than of a document it could not handle.",
                run.CommandLine());
        }

        if (run.ExitCode != 0)
        {
            return CheckResult.Fail(group, name,
                "LibreOffice exited " + run.ExitCode.ToString(CultureInfo.InvariantCulture) + "." +
                Tail(run.StandardError),
                run.CommandLine());
        }

        if (run.OutputPath is null || !File.Exists(run.OutputPath))
        {
            return CheckResult.Fail(group, name,
                "LibreOffice exited 0 and wrote no output file. Converting from HTML to a Writer " +
                "format does exactly this when no --infilter is given.",
                run.CommandLine());
        }

        var information = new FileInfo(run.OutputPath);
        if (information.Length < minimumSize)
        {
            return CheckResult.Fail(group, name,
                "the output is " + information.Length.ToString(CultureInfo.InvariantCulture) +
                " bytes, below the " + minimumSize.ToString(CultureInfo.InvariantCulture) +
                " byte floor for this format.",
                run.CommandLine());
        }

        if (run.FilterUsed is null)
        {
            return CheckResult.Fail(group, name,
                "LibreOffice did not report which filter it used, so there is no evidence it used " +
                "the one it was asked for.",
                run.CommandLine());
        }

        if (!string.Equals(run.FilterUsed, expectedFilter, StringComparison.Ordinal))
        {
            return CheckResult.Fail(group, name,
                "LibreOffice used the filter '" + run.FilterUsed + "' where '" + expectedFilter +
                "' was asked for. It falls back to the plain-text importer on input it cannot parse, " +
                "and exits 0 when it does.",
                run.CommandLine());
        }

        return CheckResult.Pass(group, name, run.Duration);
    }

    /// <summary>
    /// A floor per format, below which the file cannot be carrying a document.
    /// </summary>
    /// <remarks>
    /// Deliberately crude. Its job is to catch an empty or stub output, not to
    /// assert anything about content - the checks that follow do that, and they
    /// do it through the model rather than through a byte count.
    /// </remarks>
    private static long MinimumSize(string via) => via switch
    {
        "docx" or "odt" => 1024,
        _ => 64,
    };

    private static string Tail(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
            return string.Empty;

        text = text.ReplaceLineEndings("\n      ");
        return "\n      stderr: " + (text.Length <= 600 ? text : text[..600] + " ...");
    }
}

/// <summary>
/// The filter strings, spelled out.
/// </summary>
/// <remarks>
/// Every one of these is spelled rather than left to LibreOffice's short alias,
/// and the reason is that the aliases move. A bare <c>docx</c> resolves to
/// <c>Office Open XML Text</c> on a current build and resolved to
/// <c>MS Word 2007 XML</c> on older ones. Those are different writers. A
/// baseline generated under one meaning and read under the other would be
/// silently wrong, which is the worst kind of wrong a control file can be.
/// </remarks>
internal static class LibreOfficeFilters
{
    public const string Text = "txt:Text (encoded):UTF8";
    public const string Pdf = "pdf:writer_pdf_Export";

    public static string For(string via) => via switch
    {
        "docx" => "docx:Office Open XML Text",
        "odt" => "odt:writer8",
        "rtf" => "rtf:Rich Text Format",
        "html" => "html:HTML (StarWriter)",
        "md" => "md:Markdown",
        _ => throw new ArgumentException("No LibreOffice filter is recorded for '" + via + "'.", nameof(via)),
    };

    /// <summary>The name LibreOffice echoes back in its "using filter :" line.</summary>
    public static string NameOf(string via)
    {
        string filter = For(via);
        int colon = filter.IndexOf(':');
        return colon < 0 ? filter : filter[(colon + 1)..];
    }

    /// <summary>The format name broilerdoc's <c>probe</c> is expected to select.</summary>
    public static string BroilerdocFormat(string via) => via switch
    {
        "docx" => "DOCX",
        "odt" => "ODT",
        "rtf" => "RTF",
        "html" => "HTML",
        "md" => "Markdown",
        _ => via.ToUpperInvariant(),
    };
}

/// <summary>Bounded fan-out, matching the corpus suite's shape.</summary>
internal static class Parallelism
{
    public static async Task ForEachAsync<T>(IEnumerable<T> items, int jobs, Func<T, Task> body)
    {
        using var slots = new SemaphoreSlim(Math.Max(1, jobs));
        var running = new List<Task>();

        foreach (T item in items)
        {
            await slots.WaitAsync().ConfigureAwait(false);
            running.Add(Task.Run(async () =>
            {
                try
                {
                    await body(item).ConfigureAwait(false);
                }
                finally
                {
                    slots.Release();
                }
            }));
        }

        await Task.WhenAll(running).ConfigureAwait(false);
    }
}
