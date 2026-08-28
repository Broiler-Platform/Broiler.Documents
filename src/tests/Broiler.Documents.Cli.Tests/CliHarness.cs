using System.Text.Json;
using System.Text.Json.Nodes;

namespace Broiler.Documents.Cli.Tests;

/// <summary>What one in-process run of the tool produced.</summary>
public sealed record CliRun(int ExitCode, string Output, string Error)
{
    /// <summary>The <c>--json</c> payload, parsed. Fails the test when the run did not emit one.</summary>
    public JsonObject Json()
    {
        JsonNode? node = JsonNode.Parse(Output);
        Assert.NotNull(node);
        return Assert.IsType<JsonObject>(node);
    }

    public bool OutputContains(string value) =>
        Output.Contains(value, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Runs the tool in-process against a temporary directory.
/// </summary>
/// <remarks>
/// In-process rather than by spawning <c>broilerdoc.exe</c>: a failing assertion
/// then breaks in the code that caused it, and the tests do not depend on the
/// build having produced an executable at a path they have to guess.
/// </remarks>
public sealed class CliHarness : IDisposable
{
    private bool _disposed;

    public CliHarness()
    {
        Directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "broilerdoc-tests",
            Guid.NewGuid().ToString("N"));

        System.IO.Directory.CreateDirectory(Directory);
    }

    /// <summary>A scratch directory, removed when the test finishes.</summary>
    public string Directory { get; }

    /// <summary>A path inside the scratch directory.</summary>
    public string Path(string name) => System.IO.Path.Combine(Directory, name);

    public CliRun Run(params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exitCode = Program.Run(arguments, output, error);
        return new CliRun(exitCode, output.ToString(), error.ToString());
    }

    /// <summary>Runs and asserts the exit code, reporting both streams when it does not match.</summary>
    public CliRun RunExpecting(int expected, params string[] arguments)
    {
        CliRun run = Run(arguments);
        Assert.True(
            run.ExitCode == expected,
            $"expected exit {expected} but got {run.ExitCode}\n" +
            $"args: {string.Join(' ', arguments)}\n" +
            $"stdout:\n{run.Output}\nstderr:\n{run.Error}");
        return run;
    }

    /// <summary>Writes a document with the tool itself, and returns its path.</summary>
    public string MakeDocument(string name, string text, params string[] operations)
    {
        string path = Path(name);
        var arguments = new List<string> { "new", "--out", path, "--text", text, "--quiet" };
        foreach (string operation in operations)
        {
            arguments.Add("--op");
            arguments.Add(operation);
        }

        RunExpecting(ExitCode.Ok, arguments.ToArray());
        return path;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary directory is not worth failing a passing test
            // over; the operating system reclaims it.
        }
    }
}
