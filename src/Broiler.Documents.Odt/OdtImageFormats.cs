using System;

namespace Broiler.Documents.Odt;

/// <summary>
/// The raster image formats the ODT codec carries between a package's
/// <c>Pictures</c> entries and <see cref="Model.InlineImage"/>.
/// </summary>
/// <remarks>
/// Raster only, for the same reason as the DOCX codec: an ODF producer also
/// stores SVG, EMF, and WMF — usually as the replacement image beside a chart or
/// an embedded object — and those cannot be decoded into pixels here, so they are
/// reported rather than carried as an image that would draw as nothing.
/// </remarks>
internal static class OdtImageFormats
{
    /// <summary>The media type for a picture's extension, or null when it is not a raster image.</summary>
    public static string? ContentTypeForExtension(string? extension)
    {
        if (string.IsNullOrEmpty(extension))
            return null;

        return extension.TrimStart('.').ToLowerInvariant() switch
        {
            "png" => "image/png",
            "jpg" or "jpeg" or "jpe" => "image/jpeg",
            "gif" => "image/gif",
            "bmp" or "dib" => "image/bmp",
            "tif" or "tiff" => "image/tiff",
            "webp" => "image/webp",
            "ico" => "image/x-icon",
            _ => null,
        };
    }

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
    /// The media type implied by the leading bytes, used when the extension and
    /// the manifest are both unhelpful. Sniffing only confirms formats already on
    /// the supported list; it never widens it.
    /// </summary>
    public static string? ContentTypeForSignature(ReadOnlySpan<byte> data)
    {
        if (StartsWith(data, [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
            return "image/png";
        if (StartsWith(data, [0xFF, 0xD8, 0xFF]))
            return "image/jpeg";
        if (StartsWith(data, "GIF87a"u8) || StartsWith(data, "GIF89a"u8))
            return "image/gif";
        if (StartsWith(data, "BM"u8))
            return "image/bmp";
        if (StartsWith(data, [0x49, 0x49, 0x2A, 0x00]) || StartsWith(data, [0x4D, 0x4D, 0x00, 0x2A]))
            return "image/tiff";
        if (data.Length >= 12 && StartsWith(data, "RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
            return "image/webp";

        return null;
    }

    /// <summary>The file extension a writer should give a picture of <paramref name="contentType"/>.</summary>
    public static string ExtensionForContentType(string? contentType) =>
        contentType?.ToLowerInvariant() switch
        {
            "image/png" => "png",
            "image/jpeg" or "image/jpg" => "jpg",
            "image/gif" => "gif",
            "image/bmp" or "image/x-ms-bmp" => "bmp",
            "image/tiff" => "tif",
            "image/webp" => "webp",
            "image/x-icon" or "image/vnd.microsoft.icon" => "ico",
            _ => "png",
        };

    private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix) =>
        data.Length >= prefix.Length && data[..prefix.Length].SequenceEqual(prefix);
}
