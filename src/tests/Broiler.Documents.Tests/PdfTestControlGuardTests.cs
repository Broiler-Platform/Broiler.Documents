using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Broiler.Documents.Tests;

/// <summary>
/// The §6.6 test controls: the tool manifest and the performance baseline.
/// </summary>
/// <remarks>
/// <para>
/// Both files start with no rows, exactly as the Phase 0 corpus manifest does,
/// and for the same reason: the control exists so that content has somewhere to
/// land under review, not because anything has been approved. These guards check
/// the shape and the claims, never the emptiness — a populated file is the
/// intended end state.
/// </para>
/// <para>
/// Governance guards, in the same spirit as <see cref="PdfClaimGuardTests"/>:
/// they check that the description and the artifact still match, not that either
/// is correct.
/// </para>
/// </remarks>
public sealed class PdfTestControlGuardTests
{
    private const string ToolManifest = "tests/pdf/tools/manifest.json";
    private const string ToolSchema = "tests/pdf/tools/manifest.schema.json";
    private const string Baseline = "tests/pdf/performance-baseline.json";
    private const string BaselineSchema = "tests/pdf/performance-baseline.schema.json";

    private static JsonDocument Load(string relativePath) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(PdfGuardRoots.Component, relativePath)));

    [Fact(Timeout = 600000)]
    public void Both_Controls_And_Their_Schemas_Parse()
    {
        foreach (string path in new[] { ToolManifest, ToolSchema, Baseline, BaselineSchema })
        {
            using JsonDocument document = Load(path);
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        }
    }

    [Fact(Timeout = 600000)]
    public void No_Tool_Is_Approved_Merely_By_Being_Written_Down()
    {
        // A row is evidence that somebody wrote the tool's details down. The
        // review state is the separate thing, and CI may not run a pending tool
        // in a required check.
        using JsonDocument manifest = Load(ToolManifest);
        foreach (JsonElement tool in manifest.RootElement.GetProperty("tools").EnumerateArray())
        {
            JsonElement review = tool.GetProperty("review");
            string state = review.GetProperty("state").GetString() ?? string.Empty;
            Assert.Contains(state, new[] { "pending", "approved", "rejected" });

            if (state != "approved")
                continue;

            // An approval names who gave it and when, or it is not one.
            Assert.False(
                string.IsNullOrWhiteSpace(review.GetProperty("reviewer").GetString()),
                "An approved tool names its reviewer.");
            Assert.False(
                string.IsNullOrWhiteSpace(review.GetProperty("decided").GetString()),
                "An approved tool records the date it was decided.");
        }
    }

    [Fact(Timeout = 600000)]
    public void No_Tool_Is_A_Product_Reference()
    {
        // §6.6's hard rule: independent tools run out of process in CI and stay
        // absent from product references, packages, applications and release
        // containers. Checked against the project files rather than against
        // intent, because intent does not survive a merge.
        using JsonDocument manifest = Load(ToolManifest);
        string[] names = manifest.RootElement.GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetProperty("identity").GetProperty("name").GetString() ?? string.Empty)
            .Where(name => name.Length > 0)
            .ToArray();

        if (names.Length == 0)
            return;

        foreach (string project in Directory.EnumerateFiles(
            Path.Combine(PdfGuardRoots.Component, "src"),
            "*.csproj",
            SearchOption.AllDirectories))
        {
            if (project.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            string text = File.ReadAllText(project);
            foreach (string name in names)
            {
                Assert.False(
                    text.Contains(name, StringComparison.OrdinalIgnoreCase),
                    $"{Path.GetFileName(project)} references the test tool {name}.");
            }
        }
    }

    [Fact(Timeout = 600000)]
    public void The_Baseline_Runner_Matches_The_Runners_CI_Actually_Uses()
    {
        // A threshold measured on a machine the project does not run is not
        // evidence about anything. This is the drift check: change the workflow's
        // runners and this says so.
        using JsonDocument baseline = Load(Baseline);
        string[] declared = baseline.RootElement.GetProperty("runner").GetProperty("images")
            .EnumerateArray()
            .Select(image => image.GetString() ?? string.Empty)
            .ToArray();

        string workflow = File.ReadAllText(
            Path.Combine(PdfGuardRoots.Component, ".github/workflows/ci.yml"));

        foreach (string image in declared)
        {
            Assert.True(
                workflow.Contains(image, StringComparison.Ordinal),
                $"The baseline pins {image}, which the CI workflow does not use.");
        }
    }

    [Fact(Timeout = 600000)]
    public void A_Scenario_Cannot_Pass_A_Gate_By_Being_Recorded()
    {
        // Recording a measurement is not passing one. An approved scenario has to
        // carry the budgets it is judged against, and the absolute caps are
        // required even where a wall-time budget is deliberately absent.
        using JsonDocument baseline = Load(Baseline);
        foreach (JsonElement scenario in baseline.RootElement.GetProperty("scenarios").EnumerateArray())
        {
            JsonElement budgets = scenario.GetProperty("budgets");
            Assert.True(budgets.GetProperty("maxPeakMemoryMib").GetInt32() > 0);
            Assert.True(budgets.GetProperty("maxWorkUnits").GetInt64() > 0);

            string state = scenario.GetProperty("approval").GetProperty("state").GetString() ?? string.Empty;
            Assert.Contains(state, new[] { "pending", "approved" });
        }
    }

    [Fact(Timeout = 600000)]
    public void The_Fuzz_Harness_Is_Wired_To_A_Nightly_Job()
    {
        // The pull-request campaign runs as an ordinary test; the long one needs
        // a job, and a job nobody wired up is a plan rather than a control.
        string workflow = Path.Combine(PdfGuardRoots.Component, ".github/workflows/nightly-fuzz.yml");
        Assert.True(File.Exists(workflow), "The nightly fuzz workflow is missing.");

        string text = File.ReadAllText(workflow);
        Assert.Contains("schedule:", text, StringComparison.Ordinal);
        Assert.Contains("BROILER_FUZZ_SEED", text, StringComparison.Ordinal);

        // The outer time/RSS supervisor §6.6 requires: the campaign bounds
        // itself, and this bounds it when it cannot.
        Assert.Contains("timeout-minutes:", text, StringComparison.Ordinal);
    }
}
