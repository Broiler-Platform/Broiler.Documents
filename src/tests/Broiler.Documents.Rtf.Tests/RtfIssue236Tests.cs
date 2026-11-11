using System.Text;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>Office conformance issue #236: paragraph tab stops over a style's, and starred character styles.</summary>
public sealed class RtfIssue236Tests
{
    private static DocumentReadResult Read(string rtf) => RtfReader.Read(Encoding.ASCII.GetBytes(rtf));

    [Fact]
    public void A_Paragraphs_Own_Tab_Stops_Replace_Its_Styles()
    {
        // rtfimport/data/fdo81033.rtf, pinned by rtfimport.cxx testFdo81033:
        // two stops, the second a left stop with an underscore leader.
        RichTextDocument document = Read(
            @"{\rtf1{\stylesheet{\s15\ql \tqc\tx4536\tqr\tx9072 header;}}" +
            @"\pard\plain \s15\ql \tx3293\tlul\tx9072 \tab\tab\par}").Document;

        Assert.Equal(
            [new ParagraphTabStop(164.65, TabAlignment.Left), new ParagraphTabStop(453.6, TabAlignment.Left, "_")],
            document.Paragraphs[0].Style.TabStops!.ToArray());
    }

    [Fact]
    public void A_Paragraph_With_No_Tab_Stops_Of_Its_Own_Keeps_Its_Styles()
    {
        RichTextDocument document = Read(
            @"{\rtf1{\stylesheet{\s15\tqr\tx9072 header;}}\pard\s15 a\tab b\par\pard\s15\tx720 c\tx1440\tab d\par}").Document;

        Assert.Equal([new ParagraphTabStop(453.6, TabAlignment.Right)], document.Paragraphs[0].Style.TabStops!.ToArray());
        Assert.Equal([new ParagraphTabStop(36), new ParagraphTabStop(72)], document.Paragraphs[1].Style.TabStops!.ToArray());
    }

    [Fact]
    public void A_Starred_Character_Style_In_A_Field_Result_Keeps_The_Result()
    {
        // rtfexport/data/tdf141964_numId0.rtf: Writer's export opens a link's
        // result with \*\cs12, and the link text was skipped with it.
        RichTextDocument document = Read(
            @"{\rtf1{\colortbl;\red0\green0\blue128;}{\stylesheet{\*\cs12\cf1\ul Internet Link;}}" +
            @"\pard [{\field{\*\fldinst HYPERLINK ""javascript:toggleToc()"" }{\fldrslt \*\cs12\cf1\ul Verbergen}}]\par}").Document;

        Assert.Equal("[Verbergen]", document.Paragraphs[0].Text);
    }

    [Fact]
    public void First_Line_And_Right_Indents_Are_Read()
    {
        // tdf141964_numId0.rtf's list items hang their bullet with \fi-283.
        RichTextDocument document = Read(
            @"{\rtf1\pard\li707\ri120\fi-283 a\par\pard\li707\fi0 b\par}").Document;

        Assert.Equal(-14.15f, document.Paragraphs[0].Style.FirstLineIndent!.Value, 3);
        Assert.Equal(6f, document.Paragraphs[0].Style.RightIndent!.Value, 3);
        Assert.Null(document.Paragraphs[1].Style.FirstLineIndent);
        Assert.Null(document.Paragraphs[1].Style.RightIndent);
    }

    [Fact]
    public void A_Style_Carries_Its_Indents_And_A_Based_On_Style_May_Clear_Them()
    {
        RichTextDocument document = Read(
            @"{\rtf1{\stylesheet{\s1\li720\fi-360\ri240 hang;}{\s2\sbasedon1\fi0 flat;}}" +
            @"\pard\s1 a\par\pard\s2 b\par}").Document;

        Assert.Equal(-18f, document.Paragraphs[0].Style.FirstLineIndent!.Value, 3);
        Assert.Equal(12f, document.Paragraphs[0].Style.RightIndent!.Value, 3);
        Assert.Null(document.Paragraphs[1].Style.FirstLineIndent);
        Assert.Equal(12f, document.Paragraphs[1].Style.RightIndent!.Value, 3);
    }
}
