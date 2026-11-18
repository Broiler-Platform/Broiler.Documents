using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace Broiler.Documents.Cli.Tests;

// CLI render commands share the graphics font resolver when run in-process.
[CollectionDefinition(DisableParallelization = true)]
public sealed class CliFontMappingCollection;

/// <summary>
/// A reader draws a metafile picture's text as it reads (a formula's SVM
/// replacement, office issue #249), so the render's font mapping has to be in
/// place before the document is read. It was installed after, and the text of
/// tdf96113.odt's formula was drawn in the host face instead of the mapped
/// Liberation Serif.
/// </summary>
[Collection<CliFontMappingCollection>]
public sealed class MetafileTextFontTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    [Fact]
    public void Metafile_Text_Drawn_While_Reading_Goes_Through_The_Font_Mapping()
    {
        // Each invocation must make a fresh lookup in the graphics font cache.
        string family = "Formula " + Guid.NewGuid().ToString("N")[..20];
        string odt = _cli.Path("formula.odt");
        WriteFormulaOdt(odt, family);
        string face = _cli.Path("mapped.ttf");
        File.WriteAllBytes(face, [0]);

        JsonObject json = CliHarness.RunExpecting(ExitCode.Ok,
            "render", odt, "--out", _cli.Path("formula.png"), "--json", "--font-file", "Some Other Family=" + face).Json();

        // Only the metafile requests this family, while the document is read.
        JsonArray unmapped = json["render"]!["fonts"]!["unmappedFamilies"]!.AsArray();
        Assert.Contains(unmapped, node => node!.GetValue<string>() == family);
    }

    private static void WriteFormulaOdt(string path, string family)
    {
        const string content =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<office:document-content xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
            "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" " +
            "xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\" " +
            "xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\" " +
            "xmlns:xlink=\"http://www.w3.org/1999/xlink\" office:version=\"1.3\">" +
            "<office:body><office:text><text:p>" +
            "<draw:frame draw:name=\"Object1\" text:anchor-type=\"as-char\" svg:width=\"2cm\" svg:height=\"1cm\">" +
            "<draw:object xlink:href=\"./Object 1\"/><draw:image xlink:href=\"./ObjectReplacements/Object 1\"/>" +
            "</draw:frame></text:p></office:text></office:body></office:document-content>";

        File.Delete(path);
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("mimetype", CompressionLevel.NoCompression).Open()))
            writer.Write("application/vnd.oasis.opendocument.text");
        using (var writer = new StreamWriter(archive.CreateEntry("content.xml").Open()))
            writer.Write(content);
        using (Stream stream = archive.CreateEntry("ObjectReplacements/Object 1").Open())
            stream.Write(FormulaSvm(family));
    }

    /// <summary>A 2 x 1 cm VCLMTF in 1/100 mm: FONT in <paramref name="family"/>, then one TEXT, "x".</summary>
    private static byte[] FormulaSvm(string family)
    {
        using var actions = new MemoryStream();
        using var a = new BinaryWriter(actions);

        using var font = new MemoryStream();
        using (var f = new BinaryWriter(font, Encoding.Latin1, leaveOpen: true))
        {
            f.Write((ushort)family.Length);
            f.Write(Encoding.Latin1.GetBytes(family));
            f.Write((ushort)0);
            f.Write(0); f.Write(500);
            for (int i = 0; i < 9; i++)
                f.Write((ushort)(i == 3 ? 5 : 0));
            f.Write((short)0);
            f.Write(0);
        }
        a.Write((ushort)138); a.Write((ushort)1); a.Write((uint)(font.Length + 6));
        a.Write((ushort)1); a.Write((uint)font.Length); a.Write(font.ToArray());

        a.Write((ushort)134); a.Write((ushort)1); a.Write(4u); a.Write(0u); // TEXTCOLOR black
        a.Write((ushort)112); a.Write((ushort)1); a.Write(15u);              // TEXT
        a.Write(100); a.Write(700); a.Write((ushort)1); a.Write((byte)'x'); a.Write((ushort)0); a.Write((ushort)1);
        a.Flush();

        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write("VCLMTF"u8);
        w.Write((ushort)1);
        w.Write(49u);
        w.Write(0u);
        w.Write((ushort)1); w.Write(27u);
        w.Write((ushort)0);
        w.Write(0); w.Write(0);
        w.Write(1); w.Write(1); w.Write(1); w.Write(1);
        w.Write((byte)1);
        w.Write(2000); w.Write(1000);
        w.Write(3u);
        w.Write(actions.ToArray());
        w.Flush();
        return stream.ToArray();
    }
}
