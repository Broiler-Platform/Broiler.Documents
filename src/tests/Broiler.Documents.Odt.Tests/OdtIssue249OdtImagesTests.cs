using System.Text;

namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Office conformance issue 249, ODT pictures: a package's <c>../name</c> link
/// resolves against the package as a folder, so it names a file beside the
/// document (globalfilter's document_with_linked_graphic.odt and
/// graphic_shape.odt). It is read only through a host's explicit
/// <see cref="OdtReadOptions"/> resolver, and only in exactly that form.
/// </summary>
public sealed class OdtIssue249OdtImagesTests
{
    private static string Frame(string href) =>
        "<text:p><draw:frame text:anchor-type=\"as-char\" svg:width=\"2cm\" svg:height=\"2cm\">" +
        "<draw:image xlink:href=\"" + href + "\" xlink:type=\"simple\" xlink:show=\"embed\" xlink:actuate=\"onLoad\"/>" +
        "</draw:frame></text:p>";

    private static InlineImage OnlyImage(DocumentReadResult result) =>
        Assert.IsType<InlineImage>(Assert.Single(result.Document.Paragraphs).StyleAt(0).Image);

    private static DocumentReadResult Read(byte[] package, DocumentReadOptions? options) =>
        new OdtDocumentCodec().Read(new MemoryStream(package), options);

    [Fact]
    public void A_Link_Beside_The_Package_Is_Asked_Of_The_Host_By_Its_Bare_Name()
    {
        var asked = new List<string>();
        var options = new OdtReadOptions(DocumentReadOptions.Default, name =>
        {
            asked.Add(name);
            return name == "linked_graphic.png" ? OdtTestPackage.OnePixelPng : null;
        });

        DocumentReadResult result = Read(OdtTestPackage.FromBody(Frame("../linked_graphic.png") + Frame("../linked_graphic.png")), options);

        Assert.Equal(["linked_graphic.png"], asked);
        foreach (RichTextParagraph paragraph in result.Document.Paragraphs)
        {
            InlineImage image = Assert.IsType<InlineImage>(paragraph.StyleAt(0).Image);
            Assert.False(image.IsMissingPayload);
            Assert.Equal("image/png", image.ContentType);
        }
        Assert.DoesNotContain(result.Diagnostics, d => d.Code.StartsWith("odt.image.", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_A_Resolver_The_Link_Stays_An_Empty_Frame()
    {
        DocumentReadResult result = Read(OdtTestPackage.FromBody(Frame("../linked_graphic.png")), options: null);

        InlineImage image = OnlyImage(result);
        Assert.True(image.IsMissingPayload);
        Assert.False(image.MarksMissingPayload);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.image.missing");
    }

    [Theory]
    [InlineData("../../linked_graphic.png")]
    [InlineData("../Pictures/linked_graphic.png")]
    [InlineData("../%2E%2E/linked_graphic.png")]
    [InlineData("../a%2Fb.png")]
    [InlineData("..\\linked_graphic.png")]
    [InlineData("./../linked_graphic.png")]
    [InlineData("linked_graphic.png")]
    [InlineData("Pictures/linked_graphic.png")]
    [InlineData("../")]
    public void Only_Exactly_One_Step_Out_To_A_Plain_Name_Is_Asked(string href)
    {
        var asked = new List<string>();
        var options = new OdtReadOptions(DocumentReadOptions.Default, name =>
        {
            asked.Add(name);
            return OdtTestPackage.OnePixelPng;
        });

        DocumentReadResult result = Read(OdtTestPackage.FromBody(Frame(href)), options);

        Assert.Empty(asked);
        Assert.True(OnlyImage(result).IsMissingPayload);
    }

    [Fact]
    public void An_Escaped_Plain_Name_Is_Asked_Decoded()
    {
        var asked = new List<string>();
        var options = new OdtReadOptions(DocumentReadOptions.Default, name =>
        {
            asked.Add(name);
            return OdtTestPackage.OnePixelPng;
        });

        Read(OdtTestPackage.FromBody(Frame("../linked%20graphic.png")), options);

        Assert.Equal(["linked graphic.png"], asked);
    }

    [Fact]
    public void A_Host_Answer_Over_MaxBinBytes_Is_Not_Drawn()
    {
        var limits = new DocumentLimits(maxBinBytes: 32 * 1024);
        byte[] large = new byte[64 * 1024];
        OdtTestPackage.OnePixelPng.CopyTo(large, 0);
        var options = new OdtReadOptions(new DocumentReadOptions(limits), _ => large);

        DocumentReadResult result = Read(OdtTestPackage.FromBody(Frame("../linked_graphic.png")), options);

        // As for a package entry over the limit: no picture is drawn.
        Assert.Null(Assert.Single(result.Document.Paragraphs).StyleAt(0).Image);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.image.limit");
    }

    private static string ObjectFrame(string folder) =>
        "<text:p><draw:frame text:anchor-type=\"as-char\" svg:width=\"5cm\" svg:height=\"5cm\">" +
        "<draw:object xlink:href=\"./" + folder + "\"/><draw:image xlink:href=\"./ObjectReplacements/" + folder + "\"/>" +
        "</draw:frame></text:p>";

    private static string ObjectManifest(string folder, string mediaType) =>
        "<manifest:file-entry manifest:full-path=\"/\" manifest:media-type=\"" + OdtTestPackage.TextMediaType + "\"/>" +
        "<manifest:file-entry manifest:full-path=\"content.xml\" manifest:media-type=\"text/xml\"/>" +
        "<manifest:file-entry manifest:full-path=\"" + folder + "/\" manifest:media-type=\"" + mediaType + "\"/>";

    [Fact]
    public void A_Formula_Whose_Replacement_Is_Missing_Prints_The_Object_Icon()
    {
        // tdf144319.odt's Formula-14: LibreOffice 24.2 does not lay the
        // formula out afresh, it prints the object's placeholder icon.
        DocumentReadResult result = Read(OdtTestPackage.FromBody(ObjectFrame("Formula-14"),
            manifestInnerXml: ObjectManifest("Formula-14", "application/vnd.oasis.opendocument.formula")), options: null);

        InlineImage image = OnlyImage(result);
        Assert.False(image.IsMissingPayload);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.image.object");
    }

    [Fact]
    public void A_Chart_Or_Spreadsheet_Whose_Replacement_Is_Missing_Keeps_An_Empty_Frame()
    {
        // LibreOffice draws these afresh from the object, which this codec
        // does only for the charts OdtChartPreview knows.
        foreach (string mediaType in new[] { "application/vnd.oasis.opendocument.chart", "application/vnd.oasis.opendocument.spreadsheet" })
        {
            DocumentReadResult result = Read(OdtTestPackage.FromBody(ObjectFrame("Object 1"),
                manifestInnerXml: ObjectManifest("Object 1", mediaType)), options: null);

            InlineImage image = OnlyImage(result);
            Assert.True(image.IsMissingPayload);
            Assert.False(image.MarksMissingPayload);
        }
    }

    [Fact]
    public void A_Wmf_Picture_Is_Rasterized_Instead_Of_Marked_Unreadable()
    {
        // bnc834035.odt's clip art and tdf131537.odt's OLE icon are WMFs. The
        // DOCX reader already drew them through WmfRasterizer; this one
        // reported a format it did not carry and drew the marked box.
        DocumentReadResult result = OdtTestPackage.ReadWithPictures(
            "<text:p><draw:frame text:anchor-type=\"as-char\" svg:width=\"2cm\" svg:height=\"2cm\">" +
            "<draw:image xlink:href=\"Pictures/clip.wmf\"/></draw:frame></text:p>",
            new Dictionary<string, byte[]> { ["Pictures/clip.wmf"] = RedSquareWmf() });

        InlineImage image = OnlyImage(result);
        Assert.False(image.IsMissingPayload);
        Assert.Equal("image/png", image.ContentType);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "odt.image.format");
    }

    /// <summary>A placeable WMF, 1000 units an inch: one red polygon over the whole picture.</summary>
    private static byte[] RedSquareWmf()
    {
        using var records = new MemoryStream();
        using (var w = new BinaryWriter(records, Encoding.Latin1, leaveOpen: true))
        {
            void Record(ushort function, params short[] parameters)
            {
                w.Write(3 + parameters.Length);
                w.Write(function);
                foreach (short value in parameters)
                    w.Write(value);
            }
            Record(0x020B, 0, 0);       // SETWINDOWORG
            Record(0x020C, 1000, 1000); // SETWINDOWEXT
            w.Write(7); w.Write((ushort)0x02FC); w.Write((ushort)0); w.Write(0x0000FF); w.Write((ushort)0); // red brush, slot 0
            Record(0x012D, 0);          // SELECTOBJECT
            Record(0x0324, 4, 0, 0, 1000, 0, 1000, 1000, 0, 1000); // POLYGON
            Record(0x0000);             // EOF
        }

        using var output = new MemoryStream();
        using (var w = new BinaryWriter(output))
        {
            w.Write(0x9AC6CDD7u);
            w.Write((ushort)0);
            w.Write((short)0); w.Write((short)0); w.Write((short)1000); w.Write((short)1000);
            w.Write((ushort)1000);
            w.Write(0u);
            w.Write((ushort)0);
            w.Write((ushort)1); w.Write((ushort)9); w.Write((ushort)0x0300);
            w.Write((int)((18 + records.Length) / 2));
            w.Write((ushort)1); w.Write(0); w.Write((ushort)0);
            w.Write(records.ToArray());
        }
        return output.ToArray();
    }

    [Fact]
    public void A_Formula_Written_Into_Its_Frame_Prints_As_An_Empty_Frame()
    {
        // math-bold-italic-export.fodt and tdf157533.fodt: the draw:object holds
        // MathML and the draw:image beside it a replacement, which LibreOffice
        // 24.2 does not print.
        string flat =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<office:document " + OdtTestPackage.NamespaceDeclarations +
            " office:version=\"1.3\" office:mimetype=\"application/vnd.oasis.opendocument.text\">" +
            "<office:body><office:text><text:p><draw:frame text:anchor-type=\"as-char\" svg:width=\"1cm\" svg:height=\"1cm\">" +
            "<draw:object><math xmlns=\"http://www.w3.org/1998/Math/MathML\"><mi>a</mi></math></draw:object>" +
            "<draw:image><office:binary-data>" + Convert.ToBase64String(OdtTestPackage.OnePixelPng) + "</office:binary-data></draw:image>" +
            "</draw:frame></text:p></office:text></office:body></office:document>";

        DocumentReadResult result = new OdtDocumentCodec().Read(new MemoryStream(Encoding.UTF8.GetBytes(flat)));

        InlineImage image = OnlyImage(result);
        Assert.True(image.IsMissingPayload);
        Assert.False(image.MarksMissingPayload);
    }

    [Fact]
    public void A_Flat_Document_Has_No_Package_To_Step_Out_Of()
    {
        var asked = new List<string>();
        var options = new OdtReadOptions(DocumentReadOptions.Default, name =>
        {
            asked.Add(name);
            return OdtTestPackage.OnePixelPng;
        });
        string flat =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<office:document " + OdtTestPackage.NamespaceDeclarations +
            " office:version=\"1.3\" office:mimetype=\"application/vnd.oasis.opendocument.text\">" +
            "<office:body><office:text>" + Frame("../linked_graphic.png") + "</office:text></office:body></office:document>";

        DocumentReadResult result = new OdtDocumentCodec().Read(new MemoryStream(Encoding.UTF8.GetBytes(flat)), options);

        Assert.Empty(asked);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.image.external");
    }
}
