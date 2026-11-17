using System.Text.Json;
using Broiler.Documents.Corpus;
using Broiler.Documents.Office;

namespace Broiler.Documents.Office.Tests;

public sealed class ReSaveTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "office-resave-tests", Guid.NewGuid().ToString("N"));

    public ReSaveTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData(0, "same", "same", CheckOutcome.Passed)]
    [InlineData(0, "same", "diff", CheckOutcome.Failed)]
    [InlineData(0, "same", "longer", CheckOutcome.Failed)]
    [InlineData(0, "same", null, CheckOutcome.Failed)]
    [InlineData(70, "same", "same", CheckOutcome.Failed)]
    internal void Evaluation_Requires_Successful_Conversion_And_Identical_Output(
        int exit, string input, string? output, CheckOutcome expected)
    {
        string source = Path.Combine(_directory, "input.md");
        string target = Path.Combine(_directory, "output.md");
        File.WriteAllText(source, input);
        if (output is not null) File.WriteAllText(target, output);
        var run = new ToolRun(["convert", source, "--out", target], exit, "", "conversion diagnostic", TimeSpan.Zero);
        var result = ReSaveConformance.Evaluate(new ExtrasSample("input.md", ".md", "md", source, null), target, run);
        Assert.Equal(expected, result.Outcome);
        if (expected == CheckOutcome.Failed) Assert.NotNull(result.Command);
        if (exit != 0) Assert.Contains("conversion diagnostic", result.Detail);
    }

    [Theory]
    [InlineData("--seed-corpus")]
    [InlineData("--update-baseline")]
    [InlineData("--restamp")]
    [InlineData("--create-github-issue")]
    public void Conflicting_Modes_Are_Rejected(string option) =>
        Assert.Throws<ArgumentException>(() => Program.Options.Parse(["--resave", option]));

    [Fact]
    public async Task Empty_Incremental_Run_Writes_Zero_Check_Report_Without_Acquiring_Corpus()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Broiler.Documents.slnx"))) root = Path.GetDirectoryName(root)!;
        string json = Path.Combine(_directory, "empty.json");
        var options = Program.Options.Parse(["--resave", "--incremental", "--extras-dir", "missing-corpus",
            "--failed-documents", Path.Combine(_directory, "empty.txt"), "--report", json]);
        int exit = await ReSaveConformance.RunAsync(options, BroilerDocTool.Locate(root, null),
            Path.Combine(_directory, "workspace"), root);
        Assert.Equal(0, exit);
        using var report = JsonDocument.Parse(File.ReadAllText(json));
        Assert.Empty(report.RootElement.GetProperty("checks").EnumerateArray());
    }

    [Fact]
    public async Task Incremental_Selection_Reports_And_Preserves_Unevaluated_Failures()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Broiler.Documents.slnx"))) root = Path.GetDirectoryName(root)!;
        string source = Path.Combine(_directory, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "selected.md"), "hello\n");
        File.WriteAllText(Path.Combine(source, "unselected.md"), "hello\n");
        string tracked = Path.Combine(_directory, "failures.txt");
        FailedDocuments.Save(tracked, ["selected.md", "unselected.md"]);
        string json = Path.Combine(_directory, "report.json");
        string junit = Path.Combine(_directory, "report.xml");
        string markdown = Path.Combine(_directory, "report.md");
        var options = Program.Options.Parse(["--resave", "--incremental", "--only", "selected.md",
            "--max-files", "1", "--extras-dir", source, "--failed-documents", tracked,
            "--report", json, "--junit", junit, "--issue-out", markdown]);
        // The substring also matches unselected.md; the cap must select just the first ID.
        await ReSaveConformance.RunAsync(options, BroilerDocTool.Locate(root, null),
            Path.Combine(_directory, "workspace"), root);
        using var report = JsonDocument.Parse(File.ReadAllText(json));
        var checks = report.RootElement.GetProperty("checks");
        Assert.Equal(1, checks.GetArrayLength());
        Assert.Equal("resave/selected.md", checks[0].GetProperty("name").GetString());
        Assert.Contains("unselected.md", FailedDocuments.Load(tracked));
        Assert.Equal(checks[0].GetProperty("outcome").GetString() == "failed",
            FailedDocuments.Load(tracked).Contains("selected.md"));
        Assert.True(File.Exists(junit));
        Assert.Contains("strict byte identity", File.ReadAllText(markdown));
        Assert.NotEqual(FailedDocuments.DefaultPath(root), ReSaveConformance.DefaultFailedPath(root));
    }
}
