using System.Text;
using Broiler.Documents.Text;

namespace Broiler.Documents.Tests;

/// <summary>
/// The Windows-1252 table RTF and CSV share. The framework's own decoder for the
/// code page needs an encoding provider registered process-wide, so these check
/// the table against what the code page is known to say rather than against it.
/// </summary>
public sealed class Windows1252Tests
{
    [Theory]
    [InlineData(0x80, 0x20AC)] // euro sign
    [InlineData(0x84, 0x201E)] // low double quote, as German opens a quotation
    [InlineData(0x93, 0x201C)]
    [InlineData(0x94, 0x201D)]
    [InlineData(0x96, 0x2013)] // en dash
    [InlineData(0x97, 0x2014)] // em dash
    [InlineData(0x8A, 0x0160)]
    [InlineData(0x9F, 0x0178)]
    public void The_Positions_Where_It_Differs_From_Latin1_Decode_To_Its_Characters(int value, int expected)
    {
        Assert.Equal((char)expected, Windows1252.Decode((byte)value));
    }

    [Theory]
    [InlineData(0x81)]
    [InlineData(0x8D)]
    [InlineData(0x8F)]
    [InlineData(0x90)]
    [InlineData(0x9D)]
    public void The_Five_Undefined_Positions_Pass_Through(int value)
    {
        Assert.Equal((char)value, Windows1252.Decode((byte)value));
    }

    [Fact]
    public void Every_Other_Byte_Is_Its_Latin1_Character()
    {
        for (int value = 0; value < 256; value++)
        {
            if (value is >= 0x80 and <= 0x9F)
                continue;

            Assert.Equal((char)value, Windows1252.Decode((byte)value));
        }
    }

    [Fact]
    public void A_Run_Decodes_Byte_By_Byte()
    {
        byte[] bytes = [.. "Gr"u8, 0xF6, 0xDF, .. "e "u8, 0x80];

        Assert.Equal("Größe €", Windows1252.Decode(bytes));
        Assert.Equal(string.Empty, Windows1252.Decode([]));
    }

    [Fact]
    public void The_Table_Holds_No_Character_Utf8_Could_Not_Carry()
    {
        // A table entry that was a lone surrogate would decode to text that
        // cannot be written back out.
        for (int value = 0x80; value <= 0x9F; value++)
            Assert.False(char.IsSurrogate(Windows1252.Decode((byte)value)));

        Assert.Equal("€", Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(Windows1252.Decode([0x80]))));
    }
}
