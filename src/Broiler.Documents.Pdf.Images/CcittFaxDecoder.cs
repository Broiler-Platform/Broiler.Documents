using System;
using System.Collections.Generic;

namespace Broiler.Documents.Pdf.Images;

/// <summary>How a fax stream is coded, as the PDF <c>K</c> parameter selects it.</summary>
internal enum CcittCoding
{
    /// <summary><c>K = 0</c>: every line one-dimensional (Modified Huffman).</summary>
    OneDimensional,

    /// <summary><c>K &gt; 0</c>: each line tagged as one- or two-dimensional (Modified READ).</summary>
    Mixed,

    /// <summary><c>K &lt; 0</c>: every line two-dimensional (Modified Modified READ, T.6).</summary>
    TwoDimensional,
}

/// <summary>What the PDF stream dictionary says about the fax data.</summary>
internal readonly record struct CcittFaxOptions(
    CcittCoding Coding,
    int Columns,
    int Rows,
    bool BlackIs1,
    bool EncodedByteAlign,
    bool ExpectsEndOfLine);

/// <summary>How a decode ended.</summary>
internal enum CcittFaxOutcome
{
    Decoded,
    Malformed,
    TooLarge,
}

/// <summary>The result of decoding one fax stream.</summary>
internal readonly record struct CcittFaxResult(
    CcittFaxOutcome Outcome,
    byte[]? Rows,
    int RowCount,
    string? Failure);

/// <summary>
/// Decodes ITU-T T.4 and T.6 fax data into packed one-bit-per-pixel rows.
/// </summary>
/// <remarks>
/// <para>
/// The three algorithms are one decoder because they are one algorithm with two
/// switches. A line is coded either as runs of alternating colour (Modified
/// Huffman) or as differences from the line above it (Modified READ); T.6 is
/// Modified READ with the one-dimensional option removed and the end-of-line
/// codes dropped. What the PDF <c>K</c> parameter selects is which of those a
/// line may be.
/// </para>
/// <para>
/// Lines are held as <em>changing elements</em> — the positions where colour
/// flips — rather than as pixels, because that is the form the two-dimensional
/// modes are defined against. Pixels are painted only once a line is complete.
/// </para>
/// <para>
/// Every loop is bounded by the declared column count and by the row ceiling the
/// caller passes, and a code that does not resolve ends the decode rather than
/// resynchronising by guesswork. Fax data is untrusted input reached through a
/// bit reader, which is the shape of parser that runs away if it is allowed to.
/// </para>
/// </remarks>
internal static class CcittFaxDecoder
{
    /// <summary>Rows decoded before an unbounded stream is refused.</summary>
    private const int RowCeiling = 1 << 16;

    internal static CcittFaxResult Decode(ReadOnlySpan<byte> data, in CcittFaxOptions options, long maxBytes)
    {
        if (options.Columns is <= 0 or > (1 << 16))
            return Failed("The stream declares a column count outside the supported range.");

        int stride = (options.Columns + 7) / 8;
        int rowLimit = options.Rows > 0 ? options.Rows : RowCeiling;
        if (options.Rows > 0 && (long)options.Rows * stride > maxBytes)
            return new CcittFaxResult(CcittFaxOutcome.TooLarge, null, 0, "declared rows");

        var reader = new BitReader(data);
        var output = new List<byte>(options.Rows > 0 ? options.Rows * stride : stride * 64);

        // Changing elements of the line above. An all-white imaginary line
        // precedes the first, which is what makes the first two-dimensional line
        // decodable at all.
        var reference = new List<int> { options.Columns, options.Columns };
        var coding = new List<int>();
        int rows = 0;

        while (rows < rowLimit)
        {
            if (options.EncodedByteAlign && options.Coding != CcittCoding.Mixed)
                reader.AlignToByte();

            if (!reader.HasMore)
                break;

            SkipFill(reader, options);
            if (!reader.HasMore)
                break;

            bool twoDimensional;
            switch (options.Coding)
            {
                case CcittCoding.OneDimensional:
                    twoDimensional = false;
                    break;
                case CcittCoding.TwoDimensional:
                    twoDimensional = true;
                    break;
                default:
                    // Modified READ tags every line with one bit after its
                    // end-of-line code: set for one-dimensional, clear for two.
                    if (!reader.TryReadBit(out int tag))
                        goto done;
                    twoDimensional = tag == 0;
                    break;
            }

            coding.Clear();
            bool ok = twoDimensional
                ? DecodeTwoDimensionalLine(reader, reference, coding, options.Columns)
                : DecodeOneDimensionalLine(reader, coding, options.Columns);

            if (!ok)
            {
                // A line that does not resolve ends the image. What decoded is
                // kept: a truncated fax is still a picture of something, and the
                // caller is told how many rows it got.
                break;
            }

            if ((long)output.Count + stride > maxBytes)
                return new CcittFaxResult(CcittFaxOutcome.TooLarge, null, 0, "row ceiling");

            Paint(output, coding, options.Columns, stride, options.BlackIs1);
            rows++;

            reference.Clear();
            reference.AddRange(coding);
            reference.Add(options.Columns);
            reference.Add(options.Columns);

            if (options.EncodedByteAlign && options.Coding == CcittCoding.Mixed)
                reader.AlignToByte();
        }

    done:
        return rows == 0
            ? Failed("No fax line could be decoded from the stream.")
            : new CcittFaxResult(CcittFaxOutcome.Decoded, output.ToArray(), rows, null);
    }

    /// <summary>
    /// Consumes any end-of-line codes and the fill bits before them. They are
    /// optional in PDF data and mandatory in some producers' output, so they are
    /// skipped wherever they appear rather than required or refused.
    /// </summary>
    private static void SkipFill(BitReader reader, in CcittFaxOptions options)
    {
        while (reader.TryPeekEndOfLine())
        {
            reader.SkipEndOfLine();
            if (!options.ExpectsEndOfLine && options.Coding != CcittCoding.Mixed)
                continue;

            // In mixed coding the tag bit belongs to the line, so stop here and
            // let the caller read it.
            if (options.Coding == CcittCoding.Mixed)
                return;
        }
    }

    /// <summary>Decodes a line as alternating white and black runs.</summary>
    private static bool DecodeOneDimensionalLine(BitReader reader, List<int> coding, int columns)
    {
        int position = 0;
        bool white = true;

        while (position < columns)
        {
            if (!TryReadRun(reader, white, out int run))
                return false;

            position = Math.Min(columns, position + run);
            coding.Add(position);
            white = !white;
        }

        return true;
    }

    /// <summary>Decodes a line as differences from the line above it.</summary>
    private static bool DecodeTwoDimensionalLine(BitReader reader, List<int> reference, List<int> coding, int columns)
    {
        int a0 = -1;
        bool white = true;

        while (a0 < columns)
        {
            if (!TryReadMode(reader, out int mode))
                return false;

            (int b1, int b2) = Transitions(reference, a0, white, columns);

            switch (mode)
            {
                case CcittFaxTables.ModePass:
                    a0 = b2;
                    break;

                case CcittFaxTables.ModeHorizontal:
                {
                    if (!TryReadRun(reader, white, out int first) || !TryReadRun(reader, !white, out int second))
                        return false;

                    int start = a0 < 0 ? 0 : a0;
                    int a1 = Math.Min(columns, start + first);
                    int a2 = Math.Min(columns, a1 + second);
                    coding.Add(a1);
                    coding.Add(a2);
                    a0 = a2;
                    break;
                }

                case CcittFaxTables.ModeEndOfLine:
                    return coding.Count > 0;

                case CcittFaxTables.ModeExtension:
                    return false;

                default:
                {
                    int delta = mode switch
                    {
                        CcittFaxTables.ModeVertical0 => 0,
                        CcittFaxTables.ModeVerticalRight1 => 1,
                        CcittFaxTables.ModeVerticalRight2 => 2,
                        CcittFaxTables.ModeVerticalRight3 => 3,
                        CcittFaxTables.ModeVerticalLeft1 => -1,
                        CcittFaxTables.ModeVerticalLeft2 => -2,
                        _ => -3,
                    };

                    int a1 = Math.Clamp(b1 + delta, 0, columns);
                    coding.Add(a1);
                    a0 = a1;
                    white = !white;
                    break;
                }
            }

            // Every mode moves a0 forward, except a pass that lands where it
            // started. Refusing to loop is cheaper than proving it cannot.
            if (coding.Count > (columns * 2) + 2)
                return false;
        }

        return true;
    }

    /// <summary>
    /// The two changing elements on the reference line that the two-dimensional
    /// modes are defined against: <c>b1</c> is the first one right of <c>a0</c>
    /// with the opposite colour to the current run, and <c>b2</c> the one after.
    /// </summary>
    private static (int B1, int B2) Transitions(List<int> reference, int a0, bool white, int columns)
    {
        int index = 0;
        while (index < reference.Count && reference[index] <= a0)
            index++;

        // Changing elements alternate colour starting from white, so the parity of
        // the index is the colour of the element.
        if ((index & 1) != (white ? 0 : 1))
            index++;

        int b1 = index < reference.Count ? reference[index] : columns;
        int b2 = index + 1 < reference.Count ? reference[index + 1] : columns;
        return (Math.Min(b1, columns), Math.Min(b2, columns));
    }

    /// <summary>Reads one run, following makeup codes until a terminating one.</summary>
    private static bool TryReadRun(BitReader reader, bool white, out int total)
    {
        total = 0;
        IReadOnlyDictionary<int, int> table = white ? CcittFaxTables.White : CcittFaxTables.Black;

        for (int codes = 0; codes < 64; codes++)
        {
            if (!TryReadCode(reader, table, CcittFaxTables.MaxRunCodeLength, out int run))
                return false;

            total += run;
            if (run <= CcittFaxTables.MaxTerminatingRun)
                return true;
        }

        return false;
    }

    private static bool TryReadMode(BitReader reader, out int mode) =>
        TryReadCode(reader, CcittFaxTables.Modes, CcittFaxTables.MaxModeCodeLength, out mode);

    /// <summary>
    /// Reads one variable-length code a bit at a time. These are prefix codes, so
    /// the first length at which the accumulated bits resolve is the answer.
    /// </summary>
    private static bool TryReadCode(BitReader reader, IReadOnlyDictionary<int, int> table, int maxLength, out int value)
    {
        value = 0;
        int code = 0;

        for (int length = 1; length <= maxLength; length++)
        {
            if (!reader.TryReadBit(out int bit))
                return false;

            code = (code << 1) | bit;
            if (table.TryGetValue(CcittFaxTables.Key(length, code), out value))
                return true;
        }

        return false;
    }

    /// <summary>Paints a line's changing elements into packed one-bit pixels.</summary>
    private static void Paint(List<byte> output, List<int> coding, int columns, int stride, bool blackIs1)
    {
        int start = output.Count;
        for (int i = 0; i < stride; i++)
            output.Add(0);

        // Runs alternate starting white, so only the black ones are painted; the
        // row starts as whichever bit value means white.
        if (!blackIs1)
        {
            for (int i = 0; i < stride; i++)
                output[start + i] = 0xFF;
        }

        int position = 0;
        bool white = true;
        foreach (int change in coding)
        {
            int end = Math.Min(change, columns);
            if (!white)
            {
                for (int x = position; x < end; x++)
                {
                    int index = start + (x >> 3);
                    int mask = 0x80 >> (x & 7);
                    output[index] = blackIs1
                        ? (byte)(output[index] | mask)
                        : (byte)(output[index] & ~mask);
                }
            }

            position = end;
            white = !white;
            if (position >= columns)
                break;
        }
    }

    private static CcittFaxResult Failed(string reason) =>
        new(CcittFaxOutcome.Malformed, null, 0, reason);

    /// <summary>A most-significant-bit-first reader over the encoded data.</summary>
    private sealed class BitReader(ReadOnlySpan<byte> data)
    {
        private readonly byte[] _data = data.ToArray();
        private int _bit;

        public bool HasMore => _bit < _data.Length * 8;

        public bool TryReadBit(out int bit)
        {
            if (_bit >= _data.Length * 8)
            {
                bit = 0;
                return false;
            }

            bit = (_data[_bit >> 3] >> (7 - (_bit & 7))) & 1;
            _bit++;
            return true;
        }

        public void AlignToByte() => _bit = (_bit + 7) & ~7;

        public bool TryPeekEndOfLine()
        {
            int saved = _bit;
            try
            {
                // An end-of-line code may be preceded by any number of fill zeroes,
                // so a run of at least eleven zeroes then a one is one.
                int zeroes = 0;
                while (TryReadBit(out int bit))
                {
                    if (bit == 0)
                    {
                        zeroes++;
                        if (zeroes > 64)
                            return false;
                        continue;
                    }

                    return zeroes >= CcittFaxTables.EndOfLineLength - 1;
                }

                return false;
            }
            finally
            {
                _bit = saved;
            }
        }

        public void SkipEndOfLine()
        {
            while (TryReadBit(out int bit))
            {
                if (bit == 1)
                    return;
            }
        }
    }
}
