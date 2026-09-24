using System.Text.Json.Nodes;

namespace Broiler.Documents.Cli.Tests;

/// <summary>End-to-end runs of each command, through the same entry point a shell uses.</summary>
public sealed class CommandTests : IDisposable
{
    private readonly CliHarness _cli = new();
    private static readonly string[] expected = ["DOCX", "ODT", "RTF", "HTML", "Markdown", "PDF"];

    public void Dispose() => _cli.Dispose();

    [Fact]
    public void Help_Lists_Every_Command_And_Exits_Zero()
    {
        CliRun run = CliHarness.RunExpecting(ExitCode.Ok, "--help");

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
        CliRun run = CliHarness.RunExpecting(ExitCode.Usage);

        Assert.Contains("usage:", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void An_Unknown_Command_Suggests_The_Closest_Match()
    {
        CliRun run = CliHarness.RunExpecting(ExitCode.Usage, "compair", "a", "b");

        Assert.Contains("compare", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Formats_Reports_The_Composed_Codecs_With_Pdf_Read_Only()
    {
        CliRun run = CliHarness.RunExpecting(ExitCode.Ok, "formats", "--json");
        JsonObject json = run.Json();

        var names = json["formats"]!.AsArray()
            .Select(entry => entry!["name"]!.GetValue<string>())
            .ToArray();

        Assert.Equal(expected, names);

        // docs/pdf-support-roadmap.md 4.1 lets an application read PDF before it
        // lets one write it. This is the assertion that keeps a future edit from
        // quietly giving this tool PDF destinations.
        JsonNode pdf = json["formats"]!.AsArray().Single(entry => entry!["name"]!.GetValue<string>() == "PDF")!;
        Assert.True(json["pdfComposed"]!.GetValue<bool>());
        Assert.True(pdf["canRead"]!.GetValue<bool>());
        Assert.False(pdf["canWrite"]!.GetValue<bool>());
    }

    [Fact]
    public void New_Writes_A_Document_That_Reads_Back()
    {
        string path = _cli.MakeDocument("hello.docx", "Hello world");

        CliRun info = CliHarness.RunExpecting(ExitCode.Ok, "info", path, "--json");
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
        CliHarness.RunExpecting(ExitCode.Ok, "new", "--out", path, "--text", "Hello world", "--quiet");

        Assert.True(File.Exists(path));
        CliRun dump = CliHarness.RunExpecting(ExitCode.Ok, "dump", path, "--as", "text");
        Assert.Contains("Hello world", dump.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Probe_Identifies_A_Docx_And_Reports_Every_Codec()
    {
        string path = _cli.MakeDocument("hello.docx", "Hello");

        JsonObject json = CliHarness.RunExpecting(ExitCode.Ok, "probe", path, "--json").Json();

        Assert.Equal("DOCX", json["selected"]!.GetValue<string>());
        Assert.Equal(expected.Length, json["probes"]!.AsArray().Count);
    }

    [Fact]
    public void A_Pdf_Is_Read()
    {
        string path = _cli.Path("hello.pdf");
        File.WriteAllBytes(path, MinimalPdf("Hello from a PDF"));

        JsonObject json = CliHarness.RunExpecting(ExitCode.Ok, "info", path, "--json").Json();

        Assert.Equal("PDF", json["format"]!.GetValue<string>());
        Assert.Contains("Hello from a PDF", CliHarness.RunExpecting(ExitCode.Ok, "dump", path).Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Pdf_Is_Never_Written()
    {
        // The codec writes; this tool composes it to read only, so asking for a
        // PDF is a usage error before anything reaches the disk.
        string source = _cli.MakeDocument("hello.docx", "Hello");
        string destination = _cli.Path("hello.pdf");

        CliRun run = CliHarness.RunExpecting(ExitCode.Usage, "convert", source, "--out", destination);

        Assert.Contains("does not write PDF", run.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public void A_Pdf_Picture_In_Icc_Colour_Is_Read()
    {
        // The one optional PDF provider this tool composes. Without a profile
        // reader a picture in ICC-based colour is refused by name, and a render
        // or a conversion of its page comes out without it.
        string path = _cli.Path("icc.pdf");
        File.WriteAllBytes(path, PdfWithIccPicture());

        JsonObject info = CliHarness.RunExpecting(ExitCode.Ok, "info", path, "--json").Json();
        Assert.Equal(1, info["statistics"]!["images"]!.GetValue<int>());
        Assert.DoesNotContain(
            info["diagnostics"]!.AsArray(),
            diagnostic => diagnostic!["message"]!.GetValue<string>().Contains("ICCBased", StringComparison.Ordinal));
    }

    /// <summary>
    /// A one-page PDF showing one line in Helvetica, with a cross-reference table
    /// whose offsets are measured as it is built.
    /// </summary>
    private static byte[] MinimalPdf(string text)
    {
        string content = $"BT /F1 12 Tf 72 720 Td ({text}) Tj ET";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
        ];

        var pdf = new System.Text.StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        int xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
            pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");

        return System.Text.Encoding.ASCII.GetBytes(pdf.ToString());
    }

    /// <summary>
    /// A one-page PDF with a line of text over a two-pixel gray picture whose
    /// colour space is an ICC profile, built here byte by byte.
    /// </summary>
    /// <remarks>
    /// Assembled as Latin-1, whose characters are the bytes one for one, so the
    /// binary profile and samples can sit in the same string as the syntax
    /// around them. No profile file is committed or read (IP-020).
    /// </remarks>
    private static byte[] PdfWithIccPicture()
    {
        string profile = System.Text.Encoding.Latin1.GetString(GrayProfile());
        const string content = "BT /F1 12 Tf 72 720 Td (A picture in ICC colour) Tj ET\nq 100 0 0 50 72 600 cm /Im0 Do Q";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> /XObject << /Im0 6 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /XObject /Subtype /Image /Width 2 /Height 1 /ColorSpace [/ICCBased 7 0 R] /BitsPerComponent 8 /Length 2 >>\nstream\n\u0080@\nendstream",
            $"<< /N 1 /Length {profile.Length} >>\nstream\n{profile}\nendstream",
        ];

        var pdf = new System.Text.StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        int xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
            pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");

        return System.Text.Encoding.Latin1.GetBytes(pdf.ToString());
    }

    /// <summary>
    /// A version 4 gray display profile: a D50 white point, and a tone curve of
    /// gamma 1, so a sample's value is its luminance.
    /// </summary>
    private static byte[] GrayProfile()
    {
        byte[] profile = new byte[190];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(profile, (uint)profile.Length);
        profile[8] = 4;
        profile[9] = 0x20;
        Signature(12, "mntr");
        Signature(16, "GRAY");
        Signature(20, "XYZ ");
        Signature(36, "acsp");
        Fixed(68, 0.9642);
        Fixed(72, 1.0);
        Fixed(76, 0.8249);

        // The tag table: a white point at 156, and the gray tone curve at 176.
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(128), 2);
        Entry(132, "wtpt", 156, 20);
        Entry(144, "kTRC", 176, 14);

        Signature(156, "XYZ ");
        Fixed(164, 0.9642);
        Fixed(168, 1.0);
        Fixed(172, 0.8249);

        Signature(176, "curv");
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(184), 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(profile.AsSpan(188), 0x0100);
        return profile;

        void Signature(int at, string signature) => System.Text.Encoding.ASCII.GetBytes(signature, profile.AsSpan(at));

        void Fixed(int at, double value) =>
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(profile.AsSpan(at), (int)Math.Round(value * 65536));

        void Entry(int at, string signature, int offset, int length)
        {
            Signature(at, signature);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(at + 4), (uint)offset);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(at + 8), (uint)length);
        }
    }

    [Fact]
    public void Probe_Exits_Three_When_Nothing_Recognizes_The_Content()
    {
        string path = _cli.Path("junk.bin");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03]);

        CliHarness.RunExpecting(ExitCode.Read, "probe", path);
    }

    [Fact]
    public void A_Missing_File_Exits_Two_And_Not_Three()
    {
        // The distinction a harness depends on: "the export did not happen" is
        // not the same finding as "the export changed".
        CliRun run = CliHarness.RunExpecting(ExitCode.Input, "info", _cli.Path("absent.docx"));

        Assert.Contains("not found", run.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dump_Json_Is_Stable_Across_Runs()
    {
        string path = _cli.MakeDocument("styled.docx", "Hello world", "inline:0:0-5:bold=on");

        string first = CliHarness.RunExpecting(ExitCode.Ok, "dump", path, "--as", "json").Output;
        string second = CliHarness.RunExpecting(ExitCode.Ok, "dump", path, "--as", "json").Output;

        Assert.Equal(first, second);
        Assert.Contains("\"bold\": true", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Dump_Codes_Emits_The_Canonical_Formatting_Codes_Grammar()
    {
        string path = _cli.MakeDocument("bold.docx", "Hello World!", "inline:0:*:bold=on");

        CliRun run = CliHarness.RunExpecting(ExitCode.Ok, "dump", path, "--as", "codes");

        // The signed-off example from the grammar document.
        Assert.Contains("[Bold ON]Hello World![Bold OFF]", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_Round_Trips_Text_Through_Another_Format()
    {
        string source = _cli.MakeDocument("source.docx", "First\nSecond");
        string destination = _cli.Path("converted.rtf");

        CliHarness.RunExpecting(ExitCode.Ok, "convert", source, "--out", destination, "--quiet");

        CliRun dump = CliHarness.RunExpecting(ExitCode.Ok, "dump", destination, "--as", "text");
        Assert.Contains("First", dump.Output, StringComparison.Ordinal);
        Assert.Contains("Second", dump.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_Infers_The_Output_Format_From_The_Extension()
    {
        string source = _cli.MakeDocument("source.docx", "Body");

        JsonObject json = CliHarness.RunExpecting(ExitCode.Ok, "convert", source, "--out", _cli.Path("out.md"), "--json")
            .Json();

        Assert.Equal("Markdown", json["destinationFormat"]!.GetValue<string>());
    }

    [Fact]
    public void Convert_Without_An_Extension_Or_To_Is_A_Usage_Error()
    {
        string source = _cli.MakeDocument("source.docx", "Body");

        CliHarness.RunExpecting(ExitCode.Usage, "convert", source, "--out", _cli.Path("output"));
    }

    [Fact]
    public void Edit_In_Place_Rewrites_The_Input()
    {
        string path = _cli.MakeDocument("draft.docx", "Status: DRAFT");

        CliHarness.RunExpecting(ExitCode.Ok, "edit", path, "--in-place", "--op", "replace:DRAFT:FINAL", "--quiet");

        CliRun dump = CliHarness.RunExpecting(ExitCode.Ok, "dump", path, "--as", "text");
        Assert.Contains("FINAL", dump.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("DRAFT", dump.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Edit_Reads_Operations_From_A_Script()
    {
        string path = _cli.MakeDocument("draft.docx", "one\ntwo");
        string script = _cli.Path("edits.txt");
        File.WriteAllLines(script,
        [
            "# comments and blank lines are skipped",
            string.Empty,
            "append:three",
            "para:*:align=center",
        ]);

        CliHarness.RunExpecting(
            ExitCode.Ok, "edit", path, "--out", path, "--script", script, "--quiet");

        JsonObject json = CliHarness.RunExpecting(ExitCode.Ok, "info", path, "--json").Json();
        Assert.Equal(3, json["statistics"]!["paragraphs"]!.GetValue<int>());
        Assert.Equal(3, json["statistics"]!["alignedParagraphs"]!.GetValue<int>());
    }

    [Fact]
    public void Edit_With_No_Operations_Is_A_Usage_Error()
    {
        string path = _cli.MakeDocument("draft.docx", "text");

        CliHarness.RunExpecting(ExitCode.Usage, "edit", path, "--out", _cli.Path("out.docx"));
    }

    [Fact]
    public void Fail_On_Turns_Diagnostics_Into_An_Exit_Code()
    {
        string path = _cli.MakeDocument("hello.docx", "Hello");

        // Without the threshold the same run succeeds; with it, the informational
        // diagnostic the DOCX reader emits is enough to fail.
        CliHarness.RunExpecting(ExitCode.Ok, "info", path, "--quiet");
        CliHarness.RunExpecting(ExitCode.Diagnostics, "info", path, "--fail-on", "info", "--quiet");
    }

    [Fact]
    public void Version_Reports_The_Font_The_Renderer_Falls_Back_To()
    {
        JsonObject json = CliHarness.RunExpecting(ExitCode.Ok, "version", "--json").Json();

        Assert.False(string.IsNullOrWhiteSpace(json["fallbackTextFont"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(json["broilerDocuments"]!.GetValue<string>()));
    }

    [Fact]
    public void Json_Output_Always_Carries_The_Exit_Code()
    {
        JsonObject json = CliHarness.RunExpecting(ExitCode.Input, "info", _cli.Path("absent.docx"), "--json").Json();

        Assert.Equal(ExitCode.Input, json["exitCode"]!.GetValue<int>());
        Assert.False(json["ok"]!.GetValue<bool>());
        Assert.Contains("not found", json["error"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Command_Help_Exits_Zero_And_Shows_The_Command_Options()
    {
        CliRun run = CliHarness.RunExpecting(ExitCode.Ok, "render", "--help");

        Assert.Contains("--dpi", run.Output, StringComparison.Ordinal);
        Assert.Contains("--continuous", run.Output, StringComparison.Ordinal);
    }
}
