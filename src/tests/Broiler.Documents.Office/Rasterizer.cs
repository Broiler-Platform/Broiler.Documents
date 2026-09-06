using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broiler.Documents.Office;

/// <summary>What one rasterisation produced.</summary>
/// <param name="Arguments">
/// The arguments as they were passed, so a failure message can be pasted into a
/// shell and re-run. A reconstructed command line is a second implementation of
/// the argument building and drifts from the first one silently.
/// </param>
/// <param name="Pages">
/// The bitmaps that appeared, one per page, in page order. Empty is a real
/// answer and not an error: a run that was killed, or that pdftoppm refused,
/// produces no pages, and the exit code and stderr say which.
/// </param>
internal sealed record RasterRun(
    IReadOnlyList<string> Arguments,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    IReadOnlyList<string> Pages)
{
    /// <summary>
    /// Set when the process outlived its timeout and was killed. It is kept
    /// apart from the exit code because a killed process has no exit code worth
    /// reading, and a check that saw only <c>-1</c> would report a hang as a
    /// crash - which sends the next reader looking for a stack trace that was
    /// never written.
    /// </summary>
    public bool TimedOut { get; init; }

    /// <summary>The command line, quoted enough to be re-run by hand.</summary>
    public string CommandLine()
    {
        var text = new StringBuilder("pdftoppm");
        foreach (string argument in Arguments)
        {
            text.Append(' ');
            text.Append(argument.Contains(' ', StringComparison.Ordinal) ? '"' + argument + '"' : argument);
        }

        return text.ToString();
    }
}

/// <summary>
/// Turns LibreOffice's PDF export into one bitmap per page, so that what
/// LibreOffice thinks the document looks like can be compared pixel for pixel
/// against what <c>broilerdoc render</c> thinks it looks like.
/// </summary>
/// <remarks>
/// <para>
/// The rasteriser is poppler's <c>pdftoppm</c> rather than LibreOffice's own PNG
/// export, and the reason is not preference. LibreOffice's PNG filter renders
/// page 1 and stops, misreports the resolution in the PNG <c>pHYs</c> chunk, and
/// lands one pixel off the size it was asked for. <c>pdftoppm -r 96</c> put a US
/// Letter page at exactly 816x1056, which is exactly what
/// <c>broilerdoc render --dpi 96</c> produced for the same document. A comparison
/// is only worth making when a disagreement means the renderers disagree, and a
/// one-pixel offset baked into the reference would mean every comparison started
/// out wrong.
/// </para>
/// <para>
/// The anti-aliasing flags are passed on every run, always, even though poppler
/// has defaults for them. The defaults are not stable across poppler builds: a
/// measurement of the same document through two of them moved about nine percent
/// of the pixels. That is well above any threshold a renderer comparison would
/// want to set, so an implicit default would make this suite's verdict depend on
/// which poppler the machine happened to install.
/// </para>
/// <para>
/// Nothing here throws. poppler is not a build dependency of this repository and
/// a machine without it is an ordinary machine, so its absence is a skip that
/// names a reason - see <see cref="Locate"/>. A rasteriser that threw would turn
/// "this check could not run" into "this check crashed", and the two deserve
/// different words in a report.
/// </para>
/// </remarks>
internal sealed class Rasterizer
{
    private const string EnvironmentVariable = "BROILER_PDFTOPPM";

    private Rasterizer(string executable, string version, string? fontLister)
    {
        Executable = executable;
        Version = version;
        FontLister = fontLister;
    }

    /// <summary>The <c>pdftoppm</c> that answered the probe.</summary>
    public string Executable { get; }

    /// <summary>
    /// The version line the probe read back, for the report header. It is
    /// recorded rather than parsed: this suite never branches on a poppler
    /// version, and a version comparison written now would be a guess about
    /// which future release breaks something.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// The <c>pdffonts</c> beside the rasteriser, when there is one. Null is not
    /// a failure - font listing is a separate binary from the same package, and
    /// the rest of this class works without it.
    /// </summary>
    public string? FontLister { get; }

    /// <summary>
    /// Finds a working <c>pdftoppm</c>, or returns null when poppler is not
    /// installed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Null rather than an exception, because a missing rasteriser is a skip with
    /// a reason and never a failure. The caller is expected to print that reason
    /// next to the pass and fail counts; a suite that quietly stopped rendering
    /// half of itself reads as a suite that passed.
    /// </para>
    /// <para>
    /// The candidate is probed with <c>-v</c> and its output has to mention
    /// <c>pdftoppm</c> or <c>poppler</c>, because a name on PATH is not a working
    /// binary. A stub, a wrapper script that lost its interpreter, or an
    /// unrelated program with the same name all satisfy "the file is there", and
    /// each of them would fail later - in the middle of a comparison, wearing the
    /// costume of a rendering difference.
    /// </para>
    /// <para>
    /// A path given explicitly or through <c>BROILER_PDFTOPPM</c> is authoritative
    /// and never falls back to PATH. Somebody who names a binary is usually
    /// testing that specific build, and silently rendering with a different one
    /// while reporting the named one is worse than reporting nothing at all.
    /// </para>
    /// </remarks>
    public static Rasterizer? Locate(string? explicitPath)
    {
        string? named = explicitPath;
        if (string.IsNullOrWhiteSpace(named))
            named = Environment.GetEnvironmentVariable(EnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(named))
        {
            string? namedVersion = ProbeVersion(named, "pdftoppm", "poppler");
            return namedVersion is null ? null : new Rasterizer(named, namedVersion, FindFontLister(named));
        }

        string? version = ProbeVersion("pdftoppm", "pdftoppm", "poppler");
        return version is null ? null : new Rasterizer("pdftoppm", version, FindFontLister("pdftoppm"));
    }

    /// <summary>
    /// Renders every page up to <paramref name="maxPages"/> into
    /// <c>&lt;outputPrefix&gt;-N.ppm</c> and returns them in page order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The output files are enumerated after the run rather than predicted before
    /// it, and the ordering is by the integer parsed off the tail rather than by
    /// name. This is the whole reason the method looks the way it does: poppler
    /// pads the page number to the width of the page count, so a three page
    /// document gives <c>-1</c>, <c>-2</c>, <c>-3</c> while a twelve page one
    /// gives <c>-01</c> upward. Constructing the expected names would need this
    /// class to know the page count before it has one, and an ordinal sort would
    /// put page 10 before page 2 in exactly the documents - the long ones - where
    /// a misordered comparison is hardest to spot by eye.
    /// </para>
    /// <para>
    /// Any bitmaps left by an earlier run under the same prefix are deleted
    /// first. Without that, a document that lost a page between runs would still
    /// show the old page, because nothing overwrote it: the regression this suite
    /// exists to catch would arrive as a passing comparison against a stale file.
    /// </para>
    /// </remarks>
    public RasterRun Render(string pdfPath, string outputPrefix, int dpi, int maxPages, TimeSpan timeout)
    {
        string prefix = Path.GetFullPath(outputPrefix);
        string directory = Path.GetDirectoryName(prefix) ?? Directory.GetCurrentDirectory();
        string prefixName = Path.GetFileName(prefix);
        string pattern = prefixName + "-*.ppm";

        try
        {
            Directory.CreateDirectory(directory);
            foreach (string stale in Directory.GetFiles(directory, pattern))
                File.Delete(stale);
        }
        catch (IOException failure)
        {
            return Failed(["-r", Text(dpi), pdfPath, prefix], failure, "the output directory could not be prepared");
        }
        catch (UnauthorizedAccessException failure)
        {
            return Failed(["-r", Text(dpi), pdfPath, prefix], failure, "the output directory could not be prepared");
        }

        // -l 0 would render nothing at all and produce a run that looks like a
        // document with no pages, which is a worse story than rendering one page.
        int last = maxPages < 1 ? 1 : maxPages;

        string[] arguments =
        [
            "-r", Text(dpi),
            "-aa", "yes",
            "-aaVector", "yes",
            "-f", "1",
            "-l", Text(last),
            Path.GetFullPath(pdfPath),
            prefix,
        ];

        (int ExitCode, string Output, string Error, TimeSpan Duration, bool TimedOut) run =
            Run(Executable, arguments, timeout);

        string[] pages;
        try
        {
            pages = Directory.GetFiles(directory, pattern)
                .OrderBy(PageNumber)
                .ThenBy(page => page, StringComparer.Ordinal)
                .ToArray();
        }
        catch (IOException)
        {
            pages = [];
        }
        catch (UnauthorizedAccessException)
        {
            pages = [];
        }

        return new RasterRun(arguments, run.ExitCode, run.Output, run.Error, run.Duration, pages)
        {
            TimedOut = run.TimedOut,
        };
    }

    /// <summary>
    /// One page, as a PNG, for a human to look at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="Render"/> and deliberately so. The measurement
    /// path wants PPM, because a raw format the harness parses itself cannot
    /// share a decoder bug with anything else in the run. A triage artifact
    /// wants PNG, because somebody is going to open it. Mixing the two would
    /// mean either measuring through a PNG decoder or handing a reviewer a file
    /// their image viewer refuses.
    /// </para>
    /// <para>
    /// It is only ever called after a check has already failed, so the cost of a
    /// second rasterisation buys a picture in the one case where a picture is
    /// worth having.
    /// </para>
    /// </remarks>
    public RasterRun RenderPagePng(string pdfPath, string outputPrefix, int page, int dpi, TimeSpan timeout)
    {
        string prefix = Path.GetFullPath(outputPrefix);
        string directory = Path.GetDirectoryName(prefix) ?? Directory.GetCurrentDirectory();
        string prefixName = Path.GetFileName(prefix);

        try
        {
            Directory.CreateDirectory(directory);
            foreach (string stale in Directory.GetFiles(directory, prefixName + "-*.png"))
                File.Delete(stale);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return Failed(["-png", pdfPath, prefix], failure, "the output directory could not be prepared");
        }

        string[] arguments =
        [
            "-png",
            "-r", Text(dpi),
            "-aa", "yes",
            "-aaVector", "yes",
            "-f", Text(page),
            "-l", Text(page),
            Path.GetFullPath(pdfPath),
            prefix,
        ];

        (int ExitCode, string Output, string Error, TimeSpan Duration, bool TimedOut) run =
            Run(Executable, arguments, timeout);

        string[] pages;
        try
        {
            pages = Directory.GetFiles(directory, prefixName + "-*.png")
                .OrderBy(PageNumber)
                .ThenBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            pages = [];
        }

        return new RasterRun(arguments, run.ExitCode, run.Output, run.Error, run.Duration, pages)
        {
            TimedOut = run.TimedOut,
        };
    }

    /// <summary>
    /// The font names a PDF embeds, subset prefix stripped, or null when
    /// <c>pdffonts</c> is not available to answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the cheapest detector there is for LibreOffice's silent font
    /// substitution. Asked for a family that does not exist, LibreOffice does not
    /// complain: it was verified swapping DejaVuSans in for a made-up family and
    /// exiting 0 with an empty stderr. Every signal a process-driving harness
    /// normally reads says the conversion went fine, and the only place the
    /// substitution is visible is the font list inside the PDF it wrote. So the
    /// list is read, and a check that expected a family can say which one turned
    /// up instead.
    /// </para>
    /// <para>
    /// The distinction between an empty list and null is load bearing and is the
    /// reason this returns a nullable. Empty means the tool ran and the PDF
    /// embeds nothing, which is a finding. Null means nothing was asked, so no
    /// conclusion is available. A run that failed or was killed returns null too:
    /// reporting it as empty would let a broken <c>pdffonts</c> masquerade as
    /// proof that a document embedded no fonts, and that proof would be used to
    /// fail a check about substitution.
    /// </para>
    /// <para>
    /// Names arrive with a seven character subset prefix - <c>BAAAAA+Calibri</c> -
    /// which the writer picks per subset and which therefore differs between two
    /// PDFs of the same document with the same fonts. It is stripped so that a
    /// comparison is about the typeface rather than about the tag.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string>? EmbeddedFonts(string pdfPath, TimeSpan timeout)
    {
        if (FontLister is null)
            return null;

        (int ExitCode, string Output, string Error, TimeSpan Duration, bool TimedOut) run =
            Run(FontLister, [Path.GetFullPath(pdfPath)], timeout);

        if (run.TimedOut || run.ExitCode != 0)
            return null;

        // A fixed width table behind two header lines: a column heading row and
        // a row of dashes. The first whitespace delimited column is the font
        // name, which is a PostScript name and so cannot itself contain a space.
        string[] lines = run.Output.Split('\n');
        if (lines.Length <= 2)
            return [];

        var fonts = new List<string>();
        foreach (string line in lines.Skip(2))
        {
            string[] columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length == 0)
                continue;

            fonts.Add(WithoutSubsetPrefix(columns[0]));
        }

        return fonts;
    }

    /// <summary>
    /// The page number off the tail of a rendered file name, or
    /// <see cref="int.MaxValue"/> when there isn't one so that a stray file sorts
    /// last instead of silently taking page 1's place in the comparison.
    /// </summary>
    private static int PageNumber(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        int dash = name.LastIndexOf('-');
        if (dash < 0 || dash == name.Length - 1)
            return int.MaxValue;

        return int.TryParse(name.AsSpan(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int page)
            ? page
            : int.MaxValue;
    }

    /// <summary>
    /// Drops a leading <c>XXXXXX+</c> subset tag - six capitals and a plus - and
    /// leaves anything else alone. The shape is checked rather than the length
    /// so that a real font name which happens to contain a plus keeps it.
    /// </summary>
    private static string WithoutSubsetPrefix(string name)
    {
        if (name.Length <= 7 || name[6] != '+')
            return name;

        for (int index = 0; index < 6; index++)
        {
            if (name[index] is < 'A' or > 'Z')
                return name;
        }

        return name[7..];
    }

    /// <summary>
    /// Runs a candidate with <c>-v</c> and returns its version line when the
    /// output mentions one of <paramref name="markers"/>, otherwise null.
    /// </summary>
    /// <remarks>
    /// stdout and stderr are both read and both searched, because poppler prints
    /// its version to stderr and exits 0. A probe that only read stdout would
    /// find nothing and report every working poppler as missing.
    /// </remarks>
    private static string? ProbeVersion(string executable, params string[] markers)
    {
        (int ExitCode, string Output, string Error, TimeSpan Duration, bool TimedOut) run =
            Run(executable, ["-v"], TimeSpan.FromSeconds(10));

        // The exit code is checked before the text, and that order is the whole
        // point. When the binary is absent, Run reports the launch failure by
        // putting the message - which names the executable it could not start -
        // on the error stream. Searching that text for the marker "pdftoppm"
        // then finds it, and a missing rasteriser probes as a present one. The
        // run has to have succeeded before its output is evidence of anything.
        if (run.TimedOut || run.ExitCode != 0)
            return null;

        string text = run.Error + "\n" + run.Output;
        foreach (string marker in markers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return FirstLine(text);
        }

        return null;
    }

    /// <summary>
    /// The <c>pdffonts</c> that belongs to a given <c>pdftoppm</c>: the one in
    /// the same directory first, then whatever PATH offers.
    /// </summary>
    /// <remarks>
    /// The sibling is preferred because the two binaries ship together and a
    /// mixed pair is a poppler nobody built. The PATH fallback exists for the
    /// ordinary case where <c>pdftoppm</c> was itself found on PATH and has no
    /// directory to be a sibling in.
    /// </remarks>
    private static string? FindFontLister(string pdftoppm)
    {
        string? directory = Path.GetDirectoryName(pdftoppm);
        if (!string.IsNullOrEmpty(directory))
        {
            string sibling = Path.Combine(directory, "pdffonts" + Path.GetExtension(pdftoppm));
            if (File.Exists(sibling))
                return sibling;
        }

        return ProbeVersion("pdffonts", "pdffonts", "poppler") is null ? null : "pdffonts";
    }

    /// <summary>
    /// Runs a child process to completion, or kills it at the timeout. It never
    /// throws: a tool that is not there, a tool that hangs and a tool that fails
    /// are three results this suite has to be able to report, and only the last
    /// of them arrives as an exit code on its own.
    /// </summary>
    private static (int ExitCode, string Output, string Error, TimeSpan Duration, bool TimedOut) Run(
        string executable, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        // poppler writes UTF-8 whatever the console code page is, and a Windows
        // runner's default is not UTF-8. Reading a font name as anything else
        // would turn a non-Latin family into a difference this suite then
        // reports as a substitution that never happened.
        start.StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        start.StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        var clock = Stopwatch.StartNew();

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Win32Exception failure)
        {
            return (-1, string.Empty, executable + " could not be started: " + failure.Message, clock.Elapsed, false);
        }
        catch (InvalidOperationException failure)
        {
            return (-1, string.Empty, executable + " could not be started: " + failure.Message, clock.Elapsed, false);
        }

        if (process is null)
            return (-1, string.Empty, executable + " could not be started.", clock.Elapsed, false);

        using (process)
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();

            int milliseconds = (int)Math.Clamp(timeout.TotalMilliseconds, 1d, int.MaxValue);
            bool exited;
            try
            {
                exited = process.WaitForExit(milliseconds);
            }
            catch (SystemException)
            {
                exited = false;
            }

            if (!exited)
            {
                // A hang is a result, not a crash of the harness. The whole tree
                // goes, because poppler on some platforms is a shim in front of
                // the real binary and killing only the shim leaves a process
                // still writing into the output directory this suite is about to
                // read.
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // It exited between the timeout and the kill.
                }
                catch (AggregateException)
                {
                    // Kill(entireProcessTree: true) walks the children and reports every
                    // failure it met as one aggregate. A child that exited while the
                    // walk was running is the ordinary case and is not worth failing a
                    // run over - the process being killed is already gone or going, and
                    // the caller's answer is the same either way.
                }
                catch (Win32Exception)
                {
                    // It could not be killed, and there is nothing further this
                    // method can honestly do about that. The report says it
                    // timed out, which is the part the reader needs.
                }

                clock.Stop();
                return (-1, Drain(output),
                    executable + " did not exit within " + Text((int)timeout.TotalSeconds) +
                    " seconds and was killed. " + Drain(error), clock.Elapsed, true);
            }

            clock.Stop();
            return (process.ExitCode, Drain(output), Drain(error), clock.Elapsed, false);
        }
    }

    /// <summary>
    /// Whatever a redirected stream managed to produce. A reader that has not
    /// finished shortly after the process is gone is abandoned rather than waited
    /// on forever, because output is evidence for the report and the report is
    /// worth more on time than complete.
    /// </summary>
    private static string Drain(Task<string> reader)
    {
        try
        {
            return reader.Wait(TimeSpan.FromSeconds(5)) ? reader.Result : string.Empty;
        }
        catch (AggregateException)
        {
            return string.Empty;
        }
    }

    private static string FirstLine(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length > 0)
                return trimmed;
        }

        return text.Trim();
    }

    private static RasterRun Failed(IReadOnlyList<string> arguments, Exception failure, string what) =>
        new(arguments, -1, string.Empty, what + ": " + failure.Message, TimeSpan.Zero, []);

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
