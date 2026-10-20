using System.Text;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// The character formatting in effect at <c>\par</c> is the paragraph mark's,
/// and it is what an empty paragraph is as tall as.
/// </summary>
public sealed class RtfParagraphMarkTests
{
    private static RichTextDocument Read(string rtf) => RtfReader.Read(Encoding.ASCII.GetBytes(rtf)).Document;

    [Fact]
    public void An_Empty_Paragraph_Takes_The_Formatting_In_Effect_At_Its_Par()
    {
        RichTextDocument document = Read(@"{\rtf1\ansi{\fs16\b\par}text\par}");

        RichTextParagraph empty = document.Paragraphs[0];
        Assert.Equal(0, empty.Length);
        Assert.Equal(8f, empty.MarkStyle.FontSize);
        Assert.True(empty.MarkStyle.Bold);
    }

    [Fact]
    public void A_Group_Closed_Before_The_Par_Does_Not_Reach_The_Mark()
    {
        RichTextDocument document = Read(@"{\rtf1\ansi{\fs16 text}\par}");

        Assert.Null(document.Paragraphs[0].MarkStyle.FontSize);
    }
}
