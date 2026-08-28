using System.Text.Json.Nodes;
using Broiler.Documents.Cli.Documents;

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
    public void An_Image_Operation_Puts_A_Picture_In_The_Model()
    {
        string image = WriteCheckerboard("dot.png", 48);
        string document = _cli.Path("pic.docx");

        _cli.RunExpecting(
            ExitCode.Ok,
            "new", "--out", document,
            "--text", "before after",
            "--op", "image:0:7:file=" + image + ",width=36,height=36,alt=a chequerboard",
            "--quiet");

        JsonObject json = _cli.RunExpecting(ExitCode.Ok, "info", document, "--json").Json();
        Assert.Equal(1, json["statistics"]!["images"]!.GetValue<int>());

        JsonObject dump = _cli.RunExpecting(ExitCode.Ok, "dump", document, "--as", "json", "--json").Json();
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
        _cli.RunExpecting(
            ExitCode.Ok,
            "new", "--out", document, "--text", "x",
            "--op", "image:0:$:file=" + mislabelled,
            "--quiet");

        string content = _cli
            .RunExpecting(ExitCode.Ok, "dump", document, "--as", "json", "--json")
            .Json()["content"]!.GetValue<string>();

        Assert.Contains("image/png", content, StringComparison.Ordinal);
    }

    [Fact]
    public void An_Image_With_Only_One_Dimension_Is_A_Usage_Error()
    {
        // The model reads a zero in either as "no stated size", so accepting one
        // alone would silently discard it.
        string image = WriteCheckerboard("dot.png", 16);

        _cli.RunExpecting(
            ExitCode.Usage,
            "new", "--out", _cli.Path("pic.docx"), "--text", "x",
            "--op", "image:0:$:file=" + image + ",width=36");
    }

    [Fact]
    public void A_Missing_Image_File_Exits_Two()
    {
        _cli.RunExpecting(
            ExitCode.Input,
            "new", "--out", _cli.Path("pic.docx"), "--text", "x",
            "--op", "image:0:$:file=" + _cli.Path("absent.png"));
    }

    [Fact]
    public void An_Image_Renders_Rather_Than_Being_Skipped()
    {
        string image = WriteCheckerboard("dot.png", 48);
        string document = _cli.Path("pic.docx");
        _cli.RunExpecting(
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

        JsonObject json = _cli.RunExpecting(ExitCode.Different, "compare", a, b, "--json").Json();
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
        _cli.RunExpecting(ExitCode.Ok, "render", a, "--out", _cli.Path("a.png"), "--continuous", "--quiet");
        _cli.RunExpecting(ExitCode.Ok, "render", b, "--out", _cli.Path("b.png"), "--continuous", "--quiet");

        string diff = _cli.Path(style + ".png");
        _cli.RunExpecting(
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
        _cli.RunExpecting(ExitCode.Ok, "render", a, "--out", _cli.Path("a.png"), "--continuous", "--quiet");
        _cli.RunExpecting(ExitCode.Ok, "render", b, "--out", _cli.Path("b.png"), "--continuous", "--quiet");

        JsonObject json = _cli
            .RunExpecting(ExitCode.Different, "compare", _cli.Path("a.png"), _cli.Path("b.png"), "--json")
            .Json();

        Assert.True(json["image"]!["sizeDiffers"]!.GetValue<bool>());
        Assert.True(json["image"]!["comparedPixels"]!.GetValue<long>() > 0);
    }

    [Fact]
    public void Roundtrip_Through_Rtf_Finds_The_Lost_Image()
    {
        // RTF is a real finding rather than a contrived one: the writer emits the
        // picture, and what comes back has lost the placeholder character.
        string image = WriteCheckerboard("dot.png", 32);
        string document = _cli.Path("pic.docx");
        _cli.RunExpecting(
            ExitCode.Ok,
            "new", "--out", document, "--text", "text around  the image",
            "--op", "image:0:12:file=" + image + ",width=24,height=24",
            "--quiet");

        JsonObject json = _cli
            .RunExpecting(ExitCode.Different, "roundtrip", document, "--via", "rtf", "--json")
            .Json();

        JsonObject comparison = json["results"]![0]!["comparison"]!.AsObject();
        Assert.False(comparison["plainTextEqual"]!.GetValue<bool>());
        Assert.Equal(1, comparison["left"]!["images"]!.GetValue<int>());
        Assert.Equal(0, comparison["right"]!["images"]!.GetValue<int>());
    }

    private int RenderHeight(string document, string name)
    {
        JsonObject json = _cli
            .RunExpecting(
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
        stream.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var header = new List<byte>();
        header.AddRange(BigEndian(size));
        header.AddRange(BigEndian(size));
        header.AddRange(new byte[] { 8, 2, 0, 0, 0 });
        WriteChunk(stream, "IHDR", header.ToArray());

        WriteChunk(stream, "IDAT", Deflate(raw.ToArray()));
        WriteChunk(stream, "IEND", Array.Empty<byte>());

        string path = _cli.Path(name);
        File.WriteAllBytes(path, stream.ToArray());
        return path;
    }

    private static byte[] BigEndian(int value) => new[]
    {
        (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value,
    };

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
