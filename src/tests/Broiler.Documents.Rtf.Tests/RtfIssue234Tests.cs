using System.Text;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>Office conformance issue #234: RTF tab stops, tabs over the margin and linked pictures.</summary>
public sealed class RtfIssue234Tests
{
    private static DocumentReadResult Read(string rtf) => RtfReader.Read(Encoding.ASCII.GetBytes(rtf));

    [Fact]
    public void Paragraph_Tab_Stops_Are_Read_With_Kind_And_Leader()
    {
        RichTextDocument document = Read(
            @"{\rtf1\pard\tx720\tqc\tx4536\tqr\tldot\tx9072\tqdec\tlhyph\tx9500 a\tab b\par}").Document;

        ParagraphTabStops stops = Assert.IsType<ParagraphTabStops>(document.Paragraphs[0].Style.TabStops);
        Assert.Equal(
            [
                new ParagraphTabStop(36, TabAlignment.Left),
                new ParagraphTabStop(226.8, TabAlignment.Center),
                new ParagraphTabStop(453.6, TabAlignment.Right, "."),
                new ParagraphTabStop(475, TabAlignment.Decimal, "-"),
            ],
            stops.ToArray());
    }

    [Fact]
    public void Pard_Clears_The_Tab_Stops()
    {
        RichTextDocument document = Read(@"{\rtf1\pard\tqr\tx4000 a\par\pard b\par}").Document;

        Assert.NotNull(document.Paragraphs[0].Style.TabStops);
        Assert.Null(document.Paragraphs[1].Style.TabStops);
    }

    [Fact]
    public void A_Bar_Tab_Is_No_Stop_And_Does_Not_Lend_Its_Kind_To_The_Next()
    {
        RichTextDocument document = Read(@"{\rtf1\pard\tqr\tb1000\tx2000 a\par}").Document;

        ParagraphTabStop stop = Assert.Single(document.Paragraphs[0].Style.TabStops!);
        Assert.Equal(new ParagraphTabStop(100, TabAlignment.Left), stop);
    }

    [Fact]
    public void A_Style_Carries_Its_Tab_Stops_To_Its_Paragraphs_And_Those_Based_On_It()
    {
        RichTextDocument document = Read(
            @"{\rtf1{\stylesheet{\s0 Normal;}{\s19\tqr\tldot\tx9350 toc 1;}{\s20\sbasedon19 toc 2;}}" +
            @"\pard\s19 a\tab 1\par\pard\s20 b\tab 2\par}").Document;

        var expected = new ParagraphTabStop(467.5, TabAlignment.Right, ".");
        Assert.Equal(expected, Assert.Single(document.Paragraphs[0].Style.TabStops!));
        Assert.Equal(expected, Assert.Single(document.Paragraphs[1].Style.TabStops!));
    }

    [Fact]
    public void Footer_Tab_Stops_Are_Read()
    {
        // layout/data/tdf158658b.rtf: the page number sits after a centre and a
        // right stop in the footer.
        RichTextDocument document = Read(
            @"{\rtf1{\footerr\pard\tqc\tx4536\tqr\tx9072 \tab\tab 4\par}\pard body\par}").Document;

        RichTextParagraph footer = Assert.Single(document.RunningContent.EffectiveFooter(PageSelection.Default));
        Assert.Equal(2, footer.Style.TabStops!.Count);
    }

    [Fact]
    public void An_Rtf_Document_Lets_Tabs_Run_Over_The_Margin()
    {
        // LibreOffice reads an RTF as a Word 2010 document (no compatibility
        // mode), which turns its TabOverMargin setting on.
        Assert.True(Read(@"{\rtf1 a\par}").Document.StyleDefaults.TabsOverMargin);
    }

    [Fact]
    public void A_Linked_Picture_With_No_Result_Is_Reported()
    {
        // rtfimport/data/tdf59699.rtf
        DocumentReadResult result = Read(
            "{\\rtf1\\pard{\\field{\\*\\fldinst{ INCLUDEPICTURE \"libreoffice.png\" \\\\* MERGEFORMAT \\\\d }}{\\fldrslt}}\\par}");

        Assert.Equal("", result.Document.PlainText);
        Assert.Contains(result.Diagnostics, d => d.Code == "rtf.image.external");
    }

    [Fact]
    public void A_Linked_Picture_With_A_Saved_Result_Is_Not_Reported()
    {
        const string Gif = "47494638396101000100800000000000ffffff21f90401000000002c00000000010001000002024401003b";
        DocumentReadResult result = Read(
            "{\\rtf1\\pard{\\field{\\*\\fldinst{ INCLUDEPICTURE \"x.gif\" }}{\\fldrslt{\\pict\\pngblip " + Gif + "}}}\\par}");

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "rtf.image.external");
        Assert.NotNull(Assert.Single(result.Document.Paragraphs[0].Runs).Style.Image);
    }
}
