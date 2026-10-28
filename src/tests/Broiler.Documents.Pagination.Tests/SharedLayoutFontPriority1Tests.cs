using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Broiler.Documents.Model.Fonts;
using Broiler.Documents.Pagination.Fonts;
using Broiler.Graphics.Text;
using Xunit;

namespace Broiler.Documents.Pagination.Tests;

public sealed class SharedLayoutFontPriority1Tests
{
    [Fact]
    public void Two_Supplied_Programs_With_Same_Family_Name_Remain_Distinct()
    {
        byte[] programA = [0, 1, 0, 0, 1, 2, 3, 4];
        byte[] programB = [0, 1, 0, 0, 5, 6, 7, 8];

        var identityA = FontFaceIdentity.FromProgramBytes(programA, "CustomArial");
        var identityB = FontFaceIdentity.FromProgramBytes(programB, "CustomArial");

        Assert.Equal("CustomArial", identityA.Family);
        Assert.Equal("CustomArial", identityB.Family);
        Assert.NotEqual(identityA.ProgramId, identityB.ProgramId);
        Assert.NotEqual(identityA, identityB);
        Assert.NotEqual(identityA.GetHashCode(), identityB.GetHashCode());
    }

    [Fact]
    public void Font_Metric_Conversions_Points_To_Pixels_Roundtrip()
    {
        double[] testPoints = [0.0, 9.0, 10.0, 11.0, 12.0, 14.5, 18.0, 24.0, 36.0, 72.0];

        foreach (double pt in testPoints)
        {
            double px = FontMetricConversions.PointsToPixels(pt);
            double backToPt = FontMetricConversions.PixelsToPoints(px);

            // 1 pt = 4/3 px (approx 1.333333333 px)
            Assert.Equal(pt * (4.0 / 3.0), px, precision: 6);
            Assert.Equal(pt, backToPt, precision: 6);
        }
    }

    [Fact]
    public void Font_Metric_Conversions_Design_Units_And_Pdf_1000_Em_Units()
    {
        int unitsPerEm = 2048;
        int ascenderFUnits = 1854;
        double fontSizePt = 12.0;

        // FUnits -> Points: (1854 * 12) / 2048 = 10.86328125 pt
        double ascentPt = FontMetricConversions.DesignUnitsToPoints(ascenderFUnits, fontSizePt, unitsPerEm);
        Assert.Equal(10.86328125, ascentPt, precision: 6);

        // Points -> CSS Pixels: 10.86328125 * (4/3) = 14.484375 px
        double ascentPx = FontMetricConversions.PointsToPixels(ascentPt);
        Assert.Equal(14.484375, ascentPx, precision: 6);

        // FUnits -> PDF 1/1000 em units: (1854 * 1000) / 2048 = 905.2734375
        double pdfUnits = FontMetricConversions.DesignUnitsToPdfUnits(ascenderFUnits, unitsPerEm);
        Assert.Equal(905.2734375, pdfUnits, precision: 5);

        // PDF units -> Points: (905.2734375 * 12) / 1000 = 10.86328125 pt
        double backToPtFromPdf = FontMetricConversions.PdfUnitsToPoints(pdfUnits, fontSizePt);
        Assert.Equal(ascentPt, backToPtFromPdf, precision: 6);
    }

    [Fact]
    public void Intrinsic_Metrics_Are_Separated_From_Line_Height_Policy()
    {
        double ascentPt = 9.6;
        double descentPt = 2.4;
        double lineGapPt = 1.0;
        double fontSizePt = 12.0;

        double intrinsicHeight = FontMetricConversions.IntrinsicLineHeightPoints(ascentPt, descentPt, lineGapPt);
        Assert.Equal(13.0, intrinsicHeight);

        // CSS normal / 1.0: 1.0 * fontSize = 12.0
        double cssSingle = FontMetricConversions.CalculateLineHeightPoints(intrinsicHeight, fontSizePt, 1.0, useNominalFontSize: true);
        Assert.Equal(12.0, cssSingle);

        // CSS 1.5x multiplier: 1.5 * 12 = 18.0 pt
        double cssOneAndHalf = FontMetricConversions.CalculateLineHeightPoints(intrinsicHeight, fontSizePt, 1.5, useNominalFontSize: true);
        Assert.Equal(18.0, cssOneAndHalf);

        // Legacy word processor proportional 1.5x (scales intrinsic height): 1.5 * 13 = 19.5 pt
        double wpOneAndHalf = FontMetricConversions.CalculateLineHeightPoints(intrinsicHeight, fontSizePt, 1.5, useNominalFontSize: false);
        Assert.Equal(19.5, wpOneAndHalf);
    }

    [Fact]
    public void FontMetadataReader_Parses_Sfnt_Table_Directory_And_Names()
    {
        // Build minimal valid sfnt in memory with 'name', 'head', 'hhea', 'OS/2'
        byte[] font = CreateMinimalTestSfnt("TestSerif", "Regular", unitsPerEm: 1000, ascender: 800, descender: -200, lineGap: 100);

        FontMetadataInfo? meta = FontMetadataReader.ReadFromBytes(font);
        Assert.NotNull(meta);
        Assert.Equal("TestSerif", meta.Family);
        Assert.Equal("Regular", meta.Subfamily);
        Assert.Equal(1000, meta.UnitsPerEm);
        Assert.Equal(800, meta.TypographicAscender);
        Assert.Equal(-200, meta.TypographicDescender);
        Assert.Equal(100, meta.TypographicLineGap);
        Assert.False(meta.IsBold);
        Assert.False(meta.IsItalic);
    }

    [Fact]
    public void FontMetadataReader_Rejects_Empty_Or_Corrupt_Streams_Safely()
    {
        Assert.Null(FontMetadataReader.ReadFromBytes(ReadOnlyMemory<byte>.Empty));
        Assert.Null(FontMetadataReader.ReadFromBytes(new byte[] { 0, 1, 2, 3 }));
    }

    [Fact]
    public void Operation_Scoped_FontService_Resolves_Explicit_Mapping()
    {
        byte[] fontData = CreateMinimalTestSfnt("CustomHeading", "Bold", unitsPerEm: 1000, ascender: 850, descender: -250, lineGap: 50);

        using var service = new DocumentFontService();
        service.AddFontProgram("CustomHeading", fontData, bold: true, italic: false, subfamily: "Bold");

        var (face, outcome) = service.ResolveWithOutcome("CustomHeading", bold: true, italic: false);
        Assert.NotNull(face);
        Assert.Equal(FontResolutionOutcome.ExactMatch, outcome);
        Assert.Equal("CustomHeading", face.Family);
        Assert.True(face.IsBold);
        Assert.False(face.IsItalic);

        var (ascent, descent, lineGap) = face.GetVerticalMetricsPoints(12.0);
        Assert.Equal(10.2, ascent, precision: 4); // (850 * 12) / 1000
        Assert.Equal(3.0, descent, precision: 4); // (250 * 12) / 1000
        Assert.Equal(0.6, lineGap, precision: 4); // (50 * 12) / 1000
    }

    [Fact]
    public void Operation_Scoped_FontService_Tracks_Unmapped_Requests_And_Fallback()
    {
        using var service = new DocumentFontService();

        var (face, outcome) = service.ResolveWithOutcome("UnknownNoveltyFont", bold: false, italic: false);
        Assert.NotNull(face);
        Assert.Equal(FontResolutionOutcome.ApproximateModel, outcome);
        Assert.Contains("UnknownNoveltyFont", service.UnmappedRequests);
    }

    [Fact]
    public void PaginationFontAdapter_Measures_Advance_And_Vertical_Metrics()
    {
        byte[] fontData = CreateMinimalTestSfnt("BodyFont", "Regular", unitsPerEm: 1000, ascender: 800, descender: -200, lineGap: 0);

        using var service = new DocumentFontService();
        service.AddFontProgram("BodyFont", fontData, bold: false, italic: false);

        var adapter = new PaginationFontAdapter(service);
        var fontStyle = new BFontStyle("BodyFont", 10.0, BFontWeight.Normal, BFontSlant.Normal);

        var (ascent, descent, lineGap) = adapter.GetVerticalMetrics(fontStyle);
        Assert.Equal(8.0, ascent, precision: 4);
        Assert.Equal(2.0, descent, precision: 4);
        Assert.Equal(0.0, lineGap, precision: 4);
        Assert.Equal(10.0, adapter.GetLineHeight(fontStyle), precision: 4);

        double spaceAdvance = adapter.GetWhitespaceWidth(fontStyle);
        Assert.True(spaceAdvance > 0 && spaceAdvance < 5.0);
    }

    [Fact]
    public void LayoutFontAdapter_Implements_ILayoutFont_With_Correct_Units()
    {
        byte[] fontData = CreateMinimalTestSfnt("CssFont", "Regular", unitsPerEm: 1000, ascender: 800, descender: -200, lineGap: 200);

        using var service = new DocumentFontService();
        service.AddFontProgram("CssFont", fontData);

        var envAdapter = new LayoutEnvironmentFontAdapter(service);
        ILayoutFont layoutFont = envAdapter.GetFont("CssFont", sizePoints: 12.0, bold: false, italic: false);

        // Size in points: 12 pt
        Assert.Equal(12.0, layoutFont.Size);

        // Height in CSS pixels:
        // Intrinsic height points = (800 + 200 + 200) * 12 / 1000 = 14.4 pt
        // CSS pixels = 14.4 * 4/3 = 19.2 px
        Assert.Equal(19.2, layoutFont.Height, precision: 4);

        SizeF measured = envAdapter.MeasureText(layoutFont, "Test Hello");
        Assert.True(measured.Width > 0);
        Assert.Equal((float)layoutFont.Height, measured.Height);

        envAdapter.MeasureText(layoutFont, "A long word that needs measuring", maxWidthPx: 50.0, out int charFit, out double charFitWidth);
        Assert.True(charFit > 0 && charFit < 32);
        Assert.True(charFitWidth <= 50.0);
    }

    [Fact]
    public async Task Concurrent_Operations_Using_Different_Faces_Do_Not_Affect_Each_Other()
    {
        // Service 1 uses Face A
        byte[] fontA = CreateMinimalTestSfnt("SharedFamily", "Regular", unitsPerEm: 1000, ascender: 750, descender: -250, lineGap: 0);
        // Service 2 uses Face B with same family name but completely different metrics
        byte[] fontB = CreateMinimalTestSfnt("SharedFamily", "Regular", unitsPerEm: 1000, ascender: 900, descender: -100, lineGap: 0);

        Task taskA = Task.Run(() =>
        {
            using var serviceA = new DocumentFontService();
            serviceA.AddFontProgram("SharedFamily", fontA);
            var adapterA = new PaginationFontAdapter(serviceA);

            for (int i = 0; i < 50; i++)
            {
                var font = new BFontStyle("SharedFamily", 10.0, BFontWeight.Normal, BFontSlant.Normal);
                var (ascent, descent, _) = adapterA.GetVerticalMetrics(font);
                Assert.Equal(7.5, ascent, precision: 4);
                Assert.Equal(2.5, descent, precision: 4);
            }
        }, TestContext.Current.CancellationToken);

        Task taskB = Task.Run(() =>
        {
            using var serviceB = new DocumentFontService();
            serviceB.AddFontProgram("SharedFamily", fontB);
            var adapterB = new PaginationFontAdapter(serviceB);

            for (int i = 0; i < 50; i++)
            {
                var font = new BFontStyle("SharedFamily", 10.0, BFontWeight.Normal, BFontSlant.Normal);
                var (ascent, descent, _) = adapterB.GetVerticalMetrics(font);
                Assert.Equal(9.0, ascent, precision: 4);
                Assert.Equal(1.0, descent, precision: 4);
            }
        }, TestContext.Current.CancellationToken);

        await Task.WhenAll(taskA, taskB);
    }

    private static byte[] CreateMinimalTestSfnt(
        string family,
        string subfamily,
        ushort unitsPerEm,
        short ascender,
        short descender,
        short lineGap)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        // Header: 4 tables ('name', 'head', 'hhea', 'OS/2')
        WriteUInt32Big(writer, 0x00010000);
        WriteUInt16Big(writer, 4); // numTables
        WriteUInt16Big(writer, 64);
        WriteUInt16Big(writer, 2);
        WriteUInt16Big(writer, 0);

        long dirOffset = stream.Position;
        // Reserve 4 table records: 4 * 16 bytes = 64 bytes
        writer.Write(new byte[64]);

        // 1. 'head' table (54 bytes)
        long headOffset = stream.Position;
        byte[] headData = new byte[54];
        WriteUInt16(headData, 18, unitsPerEm);
        writer.Write(headData);

        // 2. 'hhea' table (36 bytes)
        long hheaOffset = stream.Position;
        byte[] hheaData = new byte[36];
        WriteInt16(hheaData, 4, ascender);
        WriteInt16(hheaData, 6, descender);
        WriteInt16(hheaData, 8, lineGap);
        writer.Write(hheaData);

        // 3. 'OS/2' table (78 bytes)
        long os2Offset = stream.Position;
        byte[] os2Data = new byte[78];
        WriteUInt16(os2Data, 0, 1); // version 1
        WriteUInt16(os2Data, 4, 400); // weight
        WriteInt16(os2Data, 68, ascender);
        WriteInt16(os2Data, 70, descender);
        WriteInt16(os2Data, 72, lineGap);
        writer.Write(os2Data);

        // 4. 'name' table
        long nameOffset = stream.Position;
        byte[] nameData = BuildNameTable(family, subfamily);
        writer.Write(nameData);

        long endOffset = stream.Position;

        // Fill directory
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

        byte[] familyBytes = System.Text.Encoding.BigEndianUnicode.GetBytes(family);
        byte[] subfamilyBytes = System.Text.Encoding.BigEndianUnicode.GetBytes(subfamily);

        WriteUInt16Big(writer, 0); // format 0
        WriteUInt16Big(writer, 2); // 2 records (nameId 1 and 2)
        WriteUInt16Big(writer, 6 + 2 * 12); // string offset

        // Record 1: family (platform 3, encoding 1, lang 0x0409, nameId 1)
        WriteUInt16Big(writer, 3);
        WriteUInt16Big(writer, 1);
        WriteUInt16Big(writer, 0x0409);
        WriteUInt16Big(writer, 1);
        WriteUInt16Big(writer, (ushort)familyBytes.Length);
        WriteUInt16Big(writer, 0);

        // Record 2: subfamily (nameId 2)
        WriteUInt16Big(writer, 3);
        WriteUInt16Big(writer, 1);
        WriteUInt16Big(writer, 0x0409);
        WriteUInt16Big(writer, 2);
        WriteUInt16Big(writer, (ushort)subfamilyBytes.Length);
        WriteUInt16Big(writer, (ushort)familyBytes.Length);

        // Storage
        writer.Write(familyBytes);
        writer.Write(subfamilyBytes);

        return stream.ToArray();
    }

    private static void WriteTableRecord(BinaryWriter writer, uint tag, uint offset, uint length)
    {
        WriteUInt32Big(writer, tag);
        WriteUInt32Big(writer, 0); // checksum
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
