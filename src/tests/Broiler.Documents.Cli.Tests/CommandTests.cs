using System.Text.Json.Nodes;

namespace Broiler.Documents.Cli.Tests;

/// <summary>End-to-end runs of each command, through the same entry point a shell uses.</summary>
public sealed class CommandTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    [Fact]
    public void Help_Lists_Every_Command_And_Exits_Zero()
    {
        CliRun run = _cli.RunExpecting(ExitCode.Ok, "--help");

        foreach (string command in new[]
                 {
                     "formats", "probe", "info", "dump", "new", "edit",
                     "convert", "render", "compare", "roundtrip", "version",
                 })
        {
            Assert.Contains(command, run.Output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void No_Arguments_Prints_Help_And_Exits_As_A_Usage_Error()
    {
        // Help on stdout so it is readable, but a non-zero exit so a script that
        // forgot its arguments does not look like it succeeded.
        CliRun run = _cli.RunExpecting(ExitCode.Usage);

        Assert.Contains("usage:", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void An_Unknown_Command_Suggests_The_Closest_Match()
    {
        CliRun run = _cli.RunExpecting(ExitCode.Usage, "compair", "a", "b");

        Assert.Contains("compare", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Formats_Reports_The_Five_Composed_Codecs_And_No_Pdf()
    {
        CliRun run = _cli.RunExpecting(ExitCode.Ok, "formats", "--json");
        JsonObject json = run.Json();

        var names = json["formats"]!.AsArray()
            .Select(entry => entry!["name"]!.GetValue<string>())
            .ToArray();

        Assert.Equal(new[] { "DOCX", "ODT", "RTF", "HTML", "Markdown" }, names);

        // The PDF codec is gated by docs/pdf-support-roadmap.md 4.1 and must not
        // reach an application catalog. This is the assertion that keeps a future
        // edit from quietly composing it here.
        Assert.False(json["pdfComposed"]!.GetValue<bool>());
        Assert.DoesNotContain("PDF", names);
    }

    [Fact]
    public void New_Writes_A_Document_That_Reads_Back()
    {
        string path = _cli.MakeDocument("hello.docx", "Hello world");

        CliRun info = _cli.RunExpecting(ExitCode.Ok, "info", path, "--json");
        JsonObject json = info.Json();

        Assert.Equal("DOCX", json["format"]!.GetValue<string>());
        Assert.Equal(1, json["statistics"]!["paragraphs"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("docx")]
    [InlineData("odt")]
    [InlineData("rtf")]
    [InlineData("html")]
    [InlineData("md")]
    public void New_Writes_Every_Composed_Format(string extension)
    {
        string path = _cli.Path("sample." + extension);
        _cli.RunExpecting(ExitCode.Ok, "new", "--out", path, "--text", "Hello world", "--quiet");

        Assert.True(File.Exists(path));
        CliRun dump = _cli.RunExpecting(ExitCode.Ok, "dump", path, "--as", "text");
        Assert.Contains("Hello world", dump.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Probe_Identifies_A_Docx_And_Reports_Every_Codec()
    {
        string path = _cli.MakeDocument("hello.docx", "Hello");

        JsonObject json = _cli.RunExpecting(ExitCode.Ok, "probe", path, "--json").Json();

        Assert.Equal("DOCX", json["selected"]!.GetValue<string>());
        Assert.Equal(5, json["probes"]!.AsArray().Count);
    }

    [Fact]
    public void Probe_Exits_Three_When_Nothing_Recognizes_The_Content()
    {
        string path = _cli.Path("junk.bin");
        File.WriteAllBytes(path, new byte[] { 0x00, 0x01, 0x02, 0x03 });

        _cli.RunExpecting(ExitCode.Read, "probe", path);
    }

    [Fact]
    public void A_Missing_File_Exits_Two_And_Not_Three()
    {
        // The distinction a harness depends on: "the export did not happen" is
        // not the same finding as "the export changed".
        CliRun run = _cli.RunExpecting(ExitCode.Input, "info", _cli.Path("absent.docx"));

        Assert.Contains("not found", run.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dump_Json_Is_Stable_Across_Runs()
    {
        string path = _cli.MakeDocument("styled.docx", "Hello world", "inline:0:0-5:bold=on");

        string first = _cli.RunExpecting(ExitCode.Ok, "dump", path, "--as", "json").Output;
        string second = _cli.RunExpecting(ExitCode.Ok, "dump", path, "--as", "json").Output;

        Assert.Equal(first, second);
        Assert.Contains("\"bold\": true", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Dump_Codes_Emits_The_Canonical_Formatting_Codes_Grammar()
    {
        string path = _cli.MakeDocument("bold.docx", "Hello World!", "inline:0:*:bold=on");

        CliRun run = _cli.RunExpecting(ExitCode.Ok, "dump", path, "--as", "codes");

        // The signed-off example from the grammar document.
        Assert.Contains("[Bold ON]Hello World![Bold OFF]", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_Round_Trips_Text_Through_Another_Format()
    {
        string source = _cli.MakeDocument("source.docx", "First\nSecond");
        string destination = _cli.Path("converted.rtf");

        _cli.RunExpecting(ExitCode.Ok, "convert", source, "--out", destination, "--quiet");

        CliRun dump = _cli.RunExpecting(ExitCode.Ok, "dump", destination, "--as", "text");
        Assert.Contains("First", dump.Output, StringComparison.Ordinal);
        Assert.Contains("Second", dump.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_Infers_The_Output_Format_From_The_Extension()
    {
        string source = _cli.MakeDocument("source.docx", "Body");

        JsonObject json = _cli
            .RunExpecting(ExitCode.Ok, "convert", source, "--out", _cli.Path("out.md"), "--json")
            .Json();

        Assert.Equal("Markdown", json["destinationFormat"]!.GetValue<string>());
    }

    [Fact]
    public void Convert_Without_An_Extension_Or_To_Is_A_Usage_Error()
    {
        string source = _cli.MakeDocument("source.docx", "Body");

        _cli.RunExpecting(ExitCode.Usage, "convert", source, "--out", _cli.Path("output"));
    }

    [Fact]
    public void Edit_In_Place_Rewrites_The_Input()
    {
        string path = _cli.MakeDocument("draft.docx", "Status: DRAFT");

        _cli.RunExpecting(ExitCode.Ok, "edit", path, "--in-place", "--op", "replace:DRAFT:FINAL", "--quiet");

        CliRun dump = _cli.RunExpecting(ExitCode.Ok, "dump", path, "--as", "text");
        Assert.Contains("FINAL", dump.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("DRAFT", dump.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Edit_Reads_Operations_From_A_Script()
    {
        string path = _cli.MakeDocument("draft.docx", "one\ntwo");
        string script = _cli.Path("edits.txt");
        File.WriteAllLines(script, new[]
        {
            "# comments and blank lines are skipped",
            string.Empty,
            "append:three",
            "para:*:align=center",
        });

        _cli.RunExpecting(
            ExitCode.Ok, "edit", path, "--out", path, "--script", script, "--quiet");

        JsonObject json = _cli.RunExpecting(ExitCode.Ok, "info", path, "--json").Json();
        Assert.Equal(3, json["statistics"]!["paragraphs"]!.GetValue<int>());
        Assert.Equal(3, json["statistics"]!["alignedParagraphs"]!.GetValue<int>());
    }

    [Fact]
    public void Edit_With_No_Operations_Is_A_Usage_Error()
    {
        string path = _cli.MakeDocument("draft.docx", "text");

        _cli.RunExpecting(ExitCode.Usage, "edit", path, "--out", _cli.Path("out.docx"));
    }

    [Fact]
    public void Fail_On_Turns_Diagnostics_Into_An_Exit_Code()
    {
        string path = _cli.MakeDocument("hello.docx", "Hello");

        // Without the threshold the same run succeeds; with it, the informational
        // diagnostic the DOCX reader emits is enough to fail.
        _cli.RunExpecting(ExitCode.Ok, "info", path, "--quiet");
        _cli.RunExpecting(ExitCode.Diagnostics, "info", path, "--fail-on", "info", "--quiet");
    }

    [Fact]
    public void Version_Reports_The_Font_The_Renderer_Falls_Back_To()
    {
        JsonObject json = _cli.RunExpecting(ExitCode.Ok, "version", "--json").Json();

        Assert.False(string.IsNullOrWhiteSpace(json["fallbackTextFont"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(json["broilerDocuments"]!.GetValue<string>()));
    }

    [Fact]
    public void Json_Output_Always_Carries_The_Exit_Code()
    {
        JsonObject json = _cli.RunExpecting(ExitCode.Input, "info", _cli.Path("absent.docx"), "--json").Json();

        Assert.Equal(ExitCode.Input, json["exitCode"]!.GetValue<int>());
        Assert.False(json["ok"]!.GetValue<bool>());
        Assert.Contains("not found", json["error"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Command_Help_Exits_Zero_And_Shows_The_Command_Options()
    {
        CliRun run = _cli.RunExpecting(ExitCode.Ok, "render", "--help");

        Assert.Contains("--dpi", run.Output, StringComparison.Ordinal);
        Assert.Contains("--continuous", run.Output, StringComparison.Ordinal);
    }
}
