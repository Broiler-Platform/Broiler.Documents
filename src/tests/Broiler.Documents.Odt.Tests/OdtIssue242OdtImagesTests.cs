using System.Text;

namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Office conformance issue 242, ODT pictures and charts: an SVG picture drawn
/// (image_anchored_to_paragraph_no_wrap.fodt) or blank (tdf128820.fodt), the
/// best of a frame's alternatives, a grouped picture's own text drawn at the
/// start of its line, and a bar chart with no replacement drawn from its data
/// (horizontal_multilevel.odt, tdf127304.odt).
/// </summary>
public sealed class OdtIssue242OdtImagesTests
{
    private const string DiagonalSvg =
        "<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 20 20\" stroke=\"black\"><path d=\"M1,1L19,19\"/></svg>";

    private static string Base64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    private static InlineImage OnlyImage(DocumentReadResult result) =>
        Assert.IsType<InlineImage>(Assert.Single(result.Document.Paragraphs).StyleAt(0).Image);

    [Fact]
    public void An_Svg_Picture_Is_Drawn()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p><draw:frame text:anchor-type=\"as-char\" svg:width=\"17cm\" svg:height=\"13cm\">" +
            "<draw:image draw:mime-type=\"image/svg+xml\"><office:binary-data>" + Base64(DiagonalSvg) +
            "</office:binary-data></draw:image></draw:frame></text:p>");

        InlineImage image = OnlyImage(result);
        Assert.False(image.IsMissingPayload);
        Assert.Equal("image/png", image.ContentType);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "odt.image.format");
    }

    [Fact]
    public void An_Empty_Svg_Leaves_An_Unmarked_Empty_Frame()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p><draw:frame text:anchor-type=\"as-char\" svg:width=\"1cm\" svg:height=\"1cm\"><draw:image><office:binary-data>" +
            "PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciLz4</office:binary-data></draw:image></draw:frame></text:p>");

        InlineImage image = OnlyImage(result);
        Assert.True(image.IsMissingPayload);
        Assert.False(image.MarksMissingPayload);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "odt.image.format");
    }

    [Fact]
    public void The_Svg_Alternative_Wins_Over_A_Bitmap_Before_It()
    {
        // fdo68839.odt's frame names a PNG, then an SVG; LibreOffice keeps the SVG.
        DocumentReadResult result = OdtTestPackage.ReadWithPictures(
            "<text:p><draw:frame text:anchor-type=\"as-char\" svg:width=\"2cm\" svg:height=\"2cm\">" +
            "<draw:image xlink:href=\"Pictures/a.png\"/><draw:image xlink:href=\"Pictures/b.svg\"/></draw:frame></text:p>",
            new Dictionary<string, byte[]>
            {
                ["Pictures/a.png"] = OdtTestPackage.OnePixelPng,
                ["Pictures/b.svg"] = Encoding.UTF8.GetBytes(DiagonalSvg),
            });

        InlineImage image = OnlyImage(result);
        Assert.NotEqual(OdtTestPackage.OnePixelPng, image.Data.ToArray());
        var bitmap = new Issue242Png(image.Data.ToArray());
        Assert.Equal(1024, bitmap.Width);
    }

    [Fact]
    public void A_Bitmap_Stays_When_The_Svg_Cannot_Be_Drawn()
    {
        string withText = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 9 9\"><text>t</text></svg>";
        DocumentReadResult result = OdtTestPackage.ReadWithPictures(
            "<text:p><draw:frame text:anchor-type=\"as-char\" svg:width=\"2cm\" svg:height=\"2cm\">" +
            "<draw:image xlink:href=\"Pictures/b.svg\"/><draw:image xlink:href=\"Pictures/a.png\"/></draw:frame></text:p>",
            new Dictionary<string, byte[]>
            {
                ["Pictures/a.png"] = OdtTestPackage.OnePixelPng,
                ["Pictures/b.svg"] = Encoding.UTF8.GetBytes(withText),
            });

        Assert.Equal(OdtTestPackage.OnePixelPng, OnlyImage(result).Data.ToArray());
    }

    [Fact]
    public void A_Grouped_Picture_Draws_Its_Own_Text_From_The_Group_Corner()
    {
        // tdf128820.fodt: an as-char group whose one member sits at 1 cm, 1 cm.
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p><draw:g text:anchor-type=\"as-char\"><draw:frame svg:width=\"1cm\" svg:height=\"1cm\" svg:x=\"1cm\" svg:y=\"1cm\">" +
            "<draw:image><office:binary-data>PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciLz4</office:binary-data>" +
            "<text:p>json</text:p></draw:image></draw:frame></draw:g></text:p>");

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.Equal("json", Assert.Single(shape.Paragraphs).Text);
        Assert.True(shape.IsAsCharacter);
        Assert.True(shape.IsCustomShape);
        Assert.Equal(0, shape.OffsetX, 3);
        Assert.Equal(0, shape.OffsetY, 3);
        Assert.NotNull(shape.Image);
        Assert.False(shape.Image.MarksMissingPayload);
    }

    [Fact]
    public void A_Writer_Picture_Frame_Outside_A_Group_Has_No_Text()
    {
        DocumentReadResult result = OdtTestPackage.ReadWithPictures(
            "<text:p><draw:frame text:anchor-type=\"paragraph\" svg:width=\"1cm\" svg:height=\"1cm\">" +
            "<draw:image xlink:href=\"Pictures/a.png\"><text:p>json</text:p></draw:image></draw:frame></text:p>",
            new Dictionary<string, byte[]> { ["Pictures/a.png"] = OdtTestPackage.OnePixelPng });

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.Empty(shape.Paragraphs);
    }

    [Fact]
    public void Members_Of_An_As_Character_Group_Keep_Their_Places_From_Its_Corner()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p><draw:g text:anchor-type=\"as-char\">" +
            "<draw:custom-shape svg:x=\"2cm\" svg:y=\"3cm\" svg:width=\"1cm\" svg:height=\"1cm\"><text:p>a</text:p></draw:custom-shape>" +
            "<draw:custom-shape svg:x=\"4cm\" svg:y=\"5cm\" svg:width=\"1cm\" svg:height=\"1cm\"><text:p>b</text:p></draw:custom-shape>" +
            "</draw:g></text:p>");

        DocumentShape[] shapes = [.. result.Document.Shapes.OrderBy(shape => shape.OffsetX)];
        Assert.Equal(2, shapes.Length);
        Assert.Equal((0, 0), (Math.Round(shapes[0].OffsetX, 3), Math.Round(shapes[0].OffsetY, 3)));
        Assert.Equal((56.693, 56.693), (Math.Round(shapes[1].OffsetX, 3), Math.Round(shapes[1].OffsetY, 3)));
    }

    private const string ChartNamespaces =
        "xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
        "xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" " +
        "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" " +
        "xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" " +
        "xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\" " +
        "xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" " +
        "xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\" " +
        "xmlns:chart=\"urn:oasis:names:tc:opendocument:xmlns:chart:1.0\" " +
        "xmlns:chartooo=\"http://openoffice.org/2010/chart\"";

    /// <summary>
    /// A 10 x 10 cm two-series column chart: a 6 x 6 cm saved inner region at
    /// (2, 1) cm inside an outer plot area from 0.2 cm to 9.8 cm, values 7.2 and
    /// 4 for one category, categories in column A.
    /// </summary>
    private static string ChartContent(string chartClass = "chart:bar", bool autoPosition = false, string categoryCell = "<text:p>one</text:p>") =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?><office:document-content " + ChartNamespaces + " office:version=\"1.3\">" +
        "<office:automatic-styles>" +
        "<style:style style:name=\"ch3\" style:family=\"chart\"><style:chart-properties chart:auto-position=\"" +
        (autoPosition ? "true" : "false") + "\"/></style:style>" +
        "<style:style style:name=\"ch7\" style:family=\"chart\"><style:graphic-properties draw:stroke=\"none\" draw:fill-color=\"#ff0000\"/></style:style>" +
        "</office:automatic-styles><office:body><office:chart>" +
        "<chart:chart svg:width=\"10cm\" svg:height=\"10cm\" chart:class=\"" + chartClass + "\">" +
        "<chart:plot-area chart:style-name=\"ch3\" svg:x=\"0.2cm\" svg:y=\"0.2cm\" svg:width=\"9.6cm\" svg:height=\"9.6cm\">" +
        "<chartooo:coordinate-region svg:x=\"2cm\" svg:y=\"1cm\" svg:width=\"6cm\" svg:height=\"6cm\"/>" +
        "<chart:axis chart:dimension=\"x\" chart:name=\"primary-x\"><chart:categories table:cell-range-address=\"local-table.$A$2:.$A$2\"/></chart:axis>" +
        "<chart:axis chart:dimension=\"y\" chart:name=\"primary-y\"><chart:grid chart:class=\"major\"/></chart:axis>" +
        "<chart:series chart:style-name=\"ch7\" chart:values-cell-range-address=\"local-table.$B$2:.$B$2\" chart:class=\"chart:bar\"/>" +
        "<chart:series chart:values-cell-range-address=\"local-table.$C$2:.$C$2\" chart:class=\"chart:bar\"/>" +
        "</chart:plot-area><table:table table:name=\"local-table\"><table:table-header-rows><table:table-row>" +
        "<table:table-cell/><table:table-cell office:value-type=\"string\"><text:p>A</text:p></table:table-cell>" +
        "<table:table-cell office:value-type=\"string\"><text:p>B</text:p></table:table-cell></table:table-row></table:table-header-rows>" +
        "<table:table-rows><table:table-row><table:table-cell office:value-type=\"string\">" + categoryCell + "</table:table-cell>" +
        "<table:table-cell office:value-type=\"float\" office:value=\"7.2\"><text:p>7.2</text:p></table:table-cell>" +
        "<table:table-cell office:value-type=\"float\" office:value=\"4\"><text:p>4</text:p></table:table-cell>" +
        "</table:table-row></table:table-rows></table:table></chart:chart></office:chart></office:body></office:document-content>";

    private static DocumentReadResult ReadChart(string content)
    {
        byte[] package = OdtTestPackage.FromBody(
            "<text:p><draw:frame text:anchor-type=\"as-char\" svg:width=\"10cm\" svg:height=\"10cm\">" +
            "<draw:object xlink:href=\"./Object 1\"/><draw:image xlink:href=\"./ObjectReplacements/Object 1\"/></draw:frame></text:p>",
            extraParts: new Dictionary<string, string> { ["Object 1/content.xml"] = content });
        using var stream = new MemoryStream(package, writable: false);
        return new OdtDocumentCodec().Read(stream);
    }

    /// <summary>The first and last rows of a column of the chart's red pixels.</summary>
    private static (int Top, int Bottom) RedColumn(InlineImage image, out int width)
    {
        var bitmap = new Issue242Png(image.Data.ToArray());
        width = bitmap.Width;
        int top = -1, bottom = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A == 255 && pixel.R == 255 && pixel.G == 0 && pixel.B == 0)
                {
                    if (top < 0)
                        top = y;
                    bottom = y;
                    break;
                }
            }
        }
        return (top, bottom);
    }

    [Fact]
    public void A_Column_Chart_Without_A_Replacement_Is_Drawn_From_Its_Data()
    {
        DocumentReadResult result = ReadChart(ChartContent());

        InlineImage image = OnlyImage(result);
        Assert.False(image.IsMissingPayload);
        (int top, int bottom) = RedColumn(image, out int width);
        // The series' red column stands from the region's foot (7 cm) to
        // 7.2 on an automatic 0 to 8 axis: 1.6 cm from the top.
        double pixelsPerCm = width / 10.0;
        Assert.InRange(top, (int)(1.6 * pixelsPerCm) - 2, (int)(1.6 * pixelsPerCm) + 2);
        Assert.InRange(bottom, (int)(7 * pixelsPerCm) - 2, (int)(7 * pixelsPerCm) + 2);
    }

    [Fact]
    public void An_Automatically_Placed_Plot_Is_Laid_Out_Afresh_Above_Its_Labels()
    {
        // Three category levels, the innermost too wide for its category: it is
        // turned, and the plot ends above 2 x 1.117 x 10 pt rows, the turned
        // labels' width, a tick and a spacing, from the outer area's foot.
        string levels = "<text:p>x</text:p><text:list><text:list-item><text:p>January</text:p></text:list-item>" +
            "<text:list-item><text:p>One</text:p></text:list-item><text:list-item><text:p>2018.01.01.2018.01.01.2018.01.01.2018.01.01.</text:p>" +
            "</text:list-item></text:list>";
        InlineImage image = OnlyImage(ReadChart(ChartContent(autoPosition: true, categoryCell: levels)));

        (_, int bottom) = RedColumn(image, out int width);
        double pointsPerPixel = 10 / 2.54 * 72 / width;
        double label = OdtChartPreview.TextWidth("2018.01.01.2018.01.01.2018.01.01.2018.01.01.", 10);
        double expected = 9.8 / 2.54 * 72 - 2 * 11.17 - label - (150 + 100) * 72 / 2540.0;
        Assert.InRange(bottom * pointsPerPixel, expected - 1.5, expected + 1.5);
    }

    [Fact]
    public void A_Chart_Of_Another_Kind_Keeps_Its_Empty_Frame()
    {
        InlineImage image = OnlyImage(ReadChart(ChartContent(chartClass: "chart:circle")));
        Assert.True(image.IsMissingPayload);
        Assert.False(image.MarksMissingPayload);
    }

    [Theory]
    [InlineData(0, 9.1, 0, 10, 1)]
    [InlineData(0, 93, 0, 100, 10)]
    [InlineData(-3, 7, -3, 7, 1)]
    [InlineData(0, 0.42, 0, 0.45, 0.05)]
    [InlineData(0, 1234, 0, 1400, 200)]
    public void The_Value_Axis_Is_Scaled_As_LibreOffice_Scales_It(double minimum, double maximum, double low, double high, double interval)
    {
        (double Minimum, double Maximum, double Interval) scale = OdtChartPreview.Scale(minimum, maximum, false, false, null);
        Assert.Equal(low, scale.Minimum, 9);
        Assert.Equal(high, scale.Maximum, 9);
        Assert.Equal(interval, scale.Interval, 9);
    }
}

/// <summary>Reads the unfiltered RGBA PNG the rasterizers write, for pixel checks.</summary>
internal sealed class Issue242Png
{
    private readonly byte[] _rgba;

    public Issue242Png(byte[] png)
    {
        Width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16));
        Height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20));
        using var idat = new MemoryStream();
        for (int at = 8; at < png.Length;)
        {
            int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            if (png.AsSpan(at + 4, 4).SequenceEqual("IDAT"u8))
                idat.Write(png, at + 8, length);
            at += 12 + length;
        }
        idat.Position = 0;
        using var inflater = new System.IO.Compression.ZLibStream(idat, System.IO.Compression.CompressionMode.Decompress);
        using var raw = new MemoryStream();
        inflater.CopyTo(raw);
        byte[] rows = raw.ToArray();
        _rgba = new byte[Width * Height * 4];
        for (int row = 0; row < Height; row++)
            Array.Copy(rows, row * (Width * 4 + 1) + 1, _rgba, row * Width * 4, Width * 4);
    }

    public int Width { get; }

    public int Height { get; }

    public (byte R, byte G, byte B, byte A) GetPixel(int x, int y)
    {
        int at = (y * Width + x) * 4;
        return (_rgba[at], _rgba[at + 1], _rgba[at + 2], _rgba[at + 3]);
    }
}
