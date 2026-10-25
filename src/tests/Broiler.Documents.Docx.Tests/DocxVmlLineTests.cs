using Broiler.Graphics.Color;

namespace Broiler.Documents.Docx.Tests;

public sealed class DocxVmlLineTests
{
    private static DocumentReadResult Read(string element) => DocxTestPackage.ReadBody(
        "<w:p><w:r><w:pict xmlns:v='urn:schemas-microsoft-com:vml'>" + element + "</w:pict></w:r></w:p>");

    [Theory]
    [InlineData("20pt,30pt", "120pt,30pt", 20, 30, 100, 0, ShapeGeometry.Line)]
    [InlineData("20pt,30pt", "20pt,130pt", 20, 30, 0, 100, ShapeGeometry.Line)]
    [InlineData("20pt,30pt", "120pt,130pt", 20, 30, 100, 100, ShapeGeometry.Line)]
    [InlineData("120pt,30pt", "20pt,130pt", 20, 30, 100, 100, ShapeGeometry.LineAscending)]
    [InlineData("120pt,130pt", "20pt,30pt", 20, 30, 100, 100, ShapeGeometry.Line)]
    public void Line_Endpoints_Keep_Direction_And_Zero_Extents(string from, string to,
        double left, double top, double width, double height, ShapeGeometry geometry)
    {
        var result = Read($"<v:line from='{from}' to='{to}' strokecolor='red' strokeweight='2pt'/>");
        var shape = Assert.Single(result.Document.Shapes);
        Assert.Equal(left, shape.OffsetX, 3);
        Assert.Equal(top, shape.OffsetY, 3);
        Assert.Equal(width, shape.Width, 3);
        Assert.Equal(height, shape.Height, 3);
        Assert.Equal(geometry, shape.Geometry);
        Assert.Equal(BColor.FromArgb(255, 0, 0), shape.Outline);
        Assert.Equal(2, shape.OutlineWidth);
        Assert.Null(shape.Fill);
        using var stream = new MemoryStream(DocxWriter.WriteToArray(result.Document));
        var restored = Assert.Single(new DocxDocumentCodec().Read(stream).Document.Shapes);
        Assert.Equal(geometry, restored.Geometry);
        Assert.Equal(width, restored.Width, 3);
        Assert.Equal(height, restored.Height, 3);
    }

    [Fact]
    public void A_Two_Point_Polyline_With_Explicit_Units_Does_Not_Use_Coordsize_As_Points()
    {
        var shape = Assert.Single(Read("<v:polyline points='111.85pt,2.85pt,111.6pt,86.1pt' " +
            "coordsize='5,1665' style='margin-left:2pt;margin-top:3pt'/>").Document.Shapes);
        Assert.Equal(111.6, shape.OffsetX, 3);
        Assert.Equal(2.85, shape.OffsetY, 3);
        Assert.Equal(0.25, shape.Width, 3);
        Assert.Equal(83.25, shape.Height, 3);
        Assert.Equal(ShapeGeometry.LineAscending, shape.Geometry);
    }

    [Theory]
    [InlineData("<v:polyline points='0,0,10,10,20,20'/>")]
    [InlineData("<v:polyline points='20pt,60pt 140pt,60pt'/>")]
    [InlineData("<v:polyline points='0,0,10,10' coordsize='100,100'/>")]
    [InlineData("<v:line from='0,0' to='10pt,10pt' style='rotation:90'/>")]
    [InlineData("<v:line to='0,0,10pt,10pt'/>")]
    [InlineData("<v:line from='0,0' to='1e999pt,10pt'/>")]
    public void Unsupported_Or_Malformed_Line_Geometry_Is_Diagnosed(string xml)
    {
        var result = Read(xml);
        Assert.Empty(result.Document.Shapes);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.shape.geometry");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "docx.image.shape");
    }

    [Fact]
    public void A_Line_In_A_Cell_Retains_Its_Horizontal_Reference_On_Docx_Round_Trip()
    {
        var result = DocxTestPackage.ReadBody("<w:tbl><w:tr><w:tc><w:p/></w:tc><w:tc><w:p><w:r>" +
            "<w:pict xmlns:v='urn:schemas-microsoft-com:vml'><v:line from='20pt,20pt' to='80pt,20pt' " +
            "style='mso-position-horizontal-relative:text'/></w:pict></w:r></w:p></w:tc></w:tr></w:tbl>");
        var shape = Assert.Single(result.Document.Shapes);
        Assert.True(shape.OffsetXFromCell);
        using var stream = new MemoryStream(DocxWriter.WriteToArray(result.Document));
        var restored = Assert.Single(new DocxDocumentCodec().Read(stream).Document.Shapes);
        Assert.True(restored.OffsetXFromCell);
        Assert.Equal(shape.OffsetX, restored.OffsetX, 3);
    }

    [Fact]
    public void A_Dash_Pattern_Is_Not_Silently_Imported_As_A_Solid_Stroke()
    {
        var result = Read("<v:line from='0,0' to='40pt,0'><v:stroke dashstyle='shortdot'/></v:line>");
        Assert.Single(result.Document.Shapes);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.shape.stroke");
    }

    [Fact]
    public void A_Disabled_Stroke_Does_Not_Create_A_Drawing_Or_Image_Warning()
    {
        var result = Read("<v:line from='0,0' to='20pt,30pt' stroked='f'/>");
        Assert.Empty(result.Document.Shapes);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "docx.image.shape");
    }
}
