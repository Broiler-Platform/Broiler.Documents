using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;

namespace Broiler.Documents.Pdf.Filters;

/// <summary>
/// FlateDecode, built on the runtime's DEFLATE implementation (RFC 1950/1951).
/// </summary>
/// <remarks>
/// The algorithm and its only implementation dependency ship with .NET, so this
/// filter adds no third-party component to the codec. Output is bounded while it
/// is produced rather than checked afterwards: a decompression bomb is stopped at
/// the ceiling, not after it has been allocated.
/// </remarks>
public sealed class FlateDecodeFilter : IPdfStreamFilter
{
    public string Name => PdfFilterNames.Flate;

    public string? Abbreviation => "Fl";

    public bool ProducesByteStream => true;

    public PdfFilterResult Decode(ReadOnlySpan<byte> input, PdfFilterParameters parameters, PdfFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(context);

        if (input.Length == 0)
            return PdfFilterResult.Success([]);

        byte[] encoded = input.ToArray();
        long ceiling = context.CeilingFor(encoded.Length);

        // Well-formed streams carry a zlib wrapper. Producers exist that emit raw
        // DEFLATE, and others that prepend stray whitespace to the wrapper, so the
        // three forms are tried in order of how well-formed they are.
        if (TryInflate(encoded, 0, zlib: true, ceiling, context, out byte[]? data, out bool hitCeiling))
            return PdfFilterResult.Success(data!);
        if (hitCeiling)
            return PdfFilterResult.LimitExceeded("A FlateDecode stream expanded past its decoded-byte ceiling.");

        int skipped = 0;
        while (skipped < encoded.Length && encoded[skipped] is 0x0A or 0x0D or 0x20 or 0x09)
            skipped++;
        if (skipped > 0 && TryInflate(encoded, skipped, zlib: true, ceiling, context, out data, out hitCeiling))
            return PdfFilterResult.Success(data!);
        if (hitCeiling)
            return PdfFilterResult.LimitExceeded("A FlateDecode stream expanded past its decoded-byte ceiling.");

        if (TryInflate(encoded, 0, zlib: false, ceiling, context, out data, out hitCeiling))
            return PdfFilterResult.Success(data!);

        return hitCeiling
            ? PdfFilterResult.LimitExceeded("A FlateDecode stream expanded past its decoded-byte ceiling.")
            : PdfFilterResult.Malformed("A FlateDecode stream could not be inflated as zlib or raw DEFLATE data.");
    }

    private static bool TryInflate(
        byte[] encoded,
        int offset,
        bool zlib,
        long ceiling,
        PdfFilterContext context,
        out byte[]? data,
        out bool hitCeiling)
    {
        data = null;
        hitCeiling = false;

        var output = new MemoryStream();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            using var source = new MemoryStream(encoded, offset, encoded.Length - offset, writable: false);
            using Stream inflater = zlib
                ? new ZLibStream(source, CompressionMode.Decompress, leaveOpen: true)
                : new DeflateStream(source, CompressionMode.Decompress, leaveOpen: true);

            while (true)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                int read = inflater.Read(buffer, 0, buffer.Length);
                if (read == 0)
                    break;

                if (output.Length + read > ceiling)
                {
                    hitCeiling = true;
                    return false;
                }

                output.Write(buffer, 0, read);
            }
        }
        catch (InvalidDataException)
        {
            // A truncated stream that produced usable bytes is common in damaged
            // files; keep what inflated rather than losing the whole object.
            if (output.Length > 0)
            {
                data = output.ToArray();
                return true;
            }

            return false;
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        data = output.ToArray();
        return true;
    }
}

/// <summary>
/// ASCIIHexDecode (clause 7.4.2): hexadecimal digits terminated by <c>&gt;</c>.
/// </summary>
public sealed class AsciiHexDecodeFilter : IPdfStreamFilter
{
    public string Name => PdfFilterNames.AsciiHex;

    public string? Abbreviation => "AHx";

    public bool ProducesByteStream => true;

    public PdfFilterResult Decode(ReadOnlySpan<byte> input, PdfFilterParameters parameters, PdfFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Two hex digits make one byte, so the output can never exceed half the
        // input and needs no incremental ceiling check beyond this one.
        long ceiling = context.CeilingFor(input.Length);
        if (input.Length / 2 + 1 > ceiling)
            return PdfFilterResult.LimitExceeded("An ASCIIHexDecode stream would exceed its decoded-byte ceiling.");

        var output = new byte[input.Length / 2 + 1];
        int count = 0;
        int pending = -1;

        foreach (byte b in input)
        {
            if (b == (byte)'>')
                break;
            if (Syntax.PdfLexer.IsWhitespace(b))
                continue;
            if (!Syntax.PdfLexer.TryHexDigit(b, out int digit))
                return PdfFilterResult.Malformed("An ASCIIHexDecode stream contained a non-hexadecimal byte.");

            if (pending < 0)
            {
                pending = digit;
                continue;
            }

            output[count++] = (byte)((pending << 4) | digit);
            pending = -1;
        }

        if (pending >= 0)
            output[count++] = (byte)(pending << 4);

        Array.Resize(ref output, count);
        return PdfFilterResult.Success(output);
    }
}

/// <summary>
/// ASCII85Decode (clause 7.4.3): base-85 groups terminated by <c>~&gt;</c>.
/// </summary>
public sealed class Ascii85DecodeFilter : IPdfStreamFilter
{
    public string Name => PdfFilterNames.Ascii85;

    public string? Abbreviation => "A85";

    public bool ProducesByteStream => true;

    public PdfFilterResult Decode(ReadOnlySpan<byte> input, PdfFilterParameters parameters, PdfFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Five characters make four bytes, and a 'z' makes four from one, so four
        // times the input length bounds the output.
        long ceiling = context.CeilingFor(input.Length);
        long worstCase = (long)input.Length * 4 + 4;
        if (worstCase > ceiling)
            worstCase = ceiling;

        var output = new System.IO.MemoryStream();
        uint tuple = 0;
        int count = 0;
        int index = 0;

        // A leading '<~' is not part of the PDF form but is emitted by some tools.
        if (input.Length >= 2 && input[0] == (byte)'<' && input[1] == (byte)'~')
            index = 2;

        for (; index < input.Length; index++)
        {
            byte b = input[index];
            if (Syntax.PdfLexer.IsWhitespace(b))
                continue;

            if (b == (byte)'~')
                break;

            if (b == (byte)'z')
            {
                if (count != 0)
                    return PdfFilterResult.Malformed("An ASCII85Decode stream used 'z' inside a partial group.");
                if (output.Length + 4 > ceiling)
                    return PdfFilterResult.LimitExceeded("An ASCII85Decode stream would exceed its decoded-byte ceiling.");
                output.Write([0, 0, 0, 0], 0, 4);
                continue;
            }

            if (b < (byte)'!' || b > (byte)'u')
                return PdfFilterResult.Malformed("An ASCII85Decode stream contained a byte outside the base-85 alphabet.");

            // Checked so a malformed group cannot silently wrap into a valid value.
            try
            {
                tuple = checked(tuple * 85 + (uint)(b - (byte)'!'));
            }
            catch (OverflowException)
            {
                return PdfFilterResult.Malformed("An ASCII85Decode group exceeded the 32-bit value it encodes.");
            }

            if (++count != 5)
                continue;

            if (output.Length + 4 > ceiling)
                return PdfFilterResult.LimitExceeded("An ASCII85Decode stream would exceed its decoded-byte ceiling.");

            WriteBigEndian(output, tuple, 4);
            tuple = 0;
            count = 0;
        }

        if (count == 1)
            return PdfFilterResult.Malformed("An ASCII85Decode stream ended with a single-character group.");

        if (count > 1)
        {
            // Pad the short final group with 'u' (84) before extracting count-1 bytes.
            for (int i = count; i < 5; i++)
                tuple = tuple * 85 + 84;
            if (output.Length + count - 1 > ceiling)
                return PdfFilterResult.LimitExceeded("An ASCII85Decode stream would exceed its decoded-byte ceiling.");
            WriteBigEndian(output, tuple, count - 1);
        }

        return PdfFilterResult.Success(output.ToArray());
    }

    private static void WriteBigEndian(System.IO.MemoryStream output, uint value, int byteCount)
    {
        Span<byte> bytes =
        [
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value,
        ];
        for (int i = 0; i < byteCount; i++)
            output.WriteByte(bytes[i]);
    }
}

/// <summary>
/// RunLengthDecode (clause 7.4.5): length-prefixed literal and repeat runs
/// terminated by the byte 128.
/// </summary>
public sealed class RunLengthDecodeFilter : IPdfStreamFilter
{
    public string Name => PdfFilterNames.RunLength;

    public string? Abbreviation => "RL";

    public bool ProducesByteStream => true;

    public PdfFilterResult Decode(ReadOnlySpan<byte> input, PdfFilterParameters parameters, PdfFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        long ceiling = context.CeilingFor(input.Length);
        var output = new System.IO.MemoryStream();
        int index = 0;

        while (index < input.Length)
        {
            byte length = input[index++];
            if (length == 128)
                break;

            if (length < 128)
            {
                int run = length + 1;
                if (index + run > input.Length)
                    return PdfFilterResult.Malformed("A RunLengthDecode literal run ran past the end of the stream.");
                if (output.Length + run > ceiling)
                    return PdfFilterResult.LimitExceeded("A RunLengthDecode stream would exceed its decoded-byte ceiling.");

                output.Write(input.Slice(index, run));
                index += run;
                continue;
            }

            if (index >= input.Length)
                return PdfFilterResult.Malformed("A RunLengthDecode repeat run had no byte to repeat.");

            int repeat = 257 - length;
            if (output.Length + repeat > ceiling)
                return PdfFilterResult.LimitExceeded("A RunLengthDecode stream would exceed its decoded-byte ceiling.");

            byte value = input[index++];
            for (int i = 0; i < repeat; i++)
                output.WriteByte(value);
        }

        return PdfFilterResult.Success(output.ToArray());
    }
}
