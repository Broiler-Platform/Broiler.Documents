using System.IO.Compression;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// DOCX charts and drawn shapes of office issue #238: area charts drawn from
/// their cached values, and the automatic value axis they are scaled to.
/// </summary>
public sealed class DocxOfficeIssue238Tests
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

    [Fact]
    public void A_Percent_Stacked_Area_Chart_Is_Drawn_As_Bands_That_Fill_The_Plot()
    {
        // testAreaChartNumberFormat.docx: two series stacked to 100%. The
        // first band runs from the axis to its share, the second from there
        // to the top, each in the colour its c:spPr states.
        (int width, int height, byte[] rgba) = RenderChart(
            """
            <c:chart><c:plotArea><c:areaChart><c:grouping val="percentStacked"/>
              <c:ser><c:spPr><a:solidFill><a:srgbClr val="0000FF"/></a:solidFill></c:spPr>
                <c:val><c:numLit><c:pt idx="0"><c:v>1</c:v></c:pt><c:pt idx="1"><c:v>1</c:v></c:pt></c:numLit></c:val></c:ser>
              <c:ser><c:spPr><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill></c:spPr>
                <c:val><c:numLit><c:pt idx="0"><c:v>3</c:v></c:pt><c:pt idx="1"><c:v>3</c:v></c:pt></c:numLit></c:val></c:ser>
            </c:areaChart><c:valAx><c:crossBetween val="midCat"/></c:valAx></c:plotArea></c:chart>
            """);

        // The plot runs from 4% to 91% of the height; the first series holds
        // a quarter of it.
        int x = width / 2;
        Assert.Equal((0, 0, 255), Pixel(rgba, width, x, (int)(height * 0.87)));
        Assert.Equal((255, 0, 0), Pixel(rgba, width, x, (int)(height * 0.5)));
        Assert.Equal((255, 0, 0), Pixel(rgba, width, x, (int)(height * 0.06)));
    }

    [Fact]
    public void A_Standard_Area_Chart_Draws_Its_First_Series_In_Front()
    {
        // tdf82824.docx: the later series shows only where it rises above
        // the first. Colours come from the theme's accents when the series
        // states none.
        (int width, int height, byte[] rgba) = RenderChart(
            """
            <c:chart><c:plotArea><c:areaChart><c:grouping val="standard"/>
              <c:ser><c:spPr><a:solidFill><a:srgbClr val="00FF00"/></a:solidFill></c:spPr>
                <c:val><c:numLit><c:pt idx="0"><c:v>30</c:v></c:pt><c:pt idx="1"><c:v>10</c:v></c:pt></c:numLit></c:val></c:ser>
              <c:ser><c:spPr><a:solidFill><a:srgbClr val="0000FF"/></a:solidFill></c:spPr>
                <c:val><c:numLit><c:pt idx="0"><c:v>20</c:v></c:pt><c:pt idx="1"><c:v>20</c:v></c:pt></c:numLit></c:val></c:ser>
            </c:areaChart><c:valAx><c:crossBetween val="midCat"/></c:valAx></c:plotArea></c:chart>
            """);

        Assert.Equal((0, 255, 0), Pixel(rgba, width, (int)(width * 0.1), (int)(height * 0.85)));
        Assert.Equal((0, 0, 255), Pixel(rgba, width, (int)(width * 0.9), (int)(height * 0.5)));
        Assert.Equal((0, 255, 0), Pixel(rgba, width, (int)(width * 0.9), (int)(height * 0.85)));
    }

    [Theory]
    // tdf130031: values 23 and 32 start at zero and step by 5.
    [InlineData(23, 32, 0, 35, 5)]
    // tdf130380: 5 and 6 lie within a sixth of the largest, so the axis
    // starts half the range below the smallest.
    [InlineData(5, 6, 4.4, 6.2, 0.2)]
    [InlineData(0, 1, 0, 1.2, 0.2)]
    [InlineData(-10, 25, -15, 30, 5)]
    public void The_Automatic_Value_Axis_Matches_LibreOffices(double minimum, double maximum,
        double axisMinimum, double axisMaximum, double step)
    {
        (double low, double high, double unit) = DocxChartPreview.AutoScale(minimum, maximum);

        Assert.Equal(axisMinimum, low, 9);
        Assert.Equal(axisMaximum, high, 9);
        Assert.Equal(step, unit, 9);
    }

    [Fact]
    public void An_Area_Chart_Without_Cached_Values_Keeps_Its_Placeholder()
    {
        DocumentReadResult result = DocxTestPackage.ReadWithMedia(ChartDrawing, ChartRelationship,
            new Dictionary<string, byte[]>
            {
                ["word/charts/chart1.xml"] = System.Text.Encoding.UTF8.GetBytes(ChartSpace(
                    "<c:chart><c:plotArea><c:areaChart><c:grouping val=\"standard\"/>" +
                    "<c:ser><c:val><c:numRef><c:f>Sheet1!$B$2</c:f></c:numRef></c:val></c:ser>" +
                    "</c:areaChart></c:plotArea></c:chart>")),
            });

        InlineImage image = Assert.IsType<InlineImage>(
            Assert.Single(Assert.Single(result.Document.Paragraphs).Runs).Style.Image);
        Assert.True(image.IsMissingPayload);
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
