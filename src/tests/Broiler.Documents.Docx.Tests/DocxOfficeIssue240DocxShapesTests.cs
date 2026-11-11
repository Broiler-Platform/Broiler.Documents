using System.IO.Compression;
using System.Xml.Linq;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// DOCX charts and drawn shapes of office issue #240: pie and stacked column
/// charts drawn from their cached values, VML preset paths that name their
/// guides (<c>@n</c>) and adjust values (<c>#n</c>), and bare stroke weights
/// read as EMUs.
/// </summary>
public sealed class DocxOfficeIssue240DocxShapesTests
{
    private const string Vml =
        "xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\"";

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
    public void A_Pie_Chart_Draws_Its_Slices_Clockwise_From_Twelve_OClock()
    {
        // tdf129054.docx and tdf137154.docx: each data point's c:dPt colour
        // fills its slice; the first starts at the top and runs clockwise.
        (int width, int height, byte[] rgba) = RenderChart(
            """
            <c:chart><c:autoTitleDeleted val="1"/><c:plotArea><c:pieChart><c:varyColors val="1"/>
              <c:ser>
                <c:dPt><c:idx val="0"/><c:spPr><a:solidFill><a:srgbClr val="0000FF"/></a:solidFill></c:spPr></c:dPt>
                <c:dPt><c:idx val="1"/><c:spPr><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill></c:spPr></c:dPt>
                <c:val><c:numLit><c:pt idx="0"><c:v>1</c:v></c:pt><c:pt idx="1"><c:v>3</c:v></c:pt></c:numLit></c:val>
              </c:ser><c:firstSliceAng val="0"/></c:pieChart></c:plotArea></c:chart>
            """);

        // A quarter for the first point, top right; the rest for the second.
        int centerX = width / 2, centerY = height / 2, reach = height / 5;
        Assert.Equal((0, 0, 255), Pixel(rgba, width, centerX + reach, centerY - reach));
        Assert.Equal((255, 0, 0), Pixel(rgba, width, centerX + reach, centerY + reach));
        Assert.Equal((255, 0, 0), Pixel(rgba, width, centerX - reach, centerY - reach));
        // Outside the pie the chart is white.
        Assert.Equal((255, 255, 255), Pixel(rgba, width, width / 10, centerY));
    }

    [Fact]
    public void A_Pie_Slice_Is_Outlined_With_The_Line_Its_Data_Point_States()
    {
        // tdf138018.docx: one slice of the whole pie; its white outline shows
        // as the radius from the centre to twelve o'clock.
        (int width, int height, byte[] rgba) = RenderChart(
            """
            <c:chart><c:autoTitleDeleted val="1"/><c:plotArea><c:pieChart><c:varyColors val="1"/>
              <c:ser>
                <c:dPt><c:idx val="0"/><c:spPr><a:solidFill><a:srgbClr val="0000FF"/></a:solidFill>
                  <a:ln w="38100"><a:solidFill><a:srgbClr val="FFFFFF"/></a:solidFill></a:ln></c:spPr></c:dPt>
                <c:val><c:numLit><c:pt idx="0"><c:v>8.2</c:v></c:pt></c:numLit></c:val>
              </c:ser></c:pieChart></c:plotArea></c:chart>
            """);

        int centerX = width / 2, centerY = height / 2;
        Assert.Equal((255, 255, 255), Pixel(rgba, width, centerX, centerY - height / 5));
        Assert.Equal((0, 0, 255), Pixel(rgba, width, centerX, centerY + height / 5));
    }

    [Fact]
    public void A_Percent_Stacked_Column_Chart_Fills_Each_Column_To_The_Top()
    {
        // tdf75659.docx: every column is stacked to 100%, the first series at
        // the bottom, in the colours the series' c:spPr state.
        (int width, int height, byte[] rgba) = RenderChart(
            """
            <c:chart><c:autoTitleDeleted val="1"/><c:plotArea><c:barChart><c:barDir val="col"/><c:grouping val="percentStacked"/>
              <c:ser><c:spPr><a:solidFill><a:srgbClr val="0000FF"/></a:solidFill></c:spPr>
                <c:val><c:numLit><c:pt idx="0"><c:v>1</c:v></c:pt></c:numLit></c:val></c:ser>
              <c:ser><c:spPr><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill></c:spPr>
                <c:val><c:numLit><c:pt idx="0"><c:v>3</c:v></c:pt></c:numLit></c:val></c:ser>
              <c:gapWidth val="150"/><c:overlap val="100"/>
            </c:barChart><c:valAx/></c:plotArea></c:chart>
            """);

        // The plot runs from 4% to 91% of the height; the first series holds
        // the bottom quarter of it.
        int x = width / 2;
        Assert.Equal((0, 0, 255), Pixel(rgba, width, x, (int)(height * 0.87)));
        Assert.Equal((255, 0, 0), Pixel(rgba, width, x, (int)(height * 0.5)));
        Assert.Equal((255, 0, 0), Pixel(rgba, width, x, (int)(height * 0.06)));
        // The gap beside the column is empty.
        Assert.Equal((255, 255, 255), Pixel(rgba, width, (int)(width * 0.15), (int)(height * 0.5)));
    }

    [Fact]
    public void A_Stacked_Column_Chart_Scales_Its_Axis_To_The_Stack()
    {
        // legend-itemorder-min.docx: the segments add up; the axis reaches
        // the stack's top (3 + 3 = 6, axis 0 to 7), not the largest value.
        (int width, int height, byte[] rgba) = RenderChart(
            """
            <c:chart><c:autoTitleDeleted val="1"/><c:plotArea><c:barChart><c:barDir val="col"/><c:grouping val="stacked"/>
              <c:ser><c:spPr><a:solidFill><a:srgbClr val="0000FF"/></a:solidFill></c:spPr>
                <c:val><c:numLit><c:pt idx="0"><c:v>3</c:v></c:pt></c:numLit></c:val></c:ser>
              <c:ser><c:spPr><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill></c:spPr>
                <c:val><c:numLit><c:pt idx="0"><c:v>3</c:v></c:pt></c:numLit></c:val></c:ser>
            </c:barChart><c:valAx/></c:plotArea></c:chart>
            """);

        int x = width / 2;
        double Y(double value) => height * (0.91 - (0.91 - 0.04) * value / 7);
        Assert.Equal((0, 0, 255), Pixel(rgba, width, x, (int)Y(1.5)));
        Assert.Equal((255, 0, 0), Pixel(rgba, width, x, (int)Y(4.5)));
        Assert.Equal((255, 255, 255), Pixel(rgba, width, x, (int)Y(6.5)));
    }

    [Fact]
    public void Path_Parameters_Split_At_Guide_References()
    {
        Assert.Equal(["@1", "@0"], DocxVmlPath.Values("@1@0"));
        Assert.Equal(["", "@3", "21600"], DocxVmlPath.Values(",@3,21600"));
        Assert.Equal(["10", "20"], DocxVmlPath.Values("10 20"));
    }

    [Fact]
    public void Vml_Formulas_Are_Evaluated_In_Order()
    {
        XNamespace v = "urn:schemas-microsoft-com:vml";
        var shape = new XElement(v + "shape",
            new XElement(v + "formulas",
                new XElement(v + "f", new XAttribute("eqn", "val #1")),
                new XElement(v + "f", new XAttribute("eqn", "sum @0 width 100")),
                new XElement(v + "f", new XAttribute("eqn", "prod @1 1 2")),
                new XElement(v + "f", new XAttribute("eqn", "if #0 @2 xcenter"))));

        double[]? guides = DocxVmlPath.EvaluateFormulas(shape, [-5, 300], 0, 0, 21600, 21600);

        Assert.NotNull(guides);
        Assert.Equal([300, 21800, 10900, 10800], guides);
    }

    [Fact]
    public void A_Callout_Whose_Leader_Leaves_Its_Box_Keeps_The_Box_With_A_Thin_Outline()
    {
        // mso-spt180.docx: the line callout's path names its formulas (@n);
        // its leader runs outside the box, where the picture would cut it off,
        // so the box is drawn. Its stroke weight is a bare number of EMUs
        // (2.24 pt): read as points, the 28 440 pt outline covered the page in
        // a cross.
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:pict " + Vml + ">" +
            "<v:shapetype id='shapetype_180' coordsize='21600,21600' o:spt='180' adj='-8280,24300,-1800,4050' " +
            "path='m,l21600,l21600,21600l,21600xem@1@0l@3@2nfe'>" +
            "<v:formulas><v:f eqn='val #3'/><v:f eqn='val #2'/><v:f eqn='val #1'/><v:f eqn='val #0'/></v:formulas>" +
            "</v:shapetype>" +
            "<v:shape type='#shapetype_180' fillcolor='#003150' stroked='t' " +
            "style='position:absolute;margin-left:10pt;margin-top:10pt;width:100pt;height:34pt'>" +
            "<v:stroke color='#003150' weight='28440'/></v:shape>" +
            "</w:pict></w:r></w:p>");

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.Null(shape.Image);
        Assert.NotNull(shape.Fill);
        Assert.Equal(28440 / 12700.0, shape.OutlineWidth, 9);
    }

    [Fact]
    public void An_Elbow_Connector_Is_Drawn_As_Its_Path_With_The_Types_Default_Adjust()
    {
        // fdo73215.docx: adj=",254405,-84343" leaves the first adjust value
        // to the type's 10800, which puts the elbow in the middle of the box.
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:pict " + Vml + ">" +
            "<v:shapetype id='_x0000_t34' coordsize='21600,21600' o:spt='34' o:oned='t' adj='10800' " +
            "path='m,l@0,0@0,21600,21600,21600e' filled='f'>" +
            "<v:formulas><v:f eqn='val #0'/></v:formulas></v:shapetype>" +
            "<v:shape type='#_x0000_t34' adj=',254405,-84343' strokeweight='1pt' " +
            "style='position:absolute;margin-left:10pt;margin-top:10pt;width:100pt;height:50pt'/>" +
            "</w:pict></w:r></w:p>");

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.NotNull(shape.Image);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "docx.shape.geometry");
    }

    [Fact]
    public void A_Bare_Stroke_Weight_Is_In_Emus()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:pict " + Vml + ">" +
            "<v:rect filled='f' strokeweight='25400' style='position:absolute;width:10pt;height:10pt'/>" +
            "</w:pict></w:r></w:p>");

        Assert.Equal(2, Assert.Single(result.Document.Shapes).OutlineWidth, 9);
        Assert.True(DocxVml.TryStrokeWeight("1.5pt", out double points));
        Assert.Equal(1.5, points);
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
