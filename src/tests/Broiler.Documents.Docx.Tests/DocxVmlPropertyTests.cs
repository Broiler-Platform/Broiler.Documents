using Broiler.Graphics.Color;

namespace Broiler.Documents.Docx.Tests;

public sealed class DocxVmlPropertyTests
{
    private const string V = "xmlns:v='urn:schemas-microsoft-com:vml' xmlns:w10='urn:schemas-microsoft-com:office:word' xmlns:o='urn:schemas-microsoft-com:office:office'";
    private const string Page = "<w:sectPr><w:pgSz w:w='12000' w:h='16000'/><w:pgMar w:left='1000' w:right='1000' w:top='1000' w:bottom='1000'/></w:sectPr>";
    private static DocumentReadResult Read(string shapes) => DocxTestPackage.ReadBody(
        "<w:p><w:r><w:pict " + V + ">" + shapes + "</w:pict></w:r><w:r><w:t>body</w:t></w:r></w:p>" + Page);
    private static string Rect(string children = "", string attributes = "", string style = "") =>
        "<v:rect style='width:100pt;height:60pt;" + style + "' " + attributes + ">" + children + "</v:rect>";

    [Fact]
    public void Imported_Paint_Position_And_Wrapping_Survive_Drawingml_Export()
    {
        var document = Read(Rect("<v:fill opacity='0.5'/><v:stroke opacity='25%'/><w10:wrap type='square' side='right'/>",
            style: "mso-position-horizontal:center;mso-position-vertical:bottom;mso-position-vertical-relative:margin;mso-wrap-distance-right:5pt")).Document;
        var expected = Assert.Single(document.Shapes);
        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(document));
        var actual = Assert.Single(new DocxDocumentCodec().Read(stream).Document.Shapes);
        Assert.Equal(expected.Fill!.Start, actual.Fill!.Start); Assert.Equal(expected.Outline, actual.Outline);
        Assert.Equal(expected.OffsetX, actual.OffsetX, 3); Assert.Equal(expected.OffsetY, actual.OffsetY, 3);
        Assert.Equal(expected.Wrap, actual.Wrap); Assert.Equal(expected.WrapSide, actual.WrapSide);
        Assert.Equal(expected.WrapDistance, actual.WrapDistance, 3);
    }

    [Fact]
    public void Unitless_Line_Endpoints_Are_Pixels_While_Explicit_Points_Stay_Points()
    {
        var line = Assert.Single(Read("<v:line from='0,0' to='80,80'/>").Document.Shapes);
        Assert.Equal(60, line.Width); Assert.Equal(60, line.Height);
        var points = Assert.Single(Read("<v:line from='0pt,0pt' to='80pt,80pt'/>").Document.Shapes);
        Assert.Equal(80, points.Width); Assert.Equal(80, points.Height);
    }

    [Fact]
    public void Group_Depth_Is_Bounded_And_Invalid_Origins_Are_Diagnosed()
    {
        string group = "<v:group style='width:100;height:100' coordsize='100,100'>";
        string deep = string.Concat(Enumerable.Repeat(group, 20)) + "<v:rect style='width:10;height:10'/>" + string.Concat(Enumerable.Repeat("</v:group>", 20));
        var result = Read(deep + "<v:group style='width:10pt;height:10pt' coordorigin='invalid'><v:rect style='width:1;height:1'/></v:group>");
        Assert.Empty(result.Document.Shapes);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.shape.group");
    }

    [Fact]
    public void A_Mixed_Block_Retains_The_First_Image_And_All_Drawn_Shapes()
    {
        string body = "<w:p><w:r><w:pict " + V + ">" + Rect() +
            "<v:shape style='width:10pt;height:10pt'><v:imagedata r:id='image'/></v:shape>" + Rect() +
            "</w:pict></w:r></w:p>";
        var document = DocxTestPackage.ReadWithMedia(body,
            DocxTestPackage.ImageRelationship("image", "media/image.png"),
            new Dictionary<string, byte[]> { ["word/media/image.png"] = DocxTestPackage.OnePixelPng }).Document;
        Assert.Equal(2, document.Shapes.Count);
        Assert.NotNull(Assert.Single(Assert.Single(document.Paragraphs).Runs).Style.Image);
    }

    [Fact]
    public void All_Sibling_Shapes_And_Their_Text_Are_Retained()
    {
        string Text(string value) => "<v:textbox><w:txbxContent><w:p><w:r><w:t>" + value + "</w:t></w:r></w:p></w:txbxContent></v:textbox>";
        var document = Read(Rect(Text("first")) + Rect(Text("second"))).Document;
        Assert.Equal(2, document.Shapes.Count);
        Assert.Equal(new[] { "first", "second" }, document.Shapes.Select(s => Assert.Single(s.Paragraphs).Text));
    }

    [Fact]
    public void Group_Origin_And_Nonuniform_Scale_Are_Applied()
    {
        var shape = Assert.Single(Read("<v:group style='left:10pt;top:20pt;width:200pt;height:100pt' coordorigin='100,200' coordsize='1000,1000'>" +
            "<v:rect style='left:350;top:700;margin-left:50;width:200;height:100'/></v:group>").Document.Shapes);
        Assert.Equal(70, shape.OffsetX, 4);
        Assert.Equal(70, shape.OffsetY, 4);
        Assert.Equal(40, shape.Width, 4);
        Assert.Equal(10, shape.Height, 4);
    }

    [Fact]
    public void Nested_Group_Coordinates_Compose()
    {
        var shape = Assert.Single(Read("<v:group style='left:10pt;top:20pt;width:200pt;height:100pt' coordsize='1000,1000'>" +
            "<v:group style='left:100;top:100;width:500;height:500' coordsize='100,100'>" +
            "<v:rect style='left:10;top:20;width:30;height:40'/></v:group></v:group>").Document.Shapes);
        Assert.Equal(40, shape.OffsetX, 4); Assert.Equal(40, shape.OffsetY, 4);
        Assert.Equal(30, shape.Width, 4); Assert.Equal(20, shape.Height, 4);
    }

    [Fact]
    public void Shape_Type_Properties_Are_Inherited_With_Instance_Overrides()
    {
        var shape = Assert.Single(Read("<v:shapetype id='base' o:spt='3' fillcolor='red' style='width:120pt;height:80pt'>" +
            "<v:stroke color='blue' weight='2pt'/></v:shapetype><v:shape type='#base' style='width:120pt;height:40pt'><v:stroke weight='3pt'/></v:shape>").Document.Shapes);
        Assert.Equal(ShapeGeometry.Ellipse, shape.Geometry); Assert.Equal(120, shape.Width); Assert.Equal(40, shape.Height);
        Assert.Equal(BColor.FromArgb(255, 0, 0), shape.Fill!.Start);
        Assert.Equal(BColor.FromArgb(0, 0, 255), shape.Outline); Assert.Equal(3, shape.OutlineWidth);
    }

    [Theory]
    [InlineData("0.5", 128)]
    [InlineData("25%", 64)]
    [InlineData("32768f", 128)]
    public void Fill_Alpha_Accepts_Vml_Decimal_Percent_And_Fixed_Point(string value, int alpha)
    {
        var shape = Assert.Single(Read(Rect("<v:fill color='red' opacity='" + value + "'/>")).Document.Shapes);
        Assert.Equal(alpha, shape.Fill!.Start.A);
    }

    [Fact]
    public void Gradient_End_Opacities_Are_Independent()
    {
        var fill = Assert.Single(Read(Rect("<v:fill type='gradient' color='red' color2='blue' opacity='0.5' opacity2='0.25'/>")).Document.Shapes).Fill!;
        Assert.Equal(new byte[] { 64, 128 }, new[] { fill.Start.A, fill.End.A }.Order().ToArray());
    }

    [Theory]
    [InlineData("0.5", 128)]
    [InlineData("25%", 64)]
    public void Outline_Alpha_Is_Kept(string value, int alpha) =>
        Assert.Equal(alpha, Assert.Single(Read(Rect("<v:stroke opacity='" + value + "'/>")).Document.Shapes).Outline.A);

    [Fact]
    public void A_Polyline_Uses_Its_Css_Translation_As_Well_As_Its_Endpoints()
    {
        var line = Assert.Single(Read("<v:polyline points='10pt,20pt,50pt,60pt' style='left:3pt;top:4pt;margin-left:5pt;margin-top:6pt'/>").Document.Shapes);
        Assert.Equal(18, line.OffsetX); Assert.Equal(30, line.OffsetY);
    }

    [Fact]
    public void Wrap_Side_And_Clearance_Are_Preserved()
    {
        var shape = Assert.Single(Read(Rect("<w10:wrap type='square' side='right'/>", style: "mso-wrap-distance-left:3pt;mso-wrap-distance-right:7pt")).Document.Shapes);
        Assert.Equal(ShapeWrap.Square, shape.Wrap); Assert.Equal(WrapSide.Right, shape.WrapSide); Assert.Equal(7, shape.WrapDistance);
    }

    [Theory]
    [InlineData("left", 0)]
    [InlineData("center", 200)]
    [InlineData("right", 400)]
    public void Horizontal_Alignment_Is_Resolved_Against_The_Text_Area(string alignment, double x)
    {
        var shape = Assert.Single(Read(Rect(style: "mso-position-horizontal:" + alignment + ";mso-position-horizontal-relative:margin")).Document.Shapes);
        Assert.Equal(x, shape.OffsetX);
    }

    [Theory]
    [InlineData("top", 50)]
    [InlineData("center", 370)]
    [InlineData("bottom", 690)]
    public void Vertical_Alignment_Uses_A_Page_Relative_Offset(string alignment, double y)
    {
        var shape = Assert.Single(Read(Rect(style: "mso-position-vertical:" + alignment + ";mso-position-vertical-relative:margin")).Document.Shapes);
        Assert.True(shape.OffsetFromPage); Assert.Equal(y, shape.OffsetY);
    }

    [Theory]
    [InlineData("x", ShapeGeometry.LineAscending)]
    [InlineData("y", ShapeGeometry.LineAscending)]
    [InlineData("xy", ShapeGeometry.Line)]
    public void Line_Flips_Are_Composed(string flip, ShapeGeometry geometry) =>
        Assert.Equal(geometry, Assert.Single(Read("<v:line from='0,0' to='40,40' style='flip:" + flip + "'/>").Document.Shapes).Geometry);

    [Fact]
    public void Cyclic_Types_And_Unusable_Group_Transforms_Are_Diagnosed()
    {
        var result = Read("<v:shapetype id='cycle' type='#cycle'/><v:shape type='#cycle' style='width:10pt;height:10pt'/>" +
            "<v:group coordsize='0,0' style='width:10pt;height:10pt'><v:rect style='width:1;height:1'/></v:group>");
        Assert.Single(result.Document.Shapes);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.shape.type");
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.shape.group");
    }
}
