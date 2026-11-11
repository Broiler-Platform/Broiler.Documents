using System.Text;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>Office conformance issue #232: embedded-object results and result-less IF fields.</summary>
public sealed class RtfIssue232Tests
{
    private static DocumentReadResult Read(string rtf) => RtfReader.Read(Encoding.ASCII.GetBytes(rtf));

    private const string Gif = "47494638396101000100800000000000ffffff21f90401000000002c00000000010001000002024401003b";

    [Fact]
    public void An_Embedded_Object_Draws_Its_Result_Picture()
    {
        // rtfimport/data/ole-inline.rtf: an Equation.3 object whose \result holds
        // the picture Word saved for it.
        var result = Read(@"{\rtf1{\object\objemb\objw797\objh299{\*\objclass Equation.3}{\*\objdata 0102}" +
            @"{\result{\*\shppict{\pict\picwgoal797\pichgoal299\pngblip " + Gif + @"}}" +
            @"{\nonshppict{\pict\wmetafile8 0102}}}}\par}");

        RichTextParagraph paragraph = Assert.Single(result.Document.Paragraphs);
        InlineImage picture = Assert.IsType<InlineImage>(Assert.Single(paragraph.Runs).Style.Image);
        Assert.Equal(797 / 20.0, (double)picture.Width!, 1);
        Assert.Contains(result.Diagnostics, d => d.Code == "rtf.embedded");
    }

    [Fact]
    public void An_Object_Without_A_Result_Leaves_No_Text()
    {
        var result = Read(@"{\rtf1 a{\object{\*\objclass Equation.3}{\objname x}{\*\objdata 0102}}b\par}");
        Assert.Equal("ab", result.Document.PlainText);
    }

    [Theory]
    // rtfexport/data/tdf156030.rtf
    [InlineData("IF 1 = 2 \"t\" \"f\"", "f")]
    [InlineData("IF 1 <> 2 \"t\" \"f\"", "t")]
    [InlineData("IF 1 > 2 \"t\" \"f\"", "f")]
    [InlineData("IF 1 < 2 \"t\" \"f\"", "t")]
    [InlineData("IF 2 <= 1 \"t\" \"f\"", "f")]
    [InlineData("IF abc = abc \"t\" \"f\"", "t")]
    [InlineData("IF 0 \"t\" \"f\"", "f")]
    [InlineData("IF 1 1 = 1 2 1 true value 7 false value 7", "value")]
    [InlineData("IF 1 1 = \"1 2 1 true value 8 fal\"se \"value 8", "se")]
    public void A_Result_Less_If_Field_Shows_The_Chosen_Text(string instruction, string expected)
    {
        var result = Read(@"{\rtf1{\field{\*\fldinst " + instruction + @"}}\par}");
        Assert.Equal(expected, result.Document.PlainText);
    }

    [Fact]
    public void An_If_Field_With_Too_Few_Arguments_Shows_Nothing()
    {
        Assert.Equal("", Read(@"{\rtf1{\field{\*\fldinst IF a}}\par}").Document.PlainText);
    }
}
