using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Documents.Corpus;

/// <summary>
/// The corpus suite: a document corpus, driven through the built
/// <c>broilerdoc</c> executable, held against a committed baseline.
/// </summary>
/// <remarks>
/// <para>
/// It exits with the number of failed checks, capped at 99, so a caller can
/// branch on the code and a reader can see the count without parsing anything.
/// The last line it prints is <c>N/M passed, K failed.</c>, which is the shape
/// CI already greps for.
/// </para>
/// </remarks>
internal static class Program
{
    private static async Task<int> Main(string[] arguments)
    {
        Options options;
        try
        {
            options = Options.Parse(arguments);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine("error: " + exception.Message);
            Console.Error.WriteLine();
            Options.WriteUsage(Console.Error);
            return 99;
        }

        if (options.Help)
        {
            Options.WriteUsage(Console.Out);
            return 0;
        }

        try
        {
            return await RunAsync(options).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or FileNotFoundException)
        {
            // A corpus that will not load is a defect in this suite, not a
            // verdict about the component, and it should read that way.
            Console.Error.WriteLine("error: " + exception.Message);
            return 99;
        }
    }

    private static async Task<int> RunAsync(Options options)
    {
        string root = RepositoryRoot();
        CorpusManifest manifest = CorpusManifest.Load(root);
        BroilerDocTool tool = BroilerDocTool.Locate(root, options.ToolPath);

        string workspace = options.Workspace ?? Path.Combine(
            Path.GetTempPath(), "broilerdoc-corpus", Guid.NewGuid().ToString("N"));

        Guard(workspace, root);

        var log = new RunLog();
        var outcomes = new List<RoundTripOutcome>();

        Console.WriteLine("tool       " + tool.Description);
        Console.WriteLine("corpus     " + manifest.Samples.Count + " generated, " +
                          manifest.Authored.Count + " authored, " + manifest.Malformed.Count + " malformed");
        Console.WriteLine("workspace  " + workspace);

        var space = new CorpusWorkspace(tool, manifest, workspace);
        try
        {
            IReadOnlyList<CheckResult> materialised = await space.MaterialiseAsync().ConfigureAwait(false);
            log.AddRange(materialised);

            // An asset is one render of one character. If that failed, every
            // sample depending on it degrades to a skip and the run still prints
            // a clean line - so it stops the run instead.
            CheckResult[] assets = materialised
                .Where(result => result.Group == "asset" && result.Outcome == CheckOutcome.Failed)
                .ToArray();

            if (assets.Length > 0)
            {
                Reporting.WriteConsole(Console.Out, log.Results, options.Verbose);
                Console.Error.WriteLine();
                Console.Error.WriteLine(
                    "The corpus assets could not be produced, so the run stopped rather than " +
                    "reporting the samples that need them as skipped.");
                return Math.Min(assets.Length, 99);
            }

            if (options.Fetch)
            {
                (IReadOnlyList<CorpusDocument> fetched, IReadOnlyList<CheckResult> fetchResults) =
                    await ExternalSources.FetchAsync(root, workspace).ConfigureAwait(false);
                log.AddRange(fetchResults);
                space.Adopt(fetched);
            }

            log.AddRange(await ContractChecks.RunAsync(tool, manifest, space).ConfigureAwait(false));

            Baseline? baseline = options.UpdateBaseline || !File.Exists(Baseline.PathIn(root))
                ? SafeLoad(root)
                : Baseline.Load(root);

            bool fonts = await RenderChecks.FontsAvailableAsync(tool).ConfigureAwait(false);
            if (!fonts)
            {
                Console.WriteLine(
                    "fonts      none reported by the tool; render checks will be skipped, not failed.");
            }

            await ForEachAsync(space.Documents, options.Jobs, async document =>
            {
                log.AddRange(await DocumentChecks.RunAsync(tool, document, workspace).ConfigureAwait(false));

                RoundTripReading reading = await RoundTripChecks
                    .RunAsync(tool, document, options.UpdateBaseline ? null : baseline)
                    .ConfigureAwait(false);

                log.AddRange(reading.Results);
                lock (outcomes)
                    outcomes.AddRange(reading.Outcomes);

                log.AddRange(await RenderChecks
                    .RunAsync(tool, document, workspace, fonts).ConfigureAwait(false));
            }).ConfigureAwait(false);

            if (!options.UpdateBaseline && baseline is not null)
            {
                foreach (BaselineRow row in baseline.NotReproduced(outcomes))
                {
                    log.Add(CheckResult.Fail("roundtrip", "roundtrip/" + row.Key,
                        "the baseline holds a row for this round trip and the run never reached it. " +
                        "Either the sample was renamed or removed, or a check stopped running."));
                }
            }

            IReadOnlyList<CheckResult> results = Ordered(log.Results);

            if (options.UpdateBaseline)
            {
                Baseline.Write(root, outcomes, SafeLoad(root), options.Today);
                Console.WriteLine();
                Console.WriteLine("baseline   rewrote " + Baseline.RelativePath + " from this run.");
                Console.WriteLine("           New rows are recorded as suspected defects until somebody");
                Console.WriteLine("           classifies them. Fill in state, reference, why, reviewer and");
                Console.WriteLine("           decided before committing it.");
            }

            if (options.JsonReport is not null)
                Reporting.WriteJson(options.JsonReport, results, outcomes, tool.Description);

            if (options.JUnitReport is not null)
                Reporting.WriteJUnit(options.JUnitReport, results);

            Reporting.WriteConsole(Console.Out, results, options.Verbose);

            int failed = results.Count(result => result.Outcome == CheckOutcome.Failed);
            return Math.Min(failed, 99);
        }
        finally
        {
            if (options.Keep)
                Console.WriteLine("kept       " + workspace);
            else
                Delete(workspace);
        }
    }

    /// <summary>
    /// Refuses to materialise inside the repository.
    /// </summary>
    /// <remarks>
    /// <c>FormatClaimGuardTests.No_Document_Of_A_Supported_Format_Is_Committed</c>
    /// walks the whole component root for exactly the extensions this corpus
    /// writes, excluding only <c>bin</c>, <c>obj</c> and <c>.git</c>. A run that
    /// materialised in the tree - or a <c>--workspace</c> pointed there by
    /// somebody trying to inspect the output - would make that guard fail on the
    /// next test pass, and it would be right to. Better to refuse than to leave
    /// the trap.
    /// </remarks>
    private static void Guard(string workspace, string root)
    {
        string full = Path.GetFullPath(workspace);
        string component = Path.GetFullPath(root);

        if (full.StartsWith(component + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(full, component, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The workspace is inside the repository (" + full + "). The corpus writes .docx, " +
                ".odt, .rtf and .html files, and FormatClaimGuardTests fails the build when any of " +
                "those is found anywhere in the tree. Use a path outside it, or drop --workspace " +
                "and let the runner choose a temporary directory.");
        }

        // The edit language splits an image PROPS field on commas outside quotes.
        // The manifest quotes the substituted path for exactly this reason, but a
        // clear message here beats a confusing per-sample failure if that ever
        // regresses.
        if (full.Contains(',', StringComparison.Ordinal))
        {
            Console.WriteLine(
                "note       the workspace path contains a comma; the corpus quotes image paths, " +
                "so this is expected to work.");
        }
    }

    private static Baseline? SafeLoad(string root)
    {
        try
        {
            return File.Exists(Baseline.PathIn(root)) ? Baseline.Load(root) : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sorted by name, not by when the check finished.
    /// </summary>
    /// <remarks>
    /// The checks run in parallel, so completion order varies run to run. The
    /// names are stable identifiers, so sorting by them makes two CI logs
    /// diffable - which is most of what a reader wants from a report of several
    /// thousand lines.
    /// </remarks>
    private static IReadOnlyList<CheckResult> Ordered(IReadOnlyList<CheckResult> results) =>
        results
            .OrderBy(result => result.Group, StringComparer.Ordinal)
            .ThenBy(result => result.Name, StringComparer.Ordinal)
            .ToArray();

    private static async Task ForEachAsync<T>(IEnumerable<T> items, int jobs, Func<T, Task> body)
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

    private static void Delete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary directory is not worth failing a passing run
            // over; the operating system reclaims it.
        }
    }

    private static string RepositoryRoot()
    {
        // The same walk the guard tests use: the directory that owns
        // Directory.Build.props and holds this component's projects.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")) &&
                File.Exists(Path.Combine(
                    directory.FullName, "src", "Broiler.Documents", "Broiler.Documents.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Broiler.Documents component root not found.");
    }

    private sealed record Options(
        string? ToolPath,
        string? Workspace,
        string? JsonReport,
        string? JUnitReport,
        bool Keep,
        bool Verbose,
        bool Fetch,
        bool UpdateBaseline,
        bool Help,
        int Jobs,
        string Today)
    {
        public static Options Parse(string[] arguments)
        {
            string? tool = null, workspace = null, json = null, junit = null;
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            bool keep = false, verbose = false, fetch = false, update = false, help = false;
            int jobs = Environment.ProcessorCount;

            for (int index = 0; index < arguments.Length; index++)
            {
                string argument = arguments[index];
                string Value(string name) => index + 1 < arguments.Length
                    ? arguments[++index]
                    : throw new ArgumentException("'" + name + "' needs a value.");

                switch (argument)
                {
                    case "--tool": tool = Value(argument); break;
                    case "--workspace": workspace = Value(argument); break;
                    case "--report": json = Value(argument); break;
                    case "--junit": junit = Value(argument); break;
                    case "--jobs":
                        if (!int.TryParse(Value(argument), NumberStyles.Integer, CultureInfo.InvariantCulture,
                                out jobs) || jobs < 1)
                        {
                            throw new ArgumentException("'--jobs' expects a positive whole number.");
                        }

                        break;
                    case "--keep": keep = true; break;
                    case "--verbose": verbose = true; break;
                    case "--fetch": fetch = true; break;
                    case "--update-baseline": update = true; break;
                    case "--help" or "-h": help = true; break;
                    default:
                        throw new ArgumentException("unknown option '" + argument + "'.");
                }
            }

            return new Options(tool, workspace, json, junit, keep, verbose, fetch, update, help, jobs, today);
        }

        public static void WriteUsage(TextWriter output)
        {
            output.WriteLine("broilerdoc-corpus - drive the broilerdoc command line over a document corpus.");
            output.WriteLine();
            output.WriteLine("usage: dotnet run --project src/tests/Broiler.Documents.Corpus -- [options]");
            output.WriteLine();
            output.WriteLine("options");
            output.WriteLine("  --tool <path>        The broilerdoc executable. Derived from this runner's own");
            output.WriteLine("                       build configuration when not given.");
            output.WriteLine("  --workspace <dir>    Where to materialise the corpus. Must be outside the");
            output.WriteLine("                       repository; a temporary directory when not given.");
            output.WriteLine("  --keep               Do not delete the workspace, and print where it is.");
            output.WriteLine("  --jobs <n>           Documents checked at once. (default: processor count)");
            output.WriteLine("  --report <path>      Write the full result as JSON.");
            output.WriteLine("  --junit <path>       Write the result as JUnit XML.");
            output.WriteLine("  --update-baseline    Rewrite tests/corpus/corpus-baseline.json from this run,");
            output.WriteLine("                       keeping the classification of every row already in it.");
            output.WriteLine("  --fetch              Fetch the approved rows in");
            output.WriteLine("                       tests/corpus/external-sources.json. There are none, and");
            output.WriteLine("                       the run says so rather than passing quietly.");
            output.WriteLine("  --verbose            Name every skipped check rather than grouping them.");
            output.WriteLine("  --help               This.");
            output.WriteLine();
            output.WriteLine("Exits with the number of failed checks, capped at 99.");
        }
    }
}
