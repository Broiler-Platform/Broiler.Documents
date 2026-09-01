using System.Buffers.Binary;

namespace Broiler.Documents.Pdf.Fonts.Tests;

/// <summary>
/// Builds a minimal but real sfnt font in memory.
/// </summary>
/// <remarks>
/// <para>
/// Every fixture in this suite is generated because the corpus rule leaves no
/// alternative: a committed font would need an entry in the corpus manifest with
/// its provenance and redistribution rights, and IP-020 is explicit that
/// possessing a font is not permission to redistribute it. Generating one also
/// means each test states the exact character map it is about.
/// </para>
/// <para>
/// The font carries a table directory, <c>head</c>, <c>maxp</c>, <c>hhea</c>,
/// <c>hmtx</c>, and a format 6 <c>cmap</c>. It has no outlines, which is right:
/// nothing under test draws a glyph, and the reader being exercised asks only
/// what the glyphs mean.
/// </para>
/// </remarks>
internal static class GeneratedFont
{
    internal const int UnitsPerEm = 1000;

    /// <summary>
    /// A font whose glyph <c>i + 1</c> is the character at <c>text[i]</c>, so
    /// glyph indices 1..n spell <paramref name="text"/> in order.
    /// </summary>
    internal static byte[] SpellingOut(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        var tables = new List<(string Tag, byte[] Data)>
        {
            ("cmap", Cmap(text)),
            ("head", Head()),
            ("hhea", Hhea(text.Length + 1)),
            ("hmtx", Hmtx(text.Length + 1)),
            ("maxp", Maxp(text.Length + 1)),
        };

        // The directory is sorted by tag, as the format requires. The parser under
        // test reads it as a dictionary and would not care; the format does.
        tables.Sort((left, right) => string.CompareOrdinal(left.Tag, right.Tag));

        int directoryLength = 12 + (tables.Count * 16);
        int offset = directoryLength;
        var offsets = new int[tables.Count];
        for (int i = 0; i < tables.Count; i++)
        {
            offsets[i] = offset;
            offset += Align(tables[i].Data.Length);
        }

        var font = new byte[offset];
        WriteUInt32(font, 0, 0x00010000u);
        WriteUInt16(font, 4, tables.Count);
        WriteUInt16(font, 6, 0);
        WriteUInt16(font, 8, 0);
        WriteUInt16(font, 10, 0);

        for (int i = 0; i < tables.Count; i++)
        {
            int record = 12 + (i * 16);
            for (int c = 0; c < 4; c++)
                font[record + c] = (byte)tables[i].Tag[c];

            WriteUInt32(font, record + 4, 0);
            WriteUInt32(font, record + 8, (uint)offsets[i]);
            WriteUInt32(font, record + 12, (uint)tables[i].Data.Length);
            tables[i].Data.CopyTo(font, offsets[i]);
        }

        return font;
    }

    /// <summary>A format 6 character map: a contiguous run of codes, one glyph each.</summary>
    private static byte[] Cmap(string text)
    {
        // A format 6 subtable maps a contiguous code range, so the fixture text
        // must be contiguous too. Every caller uses a run such as "ABC".
        char first = text[0];
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != first + i)
                throw new ArgumentException("The generated font maps a contiguous code range.", nameof(text));
        }

        int subtableLength = 10 + (text.Length * 2);
        var table = new byte[12 + subtableLength];

        WriteUInt16(table, 0, 0);                       // version
        WriteUInt16(table, 2, 1);                       // one encoding record
        WriteUInt16(table, 4, 3);                       // platform: Windows
        WriteUInt16(table, 6, 1);                       // encoding: BMP Unicode
        WriteUInt32(table, 8, 12);                      // subtable offset

        WriteUInt16(table, 12, 6);                      // format
        WriteUInt16(table, 14, subtableLength);
        WriteUInt16(table, 16, 0);                      // language
        WriteUInt16(table, 18, first);                  // first code
        WriteUInt16(table, 20, text.Length);            // entry count
        for (int i = 0; i < text.Length; i++)
            WriteUInt16(table, 22 + (i * 2), i + 1);    // glyph 0 stays .notdef

        return table;
    }

    private static byte[] Head()
    {
        var table = new byte[54];
        WriteUInt32(table, 0, 0x00010000u);
        WriteUInt16(table, 18, UnitsPerEm);
        WriteUInt16(table, 50, 0);                      // short loca format
        return table;
    }

    private static byte[] Hhea(int glyphCount)
    {
        var table = new byte[36];
        WriteUInt32(table, 0, 0x00010000u);
        WriteUInt16(table, 4, (int)(UnitsPerEm * 0.8));  // ascender
        WriteUInt16(table, 6, 0);                        // descender, left at zero
        WriteUInt16(table, 34, glyphCount);              // numberOfHMetrics
        return table;
    }

    private static byte[] Hmtx(int glyphCount)
    {
        var table = new byte[glyphCount * 4];
        for (int glyph = 0; glyph < glyphCount; glyph++)
            WriteUInt16(table, glyph * 4, UnitsPerEm / 2);
        return table;
    }

    private static byte[] Maxp(int glyphCount)
    {
        var table = new byte[32];
        WriteUInt32(table, 0, 0x00010000u);
        WriteUInt16(table, 4, glyphCount);
        return table;
    }

    private static int Align(int length) => (length + 3) & ~3;

    private static void WriteUInt16(byte[] target, int offset, int value) =>
        BinaryPrimitives.WriteUInt16BigEndian(target.AsSpan(offset, 2), (ushort)value);

    private static void WriteUInt32(byte[] target, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(target.AsSpan(offset, 4), value);
}
