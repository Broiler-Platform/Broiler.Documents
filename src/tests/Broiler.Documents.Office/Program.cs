using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Broiler.Documents.Corpus;

namespace Broiler.Documents.Office;

/// <summary>
/// The office conformance suite: LibreOffice writes the documents, broilerdoc
/// reads them, and the disagreements are held against a committed baseline.
/// </summary>
/// <remarks>
/// <para>
/// It sits beside the corpus suite rather than replacing it, and the division is
/// worth stating. The corpus suite is evidence about round-trip fidelity and the
/// command contract, over documents this component wrote. Its own documentation
/// names the gap it cannot close: "It is not evidence that a file produced by
/// Word, LibreOffice or a browser reads correctly, and no amount of adding
/// samples changes that." This suite closes exactly that gap, by exactly one
/// step - the documents are still grown from seeds authored here, but the
/// markup broilerdoc is asked to read was written by an implementation that
/// knows nothing about this one.
/// </para>
/// <para>
/// It exits with the number of failed checks, capped at 99, and prints
/// <c>N/M passed, K failed.</c> as its last line, which is the shape CI already
/// greps for.
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
            // A control file that will not load is a defect in this suite, not a
            // verdict about the component, and it should read that way.
            Console.Error.WriteLine("error: " + exception.Message);
            return 99;
        }
    }

    private static async Task<int> RunAsync(Options options)
    {
        string root = RepositoryRoot();
        BroilerDocTool tool = BroilerDocTool.Locate(root, options.ToolPath);
        OfficeManifest manifest = OfficeManifest.Load(
            options.Manifest ?? Path.Combine(root, "tests", "office", "office-corpus.json"));
        manifest.Validate();

        string baselinePath = options.Baseline ?? Path.Combine(root, "tests", "office", "office-baseline.json");
        string toolsPath = options.Tools ?? Path.Combine(root, "tests", "office", "tools", "manifest.json");

        string workspace = options.Workspace ?? Path.Combine(
            Path.GetTempPath(), "broilerdoc-office", Guid.NewGuid().ToString("N"));

        Guard(workspace, root);

        var log = new RunLog();
        string platform = PlatformName();

        Console.WriteLine("tool        " + tool.Description);

        ToolRegister register = ToolRegister.Load(toolsPath);
        LibreOfficeTool? libreOffice = LibreOfficeTool.Locate(options.SofficePath);
        string? blockedLibre = register.Blocked("libreoffice", options.AllowPendingTools);

        if (libreOffice is null)
        {
            Console.WriteLine("libreoffice not installed");
            return Inert(log, "LibreOffice is not installed, so nothing could be produced to read.",
                manifest, options);
        }

        if (blockedLibre is not null)
        {
            Console.WriteLine("libreoffice " + libreOffice.Version + "  (not driven: " + blockedLibre + ")");
            return Inert(log, blockedLibre, manifest, options);
        }

        Console.WriteLine("libreoffice " + libreOffice.Version);

        Rasterizer? rasterizer = options.Axis == "semantic" ? null : Rasterizer.Locate(options.PdftoppmPath);
        string? blockedRaster = rasterizer is null
            ? null
            : register.Blocked("poppler-utils", options.AllowPendingTools);

        // The reason is kept, not just the decision. A rasteriser the register
        // has not approved is a different situation from one nobody installed,
        // and a report that said "no rasteriser is available" for both would
        // send a reader to install a package they already have.
        string rasterReason = "no rasteriser is available, so the two renderings could not be compared.";

        if (rasterizer is not null && blockedRaster is not null)
        {
            Console.WriteLine("rasterizer  " + rasterizer.Version + "  (not driven: " + blockedRaster + ")");
            rasterReason = blockedRaster;
            rasterizer = null;
        }
        else
        {
            Console.WriteLine("rasterizer  " + (rasterizer?.Version ?? "none"));
        }

        IReadOnlyList<string> pinned = PinnedFonts(options.FontDirectories);
        IReadOnlyList<string> fontArguments =
            FontPinning.Arguments(manifest.Fonts, options.FontDirectories);
        Console.WriteLine("fonts       " + (options.FontDirectories.Count == 0
            ? "not pinned; the pixel axis will measure this machine's fonts and says so"
            : pinned.Count.ToString(CultureInfo.InvariantCulture) + " face(s) from " +
              string.Join(", ", options.FontDirectories) + ", " +
              (fontArguments.Count / 2).ToString(CultureInfo.InvariantCulture) + " mapping(s)"));

        OfficeBaseline? baseline = File.Exists(baselinePath) ? OfficeBaseline.Load(baselinePath) : null;
        var observedStamp = new ToolchainStamp(
            libreOffice.Version, rasterizer?.Version, options.FontSet, platform, options.Dpi);

        string? mismatch = baseline?.Stamp.Mismatch(observedStamp);
        bool comparing = !options.UpdateBaseline && baseline is not null && mismatch is null;

        // The reason a comparison did not happen is carried down to every check
        // that would have made one, rather than being printed once at the top.
        // A reader meeting a hundred skips in a report wants the cause beside
        // them; "running without a baseline" was true and useless when the real
        // answer was that this machine has a different LibreOffice.
        string notComparing =
            options.UpdateBaseline ? "regenerating the baseline, so nothing was compared."
            : baseline is null ? "there is no baseline file, so nothing was compared."
            : mismatch is not null ? "the toolchain does not match the baseline stamp - " + mismatch
            : "nothing was compared.";

        if (mismatch is not null && !options.UpdateBaseline)
        {
            Console.WriteLine("baseline    not compared: " + mismatch);
        }

        Console.WriteLine("corpus      " + manifest.Seeds.Count + " seed(s) x " +
                          manifest.Targets.Count + " target(s) = " +
                          (manifest.Seeds.Count * manifest.Targets.Count) + " document(s)");
        Console.WriteLine("axis        " + options.Axis);
        Console.WriteLine("workspace   " + workspace);

        var space = new OfficeWorkspace(libreOffice, manifest, workspace, options.LibreOfficeTimeout);
        var readings = new List<SemanticReading>();
        var renders = new List<RenderRow>();

        try
        {
            // The self-test runs before anything is measured. Getting either raw
            // image format wrong makes every pixel metric confidently
            // meaningless, and a confidently meaningless number is worse than no
            // number at all.
            if (rasterizer is not null)
            {
                string? selfTest = InkProfile.SelfTest(space.Area("self-test"));
                if (selfTest is not null)
                {
                    Console.Error.WriteLine("error: the image self-test failed: " + selfTest);
                    return 99;
                }
            }

            log.AddRange(await space.MaterialiseAsync(options.Jobs, options.Only).ConfigureAwait(false));

            await Parallelism.ForEachAsync(space.Documents, options.Jobs, async document =>
            {
                if (options.Axis is "semantic" or "all")
                {
                    SemanticOutcome semantic = await SemanticChecks
                        .RunAsync(tool, space, document, baseline, comparing, notComparing)
                        .ConfigureAwait(false);

                    log.AddRange(semantic.Results);
                    lock (readings)
                        readings.AddRange(semantic.Readings);
                }

                if (options.Axis is "pixel" or "all" && rasterizer is not null)
                {
                    PixelOutcome pixel = await PixelChecks
                        .RunAsync(tool, space, rasterizer, document, baseline, comparing, notComparing,
                            options.Dpi, options.MaxPages, pinned, manifest.ToolFonts, options.FontDirectories,
                            fontArguments)
                        .ConfigureAwait(false);

                    log.AddRange(pixel.Results);
                    if (pixel.Row is not null)
                    {
                        lock (renders)
                            renders.Add(pixel.Row);
                    }
                }
                else if (options.Axis is "pixel" or "all")
                {
                    log.Add(CheckResult.Skip("pixel", "pixel/" + document.Key,
                        "no rasteriser is available, so the two renderings could not be compared."));
                }
            }).ConfigureAwait(false);

            // The not-reproduced check only means anything over a whole run.
            // Under --only the run deliberately never reaches most rows, and
            // reporting each of them as a row that stopped reproducing would
            // bury the one document the caller asked about under a hundred
            // failures about documents they excluded on purpose.
            if (comparing && baseline is not null && options.Only is null)
            {
                // Built through the row types' own key helpers rather than
                // spelled again here. The second spelling is what went wrong
                // first: it used a colon where the rows use a slash, so every
                // committed row reported as one the run never reached.
                var reached = new List<string>();
                reached.AddRange(readings.Select(
                    reading => ReadRow.KeyFor(reading.Seed, reading.Via, reading.Check)));
                reached.AddRange(renders.Select(render => render.Key));

                // Only the families this run could have reached are asked about.
                // Under --axis pixel the semantic checks never run, so every
                // committed read row would otherwise be reported as one that
                // stopped reproducing - a failure that says "the seed was
                // renamed" about a run that was simply not asked to look. The
                // same argument the --only guard above makes, for the other way
                // of running half the suite.
                var families = new List<string>();
                if (options.Axis is "semantic" or "all")
                    families.Add("read/");
                if (options.Axis is "pixel" or "all" && rasterizer is not null)
                    families.Add("render/");

                foreach (string missing in baseline.NotReproduced(reached)
                             .Where(key => families.Any(family =>
                                 key.StartsWith(family, StringComparison.Ordinal))))
                {
                    log.Add(CheckResult.Fail("baseline", "baseline/" + missing,
                        "the baseline holds a row for this and the run never reached it. Either the " +
                        "seed was renamed or removed, or a check stopped running."));
                }
            }

            IReadOnlyList<CheckResult> results = Ordered(log.Results);

            if (options.UpdateBaseline)
            {
                if (baseline is not null && mismatch is not null && !options.Restamp)
                {
                    Console.Error.WriteLine(
                        "error: regenerating would change the toolchain stamp (" + mismatch + "). " +
                        "A baseline generated on a different LibreOffice is not a baseline, it is a " +
                        "record of a different experiment. Pass --restamp if that is what you mean.");
                    return 99;
                }

                OfficeBaseline.Write(baselinePath, observedStamp, ToRows(readings), renders, baseline);
                Console.WriteLine();
                Console.WriteLine("baseline    rewrote " + baselinePath + " from this run.");
                Console.WriteLine("            New rows are recorded as suspected defects until somebody");
                Console.WriteLine("            classifies them. Fill in state, reference, why, reviewer");
                Console.WriteLine("            and decided before committing it.");
            }

            var header = new RunHeader(
                tool.Description, libreOffice.Version, rasterizer?.Version, options.FontSet,
                platform, options.Dpi, mismatch is null);

            if (options.JsonReport is not null)
                OfficeReporting.WriteJson(options.JsonReport, results, readings, renders, header);

            if (options.JUnitReport is not null)
                OfficeReporting.WriteJUnit(options.JUnitReport, results);

            OfficeReporting.WriteConsole(Console.Out, results, options.Verbose);

            int failed = results.Count(result => result.Outcome == CheckOutcome.Failed);
            return Math.Min(failed, 99);
        }
        finally
        {
            if (options.Keep)
                Console.WriteLine("kept        " + workspace);
            else
                Delete(workspace);
        }
    }

    /// <summary>
    /// A run with nothing to drive still reports, and still says why.
    /// </summary>
    /// <remarks>
    /// A fully-skipped run before the tool register is decided is the
    /// <em>expected</em> state of this suite, not a broken one. It prints one
    /// skip per document with the reason and exits 0, so the difference between
    /// "nobody has approved the oracle yet" and "the suite passed" is visible on
    /// the last two lines rather than inferred from a count.
    /// </remarks>
    private static int Inert(RunLog log, string reason, OfficeManifest manifest, Options options)
    {
        foreach (OfficeSeed seed in manifest.Seeds)
        {
            foreach (string via in manifest.Targets)
            {
                if (options.Only is not null &&
                    !(seed.Id + "/" + via).Contains(options.Only, StringComparison.Ordinal))
                {
                    continue;
                }

                log.Add(CheckResult.Skip("produce", "produce/" + seed.Id + "/" + via, reason));
            }
        }

        OfficeReporting.WriteConsole(Console.Out, Ordered(log.Results), options.Verbose);
        return 0;
    }

    /// <summary>
    /// The readings worth committing.
    /// </summary>
    /// <remarks>
    /// Only the ones that are not clean. The file's own policy says it records
    /// what differs and that everything absent is asserted to agree, so writing
    /// a row per clean check would turn a control into a log - several hundred
    /// rows nobody reads, in which the dozen that mean something are invisible.
    /// It would also quietly invert the check: a row saying "no differences"
    /// would pass whether or not the check still ran.
    /// </remarks>
    private static IEnumerable<ReadRow> ToRows(IEnumerable<SemanticReading> readings) =>
        readings
            .Where(reading => reading.Differences.Count > 0 || reading.Diagnostics.Count > 0)
            .Select(reading => new ReadRow(
            reading.Seed, reading.Via, reading.Check, reading.Differences, reading.Diagnostics,
            BaselineState.SuspectedDefect, null, string.Empty, null, null));

    /// <summary>
    /// Refuses to materialise inside the repository.
    /// </summary>
    /// <remarks>
    /// The same refusal the corpus suite makes, for the same reason.
    /// <c>FormatClaimGuardTests.No_Document_Of_A_Supported_Format_Is_Committed</c>
    /// walks the whole component root for exactly the extensions LibreOffice is
    /// asked to write here, and a run that materialised in the tree would make
    /// that guard fail on the next test pass - landing the failure on whoever
    /// ran the tests next rather than on whoever caused it. Better to refuse than
    /// to leave the trap.
    /// </remarks>
    private static void Guard(string workspace, string root)
    {
        string full = Path.GetFullPath(workspace);
        string component = Path.GetFullPath(root);

        if (full.StartsWith(component + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(full, component, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The workspace is inside the repository (" + full + "). LibreOffice writes .docx, " +
                ".odt, .rtf and .html files here, and FormatClaimGuardTests fails the build when any " +
                "of those is found anywhere in the tree. Use a path outside it, or drop --workspace " +
                "and let the runner choose a temporary directory.");
        }
    }

    /// <summary>
    /// Every font face the pinned directories hold, by family name.
    /// </summary>
    /// <remarks>
    /// Only the file names, because the point is not to parse a font: it is to
    /// have something concrete to hold LibreOffice's embedded-font list against.
    /// The comparison ignores spaces, since a family called "Liberation Serif"
    /// arrives from a file called LiberationSerif-Regular.ttf and from a PDF as
    /// LiberationSerif.
    /// </remarks>
    private static IReadOnlyList<string> PinnedFonts(IReadOnlyList<string> directories)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in directories)
        {
            if (!Directory.Exists(directory))
                continue;

            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                string extension = Path.GetExtension(file);
                if (extension is not (".ttf" or ".otf" or ".ttc" or ".TTF" or ".OTF" or ".TTC"))
                    continue;

                string name = Path.GetFileNameWithoutExtension(file);
                int dash = name.IndexOf('-');
                names.Add(dash > 0 ? name[..dash] : name);
            }
        }

        return names.ToArray();
    }

    private static string PlatformName() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos"
        : "linux";

    /// <summary>
    /// Sorted by name, not by when the check finished.
    /// </summary>
    /// <remarks>
    /// The checks run in parallel, so completion order varies run to run. The
    /// names are stable identifiers, so sorting by them makes two CI logs
    /// diffable - which is most of what a reader wants from a report of several
    /// hundred lines.
    /// </remarks>
    private static IReadOnlyList<CheckResult> Ordered(IReadOnlyList<CheckResult> results) =>
        results
            .OrderBy(result => result.Group, StringComparer.Ordinal)
            .ThenBy(result => result.Name, StringComparer.Ordinal)
            .ToArray();

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
        string? SofficePath,
        string? PdftoppmPath,
        string? Manifest,
        string? Baseline,
        string? Tools,
        string? Workspace,
        IReadOnlyList<string> FontDirectories,
        string? FontSet,
        string Axis,
        string? Only,
        string? JsonReport,
        string? JUnitReport,
        bool Keep,
        bool Verbose,
        bool UpdateBaseline,
        bool Restamp,
        bool AllowPendingTools,
        bool Help,
        int Jobs,
        int Dpi,
        int MaxPages,
        TimeSpan LibreOfficeTimeout)
    {
        public static Options Parse(string[] arguments)
        {
            string? tool = null, soffice = null, pdftoppm = null, manifest = null, baseline = null;
            string? tools = null, workspace = null, fontSet = null, only = null, json = null, junit = null;
            string axis = "all";
            var fontDirectories = new List<string>();
            bool keep = false, verbose = false, update = false, restamp = false;
            bool allowPending = false, help = false;
            int jobs = Environment.ProcessorCount;
            int dpi = 144, maxPages = 40, timeout = 120;

            for (int index = 0; index < arguments.Length; index++)
            {
                string argument = arguments[index];
                string Value(string name) => index + 1 < arguments.Length
                    ? arguments[++index]
                    : throw new ArgumentException("'" + name + "' needs a value.");

                int Integer(string name, int least)
                {
                    string text = Value(name);
                    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ||
                        value < least)
                    {
                        throw new ArgumentException(
                            "'" + name + "' needs a whole number of at least " +
                            least.ToString(CultureInfo.InvariantCulture) + ", not '" + text + "'.");
                    }

                    return value;
                }

                switch (argument)
                {
                    case "--tool": tool = Value(argument); break;
                    case "--soffice": soffice = Value(argument); break;
                    case "--pdftoppm": pdftoppm = Value(argument); break;
                    case "--manifest": manifest = Value(argument); break;
                    case "--baseline": baseline = Value(argument); break;
                    case "--tools": tools = Value(argument); break;
                    case "--workspace": workspace = Value(argument); break;
                    case "--font-dir": fontDirectories.Add(Value(argument)); break;
                    case "--font-set": fontSet = Value(argument); break;
                    case "--only": only = Value(argument); break;
                    case "--report": json = Value(argument); break;
                    case "--junit": junit = Value(argument); break;
                    case "--jobs": jobs = Integer(argument, 1); break;
                    case "--dpi": dpi = Integer(argument, 24); break;
                    case "--max-pages": maxPages = Integer(argument, 1); break;
                    case "--lo-timeout": timeout = Integer(argument, 1); break;
                    case "--keep": keep = true; break;
                    case "--verbose": verbose = true; break;
                    case "--update-baseline": update = true; break;
                    case "--restamp": restamp = true; break;
                    case "--allow-pending-tools": allowPending = true; break;
                    case "--help" or "-h": help = true; break;
                    case "--axis":
                        axis = Value(argument);
                        if (axis is not ("semantic" or "pixel" or "all"))
                        {
                            throw new ArgumentException(
                                "'--axis' takes semantic, pixel or all, not '" + axis + "'.");
                        }

                        break;
                    default:
                        // Unknown options are an error rather than being ignored,
                        // the same contract docs/cli.md holds the tool itself to.
                        // A harness that writes --tolerence must fail loudly.
                        throw new ArgumentException("Unknown option '" + argument + "'.");
                }
            }

            return new Options(
                tool, soffice, pdftoppm, manifest, baseline, tools, workspace, fontDirectories, fontSet,
                axis, only, json, junit, keep, verbose, update, restamp, allowPending, help, jobs, dpi,
                maxPages, TimeSpan.FromSeconds(timeout));
        }

        public static void WriteUsage(TextWriter output)
        {
            output.WriteLine("usage: broilerdoc-office [options]");
            output.WriteLine();
            output.WriteLine("LibreOffice writes the documents, broilerdoc reads them, and the");
            output.WriteLine("disagreements are held against tests/office/office-baseline.json.");
            output.WriteLine();
            output.WriteLine("options");
            output.WriteLine("  --tool <path>            The built broilerdoc. Located from this build when absent.");
            output.WriteLine("  --soffice <path>         The LibreOffice binary. Probed when absent.");
            output.WriteLine("  --pdftoppm <path>        The poppler rasteriser. Probed when absent.");
            output.WriteLine("  --manifest <path>        The seed corpus. (tests/office/office-corpus.json)");
            output.WriteLine("  --baseline <path>        The committed expectations. (tests/office/office-baseline.json)");
            output.WriteLine("  --tools <path>           The external tool register. (tests/office/tools/manifest.json)");
            output.WriteLine("  --workspace <dir>        Where to materialise. Refuses a path inside the repository.");
            output.WriteLine("  --font-dir <dir>         Pin a font directory for both sides. Repeatable.");
            output.WriteLine("  --font-set <name>        The name the baseline stamp records for that set.");
            output.WriteLine("  --axis <kind>            semantic, pixel, or all. (default all)");
            output.WriteLine("  --only <substring>       Run only documents whose seed/format key contains this.");
            output.WriteLine("  --report <path>          Write the JSON report.");
            output.WriteLine("  --junit <path>           Write the JUnit XML report.");
            output.WriteLine("  --jobs <n>               Concurrent documents. (default processor count)");
            output.WriteLine("  --dpi <n>                Resolution for both rasterisers. (default 144)");
            output.WriteLine("  --max-pages <n>          Pages compared per document. (default 40)");
            output.WriteLine("  --lo-timeout <seconds>   Per LibreOffice invocation. (default 120)");
            output.WriteLine("  --keep                   Keep the workspace and print where it is.");
            output.WriteLine("  --verbose                Name every skipped check under its grouped reason.");
            output.WriteLine("  --update-baseline        Regenerate the baseline from this run.");
            output.WriteLine("  --restamp                Permit --update-baseline to change the toolchain stamp.");
            output.WriteLine("  --allow-pending-tools    Drive tools whose register row is not approved.");
            output.WriteLine("  -h, --help               Show this and exit 0.");
            output.WriteLine();
            output.WriteLine("Exit code is the number of failed checks, capped at 99. 99 alone is a usage");
            output.WriteLine("error or a failure of this harness rather than a verdict about the component.");
        }
    }
}
