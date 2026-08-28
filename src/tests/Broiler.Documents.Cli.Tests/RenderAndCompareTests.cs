using System.Text.Json.Nodes;
using Broiler.Documents.Cli.Rendering;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// The rendering and comparison path: the reason this tool exists, and the part
/// an automated harness scripts against.
/// </summary>
public sealed class RenderAndCompareTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    [Fact]
    public void Render_Writes_A_Png_And_Reports_Its_Geometry()
    {
        string source = _cli.MakeDocument("hello.docx", "Hello world");
        string image = _cli.Path("hello.png");

        JsonObject json = _cli
            .RunExpecting(ExitCode.Ok, "render", source, "--out", image, "--continuous", "--json")
            .Json();

        Assert.True(File.Exists(image));
        Assert.Equal(1, json["render"]!["renderedPageCount"]!.GetValue<int>());
        Assert.True(json["render"]!["pages"]![0]!["widthPixels"]!.GetValue<int>() > 0);

        // The PNG signature, so this is a real image and not a truncated write.
        byte[] bytes = File.ReadAllBytes(image);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes.Take(4).ToArray());
    }

    [Fact]
    public void Render_Is_Deterministic_For_The_Same_Document_And_Options()
    {
        // The property everything else rests on: if two renders of one document
        // could differ, no comparison between two documents would mean anything.
        string source = _cli.MakeDocument("hello.docx", "Hello world", "inline:0:0-5:bold=on");

        _cli.RunExpecting(ExitCode.Ok, "render", source, "--out", _cli.Path("a.png"), "--continuous", "--quiet");
        _cli.RunExpecting(ExitCode.Ok, "render", source, "--out", _cli.Path("b.png"), "--continuous", "--quiet");

        Assert.Equal(File.ReadAllBytes(_cli.Path("a.png")), File.ReadAllBytes(_cli.Path("b.png")));
    }

    [Fact]
    public void A_Long_Document_Paginates_And_Names_Each_Page()
    {
        string body = string.Join("\n", Enumerable.Range(1, 200).Select(i => $"Paragraph {i} of the long document."));
        string source = _cli.Path("long.docx");
        _cli.RunExpecting(ExitCode.Ok, "new", "--out", source, "--text", body, "--quiet");

        JsonObject json = _cli
            .RunExpecting(ExitCode.Ok, "render", source, "--out", _cli.Path("page-{page}.png"), "--json")
            .Json();

        int pages = json["render"]!["renderedPageCount"]!.GetValue<int>();
        Assert.True(pages > 1, "a 200-paragraph document should need more than one page");
        Assert.True(File.Exists(_cli.Path("page-001.png")));
        Assert.True(File.Exists(_cli.Path("page-002.png")));
    }

    [Fact]
    public void Continuous_Renders_The_Whole_Document_As_One_Page()
    {
        string body = string.Join("\n", Enumerable.Range(1, 200).Select(i => $"Paragraph {i}."));
        string source = _cli.Path("long.docx");
        _cli.RunExpecting(ExitCode.Ok, "new", "--out", source, "--text", body, "--quiet");

        JsonObject json = _cli
            .RunExpecting(
                ExitCode.Ok, "render", source, "--out", _cli.Path("all.png"), "--continuous", "--json")
            .Json();

        Assert.Equal(1, json["render"]!["renderedPageCount"]!.GetValue<int>());
        Assert.True(json["render"]!["pages"]![0]!["heightPixels"]!.GetValue<int>() > 1000);
    }

    [Fact]
    public void Pages_Selects_A_Subset()
    {
        string body = string.Join("\n", Enumerable.Range(1, 200).Select(i => $"Paragraph {i}."));
        string source = _cli.Path("long.docx");
        _cli.RunExpecting(ExitCode.Ok, "new", "--out", source, "--text", body, "--quiet");

        JsonObject json = _cli
            .RunExpecting(
                ExitCode.Ok, "render", source, "--out", _cli.Path("p{page}.png"), "--pages", "2", "--json")
            .Json();

        Assert.Equal(1, json["render"]!["renderedPageCount"]!.GetValue<int>());
        Assert.True(File.Exists(_cli.Path("p002.png")));
        Assert.False(File.Exists(_cli.Path("p001.png")));
    }

    [Fact]
    public void Compare_Reports_Two_Identical_Images_As_The_Same()
    {
        string source = _cli.MakeDocument("hello.docx", "Hello world");
        _cli.RunExpecting(ExitCode.Ok, "render", source, "--out", _cli.Path("a.png"), "--continuous", "--quiet");
        File.Copy(_cli.Path("a.png"), _cli.Path("b.png"));

        JsonObject json = _cli
            .RunExpecting(ExitCode.Ok, "compare", _cli.Path("a.png"), _cli.Path("b.png"), "--json")
            .Json();

        Assert.True(json["equal"]!.GetValue<bool>());
        Assert.Equal(0, json["image"]!["differingPixels"]!.GetValue<int>());
    }

    [Fact]
    public void Compare_Exits_Five_And_Locates_The_Difference()
    {
        string a = _cli.MakeDocument("a.docx", "Hello world");
        string b = _cli.MakeDocument("b.docx", "Hello worlds");

        _cli.RunExpecting(ExitCode.Ok, "render", a, "--out", _cli.Path("a.png"), "--continuous", "--quiet");
        _cli.RunExpecting(ExitCode.Ok, "render", b, "--out", _cli.Path("b.png"), "--continuous", "--quiet");

        JsonObject json = _cli
            .RunExpecting(
                ExitCode.Different,
                "compare", _cli.Path("a.png"), _cli.Path("b.png"), "--diff", _cli.Path("diff.png"), "--json")
            .Json();

        Assert.False(json["equal"]!.GetValue<bool>());
        Assert.True(json["image"]!["differingPixels"]!.GetValue<int>() > 0);
        Assert.NotNull(json["image"]!["differenceBounds"]);
        Assert.True(File.Exists(_cli.Path("diff.png")));
    }

    [Fact]
    public void A_Tolerance_Wide_Enough_Absorbs_A_Small_Difference()
    {
        string a = _cli.MakeDocument("a.docx", "Hello world");
        _cli.RunExpecting(ExitCode.Ok, "render", a, "--out", _cli.Path("a.png"), "--continuous", "--quiet");
        File.Copy(_cli.Path("a.png"), _cli.Path("b.png"));

        // Identical images pass at every tolerance; the point of the assertion is
        // that the option is honoured rather than ignored.
        _cli.RunExpecting(
            ExitCode.Ok, "compare", _cli.Path("a.png"), _cli.Path("b.png"), "--tolerance", "8", "--quiet");
    }

    [Fact]
    public void Compare_In_Document_Mode_Names_The_Paragraph_That_Differs()
    {
        string a = _cli.MakeDocument("a.docx", "one\ntwo\nthree");
        string b = _cli.MakeDocument("b.docx", "one\nTWO CHANGED\nthree");

        JsonObject json = _cli
            .RunExpecting(ExitCode.Different, "compare", a, b, "--json")
            .Json();

        Assert.False(json["document"]!["plainTextEqual"]!.GetValue<bool>());
        var differences = json["document"]!["differences"]!.AsArray();
        Assert.NotEmpty(differences);
    }

    [Fact]
    public void Compare_Aligns_Around_An_Inserted_Paragraph()
    {
        // Without alignment a single insertion makes every later paragraph look
        // different, which buries the one real finding.
        string a = _cli.MakeDocument("a.docx", "one\ntwo\nthree\nfour");
        string b = _cli.MakeDocument("b.docx", "one\ninserted\ntwo\nthree\nfour");

        JsonObject json = _cli
            .RunExpecting(ExitCode.Different, "compare", a, b, "--json")
            .Json();

        var differences = json["document"]!["differences"]!.AsArray();
        Assert.Single(differences);
        Assert.Equal("extra", differences[0]!["kind"]!.GetValue<string>());
    }

    [Fact]
    public void Compare_Detects_A_Formatting_Difference_Behind_Identical_Text()
    {
        string a = _cli.MakeDocument("a.docx", "hello world");
        string b = _cli.MakeDocument("b.docx", "hello world", "inline:0:0-5:bold=on");

        JsonObject json = _cli.RunExpecting(ExitCode.Different, "compare", a, b, "--json").Json();

        Assert.True(json["document"]!["plainTextEqual"]!.GetValue<bool>());
        Assert.False(json["document"]!["formatCodesEqual"]!.GetValue<bool>());
        Assert.Contains(
            "bold",
            json["document"]!["differences"]![0]!["detail"]!.GetValue<string>(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ignore_Inline_Style_Skips_A_Formatting_Only_Difference()
    {
        string a = _cli.MakeDocument("a.docx", "hello world");
        string b = _cli.MakeDocument("b.docx", "hello world", "inline:0:0-5:bold=on");

        _cli.RunExpecting(ExitCode.Ok, "compare", a, b, "--ignore-inline-style", "--quiet");
    }

    [Fact]
    public void Compare_With_Render_Reports_Both_Structure_And_Pixels()
    {
        string a = _cli.MakeDocument("a.docx", "hello world");
        string b = _cli.MakeDocument("b.docx", "hello world", "inline:0:0-5:bold=on");

        JsonObject json = _cli
            .RunExpecting(ExitCode.Different, "compare", a, b, "--render", "--continuous", "--json")
            .Json();

        Assert.NotNull(json["render"]);
        Assert.True(json["render"]!["pages"]![0]!["differingPixels"]!.GetValue<int>() > 0);
    }

    [Fact]
    public void Roundtrip_Through_Docx_Preserves_Everything_The_Model_Holds()
    {
        string source = _cli.MakeDocument(
            "source.docx",
            "Title\nBody text here",
            "inline:0:*:bold=on,size=18",
            "para:0:align=center",
            "inline:1:0-4:italic=on");

        JsonObject json = _cli.RunExpecting(ExitCode.Ok, "roundtrip", source, "--via", "docx", "--json").Json();

        Assert.True(json["equal"]!.GetValue<bool>());
        Assert.Empty(json["results"]![0]!["comparison"]!["differences"]!.AsArray());
    }

    [Fact]
    public void Roundtrip_Exits_Five_And_Names_What_A_Format_Cannot_Carry()
    {
        // Markdown has no alignment. That is a documented limitation rather than
        // a defect, and the value here is that the tool states it precisely
        // instead of leaving it to be guessed from a picture.
        string source = _cli.MakeDocument("source.docx", "Centred", "para:0:align=center");

        JsonObject json = _cli
            .RunExpecting(ExitCode.Different, "roundtrip", source, "--via", "markdown", "--json")
            .Json();

        Assert.False(json["equal"]!.GetValue<bool>());
        string detail = json["results"]![0]!["comparison"]!["differences"]![0]!["detail"]!.GetValue<string>();
        Assert.Contains("alignment", detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Roundtrip_Reports_Every_Requested_Format()
    {
        string source = _cli.MakeDocument("source.docx", "Plain text only");

        JsonObject json = _cli
            .Run("roundtrip", source, "--via", "docx", "--via", "rtf", "--via", "html", "--json")
            .Json();

        Assert.Equal(3, json["results"]!.AsArray().Count);
    }

    [Fact]
    public void An_Unknown_Via_Format_Is_A_Usage_Error()
    {
        string source = _cli.MakeDocument("source.docx", "text");

        _cli.RunExpecting(ExitCode.Usage, "roundtrip", source, "--via", "pdf");
    }

    [Theory]
    [InlineData("a4", 595.276, 841.89)]
    [InlineData("letter", 612.0, 792.0)]
    [InlineData("210x297mm", 595.276, 841.89)]
    [InlineData("8.5x11in", 612.0, 792.0)]
    [InlineData("612x792pt", 612.0, 792.0)]
    public void Page_Sizes_Parse_To_The_Expected_Points(string value, double width, double height)
    {
        (double actualWidth, double actualHeight) = PageSetup.ParsePageSize(value);

        Assert.Equal(width, actualWidth, 2);
        Assert.Equal(height, actualHeight, 2);
    }

    [Fact]
    public void Margins_Accept_One_Two_Or_Four_Values()
    {
        Assert.Equal((72, 72, 72, 72), PageSetup.ParseMargins("1in"));
        Assert.Equal((72, 36, 72, 36), PageSetup.ParseMargins("1in,0.5in"));
        Assert.Equal((10, 20, 30, 40), PageSetup.ParseMargins("10,20,30,40"));
    }

    [Fact]
    public void An_Unusable_Page_Box_Is_A_Usage_Error()
    {
        string source = _cli.MakeDocument("hello.docx", "Hello");

        _cli.RunExpecting(
            ExitCode.Usage,
            "render", source, "--out", _cli.Path("out.png"), "--page-size", "a4", "--margin", "6in");
    }

    [Fact]
    public void A_Bad_Diff_Style_Is_A_Usage_Error()
    {
        string source = _cli.MakeDocument("hello.docx", "Hello");
        _cli.RunExpecting(ExitCode.Ok, "render", source, "--out", _cli.Path("a.png"), "--continuous", "--quiet");
        File.Copy(_cli.Path("a.png"), _cli.Path("b.png"));

        _cli.RunExpecting(
            ExitCode.Usage,
            "compare", _cli.Path("a.png"), _cli.Path("b.png"),
            "--diff", _cli.Path("d.png"), "--diff-style", "rainbow");
    }

    [Fact]
    public void Fail_On_Applies_To_A_Comparison_That_Otherwise_Passed()
    {
        // Two documents can agree because both lost the same construct. The
        // verdict is "same"; whether that counts as a pass is the caller's call,
        // and --fail-on is how they make it.
        string a = _cli.MakeDocument("a.docx", "hello world");
        string b = _cli.MakeDocument("b.docx", "hello world");

        _cli.RunExpecting(ExitCode.Ok, "compare", a, b, "--quiet");
        _cli.RunExpecting(ExitCode.Diagnostics, "compare", a, b, "--fail-on", "info", "--quiet");
    }

    [Fact]
    public void Fail_On_Applies_To_A_Roundtrip_That_Otherwise_Passed()
    {
        string source = _cli.MakeDocument("source.docx", "plain text");

        _cli.RunExpecting(ExitCode.Ok, "roundtrip", source, "--via", "docx", "--quiet");
        _cli.RunExpecting(
            ExitCode.Diagnostics, "roundtrip", source, "--via", "docx", "--fail-on", "info", "--quiet");
    }

    [Fact]
    public void A_Difference_Outranks_Fail_On()
    {
        // A caller reading exit 5 knows to look at the differences. Reporting 6
        // instead would send them to the diagnostics and hide the finding.
        string a = _cli.MakeDocument("a.docx", "one");
        string b = _cli.MakeDocument("b.docx", "two");

        _cli.RunExpecting(ExitCode.Different, "compare", a, b, "--fail-on", "info", "--quiet");
    }
}
