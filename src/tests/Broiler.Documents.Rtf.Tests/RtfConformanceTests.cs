using System.Text;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// Validates the categories in <c>docs/rtf-conformance.md</c>: honoured entity
/// control words produce the right characters, ignored control words are inert,
/// and skipped destinations contribute no text.
/// </summary>
public sealed class RtfConformanceTests
{
    private static RichTextDocument Read(string rtf) =>
        RtfReader.Read(Encoding.Latin1.GetBytes(rtf)).Document;

    [Theory]
    [InlineData("\\tab", 0x0009)]
    [InlineData("\\line", 0x2028)]
    [InlineData("\\lquote", 0x2018)]
    [InlineData("\\rquote", 0x2019)]
    [InlineData("\\ldblquote", 0x201C)]
    [InlineData("\\rdblquote", 0x201D)]
    [InlineData("\\bullet", 0x2022)]
    [InlineData("\\endash", 0x2013)]
    [InlineData("\\emdash", 0x2014)]
    public void Honoured_Entity_Control_Words_Produce_The_Right_Character(string word, int expected)
    {
        string text = Read("{\\rtf1 " + word + " x}").Paragraphs[0].Text;

        Assert.Equal(expected, (int)text[0]);
        Assert.Equal('x', text[^1]);
    }

    [Theory]
    [InlineData("\\deff0")]
    [InlineData("\\viewkind4")]
    [InlineData("\\widowctrl")]
    [InlineData("\\sl240")]
    [InlineData("\\lang1033")]
    [InlineData("\\kerning0")]
    [InlineData("\\nosupersub")]
    public void Ignored_Control_Words_Do_Not_Affect_Text(string word)
    {
        Assert.Equal("hello", Read("{\\rtf1 " + word + " hello}").PlainText);
    }

    [Fact(Timeout = 600000)]
    public void Ignored_Line_Spacing_Leaves_The_Default()
    {
        // \sl is ignored, so LineSpacing stays at the ParagraphStyle default (1).
        RichTextDocument document = Read("{\\rtf1\\sl480\\slmult1 text\\par}");

        Assert.Equal(ParagraphStyle.Default.LineSpacing, document.Paragraphs[0].Style.LineSpacing);
    }

    [Theory]
    [InlineData("{\\info{\\author Me}}")]
    [InlineData("{\\stylesheet{\\s0 Normal;}}")]
    [InlineData("{\\header a header}")]
    [InlineData("{\\footer a footer}")]
    [InlineData("{\\*\\generator App;}")]
    [InlineData("{\\*\\somethingunknown data}")]
    public void Skipped_Destinations_Contribute_No_Text(string destination)
    {
        Assert.Equal("Body", Read("{\\rtf1 " + destination + "Body}").PlainText);
    }

    [Theory]
    [InlineData("\\qc", TextAlignment.Center)]
    [InlineData("\\qr", TextAlignment.Right)]
    [InlineData("\\ql", TextAlignment.Left)]
    [InlineData("\\qj", TextAlignment.Left)] // justify approximated to left
    public void Alignment_Control_Words_Map_As_Documented(string word, TextAlignment expected)
    {
        RichTextDocument document = Read("{\\rtf1\\pard" + word + " text\\par}");

        Assert.Equal(expected, document.Paragraphs[0].Style.Alignment);
    }
}
