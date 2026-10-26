using Broiler.Graphics.Color;

namespace Broiler.Documents.Docx.Tests;

public sealed class DocxWordThemeColorTests
{
    private const string Theme = "<a:theme xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main' name='test'><a:themeElements><a:clrScheme name='test'>" +
        "<a:dk1><a:srgbClr val='000000'/></a:dk1><a:lt1><a:srgbClr val='FFFFFF'/></a:lt1>" +
        "<a:dk2><a:srgbClr val='1F497D'/></a:dk2><a:accent1><a:srgbClr val='336699'/></a:accent1><a:accent3><a:srgbClr val='4F81BD'/></a:accent3><a:hlink><a:srgbClr val='112233'/></a:hlink>" +
        "</a:clrScheme><a:fontScheme name='test'><a:majorFont><a:latin typeface='Arial'/></a:majorFont><a:minorFont><a:latin typeface='Arial'/></a:minorFont></a:fontScheme></a:themeElements></a:theme>";
    private static DocumentReadResult Read(string body, string styles = "", bool theme = true) => DocxTestPackage.ReadStyled(body, styles, theme ? Theme : null);
    private static string Paragraph(string runProperties = "", string paragraphProperties = "") =>
        "<w:p><w:pPr>" + paragraphProperties + "</w:pPr><w:r><w:rPr>" + runProperties + "</w:rPr><w:t>text</w:t></w:r></w:p>";
    private static string Table(string tableProperties = "", string cellProperties = "") =>
        "<w:tbl><w:tblPr>" + tableProperties + "</w:tblPr><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid><w:tr><w:tc><w:tcPr>" +
        cellProperties + "</w:tcPr>" + Paragraph() + "</w:tc></w:tr></w:tbl>";
    private static BColor Hex(string color) => BColor.FromArgb(Convert.ToInt32(color[..2], 16), Convert.ToInt32(color.Substring(2, 2), 16), Convert.ToInt32(color[4..], 16));

    [Theory]
    [InlineData("text2", "themeTint='99'", "548DD4")]
    [InlineData("accent3", "themeShade='BF'", "366091")]
    public void Word_Tone_Examples_Keep_Truncated_Byte_Channels(string slot, string tone, string expected)
    {
        var paragraph = Read(Paragraph("<w:color w:themeColor='" + slot + "' w:" + tone + "/>")).Document.Paragraphs[0];
        Assert.Equal(Hex(expected), paragraph.StyleAt(0).Foreground);
    }

    [Theory]
    [InlineData("accent1", "336699")]
    [InlineData("text1", "000000")]
    [InlineData("background1", "FFFFFF")]
    [InlineData("dark1", "000000")]
    [InlineData("hyperlink", "112233")]
    public void Text_Theme_Colors_Override_Stale_Rgb_And_Resolve_Word_Slot_Names(string slot, string expected)
    {
        var paragraph = Read(Paragraph("<w:color w:val='FF0000' w:themeColor='" + slot + "'/>")).Document.Paragraphs[0];
        Assert.Equal(Hex(expected), paragraph.StyleAt(0).Foreground);
    }

    [Theory]
    [InlineData("themeTint='80'", "8BB2D8")]
    [InlineData("themeShade='80'", "19334C")]
    [InlineData("themeTint='00'", "FFFFFF")]
    [InlineData("themeShade='00'", "000000")]
    [InlineData("themeTint='FF'", "336699")]
    [InlineData("themeTint='invalid'", "336699")]
    public void Text_Tint_And_Shade_Adjust_Luminance(string transform, string expected)
    {
        var paragraph = Read(Paragraph("<w:color w:themeColor='accent1' w:" + transform + "/>")).Document.Paragraphs[0];
        Assert.Equal(Hex(expected), paragraph.StyleAt(0).Foreground);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unresolved_Themes_Use_The_Rgb_Fallback_Without_Applying_Theme_Only_Modifiers(bool present)
    {
        var paragraph = Read(Paragraph("<w:color w:val='FF0000' w:themeColor='missing' w:themeTint='00'/>") , theme: present).Document.Paragraphs[0];
        Assert.Equal(Hex("FF0000"), paragraph.StyleAt(0).Foreground);
    }

    [Fact]
    public void Paragraph_Shading_Uses_Theme_Fill_And_Its_Tint()
    {
        var paragraph = Read(Paragraph(paragraphProperties: "<w:shd w:val='clear' w:fill='FF0000' w:themeFill='accent1' w:themeFillTint='80'/>")).Document.Paragraphs[0];
        Assert.Equal(Hex("8BB2D8"), paragraph.Style.Background);
    }

    [Fact]
    public void Run_Shading_Uses_The_Theme_And_Highlighting_Still_Wins()
    {
        const string shade = "<w:shd w:fill='auto' w:themeFill='accent1'/>";
        Assert.Equal(Hex("336699"), Read(Paragraph(shade)).Document.Paragraphs[0].StyleAt(0).Background);
        Assert.Equal(Hex("FFFF00"), Read(Paragraph(shade + "<w:highlight w:val='yellow'/>")).Document.Paragraphs[0].StyleAt(0).Background);
    }

    [Fact]
    public void Run_Nil_Shading_Clears_An_Inherited_Background()
    {
        string style = DocxTestPackage.Style("s", "<w:rPr><w:shd w:fill='FF0000'/></w:rPr>");
        var paragraph = Read(Paragraph("<w:shd w:val='nil' w:fill='0000FF'/>", "<w:pStyle w:val='s'/>"), style).Document.Paragraphs[0];
        Assert.True(paragraph.StyleAt(0).Background.IsEmpty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Theme_Shading_Is_Resolved_On_Tables_And_Cells(bool cell)
    {
        const string shade = "<w:shd w:fill='FF0000' w:themeFill='accent1' w:themeFillShade='80'/>";
        var table = Read(Table(cell ? "" : shade, cell ? shade : "")).Document.Tables[0];
        Assert.Equal(Hex("19334C"), table.Rows[0].Cells[0].Shading);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("paragraph")]
    [InlineData("cell")]
    public void Solid_Shading_Uses_Its_Themed_Foreground_Not_The_Background_Fill(string context)
    {
        const string shade = "<w:shd w:val='solid' w:color='FF0000' w:themeColor='accent1' w:fill='00FF00'/>";
        var document = Read(context == "cell" ? Table(cellProperties: shade) : Paragraph(context == "run" ? shade : "", context == "paragraph" ? shade : "")).Document;
        BColor color = context == "cell" ? document.Tables[0].Rows[0].Cells[0].Shading :
            context == "run" ? document.Paragraphs[0].StyleAt(0).Background : document.Paragraphs[0].Style.Background;
        Assert.Equal(Hex("336699"), color);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Border_Theme_Colors_Are_Resolved_At_Table_And_Cell_Level(bool cell)
    {
        string edges = "<w:" + (cell ? "tcBorders" : "tblBorders") + "><w:top w:val='single' w:sz='8' w:color='FF0000' w:themeColor='accent1' w:themeTint='80'/></w:" + (cell ? "tcBorders" : "tblBorders") + ">";
        var border = Read(Table(cell ? "" : edges, cell ? edges : "")).Document.Tables[0].Rows[0].Cells[0].Borders.Top;
        Assert.Equal(Hex("8BB2D8"), border.Color);
    }

    [Fact]
    public void Auto_Color_Still_Resets_An_Inherited_Explicit_Text_Color()
    {
        string style = DocxTestPackage.Style("s", "<w:rPr><w:color w:val='FF0000'/></w:rPr>");
        Assert.True(Read(Paragraph("<w:color w:val='auto'/>", "<w:pStyle w:val='s'/>"), style).Document.Paragraphs[0].StyleAt(0).Foreground.IsEmpty);
    }

    [Fact]
    public void Resolved_Theme_Colors_Survive_Docx_Export_As_Explicit_Colors()
    {
        var source = Read(Table("<w:shd w:themeFill='accent1'/>", "<w:tcBorders><w:top w:val='single' w:themeColor='accent1'/></w:tcBorders>" ) +
            Paragraph("<w:color w:themeColor='accent1'/><w:shd w:themeFill='accent1'/>", "<w:shd w:themeFill='accent1'/>")).Document;
        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(source));
        var restored = new DocxDocumentCodec().Read(stream).Document;
        Assert.Equal(source.Tables[0].Rows[0].Cells[0].Shading, restored.Tables[0].Rows[0].Cells[0].Shading);
        Assert.Equal(source.Tables[0].Rows[0].Cells[0].Borders.Top.Color, restored.Tables[0].Rows[0].Cells[0].Borders.Top.Color);
        Assert.Equal(source.Paragraphs[1].StyleAt(0).Foreground, restored.Paragraphs[1].StyleAt(0).Foreground);
        Assert.Equal(source.Paragraphs[1].StyleAt(0).Background, restored.Paragraphs[1].StyleAt(0).Background);
        Assert.Equal(source.Paragraphs[1].Style.Background, restored.Paragraphs[1].Style.Background);
    }
}
