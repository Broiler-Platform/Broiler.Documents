using System.Text;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfShapeGroupAndFormatTests
{
    private static string P(string name, long value) => "{\\sp{\\sn " + name + "}{\\sv " + value + "}}";
    private static string Shape(string properties = "", string controls = "", string extra = "") =>
        "{\\shp{\\*\\shpinst\\shpleft0\\shptop0\\shpright2000\\shpbottom1200" + controls +
        P("fillColor", 255) + properties + extra + "}}";
    private static string Child(long left, long top, long right, long bottom, string extra = "") =>
        "{\\shp{\\*\\shpinst" + P("fillColor", 255) + P("relLeft", left) + P("relTop", top) + P("relRight", right) + P("relBottom", bottom) + extra + "}}";
    private static string Group(string children, string properties = "", string controls = "") =>
        "{\\shpgrp{\\*\\shpinst\\shpleft200\\shptop400\\shpright4200\\shpbottom2400" + controls +
        P("groupLeft", 100) + P("groupTop", 200) + P("groupRight", 1100) + P("groupBottom", 1200) + properties + children + "}}";
    private static DocumentReadResult Read(string body) => RtfReader.Read(Encoding.ASCII.GetBytes(
        "{\\rtf1\\ansi\\paperw12000\\paperh16000\\margl1000\\margr1000\\margt1000\\margb1000 " + body + "body\\par}"));

    [Fact]
    public void Drawing_Groups_Inside_An_Ignored_Destination_Stay_Opaque()
    {
        foreach (string destination in new[] { "\\*\\unknown", "\\header", "\\footer", "\\footnote" })
        {
            var result = Read("{" + destination + " " + Group(Child(0, 0, 100, 100), P("rotation", 90 * 65536)) + "}");
            Assert.Empty(result.Document.Shapes);
            Assert.DoesNotContain(result.Diagnostics, d => d.Code == "rtf.shape.group");
            Assert.Contains("body", result.Document.PlainText);
        }
    }

    [Fact]
    public void Mixed_Absolute_And_Relative_Child_Bounds_Keep_The_Existing_Fallback()
    {
        var result = Read(Group(Shape(P("relLeft", 0) + P("relTop", 0) + P("relRight", 100) + P("relBottom", 100))));
        Assert.Equal("body", result.Document.PlainText);
        Assert.Contains(result.Diagnostics, d => d.Code == "rtf.shape.group");
    }

    [Fact]
    public void Grouped_Children_Keep_Origins_Scales_And_Independent_Paint()
    {
        var shapes = Read(Group(Child(350, 700, 550, 800) + Child(100, 200, 200, 400, P("fillColor", 65280)))).Document.Shapes;
        Assert.Equal(2, shapes.Count);
        Assert.Equal(60, shapes[0].OffsetX); Assert.Equal(70, shapes[0].OffsetY);
        Assert.Equal(40, shapes[0].Width); Assert.Equal(10, shapes[0].Height);
        Assert.Equal(BColor.FromArgb(255, 0, 0), shapes[0].Fill!.Start);
        Assert.Equal(BColor.FromArgb(0, 255, 0), shapes[1].Fill!.Start);
    }

    [Fact]
    public void Nested_Groups_Compose_Their_Local_Spaces()
    {
        string nested = Group(Child(110, 220, 140, 260),
            P("relLeft", 200) + P("relTop", 300) + P("relRight", 700) + P("relBottom", 800))
            .Replace("\\shpleft200\\shptop400\\shpright4200\\shpbottom2400", "", StringComparison.Ordinal);
        var shape = Assert.Single(Read(Group(nested)).Document.Shapes);
        Assert.Equal(31, shape.OffsetX, 3); Assert.Equal(31, shape.OffsetY, 3);
        Assert.Equal(3, shape.Width, 3); Assert.Equal(2, shape.Height, 3);
    }

    [Fact]
    public void A_Group_Keeps_Its_Reference_And_Sorts_Its_Children_Within_One_Layer()
    {
        string children = Child(100, 200, 200, 300, P("dhgt", 9)) + Child(300, 200, 400, 300, P("dhgt", 2));
        var shapes = Read(Group(children, P("posrelh", 1) + P("posrelv", 1), "\\shpz7\\shpfblwtxt1")).Document.Shapes;
        Assert.Equal(2, shapes.Count);
        Assert.True(shapes[0].OffsetX > shapes[1].OffsetX);
        Assert.All(shapes, s => { Assert.Equal(7, s.ZOrder); Assert.True(s.BehindText); Assert.True(s.OffsetFromPage); });
    }

    [Fact]
    public void Unrepresentable_Groups_Report_The_Fallback_Without_Losing_Body_Text()
    {
        foreach (string group in new[]
        {
            Group(Child(100, 200, 200, 300), P("rotation", 90 * 65536)),
            Group(Child(100, 200, 200, 300), controls: "\\shpwr2"),
            Group("{\\pict 0000}" + Child(100, 200, 200, 300)),
        })
        {
            var result = Read(group);
            Assert.Equal("body", result.Document.PlainText);
            Assert.Contains(result.Diagnostics, d => d.Code == "rtf.shape.group");
        }
    }

    [Fact]
    public void Anonymous_Formatting_Groups_Do_Not_Finish_Their_Containing_Shape()
    {
        Assert.Single(Read(Shape(extra: "{\\b0}{\\i0}")).Document.Shapes);
    }

    [Fact]
    public void Modern_Stacking_Is_Used_Unless_An_Explicit_Legacy_Order_Overrides_It()
    {
        Assert.Equal(12, Assert.Single(Read(Shape(P("dhgt", 12))).Document.Shapes).ZOrder);
        Assert.Equal(7, Assert.Single(Read(Shape(P("dhgt", 12), "\\shpz7")).Document.Shapes).ZOrder);
        Assert.Equal(0, Assert.Single(Read(Shape(P("dhgt", 12), "\\shpz0")).Document.Shapes).ZOrder);
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(0, 2, 200)]
    [InlineData(0, 3, 400)]
    [InlineData(1, 1, -50)]
    [InlineData(1, 3, 450)]
    public void Shape_Horizontal_Alignment_Uses_Its_Reference_Width(int reference, int alignment, double x) =>
        Assert.Equal(x, Assert.Single(Read(Shape(P("posrelh", reference) + P("posh", alignment))).Document.Shapes).OffsetX);

    [Theory]
    [InlineData(0, 2, 370)]
    [InlineData(0, 3, 690)]
    [InlineData(1, 2, 370)]
    [InlineData(1, 3, 740)]
    public void Shape_Vertical_Alignment_Uses_Its_Fixed_Height(int reference, int alignment, double y) =>
        Assert.Equal(y, Assert.Single(Read(Shape(P("posrelv", reference) + P("posv", alignment))).Document.Shapes).OffsetY);

    [Theory]
    [InlineData("\\posyc", 370)]
    [InlineData("\\posyb", 690)]
    public void Exact_Text_Frames_Can_Be_Centred_Or_Bottom_Aligned(string alignment, double y)
    {
        var result = Read("{\\pard\\pvmrg" + alignment + "\\absw2000\\absh-1200 frame\\par}{\\pard\\par}");
        Assert.Equal(y, Assert.Single(result.Document.Shapes).OffsetY);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "rtf.frame.position");
    }

    [Fact]
    public void Repeating_An_Edge_Clearance_Replaces_Its_Old_Value()
    {
        var shape = Assert.Single(Read(Shape(P("dxWrapDistRight", 127000) + P("dxWrapDistLeft", 63500) + P("dxWrapDistRight", 0))).Document.Shapes);
        Assert.Equal(5, shape.WrapDistance);
    }

    [Theory]
    [InlineData("uldb")]
    [InlineData("uldash")]
    [InlineData("ulwave")]
    [InlineData("ulw")]
    public void Unsupported_Underline_Patterns_Keep_An_Explicitly_Diagnosed_Underline(string control)
    {
        var result = Read("\\" + control + " marked\\" + control + "0 clear ");
        Assert.True(result.Document.Paragraphs[0].StyleAt(0).Underline);
        Assert.False(result.Document.Paragraphs[0].StyleAt(8).Underline);
        Assert.Contains(result.Diagnostics, d => d.Code == "rtf.underline.pattern");
    }

    [Fact]
    public void Double_Strike_Is_Kept_As_A_Diagnosed_Single_Rule()
    {
        var result = Read("\\striked1 marked\\striked0 clear ");
        Assert.True(result.Document.Paragraphs[0].StyleAt(0).Strikethrough);
        Assert.False(result.Document.Paragraphs[0].StyleAt(8).Strikethrough);
        Assert.Contains(result.Diagnostics, d => d.Code == "rtf.strike.double");
    }

    [Fact]
    public void Character_Background_Pattern_Color_Is_Kept_And_Plain_Resets_It()
    {
        var result = Read("{\\colortbl;\\red255\\green0\\blue0;}\\chcbpat1 marked\\plain clear ");
        Assert.Equal(BColor.FromArgb(255, 0, 0), result.Document.Paragraphs[0].StyleAt(0).Background);
        Assert.True(result.Document.Paragraphs[0].StyleAt(8).Background.IsEmpty);
    }
}
