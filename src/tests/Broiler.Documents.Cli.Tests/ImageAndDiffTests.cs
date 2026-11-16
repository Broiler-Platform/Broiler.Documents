using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// Inline images, and the parts of the comparison that only show up once a
/// document contains something a format cannot carry.
/// </summary>
public sealed class ImageAndDiffTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    [Fact]
    public void Html_Render_Only_Loads_Sibling_Images_With_Explicit_Opt_In()
    {
        string parentImage = WriteCheckerboard("dot.png", 32);
        Directory.CreateDirectory(_cli.Path("assets"));
        File.Copy(parentImage, _cli.Path("assets/dot.png"));
        string html = _cli.Path("assets/linked.html");
        string output = _cli.Path("linked.png");

        File.WriteAllText(html, "<p><img src='dot.png'></p>");
        var defaultRun = CliHarness.RunExpecting(ExitCode.Ok, "render", html, "--out", output, "--json").Json();
        Assert.Contains("html.skip.external", defaultRun["diagnostics"]!.ToJsonString(), StringComparison.Ordinal);

        var optedIn = CliHarness.RunExpecting(ExitCode.Ok, "render", html, "--out", output, "--json", "--allow-local-images").Json();
        Assert.DoesNotContain("html.skip.external", optedIn["diagnostics"]!.ToJsonString(), StringComparison.Ordinal);

        File.WriteAllText(html, "<p><img src='../dot.png'></p>");
        var escaped = CliHarness.RunExpecting(ExitCode.Ok, "render", html, "--out", output, "--json", "--allow-local-images").Json();
        Assert.Contains("html.skip.external", escaped["diagnostics"]!.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Odt_Render_Only_Loads_Pictures_Linked_Beside_The_Package_With_Explicit_Opt_In()
    {
        // A package's relative link resolves against the package as a folder,
        // so ../dot.png is the file beside the document (globalfilter's
        // document_with_linked_graphic.odt, office issue #249). It is read only
        // with --allow-local-images, under the same name rules as HTML.
        string parentImage = WriteCheckerboard("dot.png", 32);
        Directory.CreateDirectory(_cli.Path("assets"));
        File.Copy(parentImage, _cli.Path("assets/dot.png"));
        string odt = _cli.Path("assets/linked.odt");
        string output = _cli.Path("linked.png");
        string optedInOutput = _cli.Path("linked-opted-in.png");

        WriteOdtLinking(odt, "../dot.png");
        var defaultRun = CliHarness.RunExpecting(ExitCode.Ok, "render", odt, "--out", output, "--json").Json();
        Assert.Contains("odt.image.missing", defaultRun["diagnostics"]!.ToJsonString(), StringComparison.Ordinal);

        var optedIn = CliHarness.RunExpecting(ExitCode.Ok, "render", odt, "--out", optedInOutput, "--json", "--allow-local-images").Json();
        Assert.DoesNotContain("odt.image.missing", optedIn["diagnostics"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.NotEqual(File.ReadAllBytes(output), File.ReadAllBytes(optedInOutput));

        // Nothing deeper, no further out, and no other traversal: each still
        // reads as a link the package does not hold.
        foreach (string link in new[] { "../../dot.png", "../assets/dot.png", "../%2E%2E/dot.png", "./../dot.png", "dot.png" })
        {
            WriteOdtLinking(odt, link);
            var escaped = CliHarness.RunExpecting(ExitCode.Ok, "render", odt, "--out", output, "--json", "--allow-local-images").Json();
            Assert.Contains("odt.image.missing", escaped["diagnostics"]!.ToJsonString(), StringComparison.Ordinal);
        }

        // A sibling the host cannot read keeps today's empty frame.
        WriteOdtLinking(odt, "../absent.png");
        var absent = CliHarness.RunExpecting(ExitCode.Ok, "render", odt, "--out", output, "--json", "--allow-local-images").Json();
        Assert.Contains("odt.image.missing", absent["diagnostics"]!.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Allow_Local_Images_Leaves_Other_Formats_Their_Own_Options()
    {
        // The flag once handed HTML's option type to every codec, and a codec
        // that checks its options (CSV) refused the read.
        string csv = _cli.Path("table.csv");
        File.WriteAllText(csv, "a,b\n1,2\n");

        CliHarness.RunExpecting(ExitCode.Ok, "render", csv, "--out", _cli.Path("table.png"), "--json", "--allow-local-images");
    }

    private static void WriteOdtLinking(string path, string href)
    {
        const string content =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<office:document-content xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
            "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" " +
            "xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\" " +
            "xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\" " +
            "xmlns:xlink=\"http://www.w3.org/1999/xlink\" office:version=\"1.3\">" +
            "<office:body><office:text><text:p>" +
            "<draw:frame draw:name=\"Image1\" text:anchor-type=\"as-char\" svg:width=\"2cm\" svg:height=\"2cm\">" +
            "<draw:image xlink:href=\"{0}\" xlink:type=\"simple\" xlink:show=\"embed\" xlink:actuate=\"onLoad\"/>" +
            "</draw:frame></text:p></office:text></office:body></office:document-content>";

        File.Delete(path);
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("mimetype", CompressionLevel.NoCompression).Open()))
            writer.Write("application/vnd.oasis.opendocument.text");
        using (var writer = new StreamWriter(archive.CreateEntry("content.xml").Open()))
            writer.Write(string.Format(System.Globalization.CultureInfo.InvariantCulture, content, System.Security.SecurityElement.Escape(href)));
    }

    [Fact]
    public void An_Image_Operation_Puts_A_Picture_In_The_Model()
    {
        string image = WriteCheckerboard("dot.png", 48);
        string document = _cli.Path("pic.docx");

        CliHarness.RunExpecting(
            ExitCode.Ok,
            "new", "--out", document,
            "--text", "before after",
            "--op", "image:0:7:file=" + image + ",width=36,height=36,alt=a chequerboard",
            "--quiet");

        JsonObject json = CliHarness.RunExpecting(ExitCode.Ok, "info", document, "--json").Json();
        Assert.Equal(1, json["statistics"]!["images"]!.GetValue<int>());

        JsonObject dump = CliHarness.RunExpecting(ExitCode.Ok, "dump", document, "--as", "json", "--json").Json();
        string content = dump["content"]!.GetValue<string>();
        Assert.Contains("image/png", content, StringComparison.Ordinal);
        Assert.Contains("a chequerboard", content, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Content_Type_Comes_From_The_Bytes_Not_The_Extension()
    {
        // A PNG named .jpg would otherwise be written into a DOCX part declaring
        // a content type nothing can decode.
        string mislabelled = _cli.Path("actually-a-png.jpg");
        File.WriteAllBytes(mislabelled, File.ReadAllBytes(WriteCheckerboard("real.png", 16)));

        string document = _cli.Path("pic.docx");
        CliHarness.RunExpecting(
            ExitCode.Ok,
            "new", "--out", document, "--text", "x",
            "--op", "image:0:$:file=" + mislabelled,
            "--quiet");

        string content = CliHarness.RunExpecting(ExitCode.Ok, "dump", document, "--as", "json", "--json")
            .Json()["content"]!.GetValue<string>();

        Assert.Contains("image/png", content, StringComparison.Ordinal);
    }

    [Fact]
    public void An_Image_With_Only_One_Dimension_Is_A_Usage_Error()
    {
        // The model reads a zero in either as "no stated size", so accepting one
        // alone would silently discard it.
        string image = WriteCheckerboard("dot.png", 16);

        CliHarness.RunExpecting(
            ExitCode.Usage,
            "new", "--out", _cli.Path("pic.docx"), "--text", "x",
            "--op", "image:0:$:file=" + image + ",width=36");
    }

    [Fact]
    public void A_Missing_Image_File_Exits_Two()
    {
        CliHarness.RunExpecting(
            ExitCode.Input,
            "new", "--out", _cli.Path("pic.docx"), "--text", "x",
            "--op", "image:0:$:file=" + _cli.Path("absent.png"));
    }

    [Fact]
    public void An_Image_Renders_Rather_Than_Being_Skipped()
    {
        string image = WriteCheckerboard("dot.png", 48);
        string document = _cli.Path("pic.docx");
        CliHarness.RunExpecting(
            ExitCode.Ok,
            "new", "--out", document, "--text", "before after",
            "--op", "image:0:7:file=" + image + ",width=36,height=36",
            "--quiet");

        // The same document without the picture renders shorter: the image makes
        // its line taller. If the image were quietly dropped the two would match.
        string plain = _cli.MakeDocument("plain.docx", "before after");

        int withImage = RenderHeight(document, "with.png");
        int without = RenderHeight(plain, "without.png");

        Assert.True(
            withImage > without,
            $"expected the image to make the page taller, got {withImage} vs {without}");
    }

    [Fact]
    public void A_Changed_Paragraph_Reports_As_One_Difference_Not_Two()
    {
        // A longest-common-subsequence pass alone produces a delete and an
        // insert for an edited paragraph. Reported that way, one reworded
        // sentence reads as two findings and says nothing about what changed.
        string a = _cli.MakeDocument("a.docx", "one\ntwo\nthree");
        string b = _cli.MakeDocument("b.docx", "one\ntwo changed\nthree");

        JsonObject json = CliHarness.RunExpecting(ExitCode.Different, "compare", a, b, "--json").Json();
        JsonArray differences = json["document"]!["differences"]!.AsArray();

        Assert.Single(differences);
        Assert.Equal("text", differences[0]!["kind"]!.GetValue<string>());
        Assert.Equal(1, differences[0]!["leftParagraph"]!.GetValue<int>());
        Assert.Equal(1, differences[0]!["rightParagraph"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("overlay")]
    [InlineData("mask")]
    [InlineData("heat")]
    public void Every_Diff_Style_Writes_An_Image(string style)
    {
        string a = _cli.MakeDocument("a.docx", "hello world");
        string b = _cli.MakeDocument("b.docx", "hello worlds");
        CliHarness.RunExpecting(ExitCode.Ok, "render", a, "--out", _cli.Path("a.png"), "--continuous", "--quiet");
        CliHarness.RunExpecting(ExitCode.Ok, "render", b, "--out", _cli.Path("b.png"), "--continuous", "--quiet");

        string diff = _cli.Path(style + ".png");
        CliHarness.RunExpecting(
            ExitCode.Different,
            "compare", _cli.Path("a.png"), _cli.Path("b.png"),
            "--diff", diff, "--diff-style", style, "--quiet");

        Assert.True(File.Exists(diff));
    }

    [Fact]
    public void Images_Of_Different_Sizes_Compare_Over_The_Shared_Region()
    {
        string a = _cli.MakeDocument("a.docx", "one line");
        string b = _cli.MakeDocument("b.docx", "one line\ntwo lines\nthree lines");
        CliHarness.RunExpecting(ExitCode.Ok, "render", a, "--out", _cli.Path("a.png"), "--continuous", "--quiet");
        CliHarness.RunExpecting(ExitCode.Ok, "render", b, "--out", _cli.Path("b.png"), "--continuous", "--quiet");

        JsonObject json = CliHarness.RunExpecting(ExitCode.Different, "compare", _cli.Path("a.png"), _cli.Path("b.png"), "--json")
            .Json();

        Assert.True(json["image"]!["sizeDiffers"]!.GetValue<bool>());
        Assert.True(json["image"]!["comparedPixels"]!.GetValue<long>() > 0);
    }

    [Fact]
    public void Roundtrip_Through_Rtf_Preserves_The_Inline_Image()
    {
        string image = WriteCheckerboard("dot.png", 32);
        string document = _cli.Path("pic.docx");
        CliHarness.RunExpecting(
            ExitCode.Ok,
            "new", "--out", document, "--text", "text around  the image",
            "--op", "image:0:12:file=" + image + ",width=24,height=24",
            "--quiet");

        JsonObject json = CliHarness.RunExpecting(ExitCode.Different, "roundtrip", document, "--via", "rtf", "--json")
            .Json();

        JsonObject comparison = json["results"]![0]!["comparison"]!.AsObject();
        Assert.True(comparison["plainTextEqual"]!.GetValue<bool>());
        Assert.Equal(1, comparison["left"]!["images"]!.GetValue<int>());
        Assert.Equal(1, comparison["right"]!["images"]!.GetValue<int>());
    }

    private int RenderHeight(string document, string name)
    {
        JsonObject json = CliHarness.RunExpecting(
                ExitCode.Ok, "render", document, "--out", _cli.Path(name), "--continuous", "--json")
            .Json();

        return json["render"]!["pages"]![0]!["heightPixels"]!.GetValue<int>();
    }

    /// <summary>
    /// Writes a small PNG built from raw chunks, so the tests carry no committed
    /// binary fixture and nothing has to be decoded to produce one.
    /// </summary>
    private string WriteCheckerboard(string name, int size)
    {
        var raw = new List<byte>(size * ((size * 3) + 1));
        for (int y = 0; y < size; y++)
        {
            raw.Add(0);
            for (int x = 0; x < size; x++)
            {
                bool light = ((x / 8) + (y / 8)) % 2 == 0;
                raw.Add(light ? (byte)0xDC : (byte)0xFA);
                raw.Add(light ? (byte)0x28 : (byte)0xE6);
                raw.Add(light ? (byte)0x3C : (byte)0x78);
            }
        }

        using var stream = new MemoryStream();
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new List<byte>();
        header.AddRange(BigEndian(size));
        header.AddRange(BigEndian(size));
        header.AddRange([8, 2, 0, 0, 0]);
        WriteChunk(stream, "IHDR", [.. header]);

        WriteChunk(stream, "IDAT", Deflate([.. raw]));
        WriteChunk(stream, "IEND", []);

        string path = _cli.Path(name);
        File.WriteAllBytes(path, stream.ToArray());
        return path;
    }

    private static byte[] BigEndian(int value) =>
    [
        (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value,
    ];

    private static byte[] Deflate(byte[] data)
    {
        // A zlib stream: the two-byte header, raw deflate, and the Adler-32 the
        // format requires. System.IO.Compression writes the deflate body but not
        // the zlib wrapper a PNG IDAT needs.
        using var output = new MemoryStream();
        output.WriteByte(0x78);
        output.WriteByte(0x9C);

        using (var deflate = new System.IO.Compression.DeflateStream(
            output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }

        output.Write(BigEndian(unchecked((int)Adler32(data))));
        return output.ToArray();
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1;
        uint b = 0;
        foreach (byte value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        return (b << 16) | a;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(BigEndian(data.Length));
        stream.Write(typeBytes);
        stream.Write(data);

        var crcInput = new byte[typeBytes.Length + data.Length];
        typeBytes.CopyTo(crcInput, 0);
        data.CopyTo(crcInput, typeBytes.Length);
        stream.Write(BigEndian(unchecked((int)Crc32(crcInput))));
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (0xEDB88320 & (uint)(-(crc & 1)));
        }

        return crc ^ 0xFFFFFFFF;
    }
}
