using System.Text.Json.Nodes;

namespace Broiler.Documents.Cli.Tests;

public sealed class RoundtripRenderTests : IDisposable
{
    private readonly CliHarness _cli = new();
    public void Dispose() => _cli.Dispose();

    [Fact]
    public void Roundtrip_Renders_Both_Sides_And_Keeps_Images_When_Asked()
    {
        string source = _cli.Path("input.md");
        File.WriteAllText(source, "Hello world\n");
        var json = CliHarness.RunExpecting(0, "roundtrip", source, "--via", "markdown", "--render",
            "--dpi", "24", "--render-out", _cli.Path("{via}-{side}-{page}.png"), "--json", "--quiet").Json();
        JsonNode render = json["results"]![0]!["render"]!;
        Assert.False(render["beforeTruncated"]!.GetValue<bool>());
        Assert.False(render["afterTruncated"]!.GetValue<bool>());
        Assert.Equal(0, render["pages"]![0]!["differingPixels"]!.GetValue<int>());
        Assert.True(File.Exists(render["beforeImages"]![0]!.GetValue<string>()));
        Assert.True(File.Exists(render["afterImages"]![0]!.GetValue<string>()));
        Assert.NotEqual(render["beforeImages"]![0]!.GetValue<string>(), render["afterImages"]![0]!.GetValue<string>());
    }

    [Fact]
    public void Ratio_Threshold_Permits_Nonzero_Pixel_Differences()
    {
        string source = _cli.Path("red.html");
        File.WriteAllText(source, "<p style='color:#ff0000'>Colored text loses its color in Markdown.</p>");
        // The model still fails on the lost colour; the image verdict must
        // independently honor the explicitly permitted pixel difference.
        var allowed = CliHarness.RunExpecting(5, "roundtrip", source, "--via", "markdown", "--render",
            "--dpi", "24", "--max-different-ratio", "0.2", "--json", "--quiet").Json();
        JsonNode page = allowed["results"]![0]!["render"]!["pages"]![0]!;
        Assert.True(page["differingPixels"]!.GetValue<int>() > 0);
        Assert.True(page["passes"]!.GetValue<bool>());
        var exact = CliHarness.RunExpecting(5, "roundtrip", source, "--via", "markdown", "--render",
            "--dpi", "24", "--max-different-ratio", "0", "--json", "--quiet").Json();
        Assert.False(exact["results"]![0]!["render"]!["pages"]![0]!["passes"]!.GetValue<bool>());
    }

    [Fact]
    public void Truncated_Identical_Renderings_Do_Not_Pass()
    {
        string source = _cli.Path("input.html");
        File.WriteAllText(source, "<p>first</p><p style='page-break-before:always'>second</p>");
        var json = CliHarness.RunExpecting(5, "roundtrip", source, "--via", "html", "--render",
            "--dpi", "24", "--max-pages", "1", "--json", "--quiet").Json();
        JsonNode result = json["results"]![0]!;
        Assert.True(result["comparison"]!["equal"]!.GetValue<bool>());
        Assert.True(result["render"]!["beforeTruncated"]!.GetValue<bool>());
        Assert.False(result["equal"]!.GetValue<bool>());
    }
}
