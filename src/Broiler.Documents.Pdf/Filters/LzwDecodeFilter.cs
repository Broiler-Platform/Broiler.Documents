using System;
using System.IO;

namespace Broiler.Documents.Pdf.Filters;

/// <summary>
/// LZWDecode (ISO 32000-1 clause 7.4.4): variable-width Lempel-Ziv-Welch over a
/// most-significant-bit-first code stream.
/// </summary>
/// <remarks>
/// <para>
/// Built in rather than composed, which is a deliberate departure from the
/// technologies that arrive through <see cref="IPdfStreamFilter"/> from outside.
/// The composition boundary exists for three reasons (PDF extension points §1),
/// and LZW meets none of them once its register row is clear: there is no outside
/// component to review, because the algorithm is a page of this repository's own
/// code with no table, asset, or dependency; and the security argument for
/// keeping a codec out of the default build is about image decoders and font
/// parsers, not about a bounded byte-stream decompressor of the same shape as
/// <see cref="RunLengthDecodeFilter"/> and <see cref="FlateDecodeFilter"/>, which
/// have always been built in.
/// </para>
/// <para>
/// Like every filter here, its output is bounded while it is produced. LZW is a
/// compression format and therefore a decompression-bomb vector; the ceiling is
/// checked before each string is appended rather than after the buffer exists.
/// </para>
/// <para>
/// <c>EarlyChange</c> is honoured. It decides whether the code width grows one
/// code before the table fills or exactly when it does, and getting it wrong does
/// not fail — it silently produces different bytes from the same input, which is
/// the worst way for a filter to be wrong.
/// </para>
/// </remarks>
public sealed class LzwDecodeFilter : IPdfStreamFilter
{
    /// <summary>Resets the table and the code width.</summary>
    private const int ClearCode = 256;

    /// <summary>Ends the stream.</summary>
    private const int EndOfDataCode = 257;

    /// <summary>The first code the decoder assigns; 0-255 are literals, 256-257 are control.</summary>
    private const int FirstAssignedCode = 258;

    /// <summary>One past the largest code a 12-bit stream can carry.</summary>
    private const int MaxCodes = 4096;

    private const int MinCodeWidth = 9;

    private const int MaxCodeWidth = 12;

    public string Name => PdfFilterNames.Lzw;

    public string? Abbreviation => "LZW";

    public bool ProducesByteStream => true;

    public PdfFilterResult Decode(ReadOnlySpan<byte> input, PdfFilterParameters parameters, PdfFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(context);

        if (input.Length == 0)
            return PdfFilterResult.Success([]);

        // 0 and 1 are the only values the format defines; anything else is read
        // as the default rather than rejected, because a wrong EarlyChange is a
        // producer's bug and the stream may still decode.
        int earlyChange = parameters.GetInt32("EarlyChange", 1) == 0 ? 0 : 1;
        long ceiling = context.CeilingFor(input.Length);

        // The table as prefix chains rather than materialized strings: 4096 entries
        // of two words each, whatever the strings grow to.
        var prefix = new int[MaxCodes];
        var suffix = new byte[MaxCodes];
        for (int code = 0; code < 256; code++)
        {
            prefix[code] = -1;
            suffix[code] = (byte)code;
        }

        // An entry can be no longer than the number of entries, so this is the
        // largest string the table can express.
        byte[] scratch = new byte[MaxCodes];

        var output = new MemoryStream();
        int next = FirstAssignedCode;
        int codeWidth = MinCodeWidth;
        int previous = -1;

        int bitBuffer = 0;
        int bitCount = 0;
        int position = 0;

        while (true)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            while (bitCount < codeWidth)
            {
                if (position >= input.Length)
                {
                    // A stream that stops without EOD is common enough that
                    // refusing it would lose readable documents. What was decoded
                    // is returned; nothing is invented to fill the gap.
                    return PdfFilterResult.Success(output.ToArray());
                }

                bitBuffer = (bitBuffer << 8) | input[position++];
                bitCount += 8;
            }

            bitCount -= codeWidth;
            int code = (bitBuffer >> bitCount) & ((1 << codeWidth) - 1);
            bitBuffer &= (1 << bitCount) - 1;

            if (code == EndOfDataCode)
                break;

            if (code == ClearCode)
            {
                next = FirstAssignedCode;
                codeWidth = MinCodeWidth;
                previous = -1;
                continue;
            }

            byte firstByte;
            if (previous < 0)
            {
                // The first code of a stream, or of a run after a clear, can only
                // be a literal: nothing else is in the table yet.
                if (code >= FirstAssignedCode)
                    return PdfFilterResult.Malformed("An LZWDecode stream began with a code that was not in its table.");

                firstByte = suffix[code];
                if (output.Length + 1 > ceiling)
                    return Overflowed();

                output.WriteByte(firstByte);
                previous = code;
                continue;
            }

            if (code < next)
            {
                if (!TryWrite(output, ceiling, prefix, suffix, scratch, code, out firstByte, out bool over))
                    return over ? Overflowed() : Corrupt();
            }
            else if (code == next)
            {
                // The case an encoder creates by using an entry in the same step
                // that defines it: the string is the previous one followed by its
                // own first byte, which the table cannot yet supply.
                if (!TryWrite(output, ceiling, prefix, suffix, scratch, previous, out firstByte, out bool over))
                    return over ? Overflowed() : Corrupt();

                if (output.Length + 1 > ceiling)
                    return Overflowed();

                output.WriteByte(firstByte);
            }
            else
            {
                return PdfFilterResult.Malformed("An LZWDecode stream used a code that was not in its table.");
            }

            if (next < MaxCodes)
            {
                prefix[next] = previous;
                suffix[next] = firstByte;
                next++;
            }

            previous = code;

            // With EarlyChange the width grows one code before the table needs it,
            // which is the default and what nearly every producer emits.
            if (codeWidth < MaxCodeWidth && next + earlyChange >= 1 << codeWidth)
                codeWidth++;
        }

        return PdfFilterResult.Success(output.ToArray());
    }

    /// <summary>
    /// Writes the string a code stands for by walking its prefix chain, and
    /// reports the first byte of that string — which is what the next table entry
    /// is built from.
    /// </summary>
    private static bool TryWrite(
        MemoryStream output,
        long ceiling,
        int[] prefix,
        byte[] suffix,
        byte[] scratch,
        int code,
        out byte firstByte,
        out bool overCeiling)
    {
        firstByte = 0;
        overCeiling = false;

        // The chain is built backwards, so it is collected backwards and written
        // forwards. The bound is a guard rather than a real case: entries only
        // ever point at lower-numbered ones, so a chain cannot loop.
        int count = 0;
        for (int current = code; current >= 0; current = prefix[current])
        {
            if (count >= scratch.Length)
                return false;
            scratch[count++] = suffix[current];
        }

        if (count == 0)
            return false;

        if (output.Length + count > ceiling)
        {
            overCeiling = true;
            return false;
        }

        firstByte = scratch[count - 1];
        for (int i = count - 1; i >= 0; i--)
            output.WriteByte(scratch[i]);

        return true;
    }

    private static PdfFilterResult Overflowed() =>
        PdfFilterResult.LimitExceeded("An LZWDecode stream would exceed its decoded-byte ceiling.");

    private static PdfFilterResult Corrupt() =>
        PdfFilterResult.Malformed("An LZWDecode table entry did not resolve to a string.");
}
