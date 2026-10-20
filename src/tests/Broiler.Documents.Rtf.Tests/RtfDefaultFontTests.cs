using System.Text;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfDefaultFontTests
{
    private static RichTextDocument Read(string rtf) => RtfReader.Read(Encoding.ASCII.GetBytes(rtf)).Document;

    [Fact]
    public void Declared_Default_Font_Applies_After_Plain_And_Survives_Export()
    {
        RichTextDocument document = Read(@"{\rtf1\deff3{\fonttbl{\f0 Arial;}{\f3 Liberation Serif;}}\plain inherited {\f0 explicit}\plain inherited again\par}");

        Assert.Equal("Liberation Serif", document.StyleDefaults.FontFamily);
        Assert.Equal("Liberation Serif", document.StyleDefaults.FontFamilyOf(document.Paragraphs[0].Runs[0].Style));
        Assert.Equal("Arial", document.StyleDefaults.FontFamilyOf(document.Paragraphs[0].Runs[1].Style));
        Assert.Equal("Liberation Serif", document.StyleDefaults.FontFamilyOf(document.Paragraphs[0].Runs[^1].Style));
        RichTextDocument restored = RtfReader.Read(RtfWriter.WriteToArray(document)).Document;
        Assert.Equal(document.StyleDefaults, restored.StyleDefaults);
        DocumentAssert.Equivalent(document, restored);
    }

    [Theory]
    [InlineData(@"{\rtf1{\fonttbl{\f0 Arial;}}text\par}")]
    [InlineData(@"{\rtf1\deff9{\fonttbl{\f0 Arial;}}text\par}")]
    [InlineData(@"{\rtf1\deff0{\fonttbl{\f0 ;}}text\par}")]
    public void Missing_Unknown_Or_Blank_Default_Font_Does_Not_Invent_A_Family(string rtf)
    {
        Assert.Null(Read(rtf).StyleDefaults.FontFamily);
    }

    [Fact]
    public void Export_Preserves_An_Unspecified_Default_Despite_Explicit_Run_Fonts()
    {
        RichTextDocument document = Read(@"{\rtf1{\fonttbl{\f1 Arial;}}{\f1 explicit} inherited\par}");
        Assert.Null(RtfReader.Read(RtfWriter.WriteToArray(document)).Document.StyleDefaults.FontFamily);
    }
}
