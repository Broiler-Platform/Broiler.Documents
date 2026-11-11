namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Office conformance issue 236: charts and OLE objects whose replacement
/// picture is a StarView metafile (VCLMTF) or an EMF, stored without an
/// extension under <c>ObjectReplacements/</c> (tdf130242.odt, tdf159443.odt,
/// embedded_chart.odt, ole1-pres-data-wmf.odt).
/// </summary>
public sealed class OdtIssue236ImageTests
{
    private const string Replacement = "ObjectReplacements/Object 1";

    private static readonly string Manifest =
        "<manifest:file-entry manifest:full-path=\"/\" manifest:media-type=\"application/vnd.oasis.opendocument.text\"/>" +
        "<manifest:file-entry manifest:full-path=\"content.xml\" manifest:media-type=\"text/xml\"/>" +
        "<manifest:file-entry manifest:full-path=\"ObjectReplacements/Object 1\" " +
        "manifest:media-type=\"application/x-openoffice-gdimetafile;windows_formatname=&quot;GDIMetaFile&quot;\"/>";

    private static string ObjectFrame(string objectElement = "draw:object") =>
        "<text:p><draw:frame draw:name=\"Object1\" text:anchor-type=\"as-char\" svg:width=\"2in\" svg:height=\"1in\">" +
        "<" + objectElement + " xlink:href=\"./Object 1\" xlink:type=\"simple\" xlink:show=\"embed\"/>" +
        "<draw:image xlink:href=\"./" + Replacement + "\" xlink:type=\"simple\" xlink:show=\"embed\"/>" +
        "</draw:frame></text:p>";

    [Fact]
    public void A_Chart_Replacement_Metafile_Is_Rasterized_Instead_Of_Marked_Missing()
    {
        DocumentReadResult result = OdtTestPackage.ReadWithPictures(ObjectFrame(),
            new Dictionary<string, byte[]> { [Replacement] = Svm(fillRgb: 0x004586) }, Manifest);

        InlineImage image = Assert.IsType<InlineImage>(Assert.Single(result.Document.Paragraphs).StyleAt(0).Image);
        Assert.False(image.IsMissingPayload);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal(144, image.Width);
        Assert.Equal(72, image.Height);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "odt.image.format");
    }

    [Fact]
    public void An_Ole_Objects_Emf_Replacement_Is_Rasterized()
    {
        DocumentReadResult result = OdtTestPackage.ReadWithPictures(ObjectFrame("draw:object-ole"),
            new Dictionary<string, byte[]> { [Replacement] = Emf() }, Manifest);

        InlineImage image = Assert.IsType<InlineImage>(Assert.Single(result.Document.Paragraphs).StyleAt(0).Image);
        Assert.False(image.IsMissingPayload);
        Assert.Equal("image/png", image.ContentType);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "odt.image.format");
    }

    [Fact]
    public void A_Metafile_That_Draws_Nothing_Understood_Keeps_The_Format_Mark()
    {
        // Text alone is not drawn, so a metafile of nothing else is still a
        // picture this codec does not carry, and the page shows where it was.
        DocumentReadResult result = OdtTestPackage.ReadWithPictures(ObjectFrame(),
            new Dictionary<string, byte[]> { [Replacement] = Svm(fillRgb: null) }, Manifest);

        InlineImage image = Assert.IsType<InlineImage>(Assert.Single(result.Document.Paragraphs).StyleAt(0).Image);
        Assert.True(image.IsMissingPayload);
        Assert.True(image.MarksMissingPayload);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.image.format");
    }

    /// <summary>
    /// A VCLMTF in 1/100 mm, 2000 x 1000, with one filled rectangle when
    /// <paramref name="fillRgb"/> is given and only a fill colour otherwise.
    /// </summary>
    private static byte[] Svm(int? fillRgb)
    {
        using var actions = new MemoryStream();
        using var a = new BinaryWriter(actions);
        int count = 0;
        void Action(ushort type, byte[] payload)
        {
            a.Write(type);
            a.Write((ushort)1);
            a.Write((uint)payload.Length);
            a.Write(payload);
            count++;
        }
        Action(133, [.. BitConverter.GetBytes(fillRgb ?? 0xFF0000), 1]); // FILLCOLOR
        if (fillRgb is not null)
        {
            // RECT left, top, right, bottom
            Action(103, [.. BitConverter.GetBytes(0), .. BitConverter.GetBytes(0),
                .. BitConverter.GetBytes(1000), .. BitConverter.GetBytes(1000)]);
        }

        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write("VCLMTF"u8);
        w.Write((ushort)1);
        w.Write(49u);
        w.Write(0u);                                // compression
        w.Write((ushort)1); w.Write(27u);           // map mode compat header
        w.Write((ushort)0);                         // 1/100 mm
        w.Write(0); w.Write(0);                     // origin
        w.Write(1); w.Write(1); w.Write(1); w.Write(1);
        w.Write((byte)1);
        w.Write(2000); w.Write(1000);               // preferred size
        w.Write((uint)count);
        a.Flush();
        w.Write(actions.ToArray());
        w.Flush();
        return stream.ToArray();
    }

    /// <summary>A one-inch EMF holding one red POLYGON16, as the RTF tests build it.</summary>
    private static byte[] Emf()
    {
        var emf = new List<byte>();
        void Int(int value) => emf.AddRange(BitConverter.GetBytes(value));
        void Short(short value) => emf.AddRange(BitConverter.GetBytes(value));
        Int(1); Int(88); Int(0); Int(0); Int(25); Int(25); Int(0); Int(0); Int(2540); Int(2540);
        Int(0x464D4520); Int(0x10000); Int(0); Int(5); Int(2); Int(0); Int(0); Int(0);
        Int(100); Int(100); Int(100); Int(100);
        Int(39); Int(24); Int(1); Int(0); Int(0x0000FF); Int(0); // CREATEBRUSHINDIRECT red
        Int(37); Int(12); Int(1);                                  // SELECTOBJECT brush
        Int(86); Int(44); Int(0); Int(0); Int(25); Int(25); Int(4); // POLYGON16
        Short(0); Short(0); Short(25); Short(0); Short(25); Short(25); Short(0); Short(25);
        Int(14); Int(20); Int(0); Int(0); Int(20);                  // EOF
        return [.. emf];
    }
}
