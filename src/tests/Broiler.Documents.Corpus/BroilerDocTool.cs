using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Documents.Corpus;

/// <summary>What one run of the tool produced.</summary>
/// <param name="Arguments">The arguments as given, for a failure message that can be pasted into a shell.</param>
internal sealed record ToolRun(
    IReadOnlyList<string> Arguments,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration)
{
    /// <summary>The <c>--json</c> payload, or null when the run did not emit one.</summary>
    public JsonElement? Json()
    {
        if (string.IsNullOrWhiteSpace(StandardOutput))
            return null;

        try
        {
            using var document = JsonDocument.Parse(StandardOutput);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The command line, quoted enough to be re-run by hand.</summary>
    public string CommandLine()
    {
        var text = new StringBuilder("broilerdoc");
        foreach (string argument in Arguments)
        {
            text.Append(' ');
            text.Append(argument.Contains(' ', StringComparison.Ordinal) ? '"' + argument + '"' : argument);
        }

        return text.ToString();
    }
}

/// <summary>
/// The tool under test, driven the way an automated caller would drive it.
/// </summary>
/// <remarks>
/// <para>
/// A child process rather than an in-process call, which is the whole reason
/// this suite exists alongside <c>Broiler.Documents.Cli.Tests</c>. Those tests
/// call <c>Program.Run</c> directly and are better at what they do: a failing
/// assertion breaks in the code that caused it. What they cannot see is the
/// part a caller depends on - that the build produced a runnable executable,
/// that the process exit code is the documented one rather than a return value
/// that happens to match it, and that stdout carries the JSON and nothing else.
/// </para>
/// <para>
/// Nothing here interprets a non-zero exit as a failure. Exit 5 and exit 6 are
/// verdicts the tool is supposed to reach, and a harness that treated them as
/// errors would be unable to test the commands whose job is to reach them.
/// </para>
/// </remarks>
internal sealed class BroilerDocTool
{
    private readonly string _executable;
    private readonly string? _dllArgument;

    private BroilerDocTool(string executable, string? dllArgument)
    {
        _executable = executable;
        _dllArgument = dllArgument;
    }

    /// <summary>How the tool is being invoked, for the report header.</summary>
    public string Description =>
        _dllArgument is null ? _executable : _executable + " " + _dllArgument;

    /// <summary>
    /// Finds the built tool.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the objection the in-process harness raises against spawning a
    /// process, and it deserves a real answer rather than a guessed path. The
    /// answer is: look where this runner's own build configuration says, then
    /// look at every configuration the CLI has been built in, and if neither
    /// finds it, say exactly where it looked.
    /// </para>
    /// <para>
    /// Both steps are needed because the solution and the project disagree about
    /// the word "configuration". <c>Broiler.Documents.slnx</c> maps the solution
    /// configuration <c>Release-Windows</c> to the project configuration
    /// <c>Release</c>, so a solution build lands in <c>bin/Release</c> while a
    /// direct <c>dotnet build -c Release-Windows</c> lands in
    /// <c>bin/Release-Windows</c>. A runner that only derived one of them would
    /// work locally and fail in CI, or the other way round.
    /// </para>
    /// </remarks>
    public static BroilerDocTool Locate(string repositoryRoot, string? explicitPath)
    {
        if (explicitPath is not null)
        {
            if (!File.Exists(explicitPath))
                throw new FileNotFoundException("The tool named by --tool is not there: " + explicitPath);

            return Wrap(explicitPath);
        }

        // AppContext.BaseDirectory is
        // src/tests/Broiler.Documents.Corpus/bin/<configuration>/<tfm>/.
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        string targetFramework = here.Name;
        string configuration = here.Parent?.Name ?? "Release";
        string bin = Path.Combine(repositoryRoot, "src", "Broiler.Documents.Cli", "bin");

        var looked = new List<string>();

        BroilerDocTool? found = In(Path.Combine(bin, configuration, targetFramework), looked);
        if (found is not null)
            return found;

        if (Directory.Exists(bin))
        {
            // Any other configuration, oldest first so the message names them in
            // a stable order. Taking a build from a different configuration is
            // worth a note rather than a refusal: it is the right tool from the
            // wrong pass, and the version command records which.
            foreach (string other in Directory.EnumerateDirectories(bin)
                         .SelectMany(directory => Directory.EnumerateDirectories(directory))
                         .Order(StringComparer.Ordinal))
            {
                found = In(other, looked);
                if (found is not null)
                    return found;
            }
        }

        throw new FileNotFoundException(
            "No built broilerdoc found. Looked in:\n  " + string.Join("\n  ", looked) +
            "\nBuild src/Broiler.Documents.Cli first, or point at one with --tool.");
    }

    /// <summary>
    /// The tool in one directory, if it is there and runnable.
    /// </summary>
    /// <remarks>
    /// The managed assembly has to be present, not just the apphost. A project
    /// reference copies a referenced executable's apphost into the referencing
    /// project's output without its assembly, so an apphost alone is a file that
    /// exists and cannot run - which would turn a missing build into a confusing
    /// runtime error rather than this method's clear one.
    /// </remarks>
    private static BroilerDocTool? In(string directory, List<string> looked)
    {
        looked.Add(directory);
        string library = Path.Combine(directory, "broilerdoc.dll");
        if (!File.Exists(library))
            return null;

        foreach (string name in new[] { "broilerdoc.exe", "broilerdoc" })
        {
            string candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
                return new BroilerDocTool(candidate, null);
        }

        return new BroilerDocTool("dotnet", library);
    }

    private static BroilerDocTool Wrap(string path) =>
        path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? new BroilerDocTool("dotnet", path)
            : new BroilerDocTool(path, null);

    public Task<ToolRun> RunAsync(params string[] arguments) =>
        RunAsync(arguments, standardInput: null);

    /// <summary>
    /// Runs the tool once. <paramref name="standardInput"/> is written to the
    /// child's stdin when given, which is how the <c>-</c> path is exercised.
    /// </summary>
    public async Task<ToolRun> RunAsync(IReadOnlyList<string> arguments, byte[]? standardInput)
    {
        var start = new ProcessStartInfo
        {
            FileName = _executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (_dllArgument is not null)
            start.ArgumentList.Add(_dllArgument);

        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        // The tool writes UTF-8 whatever the console code page is, and a Windows
        // runner's default is not UTF-8. Reading it as anything else turns the
        // corpus's own non-Latin samples into a difference this suite would then
        // report against the codecs, which is exactly the kind of false failure
        // a corpus runner exists to avoid producing.
        start.StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        start.StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        var clock = Stopwatch.StartNew();
        using var process = new Process { StartInfo = start };
        process.Start();

        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();

        if (standardInput is not null)
        {
            await using Stream input = process.StandardInput.BaseStream;
            await input.WriteAsync(standardInput).ConfigureAwait(false);
            await input.FlushAsync().ConfigureAwait(false);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A hang is a result, not a crash of the harness: kill it and let
            // the check report the exit code it never produced.
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It exited between the timeout and the kill.
            }

            return new ToolRun(arguments, -1, await output.ConfigureAwait(false),
                "The tool did not exit within five minutes and was killed.", clock.Elapsed);
        }

        clock.Stop();
        return new ToolRun(
            arguments,
            process.ExitCode,
            await output.ConfigureAwait(false),
            await error.ConfigureAwait(false),
            clock.Elapsed);
    }
}
