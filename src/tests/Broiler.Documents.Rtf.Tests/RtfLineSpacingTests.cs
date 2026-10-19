using System.Text;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfLineSpacingTests
{
    private static RichTextDocument Read(string content) => RtfReader.Read(Encoding.ASCII.GetBytes(content)).Document;

    [Theory]
    [InlineData(240, 1f)]
    [InlineData(276, 1.15f)]
    [InlineData(360, 1.5f)]
    [InlineData(480, 2f)]
    public void Multiple_Line_Spacing_Is_Read_And_Preserved_On_Write(int amount, float expected)
    {
        RichTextDocument document = Read("{\\rtf1\\sl" + amount + "\\slmult1 first\\line second\\par}");
        Assert.Equal("first\u2028second", document.PlainText);
        Assert.Equal(expected, document.Paragraphs[0].Style.LineSpacing);
        DocumentAssert.Equivalent(document, RtfReader.Read(RtfWriter.WriteToArray(document)).Document);
    }

    [Fact]
    public void Final_Paragraph_Keeps_Spacing_Without_An_Explicit_Paragraph_Terminator()
    {
        RichTextDocument document = Read(@"{\rtf1\sl276\slmult1 first\line second}");
        Assert.Single(document.Paragraphs);
        Assert.Equal(1.15f, document.Paragraphs[0].Style.LineSpacing);
    }

    [Fact]
    public void Group_Restoration_And_Paragraph_Reset_Restore_Spacing_State()
    {
        RichTextDocument document = Read(@"{\rtf1\sl276\slmult1 A\par{\sl480 B\par}C\par\pard D\par}");
        Assert.Equal(new[] { 1.15f, 2f, 1.15f, 1f }, document.Paragraphs.Select(p => p.Style.LineSpacing));
    }

    [Theory]
    [InlineData(@"\slmult1\sl360", 1.5f)]
    [InlineData(@"\sl480", 1f)]
    [InlineData(@"\sl-480\slmult0", 1f)]
    [InlineData(@"\sl480\slmult1\slmult0", 1f)]
    [InlineData(@"\sl0\slmult1", 1f)]
    [InlineData(@"\sl-480\slmult1", 1f)]
    public void Only_Positive_Multiplier_Forms_Are_Projected(string controls, float expected)
    {
        Assert.Equal(expected, Read("{\\rtf1" + controls + " text\\par}").Paragraphs[0].Style.LineSpacing);
    }
}
