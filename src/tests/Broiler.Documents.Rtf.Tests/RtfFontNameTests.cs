using System.Text;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfFontNameTests
{
    [Fact]
    public void Literal_High_Bytes_In_Font_Names_Use_The_Declared_Code_Page()
    {
        byte[] prefix = Encoding.ASCII.GetBytes(@"{\rtf1\ansi\ansicpg1252\deff1{\fonttbl{\f1 Family ");
        byte[] suffix = Encoding.ASCII.GetBytes(@"s;}}body\par}");
        byte[] rtf = [.. prefix, 0x92, .. suffix];
        RichTextDocument document = RtfReader.Read(rtf).Document;

        Assert.Equal("Family ’s", document.StyleDefaults.FontFamily);
        Assert.Equal("body", document.PlainText);
    }

    [Theory]
    [InlineData("München")]
    [InlineData("日本語")]
    [InlineData("Family 🐔")]
    [InlineData(@"Family {name}\face")]
    [InlineData("Family;Variant")]
    public void Exported_Font_Names_Survive_Import(string family)
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("body", InlineStyle.Default with { FontFamily = family })])
            .WithStyleDefaults(DocumentStyleDefaults.Default with { FontFamily = family });
        RichTextDocument restored = RtfReader.Read(RtfWriter.WriteToArray(document)).Document;

        Assert.Equal(family, restored.StyleDefaults.FontFamily);
        Assert.Equal(family, restored.Paragraphs[0].Runs[0].Style.FontFamily);
        Assert.Equal("body", restored.PlainText);
    }

    [Theory]
    [InlineData(@"\uc0 M\u252 nchen", "München")]
    [InlineData(@"\uc2 M\u252??nchen", "München")]
    [InlineData(@"M\'fcnchen", "München")]
    [InlineData(@"\uc1 M\u252\'fcnchen", "München")]
    public void Font_Name_Decoding_Honours_Unicode_Fallback_And_Hex_Bytes(string name, string expected)
    {
        string rtf = @"{\rtf1\ansi\deff1{\fonttbl{\f1 " + name + @";}}body\par}";
        RichTextDocument document = RtfReader.Read(Encoding.ASCII.GetBytes(rtf)).Document;

        Assert.Equal(expected, document.StyleDefaults.FontFamily);
        Assert.Equal("body", document.PlainText);
    }
}
