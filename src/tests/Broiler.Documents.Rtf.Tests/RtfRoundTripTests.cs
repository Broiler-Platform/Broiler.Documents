using System.Text;
using Broiler.Graphics;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfRoundTripTests
{
    private static RichTextParagraph Para(ParagraphStyle style, params (string Text, InlineStyle Style)[] runs)
    {
        RichTextParagraph paragraph = RichTextParagraph.Create(string.Empty, InlineStyle.Default, style);
        foreach ((string text, InlineStyle runStyle) in runs)
            paragraph = paragraph.InsertText(paragraph.Length, text, runStyle);
        return paragraph;
    }

    private static RichTextParagraph Para(params (string Text, InlineStyle Style)[] runs) =>
        Para(ParagraphStyle.Default, runs);

    private static RichTextDocument Doc(params RichTextParagraph[] paragraphs) =>
        RichTextDocument.FromParagraphs(paragraphs);

    private static RichTextDocument RoundTrip(RichTextDocument document) =>
        RtfReader.Read(RtfWriter.WriteToArray(document)).Document;

    private static void AssertEquivalent(RichTextDocument expected, RichTextDocument actual)
    {
        Assert.Equal(expected.ParagraphCount, actual.ParagraphCount);
        for (int i = 0; i < expected.ParagraphCount; i++)
        {
            RichTextParagraph pe = expected.Paragraphs[i];
            RichTextParagraph pa = actual.Paragraphs[i];
            Assert.Equal(pe.Text, pa.Text);
            Assert.Equal(pe.Style, pa.Style);
            Assert.Equal(pe.Runs.Count, pa.Runs.Count);
            for (int j = 0; j < pe.Runs.Count; j++)
            {
                Assert.Equal(pe.Runs[j].Length, pa.Runs[j].Length);
                Assert.Equal(pe.Runs[j].Style, pa.Runs[j].Style);
            }
        }
    }

    [Fact(Timeout = 600000)]
    public void Plain_Paragraphs()
    {
        RichTextDocument document = Doc(
            Para(("Alpha", InlineStyle.Default)),
            Para(("Beta", InlineStyle.Default)),
            Para(("Gamma", InlineStyle.Default)));

        AssertEquivalent(document, RoundTrip(document));
    }

    [Fact(Timeout = 600000)]
    public void Inline_Styles()
    {
        var bold = new InlineStyle { Bold = true };
        var italicUnderline = new InlineStyle { Italic = true, Underline = true };
        var strike = new InlineStyle { Strikethrough = true };
        var sized = new InlineStyle { FontSize = 18f };

        RichTextDocument document = Doc(Para(
            ("normal", InlineStyle.Default),
            ("bold", bold),
            ("iu", italicUnderline),
            ("struck", strike),
            ("big", sized)));

        AssertEquivalent(document, RoundTrip(document));
    }

    [Fact(Timeout = 600000)]
    public void Colors_Fonts_And_Highlight()
    {
        var red = new InlineStyle { Foreground = new BColor(255, 0, 0) };
        var arialBlue = new InlineStyle { FontFamily = "Arial", Foreground = new BColor(0, 0, 255) };
        var highlighted = new InlineStyle { Background = new BColor(255, 255, 0) };

        RichTextDocument document = Doc(Para(
            ("r", red),
            ("ab", arialBlue),
            ("hl", highlighted)));

        AssertEquivalent(document, RoundTrip(document));
    }

    [Fact(Timeout = 600000)]
    public void Alignment_Indent_And_Spacing()
    {
        ParagraphStyle style = ParagraphStyle.Default with
        {
            Alignment = TextAlignment.Center,
            IndentLevel = 2,
            SpacingBefore = 6f,
            SpacingAfter = 12f,
        };

        RichTextDocument document = Doc(Para(style, ("centered", InlineStyle.Default)));

        AssertEquivalent(document, RoundTrip(document));
    }

    [Fact(Timeout = 600000)]
    public void Unicode_Including_A_Supplementary_Character()
    {
        string text = "caf" + (char)0x00E9 + (char)0x2019 + char.ConvertFromUtf32(0x1F600);
        RichTextDocument document = Doc(Para((text, InlineStyle.Default)));

        RichTextDocument round = RoundTrip(document);

        Assert.Equal(text, round.Paragraphs[0].Text);
        AssertEquivalent(document, round);
    }

    [Fact(Timeout = 600000)]
    public void Hyperlink()
    {
        var link = new InlineStyle { LinkHref = "https://example.com" };
        RichTextDocument document = Doc(Para(("visit", link)));

        RichTextDocument round = RoundTrip(document);

        Assert.Equal("https://example.com", round.Paragraphs[0].StyleAt(0).LinkHref);
        AssertEquivalent(document, round);
    }

    [Fact(Timeout = 600000)]
    public void Empty_Document()
    {
        AssertEquivalent(RichTextDocument.Empty, RoundTrip(RichTextDocument.Empty));
    }

    [Fact(Timeout = 600000)]
    public void Trailing_Empty_Paragraph_Is_Preserved()
    {
        RichTextDocument document = Doc(Para(("A", InlineStyle.Default)), RichTextParagraph.Empty);

        RichTextDocument round = RoundTrip(document);

        Assert.Equal(2, round.ParagraphCount);
        AssertEquivalent(document, round);
    }

    [Fact(Timeout = 600000)]
    public void Read_Write_Read_Is_Stable_On_A_Fixture()
    {
        string fixture =
            "{\\rtf1\\ansi\\ansicpg1252\\deff0" +
            "{\\fonttbl{\\f0\\fnil\\fcharset0 Calibri;}}" +
            "{\\colortbl ;\\red255\\green0\\blue0;}" +
            "\\pard\\f0\\fs22 Hello \\b bold\\b0  and \\cf1 red\\cf0 .\\par\n" +
            "Second line.\\par}";

        RichTextDocument first = RtfReader.Read(Encoding.Latin1.GetBytes(fixture)).Document;
        RichTextDocument second = RoundTrip(first);

        AssertEquivalent(first, second);
    }
}
