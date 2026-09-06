using System;
using System.Collections.Generic;

namespace Broiler.Documents.Corpus;

internal enum CheckOutcome
{
    Passed,
    Failed,

    /// <summary>
    /// Not run, and the report says why. A skip is never silent: the count is
    /// printed next to the pass and fail counts, and every skipped check names
    /// its reason, because a suite that quietly stopped running half of itself
    /// reads as a suite that passed.
    /// </summary>
    Skipped,
}

/// <summary>One named check and what it produced.</summary>
/// <param name="Group">The family the check belongs to, for the summary table.</param>
/// <param name="Name">A stable identifier, which is what <c>--only</c> matches against.</param>
/// <param name="Detail">On a failure, what was expected and what happened.</param>
/// <param name="Command">The tool invocation to re-run by hand, when there was one.</param>
internal sealed record CheckResult(
    string Group,
    string Name,
    CheckOutcome Outcome,
    string? Detail = null,
    string? Command = null,
    TimeSpan Duration = default)
{
    public static CheckResult Pass(string group, string name, TimeSpan duration = default) =>
        new(group, name, CheckOutcome.Passed, Duration: duration);

    public static CheckResult Fail(string group, string name, string detail, string? command = null) =>
        new(group, name, CheckOutcome.Failed, detail, command);

    public static CheckResult Skip(string group, string name, string reason) =>
        new(group, name, CheckOutcome.Skipped, reason);

    /// <summary>
    /// Asserts an exit code, and says which one it got when it is wrong. The
    /// message names both because the exit codes in docs/cli.md carry meaning
    /// individually - a 3 where a 2 was expected is a different bug from a 70.
    /// </summary>
    public static CheckResult ExpectExit(string group, string name, int expected, ToolRun run)
    {
        if (run.ExitCode == expected)
            return Pass(group, name, run.Duration);

        string detail = "expected exit " + expected + " but the tool exited " + run.ExitCode + ".";
        if (run.ExitCode == 70)
            detail += " Exit 70 is documented as always a defect in the tool.";
        if (!string.IsNullOrWhiteSpace(run.StandardError))
            detail += "\n      stderr: " + Trim(run.StandardError);

        return Fail(group, name, detail, run.CommandLine());
    }

    private static string Trim(string text)
    {
        text = text.Trim().ReplaceLineEndings("\n      ");
        return text.Length <= 600 ? text : text[..600] + " ...";
    }
}

/// <summary>Everything one run produced, in the order the checks were defined.</summary>
internal sealed class RunLog
{
    private readonly List<CheckResult> _results = [];
    private readonly object _gate = new();

    public void Add(CheckResult result)
    {
        lock (_gate)
            _results.Add(result);
    }

    public void AddRange(IEnumerable<CheckResult> results)
    {
        lock (_gate)
            _results.AddRange(results);
    }

    public IReadOnlyList<CheckResult> Results
    {
        get
        {
            lock (_gate)
                return _results.ToArray();
        }
    }
}
