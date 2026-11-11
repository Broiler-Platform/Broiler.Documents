using System.Text;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>Office conformance issue #238: page borders and WordArt shapes.</summary>
public sealed class RtfIssue238Tests
{
    private const string Page = @"\paperw12240\paperh15840\margl1440\margr1440\margt1440\margb1440 ";

    private static DocumentReadResult Read(string rtf) => RtfReader.Read(Encoding.ASCII.GetBytes(rtf));

    [Fact]
    public void Page_Border_Sides_Keep_Their_Own_Widths_And_Sit_Their_Space_Outside_The_Text()
    {
        // rtfexport/data/page-border.rtf.
        PageGeometry page = Read(
            @"{\rtf1" + Page +
            @"\pgbrdrt\brdrs\brdrw10\brsp480 \pgbrdrl\brdrs\brdrw20\brsp480 " +
            @"\pgbrdrb\brdrs\brdrw30\brsp480 \pgbrdrr\brdrs\brdrw40\brsp480 hello\par}").Document.PageGeometry!;

        Assert.Equal(0.5, page.Borders.Top.Width, 3);
        Assert.Equal(1, page.Borders.Left.Width, 3);
        Assert.Equal(1.5, page.Borders.Bottom.Width, 3);
        Assert.Equal(2, page.Borders.Right.Width, 3);
        Assert.Equal(BColor.Black, page.Borders.Top.Color);

        // The margin less the space less the line: the line is drawn outward.
        Assert.Equal(72 - 24 - 0.5, page.BorderInsets.Top, 3);
        Assert.Equal(72 - 24 - 1, page.BorderInsets.Left, 3);
        Assert.Equal(72 - 24 - 1.5, page.BorderInsets.Bottom, 3);
        Assert.Equal(72 - 24 - 2, page.BorderInsets.Right, 3);

        // The text stays where the margins put it.
        Assert.Equal(72, page.MarginLeft, 3);
        Assert.Equal(72, page.MarginTop, 3);
    }

    [Fact]
    public void A_Page_Border_Measured_From_The_Page_Edge_Sits_Its_Space_In_From_The_Edge()
    {
        PageGeometry page = Read(
            @"{\rtf1" + Page + @"\pgbrdropt32\pgbrdrt\brdrs\brdrw10\brsp480 hello\par}").Document.PageGeometry!;

        Assert.Equal(24, page.BorderInsets.Top, 3);
        Assert.True(page.Borders.Top.IsVisible);
        Assert.False(page.Borders.Left.IsVisible);
    }

    [Fact]
    public void Page_Border_Colour_Comes_From_The_Colour_Table()
    {
        PageGeometry page = Read(
            @"{\rtf1{\colortbl;\red255\green0\blue0;}" + Page +
            @"\pgbrdrb\brdrs\brdrw15\brdrcf1\brsp240 hello\par}").Document.PageGeometry!;

        Assert.Equal(new BColor(255, 0, 0), page.Borders.Bottom.Color);
        Assert.Equal(72 - 12 - 0.75, page.BorderInsets.Bottom, 3);
    }

    [Fact]
    public void Writers_Empty_Head_And_Foot_Flags_Draw_No_Border()
    {
        // LibreOffice's export writes these on every RTF page.
        PageGeometry? page = Read(@"{\rtf1" + Page + @"\pgbrdrhead\pgbrdrfoot hello\par}").Document.PageGeometry;

        Assert.NotNull(page);
        Assert.False(page.Borders.IsVisible);
    }

    [Fact]
    public void A_Paragraph_Border_After_A_Page_Border_Stays_The_Paragraphs()
    {
        PageGeometry page = Read(
            @"{\rtf1" + Page + @"\pgbrdrt\brdrs\brdrw10\brsp480 \pard\brdrb\brdrs\brdrw60 hello\par}").Document.PageGeometry!;

        Assert.True(page.Borders.Top.IsVisible);
        Assert.False(page.Borders.Bottom.IsVisible);
        Assert.Equal(0.5, page.Borders.Top.Width, 3);
    }

    [Fact]
    public void Sectd_Clears_A_Page_Border()
    {
        PageGeometry page = Read(
            @"{\rtf1" + Page + @"\pgbrdrt\brdrs\brdrw10\brsp480 \sectd hello\par}").Document.PageGeometry!;

        Assert.False(page.Borders.IsVisible);
    }

    [Fact]
    public void A_WordArt_Shape_Draws_Its_String_Rather_Than_A_Filled_Box()
    {
        // rtfimport/data/watermark.rtf: Word's PowerPlusWaterMarkObject, a
        // shapeType 136 whose fill colour is the colour of its letters.
        string shape =
            @"{\shp{\*\shpinst\shpleft0\shptop0\shpright8484\shpbottom4242" +
            @"{\sp{\sn shapeType}{\sv 136}}{\sp{\sn gtextUNICODE}{\sv ASAP}}{\sp{\sn gtextFont}{\sv DejaVu Sans}}" +
            @"{\sp{\sn fGtext}{\sv 1}}{\sp{\sn fillColor}{\sv 6108695}}{\sp{\sn fillOpacity}{\sv 32768}}" +
            @"{\sp{\sn fFilled}{\sv 1}}{\sp{\sn fLine}{\sv 0}}}}";
        DocumentReadResult result = Read(@"{\rtf1" + Page + shape + @"body\par}");

        DocumentShape wordArt = Assert.Single(result.Document.Shapes);
        Assert.Null(wordArt.Fill);
        Assert.True(wordArt.Outline.IsEmpty);
        Assert.Equal(CellVerticalAlignment.Center, wordArt.TextVerticalAlignment);
        RichTextParagraph line = Assert.Single(wordArt.Paragraphs);
        Assert.Equal("ASAP", line.Text);
        Assert.Equal(TextAlignment.Center, line.Style.Alignment);

        InlineStyle style = line.StyleAt(0);
        Assert.Equal("DejaVu Sans", style.FontFamily);
        Assert.Equal(BColor.FromArgb(128, 0x17, 0x36, 0x5D), style.Foreground);
        // Narrow enough that the four letters fit the box's 424 points.
        Assert.True(style.FontSize * 4 * 0.7 <= 424.2 + 0.01, $"font size {style.FontSize}");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "rtf.shape.wordart");
    }

    [Fact]
    public void A_Shape_That_Is_Not_WordArt_Keeps_Its_Fill()
    {
        string shape =
            @"{\shp{\*\shpinst\shpleft0\shptop0\shpright2000\shpbottom2000" +
            @"{\sp{\sn gtextUNICODE}{\sv ASAP}}{\sp{\sn fillColor}{\sv 255}}}}";
        DocumentShape box = Assert.Single(Read(@"{\rtf1" + Page + shape + @"body\par}").Document.Shapes);

        Assert.NotNull(box.Fill);
        Assert.Empty(box.Paragraphs);
    }
}
