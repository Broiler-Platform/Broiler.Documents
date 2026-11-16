using System.Text;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>Office conformance issue #249: code pages and SYMBOL fields as LibreOffice 24.2 reads them.</summary>
public sealed class RtfIssue249Tests
{
    private static RichTextDocument Read(string rtf) => RtfReader.Read(Encoding.Latin1.GetBytes(rtf)).Document;

    [Theory]
    // fdo85889-pc, -pca and -mac: the header's character set names the code page.
    [InlineData(@"\pc", "±≥≤")]
    [InlineData(@"\pca", "±‗¾")]
    [InlineData(@"\mac", "ÒÚÛ")]
    [InlineData(@"\ansi", "ñòó")]
    public void The_Header_Character_Set_Decodes_A_Font_With_No_Charset(string set, string expected)
    {
        Assert.Equal(expected, Read(@"{\rtf1" + set + @" \deff0{\fonttbl{\f0\fmodern Helvetica;}}\pard\f0 \'f1\'f2\'f3\par}").PlainText);
    }

    [Fact]
    public void A_Font_With_No_Charset_Is_In_The_Documents_Code_Page()
    {
        Assert.Equal("Имя", Read(@"{\rtf1\ansi\ansicpg1251{\fonttbl{\f1 Arial;}}\f1 " + "Èìÿ" + @"\par}").PlainText);
    }

    [Fact]
    public void A_Fonts_Code_Page_Comes_From_Cpg()
    {
        // fdo48446: \cpg1251 in the font entry, the text in raw high bytes.
        Assert.Equal("Имя", Read(@"{\rtf{\fonttbl{\f1\cpg1251 Arial;}}\f1 " + "Èìÿ" + @"\par}").PlainText);
    }

    [Theory]
    // tdf95706: the suffix of the font's name names its code page, ahead of
    // the entry's \cpg and \fcharset.
    [InlineData(@"{\f1 Arial Baltic;}", "ĄĮĀĆÄ")]
    [InlineData(@"{\f1 Arial CE;}", "ŔÁÂĂÄ")]
    [InlineData(@"{\f1 Arial Cyr;}", "АБВГД")]
    [InlineData(@"{\f1 Arial Greek;}", "ΐΑΒΓΔ")]
    [InlineData(@"{\f1 Arial (Hebrew);}", "ְֱֲֳִ")]
    [InlineData(@"{\f1 Arial BlaBlaBla;}", "ÀÁÂÃÄ")]
    [InlineData(@"{\f1\cpg1253 Arial;}", "ΐΑΒΓΔ")]
    [InlineData(@"{\f1\cpg1253\fcharset161 Arial Baltic;}", "ĄĮĀĆÄ")]
    [InlineData(@"{\f1 Arial Baltic;\cpg1253\fcharset161}", "ĄĮĀĆÄ")]
    public void A_Font_Name_Suffix_Names_The_Code_Page(string font, string expected)
    {
        Assert.Equal(expected, Read(@"{\rtf\ansi{\fonttbl" + font + @"}\pard\f1 \'c0\'c1\'c2\'c3\'c4\par}").PlainText);
    }

    [Fact]
    public void An_Empty_Symbol_Field_Inserts_Its_Character_In_Its_Font()
    {
        // tdf161864: LibreOffice inserts the SYMBOL field's character (decimal or
        // 0x hexadecimal) in the font \f names.
        RichTextDocument document = Read(@"{\rtf1\ansi{\fonttbl{\f10 Mangal;}}" +
            @"{\field{\*\fldinst SYMBOL 108 \\f ""Liberation Serif"" \\s 8}{\fldrslt\f10\fs8}} First " +
            @"{\field{\*\fldinst SYMBOL 0xFD \\f Arial}{\fldrslt }}\par}");
        Assert.Equal("l First ý", document.PlainText);
        Assert.Equal("Liberation Serif", document.Paragraphs[0].StyleAt(0).FontFamily);
        Assert.Equal("Arial", document.Paragraphs[0].StyleAt(8).FontFamily);
    }

    [Fact]
    public void A_Symbol_Field_In_A_Windows_Symbol_Font_Stays_Empty()
    {
        // 165333: with no Wingdings installed LibreOffice draws nothing for it.
        Assert.Equal("<>", Read(@"{\rtf1\ansi <{\field{\*\fldinst SYMBOL 253 \\f ""Wingdings"" \\s 5}{\fldrslt }}>\par}").PlainText);
    }

    [Theory]
    [InlineData("SYMBOL", "")]
    [InlineData("SYMBOLS 65", "")]
    [InlineData("SYMBOL x", "")]
    [InlineData("SYMBOL 65", "A")]
    [InlineData(@"SYMBOL 0x41 \\s 10", "A")]
    public void Reads_The_Symbol_Fields_Character(string instruction, string expected)
    {
        Assert.Equal("<" + expected + ">",
            Read(@"{\rtf1\ansi <{\field{\*\fldinst " + instruction + @"}{\fldrslt }}>\par}").PlainText);
    }

    [Fact]
    public void A_Lowered_Equation_Field_Is_Its_Text_Lowered()
    {
        // fdo36089: EQ \s\do5(1) with no result is "1", five points down.
        RichTextDocument document = Read(@"{\rtf1{\pard\plain\fs20 Subscript{{\field{\*\fldinst{ EQ \\s\\do5({\fs16 1})}}{\fldrslt }}}\par}}");
        Assert.Equal("Subscript1", document.PlainText);
        BaselineShift? shift = document.Paragraphs[0].StyleAt(9).Shift;
        Assert.Equal(BaselinePosition.Offset, shift?.Position);
        Assert.Equal(-0.5, shift?.Offset);
    }

    [Fact]
    public void A_Page_Field_With_No_Result_Is_Still_A_Page_Number()
    {
        // fdo48193: a PAGE field with no \fldrslt shows the page number.
        RichTextDocument document = Read(@"{\rtf1\pard foo{\field{\*\fldinst {\f8\fs16 PAGE }}}bar\par}");
        Assert.Equal("foo1bar", document.PlainText);
        Assert.Equal(PageFieldKind.PageNumber, document.Paragraphs[0].StyleAt(3).PageField?.Kind);
    }

    [Fact]
    public void Index_And_Contents_Entries_Are_Not_Body_Text()
    {
        // fdo84685: \xe and \tc hold an entry's text, which is no part of the body.
        Assert.Equal(" Some text\n Some text", Read(@"{\rtf1\pard{\v {\xe {\v {\f0\fs20 Key the 1st}}}} Some text\par" +
            @"{\v {\tc {\v {\f0\fs20 foo}}}} Some text\par}").PlainText);
    }

    [Fact]
    public void A_Result_With_No_Field_Stays_Where_It_Stands()
    {
        // tdf108947: a \fldrslt group in a header with no \field around it.
        DocumentReadResult result = RtfReader.Read(Encoding.Latin1.GetBytes(
            @"{\rtf1\pard Body\par{\header\pard Header{\fldrslt {\noproof ?}}\par}}"));
        Assert.Equal("Body", result.Document.PlainText);
        Assert.Equal("Header?", Assert.Single(result.Document.RunningContent.Header(PageSelection.Default)).Text);
    }

    [Theory]
    // tdf130817: raised text just before a note is its custom mark, and stays.
    [InlineData(@"{\super ${\*\footnote\ftnalt $\pard note}}Body", "$", true)]
    [InlineData(@"{\super #{\*\footnote \pard note}}Body", "#", true)]
    [InlineData(@"{\super\chftn}{\*\footnote \pard {\super\chftn} note}Body", null, false)]
    [InlineData(@"A{\super *}B{\*\footnote \pard note}Body", null, false)]
    public void Raised_Text_Before_A_Note_Is_Its_Custom_Mark(string body, string? mark, bool kept)
    {
        RichTextDocument document = Read(@"{\rtf1\ansi " + body + @"\par}");
        DocumentNote note = Assert.Single(DocumentNote.CitedIn(document.Paragraphs));
        Assert.Equal(mark, note.CustomMark);
        if (kept)
            Assert.StartsWith(mark!, document.PlainText);
    }

    [Theory]
    // tdf169298: LibreOffice gathers a text field's result into one string and
    // puts it in where the field ends, after the paragraph marks in it.
    [InlineData("DOCVARIABLE  foobar  \\\\* MERGEFORMAT", "A\n\nfoobar")]
    [InlineData("AUTHOR", "A\n\nfoobar")]
    [InlineData("REF bm", "A\n\nfoobar")]
    // A field it keeps as a field mark keeps its result's paragraphs.
    [InlineData("QUOTE x", "A\nfoo\nbar")]
    public void A_Text_Fields_Result_Goes_In_Where_The_Field_Ends(string instruction, string expected)
    {
        RichTextDocument document = Read(@"{\rtf1 A\par {\field{\*\fldinst { " + instruction +
            @" }}{\fldrslt foo\par {\b bar}}}\par}");
        Assert.Equal(expected, document.PlainText);
    }

    [Fact]
    public void A_Symbol_Fields_Saved_Result_Stands()
    {
        Assert.Equal("<x>", Read(@"{\rtf1\ansi <{\field{\*\fldinst SYMBOL 65}{\fldrslt x}}>\par}").PlainText);
    }
}
