using System.Text;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// A paragraph positioned with <c>\posx</c>, <c>\posy</c>, <c>\absw</c> and the
/// rest is a frame (RTF 1.9.1, "Positioned Objects and Frames"). They were
/// ignored, so a bank statement drawn entirely in frames, <c>tdf133437.rtf</c>,
/// came out as 253 loose paragraphs over eight pages where LibreOffice draws
/// three.
/// </summary>
public sealed class RtfFrameTests
{
    [Theory]
    [InlineData("\\page")]
    [InlineData("\\column")]
    public void A_Break_Between_Frames_Opens_A_New_Body_Page(string pageBreak)
    {
        var document = Read("{\\pard\\pvpg\\posy400\\absw2000 first\\par}" + pageBreak +
            "{\\pard\\pvpg\\posy800\\absw2000 second\\par}");

        Assert.Equal(2, document.Shapes.Count);
        Assert.Equal(1, document.Shapes[0].ParagraphIndex);
        Assert.Equal(1, document.Shapes[1].ParagraphIndex);
        Assert.False(document.Paragraphs[0].Style.PageBreakBefore);
        Assert.True(document.Paragraphs[1].Style.PageBreakBefore);
        Assert.Equal("first", Assert.Single(document.Shapes[0].Paragraphs).Text);
        Assert.Equal("second", Assert.Single(document.Shapes[1].Paragraphs).Text);
    }

    [Fact]
    public void A_Final_Frame_After_Body_Text_Keeps_Its_Empty_Anchor()
    {
        var document = Read("body\\par\\pard\\pvpg\\posy100\\absw2000 frame\\par");
        Assert.Equal(2, document.ParagraphCount);
        Assert.Equal(1, Assert.Single(document.Shapes).ParagraphIndex);
        Assert.Equal("body", document.Paragraphs[0].Text);
        Assert.Empty(document.Paragraphs[1].Text);
    }

    // A4 with the margins the statement has: none.
    private const string Page = "\\paperw11905\\paperh16837\\margl0\\margr0\\margt0\\margb0";

    private static RichTextDocument Read(string body, string page = Page) =>
        RtfReader.Read(Encoding.ASCII.GetBytes("{\\rtf1\\ansi" + page + body + "}")).Document;

    [Fact]
    public void A_Positioned_Paragraph_Is_A_Frame_Rather_Than_Body_Text()
    {
        RichTextDocument document = Read(
            "{\\pard\\phmrg\\pvmrg\\posx840\\posy1120\\absw10200\\absh0 Bank\\par}{\\par}");

        DocumentShape frame = Assert.Single(document.Shapes);
        Assert.Equal("Bank", Assert.Single(frame.Paragraphs).Text);
        Assert.DoesNotContain("Bank", document.PlainText);
        Assert.Equal(42, frame.OffsetX, 3);
        Assert.Equal(56, frame.OffsetY, 3);
        Assert.Equal(510, frame.Width, 3);
        Assert.True(frame.OffsetFromPage);
        Assert.True(frame.GrowsToText);
        Assert.Null(frame.Fill);
        Assert.True(frame.Outline.IsEmpty);
    }

    [Fact]
    public void A_Frame_Is_Anchored_To_The_Next_Unframed_Paragraph()
    {
        RichTextDocument document = Read(
            "first\\par{\\pard\\posy400\\absw2000 framed\\par}second\\par");

        DocumentShape frame = Assert.Single(document.Shapes);
        Assert.Equal(1, frame.ParagraphIndex);
        Assert.Equal("second", document.Paragraphs[frame.ParagraphIndex].Text);
    }

    [Fact]
    public void Paragraphs_That_State_The_Same_Frame_Share_It()
    {
        RichTextDocument document = Read(
            "{\\pard\\posy400\\absw2000 one\\par}{\\pard\\posy400\\absw2000 two\\par}" +
            "{\\pard\\posy800\\absw2000 three\\par}{\\par}");

        Assert.Equal(2, document.Shapes.Count);
        Assert.Equal(["one", "two"], document.Shapes[0].Paragraphs.Select(p => p.Text));
        Assert.Equal("three", Assert.Single(document.Shapes[1].Paragraphs).Text);
    }

    [Fact]
    public void An_Open_Frame_At_The_End_Of_The_Document_Is_Kept()
    {
        RichTextDocument document = Read("body\\par{\\pard\\posy400\\absw2000 last\\par}");

        Assert.Equal("last", Assert.Single(Assert.Single(document.Shapes).Paragraphs).Text);
    }

    [Fact]
    public void The_Margin_Reference_Is_The_Default_And_Adds_The_Top_Margin()
    {
        RichTextDocument document = Read(
            "{\\pard\\posx200\\posy400\\absw2000 x\\par}{\\par}",
            "\\paperw12240\\paperh15840\\margl1440\\margr1440\\margt720\\margb720");

        DocumentShape frame = Assert.Single(document.Shapes);
        Assert.Equal(10, frame.OffsetX, 3);
        Assert.Equal(36 + 20, frame.OffsetY, 3);
        Assert.True(frame.OffsetFromPage);
    }

    [Fact]
    public void The_Page_Reference_Is_Measured_From_The_Page_Edges()
    {
        RichTextDocument document = Read(
            "{\\pard\\phpg\\pvpg\\posx200\\posy400\\absw2000 x\\par}{\\par}",
            "\\paperw12240\\paperh15840\\margl1440\\margr1440\\margt720\\margb720");

        DocumentShape frame = Assert.Single(document.Shapes);
        Assert.Equal(10 - 72, frame.OffsetX, 3);
        Assert.Equal(20, frame.OffsetY, 3);
    }

    [Fact]
    public void The_Paragraph_Reference_Is_Measured_From_Its_Paragraph()
    {
        RichTextDocument document = Read("{\\pard\\pvpara\\posy400\\absw2000 x\\par}body\\par");

        DocumentShape frame = Assert.Single(document.Shapes);
        Assert.False(frame.OffsetFromPage);
        Assert.Equal(20, frame.OffsetY, 3);
    }

    [Theory]
    [InlineData("\\posxl", 0)]
    [InlineData("\\posxc", 200)]
    [InlineData("\\posxr", 400)]
    [InlineData("\\posxi", 0)]
    [InlineData("\\posxo", 400)]
    public void A_Frame_Aligned_In_Its_Column_Is_Placed_By_Its_Width(string alignment, double offset)
    {
        // 500 points of column, 100 of frame.
        RichTextDocument document = Read(
            "{\\pard" + alignment + "\\posy400\\absw2000 x\\par}{\\par}",
            "\\paperw10000\\paperh15840\\margl0\\margr0\\margt0\\margb0");

        Assert.Equal(offset, Assert.Single(document.Shapes).OffsetX, 3);
    }

    [Fact]
    public void A_Position_After_An_Alignment_Replaces_It()
    {
        RichTextDocument document = Read("{\\pard\\posxc\\posx200\\posy400\\absw2000 x\\par}{\\par}");

        Assert.Equal(10, Assert.Single(document.Shapes).OffsetX, 3);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(600, 30, true)]
    [InlineData(-600, 30, false)]
    public void Absh_Is_Left_To_The_Text_At_Least_Or_Exact(int absh, double height, bool grows)
    {
        RichTextDocument document = Read(
            "{\\pard\\posy400\\absw2000\\absh" + absh + " x\\par}{\\par}");

        DocumentShape frame = Assert.Single(document.Shapes);
        Assert.Equal(height, frame.Height, 3);
        Assert.Equal(grows, frame.GrowsToText);
    }

    [Fact]
    public void A_Frame_With_No_Width_Is_As_Wide_As_Its_Column()
    {
        RichTextDocument document = Read(
            "{\\pard\\posy400 x\\par}{\\par}",
            "\\paperw12240\\paperh15840\\margl1440\\margr1440\\margt720\\margb720");

        Assert.Equal(468, Assert.Single(document.Shapes).Width, 3);
    }

    [Theory]
    [InlineData("", ShapeWrap.Square, 6)]
    [InlineData("\\wraparound", ShapeWrap.Square, 6)]
    [InlineData("\\nowrap", ShapeWrap.TopAndBottom, 3)]
    [InlineData("\\overlay", ShapeWrap.None, 0)]
    [InlineData("\\wrapthrough", ShapeWrap.None, 0)]
    public void The_Wrap_Words_Say_How_The_Text_Goes_Around(string words, ShapeWrap wrap, double distance)
    {
        RichTextDocument document = Read(
            "{\\pard\\posy400\\absw2000\\dfrmtxtx120\\dfrmtxty60" + words + " x\\par}{\\par}");

        DocumentShape frame = Assert.Single(document.Shapes);
        Assert.Equal(wrap, frame.Wrap);
        Assert.Equal(distance, frame.WrapDistance, 3);
    }

    [Theory]
    [InlineData("\\dxfrtext180")]
    [InlineData("\\dfrmtxtx180\\dfrmtxty180")]
    [InlineData("\\posxl")]
    [InlineData("\\posyil")]
    [InlineData("\\phcol\\pvmrg")]
    public void Words_That_Change_Nothing_Make_No_Frame(string words)
    {
        RichTextDocument document = Read("{\\pard" + words + " body\\par}");

        Assert.Empty(document.Shapes);
        Assert.Contains("body", document.PlainText);
    }

    [Fact]
    public void Pard_Ends_The_Frame()
    {
        RichTextDocument document = Read("\\posy400\\absw2000 framed\\par\\pard body\\par");

        Assert.Equal("framed", Assert.Single(Assert.Single(document.Shapes).Paragraphs).Text);
        Assert.Equal("body", document.Paragraphs[0].Text);
    }

    [Fact]
    public void A_Frame_In_A_Table_Is_Read_As_The_Tables_Text()
    {
        RichTextDocument document = Read(
            "\\trowd\\cellx2000\\pard\\intbl\\posy400\\absw2000 cell\\cell\\row\\pard after\\par");

        Assert.Empty(document.Shapes);
        Assert.Single(document.Tables);
        Assert.Contains("cell", document.PlainText);
    }

    [Fact]
    public void An_Empty_Frame_Sized_By_Its_Text_Is_Not_Kept()
    {
        RichTextDocument document = Read("{\\pard\\posy400\\absw2000\\par}{\\par}");

        Assert.Empty(document.Shapes);
    }

    [Fact]
    public void A_Frame_Comes_Back_From_The_Writer_As_A_Shape_That_Grows_To_Its_Text()
    {
        RichTextDocument document = Read("{\\pard\\phmrg\\pvmrg\\posx840\\posy1120\\absw4000 Bank\\par}{\\par}");

        RichTextDocument back = RtfReader.Read(RtfWriter.WriteToArray(document)).Document;

        DocumentShape shape = Assert.Single(back.Shapes);
        Assert.Equal("Bank", Assert.Single(shape.Paragraphs).Text);
        Assert.True(shape.GrowsToText);
        Assert.True(shape.OffsetFromPage);
        Assert.Equal(42, shape.OffsetX, 3);
        Assert.Equal(56, shape.OffsetY, 3);
    }
}
