using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace Broiler.Documents.Cli.Infrastructure;

/// <summary>
/// Everything a command writes to, and the mode it writes in. One object so a
/// command never reaches for <see cref="Console"/> directly and every command is
/// therefore runnable in-process from a test.
/// </summary>
/// <remarks>
/// Four output channels, kept apart on purpose:
/// <list type="bullet">
/// <item><see cref="WriteOut"/> is the command's payload - the converted text, the
/// dump, the projection. It is what a pipe is for, so it is never decorated and
/// never suppressed.</item>
/// <item><see cref="Report"/> is the human-readable summary. <c>--quiet</c> and
/// <c>--json</c> both silence it.</item>
/// <item><see cref="Detail"/> is per-item noise that only <c>--verbose</c> wants.</item>
/// <item><see cref="Warn"/> and <see cref="Fail"/> go to stderr unconditionally, so a
/// harness that captures only stdout still sees nothing but its payload and a
/// harness that captures only stderr still sees every problem.</item>
/// </list>
/// </remarks>
public sealed class CommandContext
{
    private readonly List<string> _capturedReport = new();

    public CommandContext(CommandLine line, TextWriter output, TextWriter error)
    {
        Line = line ?? throw new ArgumentNullException(nameof(line));
        Out = output ?? throw new ArgumentNullException(nameof(output));
        Error = error ?? throw new ArgumentNullException(nameof(error));
        Result = new JsonObject();
    }

    public CommandLine Line { get; }

    public TextWriter Out { get; }

    public TextWriter Error { get; }

    /// <summary>The object <c>--json</c> prints. Commands fill it in whatever mode they run in.</summary>
    public JsonObject Result { get; }

    public bool Json => Line.Json;

    public bool Quiet => Line.Quiet;

    public bool Verbose => Line.Verbose;

    /// <summary>The command's payload. Always written, never decorated.</summary>
    public void WriteOut(string text)
    {
        if (!Json)
            Out.Write(text);
    }

    /// <summary>The command's payload, one line.</summary>
    public void WriteOutLine(string text = "")
    {
        if (!Json)
            Out.WriteLine(text);
    }

    /// <summary>A line of the human-readable summary.</summary>
    public void Report(string text = "")
    {
        _capturedReport.Add(text);
        if (!Json && !Quiet)
            Out.WriteLine(text);
    }

    /// <summary>A line only <c>--verbose</c> asked for.</summary>
    public void Detail(string text)
    {
        if (Verbose)
            Report(text);
        else
            _capturedReport.Add(text);
    }

    /// <summary>A problem that did not stop the command.</summary>
    public void Warn(string text) => Error.WriteLine("warning: " + text);

    /// <summary>A problem that did stop it.</summary>
    public void Fail(string text) => Error.WriteLine("error: " + text);

    /// <summary>The report lines this run produced, whether or not they were printed.</summary>
    public IReadOnlyList<string> CapturedReport => _capturedReport;
}
