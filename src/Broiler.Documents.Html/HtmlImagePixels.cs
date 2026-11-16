using System;

namespace Broiler.Documents.Html;

/// <summary>
/// The pixel size an encoded picture states in its header: PNG's <c>IHDR</c>,
/// a JPEG frame header, GIF's logical screen. Read without decoding, because
/// the HTML reader needs the size before anything has decoded the picture.
/// </summary>
internal static class HtmlImagePixels
{
    /// <summary>The picture's width and height in pixels, or null where the header does not say.</summary>
    public static (int Width, int Height)? Read(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 24 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) &&
            data.Slice(12, 4).SequenceEqual("IHDR"u8))
            return Size(BigEndian(data[16..]), BigEndian(data[20..]));

        if (data.Length >= 10 && (data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8)))
            return Size(data[6] | (data[7] << 8), data[8] | (data[9] << 8));

        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            int at = 2;
            while (at + 9 <= data.Length && data[at] == 0xFF)
            {
                byte marker = data[at + 1];
                int length = (data[at + 2] << 8) | data[at + 3];
                if (marker == 0xDA || length < 2)
                    break;
                // Start of frame: every SOFn but DHT (C4), JPG (C8) and DAC (CC).
                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC && length >= 7)
                    return Size((data[at + 7] << 8) | data[at + 8], (data[at + 5] << 8) | data[at + 6]);
                at += 2 + length;
            }
        }

        return null;
    }

    private static (int Width, int Height)? Size(long width, long height) =>
        width is > 0 and <= int.MaxValue && height is > 0 and <= int.MaxValue ? ((int)width, (int)height) : null;

    private static long BigEndian(ReadOnlySpan<byte> bytes) =>
        ((long)bytes[0] << 24) | ((long)bytes[1] << 16) | ((long)bytes[2] << 8) | bytes[3];
}
