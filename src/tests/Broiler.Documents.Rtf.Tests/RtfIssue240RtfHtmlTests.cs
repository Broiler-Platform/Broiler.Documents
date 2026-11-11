using System.Text;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// Office conformance issue #240 (rtf-html): a page break followed only by
/// floating shapes (tdf153613), and the text boxes of a drawing group.
/// </summary>
public sealed class RtfIssue240RtfHtmlTests
{
    private const string Page = @"\paperw12240\paperh15840\margl1440\margr1440\margt1440\margb1440 ";

    private const string Shape =
        @"{\shp{\*\shpinst\shpleft0\shptop0\shpright1000\shpbottom1000\shpbxcolumn\shpbypara\shpwr2\shpwrk0" +
        @"{\sp{\sn shapeType}{\sv 1}}{\sp{\sn fFilled}{\sv 1}}}}";

    private static RichTextDocument Read(string rtf) => RtfReader.Read(Encoding.ASCII.GetBytes(rtf)).Document;

    [Fact]
    public void Shapes_Alone_After_A_Page_Break_Stay_Before_It_And_The_Break_Moves_On()
    {
        // tdf153613_anchoredAfterPgBreak5.rtf: "1\par \page{shapes}\par 2\par".
        RichTextDocument document = Read(@"{\rtf1" + Page + @"1\par \page " + Shape + Shape + @"\par 2\par}");

        Assert.Equal(3, document.ParagraphCount);
        Assert.False(document.Paragraphs[1].Style.PageBreakBefore);
        Assert.True(document.Paragraphs[2].Style.PageBreakBefore);
        Assert.All(document.Shapes, shape => Assert.Equal(1, shape.ParagraphIndex));
    }

    [Fact]
    public void A_Break_In_Text_Followed_Only_By_Shapes_Does_Not_Split_The_Paragraph()
    {
        // tdf153613_anchoredAfterPgBreak4.rtf: "1\par x\page{shape}\par 2\par".
        RichTextDocument document = Read(@"{\rtf1" + Page + @"1\par x\page " + Shape + @"\par 2\par}");

        Assert.Equal(["1", "x", "2"], document.Paragraphs.Select(paragraph => paragraph.Text));
        Assert.False(document.Paragraphs[1].Style.PageBreakBefore);
        Assert.True(document.Paragraphs[2].Style.PageBreakBefore);
        Assert.Equal(1, Assert.Single(document.Shapes).ParagraphIndex);
    }

    [Fact]
    public void Text_After_The_Shape_Takes_The_Break_With_It()
    {
        // tdf153613_anchoredAfterPgBreak2.rtf: the shape moves to the next page.
        RichTextDocument document = Read(@"{\rtf1" + Page + @"1\par \page " + Shape + @"x\par 2\par}");

        Assert.Equal(["1", "x", "2"], document.Paragraphs.Select(paragraph => paragraph.Text));
        Assert.True(document.Paragraphs[1].Style.PageBreakBefore);
        Assert.False(document.Paragraphs[2].Style.PageBreakBefore);
    }

    [Fact]
    public void A_Section_Break_Is_Not_Held_For_The_Next_Paragraph()
    {
        RichTextDocument document = Read(@"{\rtf1" + Page + @"1\sect\sbkpage " + Shape + @"\par 2\par}");

        int broken = document.Paragraphs.ToList().FindIndex(paragraph => paragraph.Style.PageBreakBefore);
        Assert.Equal(1, broken);
        Assert.Equal(broken, Assert.Single(document.Shapes).ParagraphIndex);
    }

    [Fact]
    public void A_Groups_Text_Boxes_Are_Drawing_Shapes_Unless_One_Holds_A_Table()
    {
        static string Group(string firstText) =>
            @"{\rtf1" + Page + @"{\shpgrp{\*\shpinst\shpleft0\shptop0\shpright4000\shpbottom2000" +
            @"{\sp{\sn groupLeft}{\sv 0}}{\sp{\sn groupTop}{\sv 0}}{\sp{\sn groupRight}{\sv 4000}}{\sp{\sn groupBottom}{\sv 2000}}" +
            @"{\shp{\*\shpinst{\sp{\sn relLeft}{\sv 0}}{\sp{\sn relTop}{\sv 0}}{\sp{\sn relRight}{\sv 2000}}{\sp{\sn relBottom}{\sv 2000}}" +
            @"{\sp{\sn shapeType}{\sv 202}}{\shptxt " + firstText + @"}}}" +
            @"{\shp{\*\shpinst{\sp{\sn relLeft}{\sv 2000}}{\sp{\sn relTop}{\sv 0}}{\sp{\sn relRight}{\sv 4000}}{\sp{\sn relBottom}{\sv 2000}}" +
            @"{\sp{\sn shapeType}{\sv 202}}{\shptxt baz}}}}}\par}";

        // groupshape.rtf: LibreOffice's text export leaves both out.
        RichTextDocument plain = Read(Group("foo"));
        Assert.Equal(2, plain.Shapes.Count);
        Assert.All(plain.Shapes, shape => Assert.True(shape.IsCustomShape));

        // fdo66040.rtf: with a table, both are text frames.
        RichTextDocument table = Read(Group(@"foo\par\pard\intbl A\cell B\cell\trowd\cellx1000\cellx2000\row\pard bar"));
        Assert.Equal(2, table.Shapes.Count);
        Assert.All(table.Shapes, shape => Assert.False(shape.IsCustomShape));
    }

    [Fact]
    public void A_Lone_Text_Box_Stays_A_Text_Frame()
    {
        RichTextDocument document = Read(@"{\rtf1" + Page +
            @"{\shp{\*\shpinst\shpleft0\shptop0\shpright2000\shpbottom2000{\sp{\sn shapeType}{\sv 202}}{\shptxt foo}}}\par}");

        Assert.False(Assert.Single(document.Shapes).IsCustomShape);
    }
}
