using System.Text;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// Office conformance issue #242 (rtf-html): the page of a document whose body
/// holds nothing (dppolyline.rtf).
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
}
