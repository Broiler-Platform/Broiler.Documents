using System;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.Documents.Pdf.Tests.Fuzzing;

/// <summary>
/// The fuzz harness itself, and the bounded campaign a pull request runs
/// (PDF roadmap §6.6).
/// </summary>
/// <remarks>
/// The campaign here is deliberately short — seconds, not the half hour §6.6
/// asks of a nightly job. What a pull request needs from it is that the harness
/// works and that nothing obvious regressed; the long campaigns are the nightly
/// job's business.
/// </remarks>
public sealed class FuzzCampaignTests
{
    private const int PullRequestIterations = 2_000;

    private static readonly TimeSpan PullRequestBudget = TimeSpan.FromSeconds(20);

    [Fact]
    public void Every_Target_Survives_A_Bounded_Campaign()
    {
        var failures = new List<FuzzFailure>();
        foreach ((string name, Action<ReadOnlySpan<byte>> target) in FuzzTargets.All)
        {
            failures.AddRange(
                new FuzzCampaign(name, target)
                    .Run(seed: 0x5EED_0001, PullRequestIterations, PullRequestBudget));
        }

        // The message is the record, so a CI log is enough to reproduce without
        // the artifact: seed, hash, profile and harness version are all in it.
        Assert.True(
            failures.Count == 0,
            "Fuzz campaign found unhandled failures:\n  " +
            string.Join("\n  ", failures.Select(static f => f.ToString())));
    }

    [Fact]
    public void A_Seed_Reproduces_Its_Input_Exactly()
    {
        // The property the whole record rests on. A failure that cannot be
        // replayed is a rumour.
        byte[] first = FuzzCampaign.Reproduce(0xC0FFEE);
        byte[] second = FuzzCampaign.Reproduce(0xC0FFEE);

        Assert.Equal(first, second);
        Assert.NotEmpty(first);
    }

    [Fact]
    public void Different_Seeds_Produce_Different_Inputs()
    {
        Assert.NotEqual(FuzzCampaign.Reproduce(1), FuzzCampaign.Reproduce(2));
    }

    [Fact]
    public void A_Campaign_Stops_At_Its_Time_Budget()
    {
        // What makes one harness serve both a pull request and a nightly job: the
        // iteration count is an upper bound, and the clock is the real limit.
        var campaign = new FuzzCampaign("pdf.read", FuzzTargets.All["pdf.read"]);

        DateTime started = DateTime.UtcNow;
        campaign.Run(seed: 7, iterations: int.MaxValue, budget: TimeSpan.FromMilliseconds(250));

        Assert.True(
            DateTime.UtcNow - started < TimeSpan.FromSeconds(10),
            "The campaign ran well past its budget.");
    }

    [Fact]
    public void A_Failure_Record_Carries_What_A_Triager_Needs()
    {
        // Built from a target that always throws something unexpected, so the
        // record's shape is asserted without needing a real defect.
        var campaign = new FuzzCampaign("always.fails", static _ => throw new InvalidOperationException("x"));

        FuzzFailure failure = Assert.Single(
            campaign.Run(seed: 42, iterations: 1, budget: TimeSpan.FromSeconds(5)));

        Assert.Equal("always.fails", failure.Target);
        Assert.Equal(nameof(InvalidOperationException), failure.FailureClass);
        Assert.Equal(FuzzCampaign.HarnessVersion, failure.HarnessVersion);
        Assert.Equal(FuzzTargets.LimitProfileName, failure.LimitProfile);
        Assert.Equal(64, failure.InputSha256.Length);
        Assert.True(failure.InputLength > 0);
        Assert.Contains("generated-in-code", failure.CorpusRights, StringComparison.Ordinal);

        // And it reproduces from what it recorded.
        Assert.Equal(failure.InputLength, FuzzCampaign.Reproduce(failure.Seed).Length);
    }

    [Fact]
    public void Cancellation_Is_Not_A_Failure()
    {
        // The only exception these targets are allowed to raise. A campaign that
        // recorded a cancelled run as a defect would fill the log with the
        // harness stopping itself.
        var campaign = new FuzzCampaign(
            "always.cancels",
            static _ => throw new OperationCanceledException());

        Assert.Empty(campaign.Run(seed: 42, iterations: 8, budget: TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void A_Reader_That_Throws_Is_A_Failure_Even_When_The_Exception_Looks_Reasonable()
    {
        // ADR 0003/0004 say a read reports malformed input through its result
        // rather than by throwing. So FormatException from a read is not a
        // refusal, it is the reader breaking its own contract — and an
        // expected-exception list that admitted it would have hidden exactly
        // that. This test is here because the first version of the harness had
        // that list, and passed every input while proving nothing.
        var campaign = new FuzzCampaign("throws.formatexception", static _ => throw new FormatException("x"));

        FuzzFailure failure = Assert.Single(
            campaign.Run(seed: 42, iterations: 1, budget: TimeSpan.FromSeconds(5)));

        Assert.Equal(nameof(FormatException), failure.FailureClass);
    }
}
