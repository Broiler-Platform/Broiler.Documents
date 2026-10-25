using Broiler.Documents.Corpus;
using Broiler.Documents.Office;

namespace Broiler.Documents.Office.Tests;

public sealed class TriageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "office-triage-tests", Guid.NewGuid().ToString("N"));
    private static readonly RunHeader Header = new("tool", "LibreOffice test", "poppler test", "test-fonts", "linux", 72, true);

    public TriageTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ExtrasDifference Difference(string id = "sample.odt", bool failed = false, int worstPage = 1, string[]? diagnostics = null)
    {
        string path = Path.Combine(_root, id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "stable input bytes");
        var sample = new ExtrasSample(id, ".odt", "odt", path, "https://example.test/" + id);
        var render = new RenderRow(id, "odt", 2, 2, worstPage,
            new RenderBands("match", "near", "close", "close", "light"),
            new RenderObserved(2, 0.98, 0.001, 0.03), BaselineState.SuspectedDefect, null, "", null, null);
        return new ExtrasDifference(sample, false, 0, null, false,
            new SemanticReading(id, "odt", "text", [], diagnostics ?? []), render, null, 500, "summary",
            [failed ? CheckResult.Fail("pixel", "pixel/" + id, "below threshold") : CheckResult.Pass("pixel", "pixel/" + id)]);
    }

    private static TriageReport Report(params ExtrasDifference[] differences) =>
        ExtrasTriage.Create(differences, Header, "all", 80, 40, TimeSpan.FromSeconds(30));

    [Fact]
    public void Selection_Without_Source_Uses_Default_Extras_Instead_Of_Seed_Corpus()
    {
        Assert.True(Program.Options.Parse(["--sample-list", "cases.txt"]).IsExtrasRun);
        Assert.True(Program.Options.Parse(["--triage-dir", "output"]).IsExtrasRun);
        Assert.False(Program.Options.Parse([]).IsExtrasRun);
        Assert.False(Program.Options.Parse(["--seed-corpus"]).IsExtrasRun);
    }

    [Fact]
    public void Focused_Selection_Cannot_Overwrite_Global_Ranking_Issue()
    {
        Assert.Throws<ArgumentException>(() => Program.Options.Parse(["--sample-list", "cases.txt", "--create-github-issue"]));
    }

    [Fact]
    public void Observations_And_Diagnostics_Do_Not_Turn_Passing_Thresholds_Into_Failures()
    {
        var report = Report(Difference(diagnostics: ["odt.list.indent", "odt.list.indent"]));
        var sample = Assert.Single(report.Cases);
        Assert.Equal("passed", sample.Outcome);
        Assert.Equal(64, sample.Sha256.Length);
        Assert.Single(sample.Diagnostics);
        Assert.Contains(report.Clusters, c => c.Observation == "visual-observation-within-threshold");
        Assert.Contains(report.Clusters, c => c.Observation == "diagnostic:odt.list.indent");
    }

    [Fact]
    public void Replay_Artifacts_Include_All_Controls_But_Only_Actually_Failing_Ranked_Cases()
    {
        var good = Difference("good.odt");
        var bad = Difference("bad.odt", failed: true);
        string output = Path.Combine(_root, "triage");
        ExtrasTriage.Write(output, [good, bad], Header, "all", 80, 40, TimeSpan.FromSeconds(30), 30, null);
        Assert.Equal(new[] { "bad.odt" }, File.ReadAllLines(Path.Combine(output, "top-samples.txt")));
        Assert.Equal(new[] { "bad.odt", "good.odt" }, File.ReadAllLines(Path.Combine(output, "all-samples.txt")));
        Assert.Equal(2, ExtrasTriage.Load(Path.Combine(output, "triage.json")).Cases.Length);
        Assert.Contains("not root-cause classifications", File.ReadAllText(Path.Combine(output, "triage.md")));
    }

    [Fact]
    public void Comparable_Failures_Becoming_Passes_Are_Check_Fixes()
    {
        var before = Report(Difference(failed: true));
        var after = Report(Difference());
        var change = Assert.Single(ExtrasTriage.Compare(before, after).Changes);
        Assert.Equal("fixed-checks", change.Status);
        Assert.Single(change.FixedChecks);
        Assert.True(change.Comparable);
    }

    [Fact]
    public void Changing_Input_Bytes_Does_Not_Claim_A_Fix()
    {
        var difference = Difference(failed: true);
        var before = Report(difference);
        File.AppendAllText(difference.Sample.LocalPath, " changed");
        var after = Report(difference with { Checks = [CheckResult.Pass("pixel", "pixel/sample.odt")] });
        var change = Assert.Single(ExtrasTriage.Compare(before, after).Changes);
        Assert.Equal("input-changed", change.Status);
        Assert.False(change.Comparable);
        Assert.Empty(change.FixedChecks);
    }

    [Fact]
    public void Different_Threshold_Or_Reference_Environment_Prevents_Regression_Claims()
    {
        var before = Report(Difference());
        var after = Report(Difference(failed: true));
        after = after with { Environment = after.Environment with { Dpi = 144, Threshold = 90 } };
        var comparison = ExtrasTriage.Compare(before, after);
        Assert.False(comparison.EnvironmentMatches);
        Assert.Contains("dpi", comparison.ChangedEnvironmentFields);
        Assert.Empty(Assert.Single(comparison.Changes).RegressedChecks);
    }

    [Fact]
    public void Changed_Worst_Page_Is_Visible_And_Its_Metrics_Are_Not_Compared()
    {
        var change = Assert.Single(ExtrasTriage.Compare(Report(Difference()), Report(Difference(worstPage: 2))).Changes);
        Assert.True(change.WorstPageChanged);
        Assert.False(change.MetricsComparable);
        Assert.Equal(1, change.BeforeRender!.WorstPage);
        Assert.Equal(2, change.AfterRender!.WorstPage);
    }

    [Fact]
    public void Unselected_And_Previously_Skipped_Checks_Are_Not_Claimed_As_Fixes()
    {
        var skipped = Difference() with { Checks = [CheckResult.Skip("pixel", "pixel/sample.odt", "no native reference")] };
        var before = Report(skipped, Difference("not-run.odt", failed: true));
        var comparison = ExtrasTriage.Compare(before, Report(Difference()));
        Assert.Equal(new[] { "not-run.odt" }, comparison.NotRun);
        var change = Assert.Single(comparison.Changes);
        Assert.Equal("coverage-changed", change.Status);
        Assert.Empty(change.FixedChecks);
    }

    [Fact]
    public void Refused_And_Crash_Inputs_Remain_Separate_Policy_Groups()
    {
        var before = Report(Difference(failed: true) with { RefusedByBoth = true });
        var change = Assert.Single(ExtrasTriage.Compare(before, Report(Difference())).Changes);
        Assert.Equal("separate-policy-group", change.Status);
        Assert.Empty(change.FixedChecks);
    }

    [Fact]
    public void Unsupported_Report_Schemas_Are_Rejected()
    {
        string path = Path.Combine(_root, "bad.json");
        File.WriteAllText(path, "{\"schemaVersion\":99}");
        Assert.Throws<InvalidDataException>(() => ExtrasTriage.Load(path));
    }

    [Theory]
    [InlineData("../escape.odt")]
    [InlineData("/absolute.odt")]
    [InlineData("C:/absolute.odt")]
    [InlineData("a\\b.odt")]
    [InlineData("a/./b.odt")]
    [InlineData("file.exe")]
    public void Sample_Lists_Reject_Nonportable_Or_Unsafe_Ids(string id) =>
        Assert.Throws<InvalidDataException>(() => ExtrasSelection.Validate([id]));

    [Fact]
    public void Sample_Selection_Is_Exact_And_Missing_Ids_Fail_Loudly()
    {
        Difference("a.odt"); Difference("a-extra.odt");
        var selected = ExtrasSelection.Validate(["a.odt"]);
        Assert.Equal("a.odt", Assert.Single(ExtrasConformance.DiscoverFromDirectory(_root, null, null, null, selected)).Id);
        Assert.Throws<InvalidDataException>(() => ExtrasConformance.DiscoverFromDirectory(_root, null, null, null,
            ExtrasSelection.Validate(["missing.odt"])));
    }

    [Fact]
    public void Comments_And_Duplicates_Are_Supported_But_Empty_Selection_Is_An_Error()
    {
        string path = Path.Combine(_root, "selection.txt");
        File.WriteAllText(path, "# comment\na.odt\n\na.odt\n");
        Assert.Single(ExtrasSelection.Load(path));
        File.WriteAllText(path, "# comment\n");
        Assert.Throws<InvalidDataException>(() => ExtrasSelection.Load(path));
    }

    [Theory]
    [InlineData("--only", "x")]
    [InlineData("--max-files", "1")]
    [InlineData("--seed-corpus", "")]
    public void Ambiguous_Or_Seed_Selection_Options_Are_Rejected(string option, string value)
    {
        string[] args = value.Length == 0 ? ["--sample-list", "cases.txt", option] : ["--sample-list", "cases.txt", option, value];
        Assert.Throws<ArgumentException>(() => Program.Options.Parse(args));
    }
}
