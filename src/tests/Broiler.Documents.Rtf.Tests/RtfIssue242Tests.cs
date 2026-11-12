using System.Text;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// Office conformance issue #242 (rtf-html): the page of a document whose body
/// holds nothing (dppolyline.rtf), and a header watermark aligned with its
/// paragraph (watermark.rtf), and a group's text boxes fitted to their text
/// (groupshape.rtf).
/// </summary>
public sealed class RtfIssue242Tests
{
    private const string Page = @"\paperw12240\paperh15840\margl1440\margr1440\margt1440\margb1440 ";

    private const string Drawing =
        @"{\*\do\dobxmargin\dobymargin\dodhgt8192\dprect\dpx0\dpy0\dpxsize1440\dpysize1440\dpfillpat1}";

    private const string Shape =
        @"{\shp{\*\shpinst\shpleft0\shptop0\shpright1440\shpbottom1440\shpbxmargin\shpbymargin" +
        @"{\sp{\sn shapeType}{\sv 1}}}}";

    private static RichTextDocument Read(string rtf) => RtfReader.Read(Encoding.ASCII.GetBytes(rtf)).Document;

    private static void AssertWriterDefaultPage(RichTextDocument document)
    {
        PageGeometry page = Assert.IsType<PageGeometry>(document.PageGeometry);
        Assert.Equal(595.3, page.Width);
        Assert.Equal(841.9, page.Height);
        Assert.Equal(56.7, page.MarginLeft);
        Assert.Equal(56.7, page.MarginRight);
        Assert.Equal(56.7, page.MarginTop);
        Assert.Equal(56.7, page.MarginBottom);
    }

    private static void AssertStatedPage(RichTextDocument document)
    {
        PageGeometry page = Assert.IsType<PageGeometry>(document.PageGeometry);
        Assert.Equal(612, page.Width);
        Assert.Equal(792, page.Height);
        Assert.Equal(72, page.MarginTop);
    }

    [Theory]
    [InlineData(Drawing)]
    [InlineData(Shape)]
    [InlineData("")]
    public void A_Document_Without_Body_Content_Opens_On_Writers_Default_Page(string content)
    {
        // dppolyline.rtf, tdf90097.rtf, custom-doc-props.rtf: writerfilter never
        // opens a section, so the stated page is not applied.
        AssertWriterDefaultPage(Read(@"{\rtf1" + Page + content + "}"));
    }

    [Fact]
    public void A_Margin_Relative_Drawing_Is_Placed_From_The_Default_Page_Margins()
    {
        RichTextDocument document = Read(@"{\rtf1" + Page + Drawing + "}");

        DocumentShape shape = Assert.Single(document.Shapes);
        Assert.Equal(0, shape.OffsetX);
        Assert.Equal(56.7, shape.OffsetY);
        Assert.True(shape.OffsetFromPage);
    }

    [Theory]
    [InlineData(@"\par")]
    [InlineData("x")]
    [InlineData(@"{\shp{\*\shpinst\shpleft0\shptop0\shpright1440\shpbottom1440{\sp{\sn shapeType}{\sv 202}}}{\shptxt x\par}}")]
    [InlineData(@"{\*\do\dobxmargin\dobymargin\dptxbx\dpx0\dpy0\dpxsize1440\dpysize1440{\dptxbxtext x\par}}")]
    [InlineData(@"{\info{\author A} }")]
    public void Body_Text_A_Paragraph_Mark_Or_Shape_Text_Applies_The_Stated_Page(string content)
    {
        // rectpar.rtf-style cut-downs through soffice 24.2.7.2; tdf115153.rtf
        // has nothing but a drawing and a space inside {\info}.
        AssertStatedPage(Read(@"{\rtf1" + Page + Drawing + content + "}"));
    }

    [Fact]
    public void A_Shape_Picture_Applies_The_Stated_Page()
    {
        // fdo58076-2.rtf: a picture shape alone.
        const string png = "89504e470d0a1a0a0000000d49484452000000010000000108060000001f15c4890000000d4944415478da63f8ffff3f0005fe02fea7d6a4d50000000049454e44ae426082";
        string shape = @"{\shp{\*\shpinst\shpleft0\shptop0\shpright1440\shpbottom1440{\sp{\sn shapeType}{\sv 75}}" +
            @"{\sp{\sn pib}{\sv {\pict\pngblip\picw1\pich1 " + png + "}}}}}";

        AssertStatedPage(Read(@"{\rtf1" + Page + shape + "}"));
    }

    [Fact]
    public void Text_In_A_Title_Does_Not_Apply_The_Stated_Page()
    {
        AssertWriterDefaultPage(Read(@"{\rtf1" + Page + @"{\info{\title A}}" + Drawing + "}"));
    }

    [Fact]
    public void Text_After_The_Document_Group_Does_Not_Apply_The_Stated_Page()
    {
        // tdf84684.rtf closes its root group before its only \par.
        AssertWriterDefaultPage(Read(@"{\rtf1" + Page + Drawing + @"}{\par}"));
    }

    private static string HeaderShape(string properties, int right = 8484, int bottom = 4242) =>
        @"{\rtf1\paperw11906\paperh16838\margl1440\margr1440\margt1440\margb1440\headery708 " +
        @"{\header \pard\plain\fs22 {\shp{\*\shpinst\shpleft0\shptop0\shpright" + right + @"\shpbottom" + bottom +
        @"\shpbxcolumn\shpbxignore\shpbypara\shpbyignore{\sp{\sn shapeType}{\sv 136}}" + properties + @"}}\par}" +
        @"\pard x\par}";

    /// <summary>The side of the square a 424.2 x 212.1 pt box turned 45° sweeps.</summary>
    private static readonly double Turned = (424.2 + 212.1) * Math.Sqrt(0.5);

    private static string P(string name, object value) => @"{\sp{\sn " + name + @"}{\sv " + value + "}}";

    private static DocumentShape HeaderShapeOf(string rtf) =>
        Assert.Single(Read(rtf).RunningContent.EffectiveHeaderShapes(PageSelection.Default));

    /// <summary>The centre of a shape the model turns about its top-left corner.</summary>
    private static (double X, double Y) Centre(DocumentShape shape)
    {
        double cos = Math.Cos(shape.RotationRadians), sin = Math.Sin(shape.RotationRadians);
        return (shape.OffsetX + shape.Width / 2 * cos - shape.Height / 2 * sin,
            shape.OffsetY + shape.Width / 2 * sin + shape.Height / 2 * cos);
    }

    [Fact]
    public void A_Header_Watermark_Centred_On_Its_Paragraph_Is_Kept_On_The_Page()
    {
        // watermark.rtf: Word's PowerPlusWaterMarkObject, 424.2 x 212.1 pt turned
        // 315°, centred on its header paragraph (posrelv 0, which LibreOffice
        // measures from the paragraph). Its 450 pt bounding square would start
        // above the page, so Writer moves it down to the page's top edge.
        DocumentShape shape = HeaderShapeOf(HeaderShape(
            P("rotation", 20643840) + P("posh", 2) + P("posrelh", 0) + P("posv", 2) + P("posrelv", 0) +
            P("fGtext", 1) + P("gtextUNICODE", "ASAP") + P("fillColor", 6108695)));

        (double x, double y) = Centre(shape);
        // The model measures across from the left margin.
        Assert.Equal((595.3 - 144) / 2, x, 0.01);
        Assert.Equal(Turned / 2, y, 0.01);
        Assert.True(shape.StretchesTextToBox);
        Assert.Equal("ASAP", Assert.Single(shape.Paragraphs).Text);
    }

    [Fact]
    public void A_Turned_Shape_Aligned_To_The_Top_Of_Its_Header_Paragraph_Aligns_Its_Bounding_Box()
    {
        // soffice: the same shape with posv 1 has its bounding square's top at
        // the header paragraph's top, the header distance.
        DocumentShape shape = HeaderShapeOf(HeaderShape(P("rotation", 20643840) + P("posv", 1) + P("fillColor", 255)));

        Assert.Equal(35.4 + Turned / 2, Centre(shape).Y, 0.01);
    }

    [Fact]
    public void An_Unturned_Header_Shape_Centred_On_Its_Paragraph_Uses_Its_Line()
    {
        // 72 x 36 pt centred on an 11 pt header line at 35.4 pt.
        DocumentShape shape = HeaderShapeOf(HeaderShape(P("posv", 2) + P("fillColor", 255), 1440, 720));

        Assert.Equal(35.4 + (11 * 1.15 - 36) / 2, shape.OffsetY, 3);
    }

    [Fact]
    public void A_Turned_Shape_Centred_On_The_Page_Has_Its_Centre_There()
    {
        DocumentShape shape = HeaderShapeOf(HeaderShape(P("rotation", 20643840) + P("posv", 2) + P("posrelv", 1) + P("fillColor", 255)));

        Assert.Equal(841.9 / 2, Centre(shape).Y, 0.01);
    }

    [Fact]
    public void A_Header_Shape_Past_The_Page_Foot_Is_Moved_Onto_The_Page()
    {
        string rtf = HeaderShape(P("fillColor", 255), 1440, 720).Replace(@"\shptop0", @"\shptop20000").Replace(@"\shpbottom720", @"\shpbottom20720");

        DocumentShape shape = HeaderShapeOf(rtf);

        Assert.Equal(841.9 - 36, shape.OffsetY, 3);
    }

    private static string Group(string firstText) =>
        @"{\rtf1" + Page + @"{\shpgrp{\*\shpinst\shpleft0\shptop0\shpright4000\shpbottom2000" +
        @"{\sp{\sn groupLeft}{\sv 0}}{\sp{\sn groupTop}{\sv 0}}{\sp{\sn groupRight}{\sv 4000}}{\sp{\sn groupBottom}{\sv 2000}}" +
        @"{\shp{\*\shpinst{\sp{\sn relLeft}{\sv 0}}{\sp{\sn relTop}{\sv 0}}{\sp{\sn relRight}{\sv 2000}}{\sp{\sn relBottom}{\sv 2000}}" +
        @"{\sp{\sn shapeType}{\sv 202}}{\shptxt " + firstText + @"}}}" +
        @"{\shp{\*\shpinst{\sp{\sn relLeft}{\sv 2000}}{\sp{\sn relTop}{\sv 0}}{\sp{\sn relRight}{\sv 4000}}{\sp{\sn relBottom}{\sv 2000}}" +
        @"{\sp{\sn shapeType}{\sv 1}}{\shptxt baz}}}}}\par}";

    [Fact]
    public void A_Groups_Drawing_Text_Box_Fits_Its_Text()
    {
        // groupshape.rtf: LibreOffice shrinks each text box about its text.
        RichTextDocument document = Read(Group("foo"));

        Assert.True(document.Shapes[0].FitsText);
        Assert.False(document.Shapes[1].FitsText);
    }

    [Fact]
    public void A_Groups_Text_Frame_Keeps_Its_Box()
    {
        // fdo66040.rtf: with a table the children are text frames, drawn as stated.
        RichTextDocument document = Read(Group(@"foo\par\pard\intbl A\cell B\cell\trowd\cellx1000\cellx2000\row\pard bar"));

        Assert.All(document.Shapes, shape => Assert.False(shape.FitsText));
    }
}
