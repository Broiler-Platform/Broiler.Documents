using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Broiler.Documents.Pdf.Tests.Fuzzing;

/// <summary>
/// The long campaign the nightly workflow runs (PDF roadmap §6.6).
/// </summary>
/// <remarks>
/// <para>
/// The same harness as the pull-request campaign, given a much larger budget and
/// a seed that changes nightly. One harness rather than two, because two would
/// diverge and the nightly one is the one nobody watches.
/// </para>
/// <para>
/// Skipped unless the environment asks for it, so an ordinary test run does not
/// spend an hour here. That makes this a test that usually does not run, which is
/// a smell worth naming: it is here rather than in a console tool because it
/// needs the test project's references and because a failure should be reported
/// the way every other failure in this repository is.
/// </para>
/// </remarks>
public sealed class FuzzNightlyTests
{
    private const string SeedVariable = "BROILER_FUZZ_SEED";
    private const string MinutesVariable = "BROILER_FUZZ_MINUTES";

    [Fact]
    public void Every_Target_Survives_The_Nightly_Campaign()
    {
        string? seedText = Environment.GetEnvironmentVariable(SeedVariable);
        if (string.IsNullOrEmpty(seedText))
        {
            // The campaign was not asked for, so there is nothing to report. This
            // returns rather than skipping because the xunit version here has no
            // skip-at-runtime: a green result for a campaign that did not run
            // would be a lie in the log, which is why the workflow that does ask
            // for it names the variable in its own step.
            return;
        }

        ulong seed = ulong.TryParse(seedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsed)
            ? parsed
            : 1;

        int minutes =
            int.TryParse(
                Environment.GetEnvironmentVariable(MinutesVariable),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int requested) && requested > 0
                ? requested
                : 30;

        var budget = TimeSpan.FromMinutes(minutes);
        var failures = new List<FuzzFailure>();

        foreach ((string name, Action<ReadOnlySpan<byte>> target) in FuzzTargets.All)
        {
            // A distinct seed per target, so one target's inputs are not the
            // other's and a night covers both rather than the same bytes twice.
            ulong targetSeed = seed * 0x9E3779B1UL + (ulong)name.GetHashCode(StringComparison.Ordinal);
            failures.AddRange(new FuzzCampaign(name, target).Run(targetSeed, int.MaxValue, budget));
        }

        Assert.True(
            failures.Count == 0,
            $"Nightly campaign (seed {seed}, {minutes} min/target) found unhandled failures:\n  " +
            string.Join("\n  ", failures.Select(static f => f.ToString())));
    }
}
