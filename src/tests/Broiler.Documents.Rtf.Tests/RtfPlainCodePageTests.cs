using System.Text;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfPlainCodePageTests
{
    [Theory]
    [InlineData(@"\deff0{\fonttbl{\f0\fcharset0 Default;}{\f1\fcharset204 Other;}}")]
    [InlineData(@"{\fonttbl{\f1\fcharset204 Other;}}")]
    public void Plain_Restores_Default_Byte_Decoding_After_A_Font_Switch(string fonts)
    {
        string rtf = @"{\rtf1\ansicpg1252" + fonts + @"\f1\'80\plain\'80\par}";
        RichTextDocument document = Read(rtf);

        // 1251 decodes 0x80 as Cyrillic Dje before the reset; the reset must
        // decode Windows-1252's euro rather than another Dje.
        Assert.Equal("\u0402€", document.PlainText);
    }

    [Fact]
    public void Plain_Uses_The_Declared_Default_Fonts_Charset()
    {
        RichTextDocument document = Read(@"{\rtf1\ansicpg1252\deff0{\fonttbl{\f0\fcharset204 Default;}{\f1\fcharset0 Other;}}\f1\'80\plain\'80\par}");
        Assert.Equal("€\u0402", document.PlainText);
    }

    [Fact]
    public void Document_Code_Page_Remains_Available_After_A_Font_Overrides_It()
    {
        RichTextDocument document = Read(@"{\rtf1\ansicpg1251{\fonttbl{\f1\fcharset0 Other;}}\f1\'80\plain\'80\par}");
        Assert.Equal("€\u0402", document.PlainText);
    }

    private static RichTextDocument Read(string rtf) => RtfReader.Read(Encoding.ASCII.GetBytes(rtf)).Document;

    [Theory]
    [InlineData(204, "'c1'e0'ed'ea", "Банк")]
    [InlineData(238, "'8a'e8", "Šč")]
    [InlineData(161, "'c1'e1", "Αα")]
    public void A_Fonts_Charset_Decodes_Its_Code_Pages_High_Bytes(int charset, string bytes, string expected)
    {
        RtfResult(@"{\rtf1\ansi{\fonttbl{\f0\fcharset" + charset + @" Arial;}}\f0 " + bytes.Replace("'", @"\'") + @"\par}",
            out RichTextDocument document, out IReadOnlyList<DocumentDiagnostic> diagnostics);

        Assert.Equal(expected, document.PlainText);
        Assert.DoesNotContain(diagnostics, d => d.Code == "rtf.codepage");
    }

    [Fact]
    public void A_Double_Byte_Code_Page_Still_Falls_Back_And_Says_So()
    {
        RtfResult(@"{\rtf1\ansi{\fonttbl{\f0\fcharset128 MS Mincho;}}\f0 \'82\par}",
            out _, out IReadOnlyList<DocumentDiagnostic> diagnostics);

        Assert.Contains(diagnostics, d => d.Code == "rtf.codepage");
    }

    private static void RtfResult(string rtf, out RichTextDocument document, out IReadOnlyList<DocumentDiagnostic> diagnostics)
    {
        DocumentReadResult result = RtfReader.Read(Encoding.ASCII.GetBytes(rtf));
        document = result.Document;
        diagnostics = result.Diagnostics;
    }
}
