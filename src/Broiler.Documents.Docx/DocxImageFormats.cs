using System;

namespace Broiler.Documents.Docx;

/// <summary>
/// The raster image formats the DOCX codec carries between a package's
/// <c>word/media</c> parts and <see cref="Model.InlineImage"/>.
/// </summary>
/// <remarks>
/// The list is deliberately raster-only. Word also embeds EMF/WMF metafiles —
/// usually as the fallback branch beside a chart or a shape — and those cannot
/// be decoded into pixels here, so they are reported rather than carried as an
/// image that would draw as nothing.
/// </remarks>
internal static class DocxImageFormats
{
    /// <summary>The media type for a media part's extension, or null when it is not a raster image.</summary>
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
    /// The media type implied by the leading bytes, used when the media part's
    /// extension is missing or unknown. Sniffing only confirms formats already on
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

    /// <summary>The file extension a writer should give a part of <paramref name="contentType"/>.</summary>
    public static string ExtensionForContentType(string? contentType) =>
        contentType?.ToLowerInvariant() switch
        {
            "image/png" => "png",
            "image/jpeg" or "image/jpg" => "jpeg",
            "image/gif" => "gif",
            "image/bmp" or "image/x-ms-bmp" => "bmp",
            "image/tiff" => "tiff",
            "image/webp" => "webp",
            "image/x-icon" or "image/vnd.microsoft.icon" => "ico",
            _ => "png",
        };

    private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix) =>
        data.Length >= prefix.Length && data[..prefix.Length].SequenceEqual(prefix);
}
