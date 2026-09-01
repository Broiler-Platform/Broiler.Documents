using System;
using System.Buffers.Binary;
using System.Globalization;

namespace Broiler.Documents.Pdf.Images;

/// <summary>
/// What a JPEG's own headers declare about the frame, read without decoding it.
/// </summary>
/// <remarks>
/// This is the tuple the IP/licensing register's IP-005 row asks a reviewer to
/// approve — coding process, entropy mode, sample precision, component count —
/// plus the two things that decide whether the row even applies: the size of the
/// buffer a decode would allocate, and whether an Adobe <c>APP14</c> marker is
/// present, which moves colour interpretation into the separate IP-006 row.
/// </remarks>
internal readonly record struct JpegFrameHeader(
    byte FrameMarker,
    int Precision,
    int Width,
    int Height,
    int Components,
    bool HasAdobeMarker,
    int AdobeTransform)
{
    /// <summary>The frame process and entropy mode, named as the standard names them.</summary>
    public string DescribeProcess() => FrameMarker switch
    {
        0xC0 => "baseline sequential DCT, Huffman (SOF0)",
        0xC1 => "extended sequential DCT, Huffman (SOF1)",
        0xC2 => "progressive DCT, Huffman (SOF2)",
        0xC3 => "lossless, Huffman (SOF3)",
        0xC5 => "differential sequential DCT, Huffman (SOF5)",
        0xC6 => "differential progressive DCT, Huffman (SOF6)",
        0xC7 => "differential lossless, Huffman (SOF7)",
        0xC9 => "extended sequential DCT, arithmetic (SOF9)",
        0xCA => "progressive DCT, arithmetic (SOF10)",
        0xCB => "lossless, arithmetic (SOF11)",
        0xCD => "differential sequential DCT, arithmetic (SOF13)",
        0xCE => "differential progressive DCT, arithmetic (SOF14)",
        0xCF => "differential lossless, arithmetic (SOF15)",
        _ => string.Create(CultureInfo.InvariantCulture, $"an unrecognized frame process (marker 0x{FrameMarker:X2})"),
    };

    /// <summary>The whole tuple as one phrase, for a diagnostic.</summary>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Width}x{Height}, {Precision}-bit, {Components} component{(Components == 1 ? string.Empty : "s")}, {DescribeProcess()}");
}

/// <summary>
/// Reads a JPEG's marker segments far enough to describe its frame, and no
/// further.
/// </summary>
/// <remarks>
/// <para>
/// A decoder cannot be handed untrusted data and asked afterwards how much it
/// allocated. The <see cref="Filters.IPdfStreamFilter"/> contract requires the
/// byte ceiling to be honoured <em>before</em> an output buffer exists, and the
/// only way to do that for an image is to read the frame header first and
/// compute the size the decode would produce.
/// </para>
/// <para>
/// So this walks segment lengths only. It never enters entropy-coded data, never
/// allocates in proportion to the input, and stops at the first <c>SOS</c>.
/// </para>
/// </remarks>
internal static class JpegFrameReader
{
    /// <summary>The <c>Adobe</c> signature that opens an APP14 colour-transform marker.</summary>
    private static ReadOnlySpan<byte> AdobeSignature => "Adobe"u8;

    public static bool TryRead(ReadOnlySpan<byte> data, out JpegFrameHeader frame, out string? error)
    {
        frame = default;

        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
        {
            error = "The stream does not begin with a JPEG SOI marker.";
            return false;
        }

        bool haveFrame = false;
        byte frameMarker = 0;
        int precision = 0, width = 0, height = 0, components = 0;
        bool adobe = false;
        int transform = -1;

        int position = 2;
        while (position + 1 < data.Length)
        {
            // Fill bytes are legal between segments; skip them one at a time
            // rather than assuming the next byte starts a marker.
            if (data[position] != 0xFF)
            {
                position++;
                continue;
            }

            byte marker = data[position + 1];
            position += 2;

            // Standalone markers carry no payload.
            if (marker == 0xFF || marker == 0x01 || marker is >= 0xD0 and <= 0xD7 || marker == 0xD8)
                continue;

            if (marker == 0xD9)
                break;

            if (position + 2 > data.Length)
            {
                error = "A JPEG segment header runs past the end of the stream.";
                return false;
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position, 2));
            if (length < 2 || position + length > data.Length)
            {
                error = "A JPEG segment declares a length that does not fit the stream.";
                return false;
            }

            ReadOnlySpan<byte> segment = data.Slice(position + 2, length - 2);

            // Start of scan: everything after it is entropy-coded, and nothing
            // this reader wants lives past it.
            if (marker == 0xDA)
                break;

            if (marker == 0xEE && segment.Length >= 12 && segment[..5].SequenceEqual(AdobeSignature))
            {
                adobe = true;
                transform = segment[^1];
            }
            else if (IsFrameMarker(marker))
            {
                if (haveFrame)
                {
                    error = "The stream declares more than one JPEG frame.";
                    return false;
                }

                if (segment.Length < 6)
                {
                    error = "A JPEG frame header is too short to describe a frame.";
                    return false;
                }

                haveFrame = true;
                frameMarker = marker;
                precision = segment[0];
                height = BinaryPrimitives.ReadUInt16BigEndian(segment.Slice(1, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(segment.Slice(3, 2));
                components = segment[5];
            }

            position += length;
        }

        if (!haveFrame)
        {
            error = "The stream carries no JPEG frame header.";
            return false;
        }

        if (width <= 0 || height <= 0 || components <= 0)
        {
            error = "The JPEG frame header declares an empty frame.";
            return false;
        }

        frame = new JpegFrameHeader(frameMarker, precision, width, height, components, adobe, transform);
        error = null;
        return true;
    }

    /// <summary>
    /// True for the SOF markers. <c>0xC4</c> (DHT), <c>0xC8</c> (reserved), and
    /// <c>0xCC</c> (DAC) sit inside the range and are not frame headers.
    /// </summary>
    private static bool IsFrameMarker(byte marker) =>
        marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);
}
