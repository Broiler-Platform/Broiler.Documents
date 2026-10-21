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
    [InlineData(@"\sl-480\slmult1", 2f)]
    public void The_Multiplier_Is_Read_Only_Under_Slmult1(string controls, float expected)
    {
        // A negative multiple is taken by its size, as LibreOffice takes it.
        Assert.Equal(expected, Read("{\\rtf1" + controls + " text\\par}").Paragraphs[0].Style.LineSpacing);
    }

    [Theory]
    [InlineData(@"\sl-200", LineHeightRule.Exact, 10f)]
    [InlineData(@"\sl-200\slmult0", LineHeightRule.Exact, 10f)]
    [InlineData(@"\sl300", LineHeightRule.AtLeast, 15f)]
    [InlineData(@"\sl480\slmult1\slmult0", LineHeightRule.AtLeast, 24f)]
    [InlineData(@"\sl0", LineHeightRule.Proportional, 0f)]
    [InlineData(@"\sl276\slmult1", LineHeightRule.Proportional, 0f)]
    public void Without_Slmult1_The_Amount_Is_A_Height_Exact_Where_Negative(
        string controls, LineHeightRule rule, float height)
    {
        // tdf133437.rtf sets every line of its statement at exactly ten
        // points with \sl-200, the way Word writes a form's fixed lines.
        ParagraphStyle style = Read("{\\rtf1" + controls + " text\\par}").Paragraphs[0].Style;

        Assert.Equal(rule, style.LineHeightRule);
        Assert.Equal(height, style.LineHeight);
    }

    [Fact]
    public void A_Height_Beyond_Words_Limit_Is_Clamped()
    {
        ParagraphStyle style = Read(@"{\rtf1\sl-2147483648 text\par}").Paragraphs[0].Style;

        Assert.Equal(LineHeightRule.Exact, style.LineHeightRule);
        Assert.Equal(ParagraphStyle.MaxLineHeight, style.LineHeight);
    }

    [Fact]
    public void Pard_Clears_A_Height()
    {
        RichTextDocument document = Read(@"{\rtf1\sl-200 A\par\pard B\par}");

        Assert.Equal(LineHeightRule.Exact, document.Paragraphs[0].Style.LineHeightRule);
        Assert.Equal(LineHeightRule.Proportional, document.Paragraphs[1].Style.LineHeightRule);
    }

    [Theory]
    [InlineData(LineHeightRule.Exact, @"\sl-200\slmult0")]
    [InlineData(LineHeightRule.AtLeast, @"\sl200\slmult0")]
    public void A_Height_Round_Trips_With_Its_Rule(LineHeightRule rule, string written)
    {
        RichTextDocument source = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Plain("x").WithParagraphStyle(
                ParagraphStyle.Default with { LineHeightRule = rule, LineHeight = 10f }),
        ]);

        byte[] rtf = RtfWriter.WriteToArray(source);
        ParagraphStyle back = RtfReader.Read(rtf).Document.Paragraphs[0].Style;

        Assert.Contains(written, Encoding.ASCII.GetString(rtf));
        Assert.Equal(rule, back.LineHeightRule);
        Assert.Equal(10f, back.LineHeight);
    }
}
