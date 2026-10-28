using System;
using System.IO;
using Broiler.Documents.Model.Fonts;
using Broiler.Documents.Pdf.Text;
using Xunit;

namespace Broiler.Documents.Pdf.Tests;

public sealed class PdfFontServiceMetricsAdapterTests
{
    [Fact]
    public void Unmapped_Service_Reports_IsApproximate_True_And_Uses_Approximate_Metrics()
    {
        using var service = new DocumentFontService();
        var adapter = new PdfFontServiceMetricsAdapter(service);

        Assert.True(adapter.IsApproximate);

        // Helvetica advance for 'A' from approximate model is 690
        double width = adapter.GetAdvanceWidth(PdfStandardFont.Helvetica, 'A');
        Assert.Equal(690, width);

        // Ascent and descent from approximate model
        Assert.Equal(718, adapter.GetAscent(PdfStandardFont.Helvetica));
        Assert.Equal(207, adapter.GetDescent(PdfStandardFont.Helvetica));
    }

    [Fact]
    public void Courier_Is_Fixed_Pitch_600_Units()
    {
        using var service = new DocumentFontService();
        var adapter = new PdfFontServiceMetricsAdapter(service);

        Assert.Equal(600, adapter.GetAdvanceWidth(PdfStandardFont.Courier, 'i'));
        Assert.Equal(600, adapter.GetAdvanceWidth(PdfStandardFont.Courier, 'M'));
        Assert.Equal(600, adapter.GetAdvanceWidth(PdfStandardFont.CourierBold, 'W'));
        Assert.Equal(600, adapter.GetAdvanceWidth(PdfStandardFont.CourierOblique, '.'));
    }

    [Fact]
    public void Mapped_Font_Reports_IsApproximate_False_And_Provides_Exact_Metrics()
    {
        // 1000 UnitsPerEm, 850 Ascender, -150 Descender, 50 LineGap
        byte[] font = CreateMinimalSfnt("Helvetica", "Regular", unitsPerEm: 1000, ascender: 850, descender: -150);

        using var service = new DocumentFontService();
        service.AddFontProgram("Helvetica", font);

        var adapter = new PdfFontServiceMetricsAdapter(service);

        Assert.False(adapter.IsApproximate);
        Assert.Equal(850, adapter.GetAscent(PdfStandardFont.Helvetica));
        Assert.Equal(150, adapter.GetDescent(PdfStandardFont.Helvetica));
    }

    private static byte[] CreateMinimalSfnt(string family, string subfamily, ushort unitsPerEm, short ascender, short descender)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        WriteUInt32Big(writer, 0x00010000);
        WriteUInt16Big(writer, 4); // 4 tables: head, hhea, OS/2, name
        WriteUInt16Big(writer, 64);
        WriteUInt16Big(writer, 2);
        WriteUInt16Big(writer, 0);

        long dirOffset = stream.Position;
        writer.Write(new byte[64]);

        long headOffset = stream.Position;
        byte[] headData = new byte[54];
        WriteUInt16(headData, 18, unitsPerEm);
        writer.Write(headData);

        long hheaOffset = stream.Position;
        byte[] hheaData = new byte[36];
        WriteInt16(hheaData, 4, ascender);
        WriteInt16(hheaData, 6, descender);
        writer.Write(hheaData);

        long os2Offset = stream.Position;
        byte[] os2Data = new byte[78];
        WriteUInt16(os2Data, 0, 1);
        WriteUInt16(os2Data, 4, 400);
        WriteInt16(os2Data, 68, ascender);
        WriteInt16(os2Data, 70, descender);
        writer.Write(os2Data);

        long nameOffset = stream.Position;
        byte[] nameData = BuildNameTable(family, subfamily);
        writer.Write(nameData);

        stream.Position = dirOffset;
        WriteTableRecord(writer, 0x68656164 /* head */, (uint)headOffset, 54);
        WriteTableRecord(writer, 0x68686561 /* hhea */, (uint)hheaOffset, 36);
        WriteTableRecord(writer, 0x4F532F32 /* OS/2 */, (uint)os2Offset, 78);
        WriteTableRecord(writer, 0x6E616D65 /* name */, (uint)nameOffset, (uint)nameData.Length);

        return stream.ToArray();
    }

    private static byte[] BuildNameTable(string family, string subfamily)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        byte[] famBytes = System.Text.Encoding.BigEndianUnicode.GetBytes(family);
        byte[] subBytes = System.Text.Encoding.BigEndianUnicode.GetBytes(subfamily);

        WriteUInt16Big(writer, 0);
        WriteUInt16Big(writer, 2);
        WriteUInt16Big(writer, 30); // 6 + 2*12

        WriteUInt16Big(writer, 3);
        WriteUInt16Big(writer, 1);
        WriteUInt16Big(writer, 0x0409);
        WriteUInt16Big(writer, 1);
        WriteUInt16Big(writer, (ushort)famBytes.Length);
        WriteUInt16Big(writer, 0);

        WriteUInt16Big(writer, 3);
        WriteUInt16Big(writer, 1);
        WriteUInt16Big(writer, 0x0409);
        WriteUInt16Big(writer, 2);
        WriteUInt16Big(writer, (ushort)subBytes.Length);
        WriteUInt16Big(writer, (ushort)famBytes.Length);

        writer.Write(famBytes);
        writer.Write(subBytes);

        return stream.ToArray();
    }

    private static void WriteTableRecord(BinaryWriter writer, uint tag, uint offset, uint length)
    {
        WriteUInt32Big(writer, tag);
        WriteUInt32Big(writer, 0);
        WriteUInt32Big(writer, offset);
        WriteUInt32Big(writer, length);
    }

    private static void WriteUInt32Big(BinaryWriter writer, uint val)
    {
        writer.Write((byte)(val >> 24));
        writer.Write((byte)(val >> 16));
        writer.Write((byte)(val >> 8));
        writer.Write((byte)val);
    }

    private static void WriteUInt16Big(BinaryWriter writer, ushort val)
    {
        writer.Write((byte)(val >> 8));
        writer.Write((byte)val);
    }

    private static void WriteUInt16(byte[] target, int offset, ushort val)
    {
        target[offset] = (byte)(val >> 8);
        target[offset + 1] = (byte)val;
    }

    private static void WriteInt16(byte[] target, int offset, short val)
    {
        target[offset] = (byte)(val >> 8);
        target[offset + 1] = (byte)val;
    }
}
