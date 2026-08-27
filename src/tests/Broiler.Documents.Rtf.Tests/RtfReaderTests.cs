using System.Text;
using Broiler.Graphics;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfReaderTests
{
    private static byte[] Bytes(string s) => Encoding.Latin1.GetBytes(s);

    private static DocumentReadResult ReadResult(string rtf) => RtfReader.Read(Bytes(rtf));

    private static RichTextDocument Read(string rtf) => ReadResult(rtf).Document;

    // ---- text and paragraph structure ----

    [Fact(Timeout = 600000)]
    public void Reads_Plain_Text()
    {
        Assert.Equal("Hello World", Read("{\\rtf1\\ansi\\deff0 Hello World}").PlainText);
    }

    [Fact(Timeout = 600000)]
    public void Par_Separates_Paragraphs()
    {
        RichTextDocument document = Read("{\\rtf1 A\\par B}");

        Assert.Equal(2, document.ParagraphCount);
        Assert.Equal("A\nB", document.PlainText);
    }

    [Fact(Timeout = 600000)]
    public void Trailing_Par_Does_Not_Add_An_Empty_Paragraph()
    {
        RichTextDocument document = Read("{\\rtf1 A\\par B\\par}");

        Assert.Equal(2, document.ParagraphCount);
    }

    [Fact(Timeout = 600000)]
    public void Consecutive_Pars_Preserve_A_Blank_Paragraph()
    {
        RichTextDocument document = Read("{\\rtf1 A\\par\\par B}");

        Assert.Equal(3, document.ParagraphCount);
        Assert.Equal("A\n\nB", document.PlainText);
    }

    [Fact(Timeout = 600000)]
    public void Empty_Body_Yields_One_Empty_Paragraph()
    {
        RichTextDocument document = Read("{\\rtf1}");

        Assert.Equal(1, document.ParagraphCount);
        Assert.Equal(string.Empty, document.PlainText);
    }

    // ---- inline formatting ----

    [Fact(Timeout = 600000)]
    public void Bold_Toggle_Applies_To_The_Enclosed_Run()
    {
        RichTextDocument document = Read("{\\rtf1 normal \\b bold \\b0 again}");
        RichTextParagraph p = document.Paragraphs[0];

        Assert.Equal("normal bold again", p.Text);
        Assert.False(p.StyleAt(0).Bold);
        Assert.True(p.StyleAt(p.Text.IndexOf('b')).Bold);
        Assert.False(p.StyleAt(p.Text.IndexOf('g')).Bold);
    }

    [Fact(Timeout = 600000)]
    public void Italic_Underline_And_Strike_Are_Read()
    {
        RichTextParagraph p = Read("{\\rtf1\\i\\ul\\strike X}").Paragraphs[0];
        InlineStyle style = p.StyleAt(0);

        Assert.True(style.Italic);
        Assert.True(style.Underline);
        Assert.True(style.Strikethrough);
    }

    [Fact(Timeout = 600000)]
    public void Font_Size_Is_Half_Points()
    {
        InlineStyle style = Read("{\\rtf1\\fs24 Test}").Paragraphs[0].StyleAt(0);

        Assert.Equal(12f, style.FontSize ?? 0f);
    }

    [Fact(Timeout = 600000)]
    public void Plain_Resets_Character_Formatting()
    {
        RichTextParagraph p = Read("{\\rtf1\\b B\\plain N}").Paragraphs[0];

        Assert.True(p.StyleAt(0).Bold);
        Assert.False(p.StyleAt(1).Bold);
    }

    [Fact(Timeout = 600000)]
    public void Color_Table_Resolves_Cf()
    {
        InlineStyle style = Read("{\\rtf1{\\colortbl;\\red255\\green0\\blue0;}\\cf1 Red}")
            .Paragraphs[0].StyleAt(0);

        Assert.Equal(new BColor(255, 0, 0), style.Foreground);
    }

    [Fact(Timeout = 600000)]
    public void Font_Table_Resolves_Family_Name()
    {
        InlineStyle style = Read("{\\rtf1{\\fonttbl{\\f0\\fswiss Arial;}}\\f0 Hi}")
            .Paragraphs[0].StyleAt(0);

        Assert.Equal("Arial", style.FontFamily);
    }

    // ---- encoding ----

    [Fact(Timeout = 600000)]
    public void Hex_Escape_Decodes_Windows_1252()
    {
        // caf\'e9 -> "café" (0xE9 == é in both CP1252 and Latin-1)
        string text = Read("{\\rtf1 caf\\'e9}").PlainText;

        Assert.Equal(4, text.Length);
        Assert.Equal(0x00E9, (int)text[3]);
    }

    [Fact(Timeout = 600000)]
    public void Hex_Escape_Decodes_Cp1252_Smart_Quote()
    {
        // \'92 is U+2019 (right single quote) in Windows-1252, not Latin-1.
        string text = Read("{\\rtf1 it\\'92s}").PlainText;

        Assert.Equal(0x2019, (int)text[2]);
    }

    [Fact(Timeout = 600000)]
    public void Unicode_Escape_Decodes_And_Honours_Uc_Skip()
    {
        // \u233 -> é, then uc1 skips the single fallback char '?'.
        string text = Read("{\\rtf1\\u233 ?z}").PlainText;

        Assert.Equal(2, text.Length);
        Assert.Equal(0x00E9, (int)text[0]);
        Assert.Equal('z', text[1]);
    }

    [Fact(Timeout = 600000)]
    public void Uc0_Skips_No_Fallback_Characters()
    {
        string text = Read("{\\rtf1\\uc0\\u233 ?z}").PlainText;

        Assert.Equal(3, text.Length);
        Assert.Equal(0x00E9, (int)text[0]);
    }

    // ---- paragraph formatting ----

    [Fact(Timeout = 600000)]
    public void Center_Alignment_Is_Read()
    {
        RichTextDocument document = Read("{\\rtf1\\qc Centered\\par}");

        Assert.Equal(TextAlignment.Center, document.Paragraphs[0].Style.Alignment);
    }

    [Fact(Timeout = 600000)]
    public void Line_Is_A_Soft_Break_Inside_One_Paragraph()
    {
        RichTextDocument document = Read("{\\rtf1 A\\line B}");

        Assert.Equal(1, document.ParagraphCount);
        string text = document.Paragraphs[0].Text;
        Assert.Equal(0x2028, (int)text[1]);
    }

    [Fact(Timeout = 600000)]
    public void Tab_Control_Word_Emits_A_Tab()
    {
        Assert.Equal("A\tB", Read("{\\rtf1 A\\tab B}").PlainText);
    }

    // ---- destinations ----

    [Fact(Timeout = 600000)]
    public void Ignorable_Destination_Is_Skipped()
    {
        Assert.Equal("Hello", Read("{\\rtf1{\\*\\generator Foo}Hello}").PlainText);
    }

    [Fact(Timeout = 600000)]
    public void Info_Group_Is_Skipped()
    {
        Assert.Equal("Body", Read("{\\rtf1{\\info{\\author Me}}Body}").PlainText);
    }

    [Fact(Timeout = 600000)]
    public void Embedded_Picture_Is_Skipped_And_Reported()
    {
        DocumentReadResult result = ReadResult("{\\rtf1{\\pict\\wmetafile8 010203}Text}");

        Assert.Equal("Text", result.Document.PlainText);
        Assert.Contains(result.Diagnostics, d => d.Code == "rtf.embedded");
    }

    // ---- hyperlink fields ----

    [Fact(Timeout = 600000)]
    public void Hyperlink_Field_Sets_Link_On_The_Result_Text()
    {
        InlineStyle style = Read("{\\rtf1{\\field{\\*\\fldinst{HYPERLINK \"https://x.com\"}}{\\fldrslt click}}}")
            .Paragraphs[0].StyleAt(0);

        Assert.Equal("click", Read("{\\rtf1{\\field{\\*\\fldinst{HYPERLINK \"https://x.com\"}}{\\fldrslt click}}}").PlainText);
        Assert.Equal("https://x.com", style.LinkHref);
    }

    [Fact(Timeout = 600000)]
    public void Hyperlink_With_Disallowed_Scheme_Is_Dropped()
    {
        DocumentReadResult result = ReadResult(
            "{\\rtf1{\\field{\\*\\fldinst{HYPERLINK \"javascript:evil\"}}{\\fldrslt click}}}");

        Assert.Equal("click", result.Document.PlainText);
        Assert.Null(result.Document.Paragraphs[0].StyleAt(0).LinkHref);
        Assert.Contains(result.Diagnostics, d => d.Code == "rtf.link");
    }

    // ---- robustness ----

    [Fact(Timeout = 600000)]
    public void Unbalanced_Groups_Do_Not_Throw()
    {
        Assert.Equal("A", Read("{\\rtf1 A").PlainText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("}}}}")]
    [InlineData("{\\rtf1\\u")]
    [InlineData("{\\rtf1{\\field{\\*\\fldinst}}")]
    [InlineData("{\\rtf1{\\colortbl;\\red")]
    public void Malformed_Input_Never_Throws(string rtf)
    {
        RichTextDocument document = Read(rtf);

        Assert.NotNull(document);
        Assert.True(document.ParagraphCount >= 1);
    }

    // ---- a representative document ----

    [Fact(Timeout = 600000)]
    public void Reads_A_Representative_WordPad_Style_Document()
    {
        string rtf =
            "{\\rtf1\\ansi\\ansicpg1252\\deff0" +
            "{\\fonttbl{\\f0\\fnil\\fcharset0 Calibri;}}" +
            "{\\colortbl ;\\red255\\green0\\blue0;}" +
            "\\pard\\f0\\fs22 Hello \\b bold\\b0  and \\cf1 red\\cf0 .\\par\n" +
            "Second line.\\par}";

        DocumentReadResult result = ReadResult(rtf);
        RichTextDocument document = result.Document;

        Assert.Equal(2, document.ParagraphCount);
        Assert.StartsWith("Hello ", document.Paragraphs[0].Text);
        Assert.Contains("bold", document.Paragraphs[0].Text);
        Assert.Contains("red", document.Paragraphs[0].Text);
        Assert.Equal("Second line.", document.Paragraphs[1].Text);

        RichTextParagraph first = document.Paragraphs[0];
        Assert.True(first.StyleAt(first.Text.IndexOf("bold")).Bold);
        Assert.Equal(new BColor(255, 0, 0), first.StyleAt(first.Text.IndexOf("red")).Foreground);
        Assert.Equal("Calibri", first.StyleAt(0).FontFamily);
        Assert.Equal(11f, first.StyleAt(0).FontSize ?? 0f);
    }
}
