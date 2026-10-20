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

        // Unsupported 1251 uses the existing Latin-1 fallback before reset;
        // the reset must decode Windows-1252's euro rather than another U+0080.
        Assert.Equal("\u0080€", document.PlainText);
    }

    [Fact]
    public void Plain_Uses_The_Declared_Default_Fonts_Charset()
    {
        RichTextDocument document = Read(@"{\rtf1\ansicpg1252\deff0{\fonttbl{\f0\fcharset204 Default;}{\f1\fcharset0 Other;}}\f1\'80\plain\'80\par}");
        Assert.Equal("€\u0080", document.PlainText);
    }

    [Fact]
    public void Document_Code_Page_Remains_Available_After_A_Font_Overrides_It()
    {
        RichTextDocument document = Read(@"{\rtf1\ansicpg1251{\fonttbl{\f1\fcharset0 Other;}}\f1\'80\plain\'80\par}");
        Assert.Equal("€\u0080", document.PlainText);
    }

    private static RichTextDocument Read(string rtf) => RtfReader.Read(Encoding.ASCII.GetBytes(rtf)).Document;
}
