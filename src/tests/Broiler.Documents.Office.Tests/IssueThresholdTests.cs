using System.Text.Json;
using Broiler.Documents.Corpus;
using Broiler.Documents.Office;

namespace Broiler.Documents.Office.Tests;

public sealed class IssueThresholdTests
{
    private static readonly ExtrasSample Sample = new("sample.odt", ".odt", "odt", "sample.odt", null);
    private static readonly RunHeader Header = new("tool", "LibreOffice", "poppler", null, "test", 144, true);

    private static RenderRow Render() => new(Sample.Id, "odt", 1, 1, 1,
        new RenderBands("match", "near", "close", "close", "light"),
        new RenderObserved(10, 0.98, 0.001, 0.03), BaselineState.SuspectedDefect, null, "", null, null);

    private static ExtrasDifference Difference(params CheckResult[] checks) => new(
        Sample, false, 0, null, false, null, Render(), null, 500, "descriptive differences", checks);

    [Fact]
    public void Passing_Threshold_Verdicts_Are_Not_Ranked_Despite_Nonbest_Bands()
    {
        var difference = Difference(CheckResult.Pass("pixel", "pixel/sample.odt"), CheckResult.Pass("text", "text/sample.odt"));
        Assert.False(Bands.IsBest(difference.Render!.Bands));
        Assert.False(difference.HasDifference);
        Assert.Empty(ExtrasConformance.TopDifferences([difference]));
        string markdown = ExtrasConformance.FormatIssueMarkdown("source", Header, [Sample], [difference]);
        Assert.Contains("**Documents With Differences** | 0 |", markdown);
        Assert.Contains("**Top Major Differences Reported** | 0 ", markdown);
    }

    [Theory]
    [InlineData("pixel")]
    [InlineData("text")]
    [InlineData("read")]
    public void An_Actual_Failure_Is_Ranked(string group)
    {
        var difference = Difference(CheckResult.Fail(group, group + "/sample.odt", "failed"));
        Assert.True(difference.HasDifference);
        Assert.Same(difference, Assert.Single(ExtrasConformance.TopDifferences([difference])));
    }

    [Fact]
    public void A_Render_Failure_Without_Measurements_Is_Still_Ranked()
    {
        var difference = Difference(CheckResult.Fail("pixel", "pixel/sample.odt", "renderer failed")) with { Render = null };
        Assert.True(difference.HasDifference);
    }

    [Fact]
    public void Skipped_Axes_And_Informational_Diagnostics_Do_Not_Create_Failures()
    {
        var difference = Difference(CheckResult.Pass("text", "text/sample.odt"), CheckResult.Skip("pixel", "pixel/sample.odt", "no rasterizer"))
            with { Semantic = new SemanticReading(Sample.Id, "odt", "text", [], ["informational"]) };
        Assert.False(difference.HasDifference);
    }

    [Fact]
    public void A_Document_Refused_By_Both_Remains_Excluded()
    {
        var difference = Difference(CheckResult.Fail("read", "read/sample.odt", "encrypted"))
            with { BroilerReadFailed = true, RefusedByBoth = true };
        Assert.False(difference.HasDifference);
        Assert.Empty(ExtrasConformance.TopDifferences([difference]));
    }

    [Fact]
    public void Crash_Inputs_Remain_Separate_From_Conformance_Ranking()
    {
        var difference = Difference(CheckResult.Fail("robust", "robust/sample.odt", "crash"))
            with { BroilerReadFailed = true, CrashRegressionFamily = "fuzzer" };
        Assert.Empty(ExtrasConformance.TopDifferences([difference]));
        Assert.Same(difference, Assert.Single(ExtrasConformance.CrashRegressionFailures([difference])));
    }

    [Fact]
    public void A_Failed_Verdict_Is_Not_Recomputed_From_The_Reported_Worst_Page()
    {
        // Another page can have the lowest similarity even though the report
        // chose this page using the separate composite severity score.
        var difference = Difference(CheckResult.Fail("pixel", "pixel/sample.odt", "another page below threshold"));
        Assert.True(RenderingThreshold.Similarity(difference.Render!.Observed) > 80);
        Assert.True(difference.HasDifference);
    }

    [Fact]
    public void Reports_Record_A_Custom_Threshold()
    {
        string markdown = ExtrasConformance.FormatIssueMarkdown("source", Header, [], [], threshold: 92.5);
        Assert.Contains("**Rendering Threshold** | 92.5% |", markdown);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            OfficeReporting.WriteJson(path, [], [], [], Header, threshold: 92.5);
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(92.5, json.RootElement.GetProperty("threshold").GetDouble());
        }
        finally { File.Delete(path); }
    }
}
