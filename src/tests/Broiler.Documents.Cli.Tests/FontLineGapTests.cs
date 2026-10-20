using Broiler.Documents.Cli.Rendering;

namespace Broiler.Documents.Cli.Tests;

public sealed class FontLineGapTests
{
    [Fact]
    public void Reads_Hhea_Line_Gap_From_An_Sfnt_Table()
    {
        string path = Path.GetTempFileName();
        try
        {
            byte[] font = new byte[54];
            WriteUInt32(font, 0, 0x00010000);
            WriteUInt16(font, 4, 1);
            WriteUInt32(font, 12, 0x68686561); // hhea
            WriteUInt32(font, 20, 28);
            WriteUInt32(font, 24, 26);
            WriteUInt16(font, 36, 87);
            File.WriteAllBytes(path, font);

            Assert.Equal(87, FontResolution.ReadHheaLineGap(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Invalid_Hhea_Table_Is_Ignored()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [0, 1, 2, 3]);
            Assert.Null(FontResolution.ReadHheaLineGap(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void WriteUInt16(byte[] target, int offset, ushort value)
    {
        target[offset] = (byte)(value >> 8);
        target[offset + 1] = (byte)value;
    }

    private static void WriteUInt32(byte[] target, int offset, uint value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }
}
