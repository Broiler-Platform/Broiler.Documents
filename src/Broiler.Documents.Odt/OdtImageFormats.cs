using System;

namespace Broiler.Documents.Odt;

/// <summary>ODT manifest media-type normalization; raster lookups live in DocumentImageFormats.</summary>
internal static class OdtImageFormats
{
    /// <summary>
    /// Normalizes a media type the manifest declares, or null when this codec
    /// does not carry that format. The manifest is a declaration, not evidence,
    /// so it only ever narrows to the list already supported.
    /// </summary>
    public static string? ContentTypeForMediaType(string? mediaType) =>
        mediaType?.Trim().ToLowerInvariant() switch
        {
            "image/png" => "image/png",
            "image/jpeg" or "image/jpg" or "image/pjpeg" => "image/jpeg",
            "image/gif" => "image/gif",
            "image/bmp" or "image/x-ms-bmp" => "image/bmp",
            "image/tiff" => "image/tiff",
            "image/webp" => "image/webp",
            "image/x-icon" or "image/vnd.microsoft.icon" => "image/x-icon",
            _ => null,
        };

    /// <summary>
    /// The size, in points, a JPEG (JFIF <c>APP0</c> density) or PNG
    /// (<c>pHYs</c>) picture states for itself: its pixels at the resolution it
    /// gives, or null when it gives none. LibreOffice sizes a bitmap fill tile
    /// from it - a 513-pixel picture at 300 DPI tiles at 4.3 cm, not the
    /// 13.6 cm its pixels make at 96.
    /// </summary>
    public static (double Width, double Height)? StatedSize(ReadOnlySpan<byte> data)
    {
        double dpiX = 0, dpiY = 0;
        int pixelsX = 0, pixelsY = 0;
        if (data.Length > 20 && data[0] == 0xFF && data[1] == 0xD8)
        {
            int at = 2;
            while (at + 4 <= data.Length && data[at] == 0xFF)
            {
                byte marker = data[at + 1];
                int length = (data[at + 2] << 8) | data[at + 3];
                if (marker == 0xDA || length < 2 || at + 2 + length > data.Length)
                    break;
                if (marker == 0xE0 && length >= 14 && data.Slice(at + 4, 5).SequenceEqual("JFIF\0"u8))
                {
                    byte unit = data[at + 11];
                    double scale = unit switch { 1 => 1, 2 => 2.54, _ => 0 };
                    dpiX = ((data[at + 12] << 8) | data[at + 13]) * scale;
                    dpiY = ((data[at + 14] << 8) | data[at + 15]) * scale;
                }
                else if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC && length >= 7)
                {
                    pixelsY = (data[at + 5] << 8) | data[at + 6];
                    pixelsX = (data[at + 7] << 8) | data[at + 8];
                    break;
                }
                at += 2 + length;
            }
        }
        else if (data.Length > 24 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            int at = 8;
            while (at + 12 <= data.Length)
            {
                int length = (data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3];
                if (length < 0 || at + 12 + length > data.Length)
                    break;
                ReadOnlySpan<byte> type = data.Slice(at + 4, 4);
                ReadOnlySpan<byte> body = data.Slice(at + 8, length);
                if (type.SequenceEqual("IHDR"u8) && length >= 8)
                {
                    pixelsX = (int)Math.Min(int.MaxValue, BigEndian(body));
                    pixelsY = (int)Math.Min(int.MaxValue, BigEndian(body[4..]));
                }
                else if (type.SequenceEqual("pHYs"u8) && length >= 9 && body[8] == 1)
                {
                    dpiX = BigEndian(body) * 0.0254;
                    dpiY = BigEndian(body[4..]) * 0.0254;
                }
                else if (type.SequenceEqual("IDAT"u8))
                {
                    break;
                }
                at += 12 + length;
            }
        }
        if (dpiX <= 0 || dpiY <= 0 || pixelsX <= 0 || pixelsY <= 0)
            return null;
        return (pixelsX * 72.0 / dpiX, pixelsY * 72.0 / dpiY);
    }

    private static long BigEndian(ReadOnlySpan<byte> bytes) =>
        ((long)bytes[0] << 24) | ((long)bytes[1] << 16) | ((long)bytes[2] << 8) | bytes[3];
}
