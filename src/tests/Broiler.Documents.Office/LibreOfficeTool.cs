using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broiler.Documents.Office;

/// <summary>What one headless conversion produced.</summary>
/// <param name="Arguments">
/// The arguments as given, so a failure message can carry a command line rather
/// than a description of one.
/// </param>
/// <param name="FilterUsed">
/// The filter LibreOffice says it used, taken from its own stdout. Null when it
/// printed no such line, which is itself the interesting case: it means nothing
/// was converted, whatever the exit code claims.
/// </param>
/// <param name="OutputPath">
/// The converted file, or null when the file the filter implies is not on disk.
/// </param>
internal sealed record LibreOfficeRun(
    IReadOnlyList<string> Arguments,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    string? FilterUsed,
    string? OutputPath)
{
    /// <summary>
    /// The child was still running when the timeout expired and was killed. The
    /// exit code is then -1, which is not a code LibreOffice produces, so a
    /// reader who checks the code alone cannot mistake a hang for a verdict.
    /// </summary>
    public bool TimedOut { get; init; }

    /// <summary>
    /// True when the run failed with nothing at all on either stream, which is
    /// the measured signature of profile contention rather than of a bad
    /// document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two headless jobs pointed at one user profile lose exactly one of
    /// themselves per round, and the loser exits 1 having printed not one byte
    /// to stdout and not one to stderr. That is indistinguishable, from the
    /// outside, from a codec that emitted nothing - which is exactly the bug
    /// this suite exists to catch, so the harness has to be able to tell the
    /// two apart or it will report its own contention as a product defect.
    /// </para>
    /// <para>
    /// The fix is a profile per concurrent job, not a retry; this flag exists so
    /// that a suite which somehow shares one anyway says so in the report
    /// instead of blaming the document. <c>--nolockcheck</c> was tried and does
    /// not change the behaviour.
    /// </para>
    /// </remarks>
    public bool LooksLikeProfileContention =>
        !TimedOut
        && ExitCode != 0
        && string.IsNullOrWhiteSpace(StandardOutput)
        && string.IsNullOrWhiteSpace(StandardError);

    /// <summary>The command line, quoted enough to be re-run by hand.</summary>
    /// <remarks>
    /// The name at the front is the Windows one deliberately, and it is the
    /// <c>.com</c>. On Windows <c>soffice.exe</c> is a GUI-subsystem program: it
    /// detaches at once, prints nothing, and its exit code is the wrapper's
    /// rather than the conversion's, so a message that invited a reader to paste
    /// <c>soffice.exe</c> would be inviting them to reproduce nothing. The
    /// resolved absolute path is left out because it is machine-specific noise
    /// in a line whose value is the argument order; on Linux the same line runs
    /// as <c>soffice</c>.
    /// </remarks>
    public string CommandLine()
    {
        var text = new StringBuilder("soffice.com");
        foreach (string argument in Arguments)
        {
            text.Append(' ');
            text.Append(argument.Contains(' ', StringComparison.Ordinal) ? '"' + argument + '"' : argument);
        }

        return text.ToString();
    }
}

/// <summary>
/// LibreOffice in headless mode, driven as a child process.
/// </summary>
/// <remarks>
/// <para>
/// This is the second opinion. The corpus suite can only ever check that the
/// codecs agree with themselves; a round trip through a writer and back through
/// the matching reader is consistent whether or not it is correct. LibreOffice
/// is an independent implementation that has read Office formats for two
/// decades, so what it makes of a file this repository wrote is evidence about
/// the file rather than about the codec pair that produced it.
/// </para>
/// <para>
/// It is also a program, not a library, and every bad habit of a program is
/// therefore this class's problem: an exit code that lies, a profile directory
/// that two jobs fight over, an environment variable the host leaked in, a
/// child process that outlives the wrapper that spawned it. Each is handled
/// below, and each comment says which of them was measured rather than feared.
/// </para>
/// <para>
/// Nothing here throws because LibreOffice is absent. A machine without it is
/// the normal case for a contributor and a common one in CI, and the answer to
/// that is a skip with a reason printed next to the pass and fail counts - not
/// an exception that fails the suite, and emphatically not a silent pass.
/// </para>
/// </remarks>
internal sealed class LibreOfficeTool
{
    /// <summary>
    /// The tail LibreOffice prints after a successful conversion, as in
    /// <c>convert x.odt as a Writer document -&gt; y.docx using filter : Office
    /// Open XML Text</c>.
    /// </summary>
    /// <remarks>
    /// Parsing this is the only reliable confirmation of what LibreOffice
    /// actually did, and the reason is a verified trap: handed bytes it cannot
    /// parse, LibreOffice does not fail. It falls back to the plain-text
    /// importer, converts the garbage happily, and exits 0. A check that
    /// asserted on the exit code would pass on a file that was never understood,
    /// so the exit code is treated as a necessary condition and this line as the
    /// sufficient one.
    /// </remarks>
    private const string FilterMarker = "using filter : ";

    /// <summary>
    /// How long <c>--version</c> gets during discovery. A cold first start was
    /// measured at 2.5-3.3 seconds and a warm one at about 1.3, so thirty
    /// seconds is room for a slow disk without making an absent tool feel like a
    /// hung suite.
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long the output streams get to finish after the child is gone. They
    /// are already at end-of-file by then in every ordinary case; the bound
    /// exists so that a wedged pipe cannot turn a reported failure into a hang.
    /// </summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(10);

    private LibreOfficeTool(string executable, string version)
    {
        Executable = executable;
        Version = version;
    }

    /// <summary>The binary that answered <c>--version</c>.</summary>
    public string Executable { get; }

    /// <summary>
    /// The banner line, such as <c>LibreOffice 26.2.5.2 &lt;buildid&gt;</c>, or
    /// <c>unknown</c> when a binary the caller named by hand did not print one.
    /// The report prints this because filter behaviour moves between versions -
    /// a bare <c>docx</c> resolves differently across them - and a comparison
    /// against an unnamed version is a comparison nobody can reproduce.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Finds a working LibreOffice, or returns null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Null when LibreOffice is not installed. A missing tool is a skip, never
    /// an exception: the suite that uses this has other checks to run, and a
    /// contributor who has not installed a 400 MB office suite has not done
    /// anything wrong.
    /// </para>
    /// <para>
    /// Every candidate is probed by actually running <c>--version</c>, because a
    /// name on PATH is not a working binary. This repository has already been
    /// bitten by exactly that: a <c>convert</c> on PATH that turned out to be
    /// Borland's compiler front end rather than ImageMagick, found by a check
    /// that had asked only whether the name resolved. So a discovered candidate
    /// has to say the words "LibreOffice" before it is believed. A binary the
    /// caller named explicitly, or named through <c>BROILER_SOFFICE</c>, is
    /// given the benefit of the doubt and recorded as version <c>unknown</c>,
    /// on the grounds that someone who spells out a path has answered the
    /// question the probe was asking.
    /// </para>
    /// <para>
    /// The order is explicit path, then environment, then PATH, then the usual
    /// install roots - most deliberate first, so a machine with two copies uses
    /// the one somebody chose rather than the one that sorts first.
    /// </para>
    /// </remarks>
    public static LibreOfficeTool? Locate(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return Probe(explicitPath, requireBanner: false);

        string? fromEnvironment = Environment.GetEnvironmentVariable("BROILER_SOFFICE");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            LibreOfficeTool? named = Probe(fromEnvironment, requireBanner: false);
            if (named is not null)
                return named;
        }

        // On PATH, .com before the bare name and before anything the shell would
        // pick: on Windows the console front end is soffice.com and the thing
        // next to it called soffice.exe detaches without printing, which would
        // read here as a tool that produced no version and therefore no tool.
        foreach (string name in (string[])["soffice.com", "soffice"])
        {
            LibreOfficeTool? onPath = Probe(name, requireBanner: true);
            if (onPath is not null)
                return onPath;
        }

        foreach (string candidate in InstallRoots())
        {
            if (!File.Exists(candidate))
                continue;

            LibreOfficeTool? installed = Probe(candidate, requireBanner: true);
            if (installed is not null)
                return installed;
        }

        return null;
    }

    /// <summary>
    /// The places an installer puts LibreOffice, in the order they are worth
    /// trying. The Windows entries are derived from the Program Files folders
    /// rather than spelled as <c>C:\Program Files\...</c>, which is what they
    /// resolve to on a stock machine and is still right on one that moved them.
    /// </summary>
    private static IEnumerable<string> InstallRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (Environment.SpecialFolder folder in (Environment.SpecialFolder[])
                     [Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86])
            {
                string root = Environment.GetFolderPath(folder);
                if (!string.IsNullOrEmpty(root))
                    yield return Path.Combine(root, "LibreOffice", "program", "soffice.com");
            }

            yield break;
        }

        if (OperatingSystem.IsMacOS())
        {
            yield return "/Applications/LibreOffice.app/Contents/MacOS/soffice";
            yield break;
        }

        yield return "/usr/bin/soffice";
        yield return "/usr/lib/libreoffice/program/soffice";

        // /opt/libreoffice26.2/program/soffice and friends. Ordered so that a
        // machine with two side-by-side installs picks the same one every run;
        // an unstable choice here would show up much later as a filter that
        // changed behaviour for no reason anyone could see.
        if (Directory.Exists("/opt"))
        {
            IEnumerable<string> versions;
            try
            {
                versions = Directory.EnumerateDirectories("/opt", "libreoffice*").Order(StringComparer.Ordinal).ToList();
            }
            catch (IOException)
            {
                yield break;
            }
            catch (UnauthorizedAccessException)
            {
                yield break;
            }

            foreach (string directory in versions)
                yield return Path.Combine(directory, "program", "soffice");
        }
    }

    /// <summary>
    /// Runs <c>--version</c> and decides whether this candidate is the real
    /// thing. Any failure to start is an answer of "no", not an exception:
    /// probing a name that is not there is the ordinary path through this
    /// method, not an error in it.
    /// </summary>
    private static LibreOfficeTool? Probe(string executable, bool requireBanner)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        start.ArgumentList.Add("--version");
        Scrub(start);

        try
        {
            using var process = new Process { StartInfo = start };
            process.Start();

            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)ProbeTimeout.TotalMilliseconds))
            {
                Terminate(process);
                return null;
            }

            string banner = Drain(output).Trim();
            _ = Drain(error);

            if (process.ExitCode != 0)
                return null;

            string first = FirstLine(banner);
            bool looksRight = first.Contains("LibreOffice", StringComparison.OrdinalIgnoreCase);
            if (requireBanner && !looksRight)
                return null;

            return new LibreOfficeTool(executable, looksRight ? first : "unknown");
        }
        catch (Win32Exception)
        {
            // No such file, or not executable. That is the answer.
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Converts one file, once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="profileDirectory"/> must be unique to this job for as
    /// long as the job runs. It is a parameter rather than something this class
    /// invents so that the caller, who is the only one that knows how many jobs
    /// it is running at once, cannot accidentally share one - see
    /// <see cref="LibreOfficeRun.LooksLikeProfileContention"/> for what sharing
    /// looks like from the outside.
    /// </para>
    /// <para>
    /// <paramref name="filter"/> is spelled in full, as in
    /// <c>docx:Office Open XML Text</c>, because the bare target name works but
    /// resolves to different filters in different versions. Naming the filter is
    /// how a result stays comparable across the machines that produced it.
    /// </para>
    /// </remarks>
    public LibreOfficeRun Convert(
        string inputPath,
        string filter,
        string outputDirectory,
        string profileDirectory,
        TimeSpan timeout,
        string? inputFilter = null)
    {
        string input = Path.GetFullPath(inputPath);
        string output = Path.GetFullPath(outputDirectory);
        string profile = Path.GetFullPath(profileDirectory);

        Directory.CreateDirectory(output);
        Directory.CreateDirectory(profile);

        // new Uri(...).AbsoluteUri, never a hand-built string. It is the one
        // call that gets file:///C:/... right: the three slashes, the drive
        // letter, backslashes turned round, and spaces and other awkward
        // characters escaped the way the URI grammar wants rather than the way a
        // shell would. A hand-built "file://" + path is how the profile silently
        // ends up shared - LibreOffice does not reject a URI it cannot parse, it
        // quietly falls back to the default profile, at which point every
        // concurrent job is pointed at the same directory and one of them starts
        // losing with no output to say why.
        string profileUri = new Uri(profile).AbsoluteUri;

        var arguments = new List<string>
        {
            "-env:UserInstallation=" + profileUri,
            "--headless",
            "--norestore",
            "--invisible",
            "--nodefault",
        };

        if (!string.IsNullOrWhiteSpace(inputFilter))
        {
            // No quotes around the value. The documented invocation writes
            // --infilter="HTML (StarWriter)" because a shell would otherwise cut
            // the argument at the space; ArgumentList hands the argument to the
            // child whole, so quoting it here would make the quotes part of the
            // filter name.
            arguments.Add("--infilter=" + inputFilter);
        }

        arguments.Add("--convert-to");
        arguments.Add(filter);
        arguments.Add("--outdir");
        arguments.Add(output);
        arguments.Add(input);

        var start = new ProcessStartInfo
        {
            FileName = Executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = output,

            // LibreOffice writes UTF-8 whatever the console code page is, and a
            // Windows runner's default is not UTF-8. Reading the filter line as
            // anything else would corrupt exactly the non-Latin filenames this
            // repository keeps in its corpus on purpose.
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        Scrub(start);

        var clock = Stopwatch.StartNew();
        using var process = new Process { StartInfo = start };

        try
        {
            process.Start();
        }
        catch (Win32Exception failure)
        {
            // The binary answered --version during discovery and will not start
            // now: an antivirus quarantine, an uninstall mid-run, a permission
            // change. Rare, but it is still a result about the environment and
            // the caller can report it; throwing would lose the command line.
            clock.Stop();
            return new LibreOfficeRun(
                arguments, -1, string.Empty, failure.Message, clock.Elapsed, null, null);
        }

        // Both streams are drained on background tasks before anything waits on
        // the process. LibreOffice is chatty on stderr even when it succeeds,
        // and a child that fills a pipe nobody is reading blocks forever - which
        // would present as a timeout, in the one class whose job is to tell a
        // real hang from a self-inflicted one.
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();

        int limit = (int)Math.Clamp(timeout.TotalMilliseconds, 0, int.MaxValue);
        if (!process.WaitForExit(limit))
        {
            Terminate(process);
            clock.Stop();

            return new LibreOfficeRun(
                arguments,
                -1,
                Drain(standardOutput),
                Drain(standardError),
                clock.Elapsed,
                null,
                null)
            {
                TimedOut = true,
            };
        }

        clock.Stop();

        string stdout = Drain(standardOutput);
        string stderr = Drain(standardError);

        // stderr is not a failure signal here. A wholly successful run prints
        // "Could not find platform independent libraries <prefix>" on it, and a
        // harness that treated any stderr as an error would fail every check it
        // ran.
        string? filterUsed = ParseFilter(stdout);

        string produced = Path.Combine(
            output, Path.GetFileNameWithoutExtension(input) + "." + ExtensionFor(filter));

        return new LibreOfficeRun(
            arguments,
            process.ExitCode,
            stdout,
            stderr,
            clock.Elapsed,
            filterUsed,
            File.Exists(produced) ? produced : null);
    }

    /// <summary>
    /// The extension LibreOffice will give the output, from the leading token of
    /// a filter string - <c>docx</c> from <c>docx:Office Open XML Text</c>,
    /// <c>txt</c> from <c>txt:Text (encoded):UTF8</c>. Returned without a
    /// leading dot.
    /// </summary>
    /// <remarks>
    /// The verified filters are listed rather than folded into the fallback even
    /// though the fallback would return the same string for all of them. They
    /// are the set this suite has actually measured, and having them written
    /// down means the day one of them stops matching its own extension the fix
    /// is a line in this switch rather than an argument about whether the rule
    /// ever held. Anything else falls back to the token itself, because that
    /// token is LibreOffice's own name for the target format and it is the
    /// extension far more often than not.
    /// </remarks>
    public static string ExtensionFor(string filter)
    {
        string token = filter;
        int colon = filter.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
            token = filter[..colon];

        token = token.Trim().ToLowerInvariant();

        return token switch
        {
            "docx" => "docx",
            "odt" => "odt",
            "rtf" => "rtf",
            "html" => "html",
            "md" => "md",
            "txt" => "txt",
            "pdf" => "pdf",
            _ => token,
        };
    }

    /// <summary>
    /// The <c>--infilter</c> argument an input of this extension needs, or null
    /// when it needs none.
    /// </summary>
    /// <remarks>
    /// HTML is the whole reason this method exists, and the trap is a silent
    /// one. Converting from HTML to a Writer format without naming the importer
    /// does not fail: html to docx and html to rtf were both measured exiting
    /// with no error and producing no file whatsoever. With
    /// <c>--infilter="HTML (StarWriter)"</c> the same html to docx call produced
    /// a 5393-byte document. A suite that did not pass this would report the
    /// missing output as a codec that wrote an unreadable file, which is a
    /// conclusion about this repository drawn entirely from a missing argument
    /// in the harness.
    /// </remarks>
    public static string? InputFilterFor(string extension)
    {
        string normalised = extension.Trim().TrimStart('.').ToLowerInvariant();

        return normalised is "html" or "htm" ? "HTML (StarWriter)" : null;
    }

    /// <summary>
    /// Takes the filter name from the last <c>using filter : </c> in stdout.
    /// </summary>
    /// <remarks>
    /// The last, not the first: one invocation can be asked to convert several
    /// files and prints a line for each, and the line that matters to a caller
    /// that passed one input is the one about the file it passed. Trimmed
    /// because the name runs to end of line and the line ends differently on
    /// each platform.
    /// </remarks>
    private static string? ParseFilter(string standardOutput)
    {
        int marker = standardOutput.LastIndexOf(FilterMarker, StringComparison.Ordinal);
        if (marker < 0)
            return null;

        int from = marker + FilterMarker.Length;
        int end = standardOutput.IndexOfAny(['\r', '\n'], from);
        string name = (end < 0 ? standardOutput[from..] : standardOutput[from..end]).Trim();

        return name.Length == 0 ? null : name;
    }

    /// <summary>
    /// Removes the host's LibreOffice and Python settings from the child's
    /// environment and puts back the three this suite wants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every name in this list was verified to leak in from the host and be
    /// acted on. <c>URE_BOOTSTRAP</c> points a run at another installation's
    /// bootstrap; <c>PYTHONHOME</c> and <c>PYTHONPATH</c> reach the scripting
    /// provider that LibreOffice loads whether or not a document uses it; and
    /// anything named <c>SAL_*</c> or <c>OOO_*</c> is read directly by the
    /// office layer, which is why the prefixes are cleared wholesale rather than
    /// name by name - the set is open-ended and a suite cannot enumerate what
    /// the next version will read.
    /// </para>
    /// <para>
    /// What goes back is the smallest useful environment.
    /// <c>SAL_USE_VCLPLUGIN=svp</c> pins the headless backend so a machine with
    /// a display cannot pick a real one; <c>LC_ALL</c> and <c>LANG</c> pin the
    /// locale to C.UTF-8 so that neither the filter line this class parses nor
    /// any sorting inside the conversion depends on where the machine thinks it
    /// is. All three are set after the removal pass, or the SAL_ sweep would
    /// take the plugin setting straight back out again.
    /// </para>
    /// </remarks>
    private static void Scrub(ProcessStartInfo start)
    {
        List<string> doomed = start.Environment.Keys
            .Where(name =>
                name.Equals("PYTHONHOME", StringComparison.OrdinalIgnoreCase)
                || name.Equals("PYTHONPATH", StringComparison.OrdinalIgnoreCase)
                || name.Equals("URE_BOOTSTRAP", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("SAL_", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("OOO_", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (string name in doomed)
            start.Environment.Remove(name);

        start.Environment["SAL_USE_VCLPLUGIN"] = "svp";
        start.Environment["LC_ALL"] = "C.UTF-8";
        start.Environment["LANG"] = "C.UTF-8";
    }

    /// <summary>
    /// Kills the child and everything it started.
    /// </summary>
    /// <remarks>
    /// <c>entireProcessTree: true</c> is not defensive tidiness. The thing named
    /// on the command line is a launcher: it starts <c>soffice.bin</c> and that
    /// process does the work, so killing only the wrapper leaves the real office
    /// process running, holding the profile directory the next job is about to
    /// be given and outliving the suite that was supposed to own it. Failing to
    /// kill is swallowed, because a process that exited between the timeout and
    /// this call has already done what was being asked of it.
    /// </remarks>
    private static void Terminate(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Gone already, between the timeout expiring and this line.
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
            // Refused by the operating system. Nothing further to try, and the
            // caller is already reporting a timeout.
        }
    }

    /// <summary>
    /// What a stream produced, waiting only <see cref="DrainTimeout"/> for it.
    /// After a kill the pipe is normally at end-of-file within milliseconds; the
    /// bound is there so the one case where it is not cannot turn a run this
    /// class has already decided to report into a wait with no end.
    /// </summary>
    private static string Drain(Task<string> stream)
    {
        try
        {
            return stream.Wait(DrainTimeout) ? stream.Result : string.Empty;
        }
        catch (AggregateException)
        {
            // The read faulted on a pipe torn down under it. Empty is then the
            // truth about what could be recovered, and the caller has the exit
            // code and the command line to report alongside it.
            return string.Empty;
        }
    }

    /// <summary>The first non-empty line, trimmed, or the empty string.</summary>
    private static string FirstLine(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length > 0)
                return trimmed;
        }

        return string.Empty;
    }
}
