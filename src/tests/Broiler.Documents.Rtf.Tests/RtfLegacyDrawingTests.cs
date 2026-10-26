using System.Text;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfLegacyDrawingTests
{
    private static DocumentReadResult Read(string body) => RtfReader.Read(Encoding.ASCII.GetBytes("{\\rtf1\\paperw12240\\paperh15840\\margl1440\\margt1440 " + body + "}"));
    private static string Drawing(string properties = "", string primitive = "dprect") =>
        "{\\*\\do\\dobxpage\\dobypage\\" + primitive + "\\dpx2000\\dpy3000\\dpxsize1000\\dpysize500" + properties + "}";

    [Theory]
    [InlineData("dprect", ShapeGeometry.Rectangle)]
    [InlineData("dpellipse", ShapeGeometry.Ellipse)]
    [InlineData("dpline", ShapeGeometry.Line)]
    public void Supported_Primitives_Retain_Their_Page_Position_And_Extent(string primitive, ShapeGeometry geometry)
    {
        var shape = Assert.Single(Read(Drawing(primitive: primitive)).Document.Shapes);
        Assert.Equal(geometry, shape.Geometry);
        Assert.Equal(28, shape.OffsetX); Assert.Equal(150, shape.OffsetY);
        Assert.Equal(50, shape.Width); Assert.Equal(25, shape.Height);
        Assert.True(shape.OffsetFromPage);
    }

    [Theory]
    [InlineData("\\dobxmargin\\dobymargin", 100, 222, true)]
    [InlineData("\\dobxcolumn\\dobypara", 100, 150, false)]
    public void Anchor_Frames_Use_Their_Own_Origins(string anchor, double x, double y, bool page)
    {
        var shape = Assert.Single(Read(Drawing(anchor)).Document.Shapes);
        Assert.Equal(x, shape.OffsetX); Assert.Equal(y, shape.OffsetY); Assert.Equal(page, shape.OffsetFromPage);
    }

    [Fact]
    public void Paint_Uses_Independent_Rgb_Components_And_Twip_Pen_Width()
    {
        var shape = Assert.Single(Read(Drawing("\\dpfillpat1\\dpfillbgcr12\\dpfillbgcg34\\dpfillbgcb56\\dplinecor78\\dplinecog90\\dplinecob123\\dplinew40\\dodhgt8192")).Document.Shapes);
        Assert.Equal(BColor.FromArgb(12, 34, 56), shape.Fill!.Start);
        Assert.Equal(BColor.FromArgb(78, 90, 123), shape.Outline); Assert.Equal(2, shape.OutlineWidth);
        Assert.Equal(8192, shape.ZOrder);
    }

    [Fact]
    public void Hollow_Line_And_Transparent_Fill_Are_Independent()
    {
        var shape = Assert.Single(Read(Drawing("\\dplinehollow1")).Document.Shapes);
        Assert.True(shape.Outline.IsEmpty); Assert.Null(shape.Fill);
    }

    [Fact]
    public void The_Default_Legacy_Outline_Uses_The_Native_Blue_Default()
    {
        Assert.Equal(BColor.FromArgb(52, 101, 164), Assert.Single(Read(Drawing()).Document.Shapes).Outline);
    }

    [Fact]
    public void Unknown_Ignorable_Subgroups_Cannot_Change_Drawing_Geometry()
    {
        var shape = Assert.Single(Read(Drawing("{\\*\\unknown\\dpx9999}\\dpfillpat1")).Document.Shapes);
        Assert.Equal(28, shape.OffsetX);
    }

    [Fact]
    public void Hollow_Is_A_Flag_Even_When_An_Unused_Zero_Parameter_Follows_It()
    {
        Assert.True(Assert.Single(Read(Drawing("\\dplinehollow0")).Document.Shapes).Outline.IsEmpty);
    }

    [Fact]
    public void A_Horizontal_Line_With_Zero_Height_Is_Retained()
    {
        var shape = Assert.Single(Read(Drawing("\\dpysize0", "dpline")).Document.Shapes);
        Assert.Equal(0, shape.Height); Assert.Null(shape.Fill);
    }

    [Fact]
    public void Reversed_Extents_Normalize_The_Box_And_Preserve_Line_Direction()
    {
        var shape = Assert.Single(Read(Drawing("\\dpxsize-1000", "dpline")).Document.Shapes);
        Assert.Equal(-22, shape.OffsetX); Assert.Equal(50, shape.Width); Assert.Equal(ShapeGeometry.LineAscending, shape.Geometry);
    }

    [Fact]
    public void A_Nested_Format_Group_Does_Not_Finish_The_Drawing_Or_Leak_Text()
    {
        var result = Read("before" + Drawing("{\\b ignored}\\dpfillpat1\\dpfillbgcg255") + "after");
        Assert.Equal(BColor.FromArgb(0, 255, 0), Assert.Single(result.Document.Shapes).Fill!.Start);
        Assert.Equal("beforeafter", result.Document.PlainText);
    }

    [Theory]
    [InlineData("{\\*\\unknown ")]
    [InlineData("{\\stylesheet ")]
    [InlineData("{\\footnote ")]
    [InlineData("{\\header ")]
    public void Opaque_And_Nonbody_Destinations_Cannot_Emit_Body_Drawings(string prefix)
    {
        Assert.Empty(Read(prefix + Drawing() + "}body").Document.Shapes);
    }

    [Fact]
    public void Unsupported_Aggregates_Are_Diagnosed_Without_Partial_Geometry()
    {
        var result = Read(Drawing("\\dprect\\dpx4000"));
        Assert.Empty(result.Document.Shapes); Assert.Contains(result.Diagnostics, d => d.Code == "rtf.drawing.legacy");
    }

    [Fact]
    public void Modern_Instructions_Suppress_Their_Legacy_Fallback()
    {
        string modern = "{\\shp{\\*\\shpinst\\shpleft0\\shptop0\\shpright1000\\shpbottom500}{\\shprslt" + Drawing() + "fallback text\\par}}";
        var result = Read(modern + "body");
        Assert.Single(result.Document.Shapes); Assert.Equal("body", result.Document.PlainText);
    }

    [Fact]
    public void A_Shape_With_Only_A_Legacy_Result_Still_Retains_The_Drawing()
    {
        Assert.Single(Read("{\\shp{\\shprslt" + Drawing() + "}}").Document.Shapes);
    }
    [Fact]
    public void Legacy_Geometry_And_Paint_Survive_Export_As_Modern_Rtf_Shapes()
    {
        var source = Read(Drawing("\\dpfillpat1\\dpfillbgcg255\\dplinew40\\dplinecor255") + "body").Document;
        var original = Assert.Single(source.Shapes);
        var restored = Assert.Single(RtfReader.Read(RtfWriter.WriteToArray(source)).Document.Shapes);
        Assert.Equal(original.OffsetX, restored.OffsetX); Assert.Equal(original.OffsetY, restored.OffsetY);
        Assert.Equal(original.Width, restored.Width); Assert.Equal(original.Height, restored.Height);
        Assert.Equal(original.Fill, restored.Fill); Assert.Equal(original.Outline, restored.Outline);
        Assert.Equal(original.OutlineWidth, restored.OutlineWidth); Assert.Equal(original.OffsetFromPage, restored.OffsetFromPage);
    }

}
