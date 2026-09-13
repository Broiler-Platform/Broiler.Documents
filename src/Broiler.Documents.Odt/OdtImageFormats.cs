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

}
