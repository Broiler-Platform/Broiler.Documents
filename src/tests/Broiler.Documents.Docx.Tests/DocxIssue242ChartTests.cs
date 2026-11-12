using System.IO.Compression;
using System.Xml.Linq;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// DOCX charts and canvases of office issue #242: clustered bar charts, 3D
/// column charts and clustered column charts drawn from their cached values
/// with LibreOffice's layout, Strict OOXML colour percentages, and locked
/// canvases scaled to their graphic frame.
/// </summary>
public sealed class DocxIssue242ChartTests
{
    private const string ChartRelationship =
        "<Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart' Target='charts/chart1.xml'/>";

    private const string ChartDrawing =
        "<w:p><w:r><w:drawing>" +
        "<wp:inline xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        "xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\">" +
        "<wp:extent cx=\"2540000\" cy=\"1270000\"/>" +
        "<wp:docPr id=\"1\" name=\"Chart 1\"/>" +
        "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\">" +
        "<c:chart r:id=\"rId1\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"/>" +
        "</a:graphicData></a:graphic>" +
        "</wp:inline>" +
        "</w:drawing></w:r></w:p>";

    private const string TwoSeries =
        """
        <c:ser><c:idx val="0"/><c:spPr><a:solidFill><a:srgbClr val="0000FF"/></a:solidFill></c:spPr>
          <c:val><c:numLit><c:pt idx="0"><c:v>4</c:v></c:pt></c:numLit></c:val></c:ser>
        <c:ser><c:idx val="1"/><c:spPr><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill></c:spPr>
          <c:val><c:numLit><c:pt idx="0"><c:v>2</c:v></c:pt></c:numLit></c:val></c:ser>
        """;

    [Fact]
    public void A_Horizontal_Bar_Chart_Draws_Its_First_Series_Lowest_From_The_Left()
    {
        // tdf128996.docx: c:barDir="bar" lays the categories bottom to top,
        // the first series lowest in its category; values run to the right.
        (int width, int height, byte[] rgba) = RenderChart(
            "<c:chart><c:autoTitleDeleted val=\"1\"/><c:plotArea><c:barChart><c:barDir val=\"bar\"/>" +
            "<c:grouping val=\"clustered\"/><c:gapWidth val=\"100\"/>" + TwoSeries +
            "</c:barChart><c:catAx><c:delete val=\"1\"/></c:catAx><c:valAx><c:delete val=\"1\"/></c:valAx></c:plotArea></c:chart>");

        // The axis runs 0 to 4.5: the blue bar (4) reaches near the right,
        // the red one (2) under halfway; blue is below red.
        int blueRow = FindRow(rgba, width, height, (0, 0, 255));
        int redRow = FindRow(rgba, width, height, (255, 0, 0));
        Assert.True(blueRow > redRow, $"blue {blueRow}, red {redRow}");
        Assert.Equal((0, 0, 255), Pixel(rgba, width, width * 3 / 4, blueRow));
        Assert.NotEqual((255, 0, 0), Pixel(rgba, width, width * 3 / 4, redRow));
        Assert.Equal((255, 0, 0), Pixel(rgba, width, width / 3, redRow));
    }

    [Fact]
    public void A_3D_Column_Shows_A_Lit_Front_And_A_Shaded_Side()
    {
        // chart-dupe.docx: c:bar3DChart columns are boxes whose front is lit
        // and whose right side is in shade, set back in the chart's depth.
        (int width, int height, byte[] rgba) = RenderChart(
            """
            <c:chart><c:autoTitleDeleted val="1"/><c:view3D><c:rotX val="15"/><c:rotY val="20"/><c:rAngAx val="1"/></c:view3D>
              <c:plotArea><c:bar3DChart><c:barDir val="col"/><c:grouping val="clustered"/>
              <c:ser><c:idx val="0"/><c:spPr><a:solidFill><a:srgbClr val="804020"/></a:solidFill></c:spPr>
                <c:val><c:numLit><c:pt idx="0"><c:v>5</c:v></c:pt></c:numLit></c:val></c:ser>
              <c:shape val="box"/></c:bar3DChart><c:catAx><c:delete val="1"/></c:catAx><c:valAx><c:delete val="1"/></c:valAx></c:plotArea></c:chart>
            """);

        var colors = new HashSet<(int, int, int)>();
        for (int x = 0; x < width; x++)
            colors.Add(Pixel(rgba, width, x, height * 2 / 3));
        Assert.Contains((0x8D, 0x46, 0x23), colors); // front, 1.1 x
        Assert.Contains((0x70, 0x38, 0x1C), colors); // side, 0.875 x
    }

    [Fact]
    public void A_3D_Chart_Axis_Takes_No_Headroom()
    {
        // chart-prop.docx: LibreOffice's 3D axis ends at the largest value,
        // 5, where a 2D one widens to 6.
        Assert.Equal((0d, 5d, 0.5d), DocxChartPreview.AutoScale(0, 5, expand: false));
        Assert.Equal((0d, 6d, 1d), DocxChartPreview.AutoScale(0, 5));
    }

    [Fact]
    public void A_Single_Value_Is_Measured_From_Zero_With_As_Many_Intervals_As_Fit()
    {
        // long_legendentry.docx and xaxis-labelbreak.docx: one value of 4.3
        // gives 0 to 5, in halves on a tall axis and in units on a short one.
        Assert.Equal((0d, 5d, 0.5d), DocxChartPreview.AutoScale(4.3, 4.3));
        Assert.Equal((0d, 5d, 1d), DocxChartPreview.AutoScale(4.3, 4.3, maxIntervals: 6));
    }

    [Fact]
    public void Chart_Text_Scales_Against_A_16_By_9_Centimetre_Page()
    {
        Assert.Equal(1, DocxChartPreview.TextScale(16 / 2.54 * 72, 9 / 2.54 * 72), 6);
        Assert.Equal(0.5, DocxChartPreview.TextScale(8 / 2.54 * 72, 9 / 2.54 * 72), 6);
        Assert.Equal(0.5, DocxChartPreview.TextScale(16 / 2.54 * 72, 4.5 / 2.54 * 72), 6);
    }

    [Fact]
    public void A_Clustered_Column_Chart_Leaves_Room_For_Its_Labels_And_Legend()
    {
        XNamespace c = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        XElement root = XElement.Parse(ChartSpace(
            "<c:chart><c:autoTitleDeleted val=\"1\"/><c:plotArea><c:barChart><c:barDir val=\"col\"/>" + TwoSeries +
            "</c:barChart><c:catAx/><c:valAx/></c:plotArea><c:legend><c:legendPos val=\"r\"/></c:legend></c:chart>"));
        XElement chart = root.Element(c + "chart")!;
        DocxChartPreview.BarLayout layout = DocxChartPreview.LayoutBars(root, chart, chart.Element(c + "plotArea")!,
            ["Series 1", "Series 2"], [], 1, 2, horizontal: false, threeD: false, 0, 4.5, 0.5, 150, 16 / 2.54 * 72, 9 / 2.54 * 72);

        // A 2% margin, then the value labels at the left and the legend at the right.
        Assert.InRange(layout.Left, 25, 35);
        Assert.InRange(layout.Right, 380, 420);
        Assert.Equal(0, layout.DepthX);
    }

    [Fact]
    public void Strict_Colour_Percentages_Modulate_Like_Transitional_Values()
    {
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XElement Fill(string modulation, string offset) => new("fill",
            new XElement(a + "srgbClr", new XAttribute("val", "000000"),
                new XElement(a + "lumMod", new XAttribute("val", modulation)),
                new XElement(a + "lumOff", new XAttribute("val", offset))));

        Assert.True(DocxDrawingColor.TryRead(Fill("15%", "85%"), DocxTheme.Empty, out var strict));
        Assert.True(DocxDrawingColor.TryRead(Fill("15000", "85000"), DocxTheme.Empty, out var transitional));
        Assert.Equal(transitional, strict);
        Assert.True(strict.R > 200);
    }

    [Fact]
    public void A_Locked_Canvas_Scales_Its_Shapes_To_Its_Frame()
    {
        // fdo76249.docx: a canvas whose group is 4 inches wide inside a
        // 1-inch frame draws its shapes at a quarter of their size.
        string body =
            "<w:p><w:r><w:drawing>" +
            "<wp:inline xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\">" +
            "<wp:extent cx=\"914400\" cy=\"914400\"/><wp:docPr id=\"1\" name=\"Canvas\"/>" +
            "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
            "<a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/lockedCanvas\">" +
            "<lc:lockedCanvas xmlns:lc=\"http://schemas.openxmlformats.org/drawingml/2006/lockedCanvas\">" +
            "<a:nvGrpSpPr><a:cNvPr id=\"0\" name=\"\"/><a:cNvGrpSpPr/></a:nvGrpSpPr>" +
            "<a:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"3657600\" cy=\"3657600\"/>" +
            "<a:chOff x=\"914400\" y=\"0\"/><a:chExt cx=\"3657600\" cy=\"3657600\"/></a:xfrm></a:grpSpPr>" +
            "<a:sp><a:nvSpPr><a:cNvPr id=\"2\" name=\"Box\"/><a:cNvSpPr/></a:nvSpPr>" +
            "<a:spPr><a:xfrm><a:off x=\"2743200\" y=\"1828800\"/><a:ext cx=\"1828800\" cy=\"914400\"/></a:xfrm>" +
            "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:solidFill><a:srgbClr val=\"FF0000\"/></a:solidFill></a:spPr>" +
            "</a:sp></lc:lockedCanvas></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>";

        DocumentReadResult result = DocxTestPackage.ReadBody(body);

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.Equal(36, shape.Width, 3);
        Assert.Equal(18, shape.Height, 3);
    }

    private static int FindRow(byte[] rgba, int width, int height, (int, int, int) color)
    {
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (Pixel(rgba, width, x, y) == color)
                    return y + 2;
        return -1;
    }

    private static (int Width, int Height, byte[] Rgba) RenderChart(string chart)
    {
        DocumentReadResult result = DocxTestPackage.ReadWithMedia(ChartDrawing, ChartRelationship,
            new Dictionary<string, byte[]> { ["word/charts/chart1.xml"] = System.Text.Encoding.UTF8.GetBytes(ChartSpace(chart)) });

        InlineImage image = Assert.IsType<InlineImage>(
            Assert.Single(Assert.Single(result.Document.Paragraphs).Runs).Style.Image);
        Assert.False(image.IsMissingPayload);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.chart.preview");
        return DecodeRasterizedPng(image.Data.ToArray());
    }

    private static string ChartSpace(string chart) =>
        "<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" " +
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" + chart + "</c:chartSpace>";

    private static (int R, int G, int B) Pixel(byte[] rgba, int width, int x, int y)
    {
        int at = (y * width + x) * 4;
        return (rgba[at], rgba[at + 1], rgba[at + 2]);
    }

    /// <summary>Decodes the unfiltered RGBA PNG the chart preview writes.</summary>
    private static (int Width, int Height, byte[] Rgba) DecodeRasterizedPng(byte[] png)
    {
        int width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16));
        int height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20));
        using var idat = new MemoryStream();
        for (int at = 8; at + 8 <= png.Length;)
        {
            int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            if (png.AsSpan(at + 4, 4).SequenceEqual("IDAT"u8))
                idat.Write(png, at + 8, length);
            at += 12 + length;
        }
        idat.Position = 0;
        using var inflate = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        inflate.CopyTo(raw);
        byte[] rows = raw.ToArray();
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            Array.Copy(rows, y * (width * 4 + 1) + 1, rgba, y * width * 4, width * 4);
        return (width, height, rgba);
    }
}
