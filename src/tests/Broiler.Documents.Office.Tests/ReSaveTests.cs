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
    [InlineData(0, true, true, "model-equal")]
    [InlineData(5, false, true, "model-difference")]
    [InlineData(70, true, true, "roundtrip-failure")]
    [InlineData(-1, true, true, "roundtrip-failure")]
    [InlineData(0, true, false, "invalid-report")]
    [InlineData(0, false, true, "invalid-report")]
    public void Roundtrip_Verdicts_Use_The_Model_And_Validate_Output(int exit, bool equal, bool exists, string category)
    {
        string source = Path.Combine(_directory, "input.md"), target = Path.Combine(_directory, "output.md");
        File.WriteAllText(source, "source bytes");
        if (exists) File.WriteAllText(target, "different serialization");
        string json = JsonSerializer.Serialize(new
        {
            sourceFormat = "Markdown",
            results = new[] { new { format = "Markdown", equal, writeStatus = "success", readStatus = "success", keptAt = target,
                comparison = new { equal, plainTextEqual = equal, formatCodesEqual = false, differenceCount = equal ? 0 : 1,
                    differences = new[] { "paragraph 1 lost formatting" } } } }
        });
        var run = new ToolRun(["roundtrip", source], exit, json, "conversion diagnostic", TimeSpan.Zero);
        var result = ReSaveConformance.EvaluateRoundtrip(new ExtrasSample("input.md", ".md", "md", source, null), "Markdown", null, run, render: false);
        Assert.Equal(category, result.Category);
        Assert.Equal(category == "model-equal" ? CheckOutcome.Passed : CheckOutcome.Failed, result.Check.Outcome);
        if (category == "model-equal") Assert.False(result.ByteIdentical);
        if (category == "model-difference") Assert.Contains("lost formatting", result.Check.Detail);
        if (result.Check.Outcome == CheckOutcome.Failed) Assert.NotNull(result.Check.Command);
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("{}")]
    [InlineData("{\"results\":[]}")]
    public void Invalid_Roundtrip_Reports_Fail(string json)
    {
        var result = ReSaveConformance.EvaluateRoundtrip(new ExtrasSample("input.md", ".md", "md", "unused", null),
            "Markdown", null, new ToolRun(["roundtrip"], 0, json, "", TimeSpan.Zero));
        Assert.Equal("invalid-report", result.Category);
        Assert.Equal(CheckOutcome.Failed, result.Check.Outcome);
    }

    [Theory]
    [InlineData("writeStatus")]
    [InlineData("readStatus")]
    public void Rejected_Writes_And_Unreadable_Output_Remain_Failures(string field)
    {
        string json = "{\"sourceFormat\":\"ODT\",\"results\":[{\"format\":\"ODT\",\"" + field + "\":\"rejected\"}]}";
        var result = ReSaveConformance.EvaluateRoundtrip(new ExtrasSample("input.odt", ".odt", "odt", "unused", null),
            "ODT", null, new ToolRun(["roundtrip"], 5, json, "", TimeSpan.Zero));
        Assert.Equal("roundtrip-failure", result.Category);
        Assert.Equal(CheckOutcome.Failed, result.Check.Outcome);
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
            "--failed-documents", Path.Combine(_directory, "empty.txt"), "--report", json, "--threshold", "100"]);
        int exit = await ReSaveConformance.RunAsync(options, BroilerDocTool.Locate(root, null),
            Path.Combine(_directory, "workspace"), root);
        Assert.Equal(0, exit);
        using var report = JsonDocument.Parse(File.ReadAllText(json));
        Assert.Empty(report.RootElement.GetProperty("checks").EnumerateArray());
        Assert.Equal(100, report.RootElement.GetProperty("threshold").GetDouble());
        Assert.Equal(100, report.RootElement.GetProperty("renderThreshold").GetDouble());
        Assert.Equal(144, report.RootElement.GetProperty("dpi").GetInt32());
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
        Assert.Contains("normalized round-trip fidelity", File.ReadAllText(markdown));
        Assert.NotEqual(FailedDocuments.DefaultPath(root), ReSaveConformance.DefaultFailedPath(root));
    }
    [Fact]
    public async Task Content_Detection_Overrides_A_Misleading_Extension_And_Uses_Canonical_Output()
    {
        string root = RepositoryRoot();
        BroilerDocTool tool = BroilerDocTool.Locate(root, null);
        string input = Path.Combine(_directory, "source.md"), odt = Path.Combine(_directory, "misnamed.docx");
        File.WriteAllText(input, "hello world\n");
        ToolRun write = await tool.RunAsync(["convert", input, "--to", "odt", "--out", odt, "--quiet"]);
        Assert.Equal(0, write.ExitCode);
        var result = await ReSaveConformance.EvaluateAsync(new ExtrasSample("misnamed.docx", ".docx", "docx", odt, null),
            Path.Combine(_directory, "saved"), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ODT" },
            ReSaveRejections.Load(Path.Combine(root, "tests", "office", "resave-known-rejections.json")), args => tool.RunAsync(args));
        Assert.Equal("ODT", result.Format);
        Assert.Equal("model-equal", result.Category);
        Assert.EndsWith(".odt", result.Roundtrip!.Value.GetProperty("results")[0].GetProperty("keptAt").GetString());
    }

    [Theory]
    [InlineData(3, "read-failure")]
    [InlineData(70, "read-failure")]
    [InlineData(-1, "read-failure")]
    [InlineData(0, "unsupported-write")]
    public async Task Unexpected_Read_Rejections_Fail_And_ReadOnly_Targets_Are_Skipped(int exit, string category)
    {
        var info = new ToolRun(["info"], exit, "{\"format\":\"PDF\"}", "read diagnostic", TimeSpan.Zero);
        var result = await ReSaveConformance.EvaluateAsync(new ExtrasSample("input.pdf", ".pdf", "pdf", "unused", null),
            _directory, new HashSet<string>(), ReSaveRejections.Load(Path.Combine(RepositoryRoot(), "tests", "office", "resave-known-rejections.json")),
            _ => Task.FromResult(info));
        Assert.Equal(category, result.Category);
        Assert.Equal(exit == 0 ? CheckOutcome.Skipped : CheckOutcome.Failed, result.Check.Outcome);
    }

    [Fact]
    public void Known_Rejections_Require_Exact_Bytes_And_Exit_3()
    {
        string file = Path.Combine(_directory, "encrypted.odt"), policy = Path.Combine(_directory, "known.json");
        File.WriteAllText(file, "encrypted fixture");
        string hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
        File.WriteAllText(policy, JsonSerializer.Serialize(new { schemaVersion = 1,
            samples = new[] { new { id = "encrypted.odt", sha256 = hash, reason = "known encrypted input", expectedExitCode = 3 } } }));
        var known = ReSaveRejections.Load(policy);
        var sample = new ExtrasSample("encrypted.odt", ".odt", "odt", file, null);
        Assert.Equal("known encrypted input", known.ReasonFor(sample, 3));
        Assert.Null(known.ReasonFor(sample, 70));
        Assert.Null(known.ReasonFor(sample, -1));
        File.AppendAllText(file, "changed");
        Assert.Null(known.ReasonFor(sample, 3));
    }

    [Fact]
    public void Summary_Groups_Read_Failures_And_Skips_Before_Excerpts_Can_Crowd_Them_Out()
    {
        var readings = Enumerable.Range(0, 100).Select(i => new ReSaveReading($"sample{i}.docx", "model-difference",
            CheckResult.Fail("resave", $"resave/sample{i}.docx", new string('x', 4000)))).ToList();
        readings.Add(new("encrypted.odt", "known-rejection", CheckResult.Skip("resave", "resave/encrypted.odt", "encrypted")));
        readings.Add(new("broken.odt", "read-failure", CheckResult.Fail("resave", "resave/broken.odt", "unexpected read failure")));
        string markdown = ReSaveConformance.Markdown(readings);
        Assert.Contains("model-difference: 100", markdown);
        Assert.Contains("known-rejection: 1", markdown);
        Assert.Contains("unexpected read failure", markdown);
        Assert.True(markdown.Length < 6000);
    }

    [Fact]
    public async Task Crash_Regression_Rejection_Is_A_Robustness_Result()
    {
        string source = Path.Combine(_directory, "corpus");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "ofz64109-1.fodt"), "<broken xml");
        string report = Path.Combine(_directory, "robust.json");
        var options = Program.Options.Parse(["--resave", "--extras-dir", source,
            "--only", "ofz64109-1", "--report", report]);
        int exit = await ReSaveConformance.RunAsync(options, BroilerDocTool.Locate(RepositoryRoot(), null),
            Path.Combine(_directory, "workspace"), RepositoryRoot());
        Assert.Equal(0, exit);
        using var json = JsonDocument.Parse(File.ReadAllText(report));
        Assert.Equal("normalized-roundtrip-render-v1", json.RootElement.GetProperty("comparisonPolicy").GetString());
        Assert.Equal("crash-regression", json.RootElement.GetProperty("resaves")[0].GetProperty("category").GetString());
    }

    [Fact]
    public void Rendering_Is_Default_And_Can_Be_Disabled()
    {
        Assert.True(Program.Options.Parse(["--resave"]).ReSaveRender);
        Assert.Equal(80, Program.Options.Parse(["--resave"]).Threshold);
        Assert.False(Program.Options.Parse(["--resave", "--no-resave-render"]).ReSaveRender);
        Assert.True(Program.Options.Parse(["--resave", "--no-resave-render", "--resave-render"]).ReSaveRender);
        Assert.Equal(100, Program.Options.Parse(["--resave", "--threshold", "100"]).Threshold);
        Assert.Throws<ArgumentException>(() => Program.Options.Parse(["--resave", "--threshold", "101"]));
    }

    [Theory]
    [InlineData(80, 20, false, false, 1, true)]
    [InlineData(80, 21, false, false, 1, false)]
    [InlineData(100, 0, false, false, 1, true)]
    [InlineData(100, 1, false, false, 1, false)]
    [InlineData(0, 100, false, false, 1, true)]
    [InlineData(0, 0, true, false, 1, false)]
    [InlineData(0, 0, false, true, 1, false)]
    [InlineData(0, 0, false, false, 2, false)]
    public void Images_Require_Threshold_Page_Count_Size_And_Complete_Rendering(
        double threshold, int different, bool sizeDiffers, bool truncated, int afterCount, bool passes)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new {
            beforePageCount = 1, afterPageCount = afterCount, beforeTruncated = truncated, afterTruncated = false,
            pages = new[] { new { page = 1, comparedPixels = 100, differingPixels = different, sizeDiffers } }
        }));
        var result = ReSaveConformance.EvaluateRender(json.RootElement, threshold);
        Assert.Equal(passes, result.Equal);
        Assert.Equal(100 - different, result.Similarity!.Value, 6);
    }

    [Fact]
    public void Image_Threshold_Applies_To_Each_Page_Rather_Than_The_Average()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new {
            beforePageCount = 2, afterPageCount = 2, beforeTruncated = false, afterTruncated = false,
            pages = new[] {
                new { page = 1, comparedPixels = 100, differingPixels = 0, sizeDiffers = false },
                new { page = 2, comparedPixels = 100, differingPixels = 21, sizeDiffers = false }
            }
        }));
        var result = ReSaveConformance.EvaluateRender(json.RootElement, 80);
        Assert.False(result.Equal);
        Assert.Equal(79, result.Similarity!.Value, 6);
        Assert.Contains("worst page 2", result.Detail);
    }

    [Fact]
    public void Image_Difference_Fails_Even_When_The_Model_Matches()
    {
        string source = Path.Combine(_directory, "input.md"), target = Path.Combine(_directory, "output.md");
        File.WriteAllText(source, "hello"); File.WriteAllText(target, "hello");
        string json = JsonSerializer.Serialize(new {
            sourceFormat = "Markdown", results = new[] { new {
                format = "Markdown", equal = false, writeStatus = "success", readStatus = "success", keptAt = target,
                comparison = new { equal = true, plainTextEqual = true, differenceCount = 0, differences = Array.Empty<string>() },
                render = new { beforePageCount = 1, afterPageCount = 1, beforeTruncated = false, afterTruncated = false,
                    pages = new[] { new { page = 1, comparedPixels = 100, differingPixels = 21, sizeDiffers = false } } }
            } }
        });
        var row = ReSaveConformance.EvaluateRoundtrip(new ExtrasSample("input.md", ".md", "md", source, null),
            "Markdown", null, new ToolRun(["roundtrip", "--render"], 5, json, "", TimeSpan.Zero));
        Assert.Equal("render-difference", row.Category);
        Assert.Equal(CheckOutcome.Failed, row.Check.Outcome);
        Assert.Equal(79, row.MinimumPageSimilarity!.Value, 6);
        Assert.Contains("79.00%", row.Check.Detail);
    }

    private static string RepositoryRoot()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Broiler.Documents.slnx"))) root = Path.GetDirectoryName(root)!;
        return root;
    }

}
