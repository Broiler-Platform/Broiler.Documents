using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Broiler.Documents.Pdf.Fonts;

/// <summary>
/// What a bare CFF program says its glyphs are called.
/// </summary>
/// <remarks>
/// <para>
/// The one structure a renderer never needs. <c>Broiler.Graphics</c> parses CFF
/// to draw it, which needs the CharStrings and the subroutines and nothing else;
/// a glyph's <em>name</em> matters only to something trying to work out what the
/// glyph says. That is why this is written here rather than reached for there.
/// </para>
/// <para>
/// Two structures are read: the charset, which maps a glyph index to a string
/// identifier, and the string index, which holds the identifiers the font
/// defines for itself. An identifier below
/// <see cref="CffStandardStrings.Count"/> names one of the format's own strings
/// instead, and those come from the transcribed table beside this file.
/// </para>
/// <para>
/// A CID-keyed font is refused rather than misread. Its charset maps glyph
/// indices to character identifiers in some registry's collection, not to names,
/// and treating a CID as a string identifier would resolve numbers into whatever
/// names happened to sit at those positions — confident nonsense of exactly the
/// kind this codec refuses elsewhere.
/// </para>
/// </remarks>
internal static class CffGlyphNames
{
    /// <summary>How many glyphs one program may contribute, matching the sfnt path.</summary>
    private const int MaxGlyphs = 65_536;

    /// <summary>How deep an INDEX may claim to be before the claim is refused.</summary>
    private const int MaxIndexEntries = 65_535;

    /// <summary>How often the charset walk checks for cancellation.</summary>
    private const int CancellationCheckMask = 0xFFF;

    /// <summary>
    /// The glyph names a program declares, keyed by glyph index, or null where it
    /// is not a CFF this build reads: malformed, CID-keyed, or naming its glyphs
    /// through a predefined charset this build does not carry.
    /// </summary>
    public static IReadOnlyDictionary<int, string>? Read(ReadOnlySpan<byte> program, CancellationToken cancellation)
    {
        // header: major, minor, hdrSize, offSize. Only hdrSize is needed; the
        // rest is version information a reader has no decision to make about.
        if (program.Length < 4)
            return null;

        int position = program[2];
        if (position < 4 || position >= program.Length)
            return null;

        // Name INDEX, then Top DICT INDEX, then String INDEX. They are positional:
        // each has to be stepped over to reach the next.
        if (!SkipIndex(program, ref position) ||
            !ReadIndex(program, ref position, out List<Range> topDicts) ||
            !ReadIndex(program, ref position, out List<Range> strings))
        {
            return null;
        }

        if (topDicts.Count == 0)
            return null;

        Dictionary<int, List<double>> top = ReadDict(program, topDicts[0]);

        // ROS (12 30) is what makes a font CID-keyed, and a CID-keyed charset
        // holds identifiers from a character collection rather than names.
        if (top.ContainsKey(OperatorRos))
            return null;

        if (!TryOffset(top, OperatorCharStrings, program.Length, out int charStringsAt))
            return null;

        int at = charStringsAt;
        if (!ReadIndexCount(program, ref at, out int glyphs) || glyphs is <= 0 or > MaxGlyphs)
            return null;

        // A font that states no charset uses the ISOAdobe ordering, where a glyph
        // index is its own identifier. That is a table this build does not carry,
        // and inventing one would name glyphs it never saw.
        if (!TryOffset(top, OperatorCharset, program.Length, out int charsetAt) || charsetAt <= 2)
            return null;

        return ReadCharset(program, charsetAt, glyphs, strings, cancellation);
    }

    // ---- charset ---------------------------------------------------------------

    private static IReadOnlyDictionary<int, string>? ReadCharset(
        ReadOnlySpan<byte> program,
        int position,
        int glyphs,
        List<Range> strings,
        CancellationToken cancellation)
    {
        if (position >= program.Length)
            return null;

        int format = program[position++];
        var names = new Dictionary<int, string>(glyphs);

        // Glyph zero is .notdef by definition and is never listed.
        int glyph = 1;

        switch (format)
        {
            case 0:
                while (glyph < glyphs)
                {
                    if (position + 1 >= program.Length)
                        return Done(names);

                    Add(names, glyph++, (program[position] << 8) | program[position + 1], strings, program);
                    position += 2;

                    if ((glyph & CancellationCheckMask) == 0)
                        cancellation.ThrowIfCancellationRequested();
                }

                break;

            // Both range formats state a first identifier and a count of further
            // ones that follow it consecutively; they differ only in the width of
            // that count.
            case 1:
            case 2:
            {
                int countWidth = format == 1 ? 1 : 2;
                while (glyph < glyphs)
                {
                    if (position + 1 + countWidth >= program.Length)
                        return Done(names);

                    int first = (program[position] << 8) | program[position + 1];
                    position += 2;

                    int left = countWidth == 1
                        ? program[position]
                        : (program[position] << 8) | program[position + 1];
                    position += countWidth;

                    for (int i = 0; i <= left && glyph < glyphs; i++)
                    {
                        Add(names, glyph++, first + i, strings, program);

                        if ((glyph & CancellationCheckMask) == 0)
                            cancellation.ThrowIfCancellationRequested();
                    }
                }

                break;
            }

            default:
                return null;
        }

        return Done(names);
    }

    private static IReadOnlyDictionary<int, string>? Done(Dictionary<int, string> names) =>
        names.Count > 0 ? names : null;

    private static void Add(
        Dictionary<int, string> names,
        int glyph,
        int sid,
        List<Range> strings,
        ReadOnlySpan<byte> program)
    {
        string? name = sid < CffStandardStrings.Count
            ? CffStandardStrings.Of(sid)
            : CustomString(program, strings, sid - CffStandardStrings.Count);

        if (!string.IsNullOrEmpty(name))
            names[glyph] = name;
    }

    /// <summary>
    /// One of the font's own strings. These are the document's data rather than
    /// the format's, and they are read as ASCII: a PostScript name is by
    /// definition printable ASCII, and a byte outside that range means this is
    /// not one.
    /// </summary>
    private static string? CustomString(ReadOnlySpan<byte> program, List<Range> strings, int index)
    {
        if (index < 0 || index >= strings.Count)
            return null;

        Range range = strings[index];
        if (range.Length is <= 0 or > 255)
            return null;

        var text = new StringBuilder(range.Length);
        for (int i = 0; i < range.Length; i++)
        {
            byte b = program[range.Start + i];
            if (b is < 0x21 or > 0x7E)
                return null;
            text.Append((char)b);
        }

        return text.ToString();
    }

    // ---- INDEX and DICT ---------------------------------------------------------

    private const int OperatorCharset = 15;
    private const int OperatorCharStrings = 17;

    /// <summary>The two-byte operator 12 30, held as one key above the one-byte range.</summary>
    private const int OperatorRos = 1200 + 30;

    private readonly record struct Range(int Start, int Length);

    private static bool SkipIndex(ReadOnlySpan<byte> program, ref int position) =>
        ReadIndex(program, ref position, out _);

    /// <summary>Reads an INDEX's count and leaves the position at its end.</summary>
    private static bool ReadIndexCount(ReadOnlySpan<byte> program, ref int position, out int count)
    {
        bool ok = ReadIndex(program, ref position, out List<Range> entries);
        count = entries.Count;
        return ok;
    }

    /// <summary>
    /// An INDEX: a count, an offset size, count+1 offsets, then the data those
    /// offsets carve up. Every offset is checked against the data actually
    /// present rather than trusted.
    /// </summary>
    private static bool ReadIndex(ReadOnlySpan<byte> program, ref int position, out List<Range> entries)
    {
        entries = [];

        if (position + 1 >= program.Length)
            return false;

        int count = (program[position] << 8) | program[position + 1];
        position += 2;

        // An empty INDEX is two bytes and carries no offset size at all.
        if (count == 0)
            return true;

        if (count > MaxIndexEntries || position >= program.Length)
            return false;

        int offsetSize = program[position++];
        if (offsetSize is < 1 or > 4)
            return false;

        long offsetsBytes = (long)(count + 1) * offsetSize;
        if (position + offsetsBytes > program.Length)
            return false;

        int offsetsAt = position;
        position += (int)offsetsBytes;

        // Offsets are one-based from the byte before the data.
        int dataAt = position - 1;
        int previous = Offset(program, offsetsAt, offsetSize, 0);
        if (previous < 1)
            return false;

        for (int i = 1; i <= count; i++)
        {
            int next = Offset(program, offsetsAt, offsetSize, i);
            if (next < previous || dataAt + next > program.Length)
                return false;

            entries.Add(new Range(dataAt + previous, next - previous));
            previous = next;
        }

        position = dataAt + previous;
        return position <= program.Length;
    }

    private static int Offset(ReadOnlySpan<byte> program, int at, int size, int index)
    {
        int value = 0;
        int start = at + (index * size);
        for (int i = 0; i < size; i++)
            value = (value << 8) | program[start + i];
        return value;
    }

    /// <summary>
    /// A DICT: operands accumulate until an operator consumes them. Only integer
    /// operands are kept, because every entry this reads is an offset.
    /// </summary>
    private static Dictionary<int, List<double>> ReadDict(ReadOnlySpan<byte> program, Range range)
    {
        var entries = new Dictionary<int, List<double>>();
        var operands = new List<double>();
        int position = range.Start;
        int end = range.Start + range.Length;

        while (position < end)
        {
            int b0 = program[position];

            if (b0 <= 21)
            {
                position++;
                int op = b0;
                if (b0 == 12 && position < end)
                    op = 1200 + program[position++];

                entries[op] = [.. operands];
                operands.Clear();
                continue;
            }

            if (!TryOperand(program, ref position, end, out double value))
                break;

            // A DICT with an absurd operand count is malformed; stopping keeps
            // what was read rather than growing a list from a hostile file.
            if (operands.Count >= 48)
                break;

            operands.Add(value);
        }

        return entries;
    }

    private static bool TryOperand(ReadOnlySpan<byte> program, ref int position, int end, out double value)
    {
        value = 0;
        int b0 = program[position];

        if (b0 is >= 32 and <= 246)
        {
            value = b0 - 139;
            position++;
            return true;
        }

        if (b0 is >= 247 and <= 250 && position + 1 < end)
        {
            value = ((b0 - 247) * 256) + program[position + 1] + 108;
            position += 2;
            return true;
        }

        if (b0 is >= 251 and <= 254 && position + 1 < end)
        {
            value = (-(b0 - 251) * 256) - program[position + 1] - 108;
            position += 2;
            return true;
        }

        if (b0 == 28 && position + 2 < end)
        {
            value = (short)((program[position + 1] << 8) | program[position + 2]);
            position += 3;
            return true;
        }

        if (b0 == 29 && position + 4 < end)
        {
            value = (program[position + 1] << 24) | (program[position + 2] << 16) |
                    (program[position + 3] << 8) | program[position + 4];
            position += 5;
            return true;
        }

        // A real operand is nibble-encoded and ends with a sentinel. Nothing this
        // reads is a real, so it is stepped over rather than parsed.
        if (b0 == 30)
        {
            position++;
            while (position < end)
            {
                byte packed = program[position++];
                if ((packed & 0x0F) == 0x0F || (packed >> 4) == 0x0F)
                    break;
            }

            return true;
        }

        return false;
    }

    private static bool TryOffset(
        Dictionary<int, List<double>> dictionary,
        int op,
        int length,
        out int offset)
    {
        offset = 0;
        if (!dictionary.TryGetValue(op, out List<double>? operands) || operands.Count == 0)
            return false;

        double value = operands[^1];
        if (!double.IsFinite(value) || value < 0 || value >= length)
            return false;

        offset = (int)value;
        return true;
    }
}
