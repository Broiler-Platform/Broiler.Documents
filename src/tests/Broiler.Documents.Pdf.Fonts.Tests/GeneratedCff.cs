using System.Text;

namespace Broiler.Documents.Pdf.Fonts.Tests;

/// <summary>
/// Builds a minimal but real bare CFF font in memory.
/// </summary>
/// <remarks>
/// <para>
/// Generated for the same reason the sfnt fixture is: a committed font would need
/// a corpus-manifest entry with its provenance and redistribution rights, and
/// possessing a font is not permission to redistribute it (IP-020). Nothing here
/// comes from the CFF specification's own example font or from any other
/// implementation's test material — the bytes are assembled from the structures
/// SRC-016 permits confirming.
/// </para>
/// <para>
/// The font carries a header, the four indexes a reader must step through, a
/// charset, and one CharString per glyph. The CharStrings are a single
/// <c>endchar</c> each: nothing under test draws a glyph, and the reader being
/// exercised asks only what the glyphs are called.
/// </para>
/// </remarks>
internal static class GeneratedCff
{
    /// <summary>
    /// A font whose glyph <c>i + 1</c> is called <c>names[i]</c>. A name the
    /// format defines is referenced by its standard identifier; any other is
    /// carried in the font's own string index, which is how a real subsetter
    /// names a glyph the standard list has no entry for.
    /// </summary>
    /// <param name="rangeCharset">
    /// True to encode the charset as consecutive ranges (format 1) rather than as
    /// a flat list (format 0). Both forms occur, and a reader that understands
    /// only one silently loses the other.
    /// </param>
    /// <param name="cidKeyed">
    /// True to add a ROS entry, which makes the font CID-keyed and its charset a
    /// list of character identifiers rather than of names.
    /// </param>
    internal static byte[] WithNames(string[] names, bool rangeCharset = false, bool cidKeyed = false)
    {
        var customStrings = new List<byte[]>();
        var sids = new List<int>(names.Length);

        foreach (string name in names)
        {
            int standard = StandardSidOf(name);
            if (standard >= 0)
            {
                sids.Add(standard);
                continue;
            }

            sids.Add(CffStandardStrings.Count + customStrings.Count);
            customStrings.Add(Encoding.ASCII.GetBytes(name));
        }

        byte[] nameIndex = Index([Encoding.ASCII.GetBytes("Generated")]);
        byte[] stringIndex = Index([.. customStrings]);
        byte[] globalSubrs = Index([]);
        byte[] charset = rangeCharset ? RangeCharset(sids) : FlatCharset(sids);

        // One endchar per glyph, .notdef included. The count is what the reader
        // takes from this index; the contents only have to be well formed.
        byte[][] charStrings = new byte[names.Length + 1][];
        for (int i = 0; i < charStrings.Length; i++)
            charStrings[i] = [14];
        byte[] charStringsIndex = Index(charStrings);

        // Every offset operand is written in the five-byte form, so the top
        // dictionary is a fixed size and the layout can be computed in one pass
        // rather than converged on.
        int topDictLength = cidKeyed ? 12 + 17 : 12;
        byte[] topDictIndexHeader = IndexHeader(topDictLength);

        int charsetAt = 4 + nameIndex.Length + topDictIndexHeader.Length + topDictLength +
            stringIndex.Length + globalSubrs.Length;
        int charStringsAt = charsetAt + charset.Length;

        var topDict = new List<byte>();
        if (cidKeyed)
        {
            // ROS takes three operands: two string identifiers and a supplement.
            topDict.AddRange(Operand(0));
            topDict.AddRange(Operand(0));
            topDict.AddRange(Operand(0));
            topDict.Add(12);
            topDict.Add(30);
        }

        topDict.AddRange(Operand(charsetAt));
        topDict.Add(15);
        topDict.AddRange(Operand(charStringsAt));
        topDict.Add(17);

        var font = new List<byte> { 1, 0, 4, 1 };
        font.AddRange(nameIndex);
        font.AddRange(topDictIndexHeader);
        font.AddRange(topDict);
        font.AddRange(stringIndex);
        font.AddRange(globalSubrs);
        font.AddRange(charset);
        font.AddRange(charStringsIndex);

        return [.. font];
    }

    /// <summary>The standard identifier for a name, or -1 where the format defines none.</summary>
    private static int StandardSidOf(string name)
    {
        for (int sid = 0; sid < CffStandardStrings.Count; sid++)
        {
            if (string.Equals(CffStandardStrings.Of(sid), name, StringComparison.Ordinal))
                return sid;
        }

        return -1;
    }

    /// <summary>Format 0: one identifier per glyph after .notdef.</summary>
    private static byte[] FlatCharset(List<int> sids)
    {
        var charset = new List<byte> { 0 };
        foreach (int sid in sids)
        {
            charset.Add((byte)(sid >> 8));
            charset.Add((byte)(sid & 0xFF));
        }

        return [.. charset];
    }

    /// <summary>Format 1: a first identifier and a count of consecutive ones after it.</summary>
    private static byte[] RangeCharset(List<int> sids)
    {
        var charset = new List<byte> { 1 };

        int index = 0;
        while (index < sids.Count)
        {
            int first = sids[index];
            int left = 0;
            while (index + left + 1 < sids.Count && sids[index + left + 1] == first + left + 1 && left < 255)
                left++;

            charset.Add((byte)(first >> 8));
            charset.Add((byte)(first & 0xFF));
            charset.Add((byte)left);
            index += left + 1;
        }

        return [.. charset];
    }

    /// <summary>The five-byte integer operand, whose width does not vary with its value.</summary>
    private static byte[] Operand(int value) =>
        [29, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    /// <summary>A one-entry INDEX's bytes before its data, for a known data length.</summary>
    private static byte[] IndexHeader(int dataLength) =>
        [0, 1, 2, 0, 1, (byte)((dataLength + 1) >> 8), (byte)((dataLength + 1) & 0xFF)];

    private static byte[] Index(byte[][] entries)
    {
        if (entries.Length == 0)
            return [0, 0];

        var data = new List<byte>();
        var offsets = new List<int> { 1 };
        foreach (byte[] entry in entries)
        {
            data.AddRange(entry);
            offsets.Add(data.Count + 1);
        }

        var index = new List<byte>
        {
            (byte)(entries.Length >> 8),
            (byte)(entries.Length & 0xFF),
            2,
        };

        foreach (int offset in offsets)
        {
            index.Add((byte)(offset >> 8));
            index.Add((byte)(offset & 0xFF));
        }

        index.AddRange(data);
        return [.. index];
    }
}
