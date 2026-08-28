using System.IO.Compression;
using System.Text;

namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Builds minimal ODT packages around a hand-written <c>office:text</c> body so
/// reader tests can state the exact OpenDocument markup they exercise. Only the
/// parts the reader needs are written: the <c>mimetype</c> entry, the manifest,
/// <c>content.xml</c>, and whatever else a test asks for.
/// </summary>
internal static class OdtTestPackage
{
    public const string TextMediaType = "application/vnd.oasis.opendocument.text";

    public const string NamespaceDeclarations =
        "xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
        "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" " +
        "xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" " +
        "xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" " +
        "xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\" " +
        "xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" " +
        "xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\" " +
        "xmlns:xlink=\"http://www.w3.org/1999/xlink\"";

    /// <summary>
    /// The smallest valid PNG this codec will accept: a one-pixel image, used
    /// wherever a test needs real image bytes rather than their content.
    /// </summary>
    public static byte[] OnePixelPng { get; } =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    /// <summary>Wraps <paramref name="bodyXml"/> in a content part and zips a package around it.</summary>
    public static byte[] FromBody(
        string bodyXml,
        string automaticStylesXml = "",
        string? stylesPartInnerXml = null,
        string? mediaType = TextMediaType,
        IReadOnlyDictionary<string, string>? extraParts = null,
        IReadOnlyDictionary<string, byte[]>? binaryParts = null,
        string? manifestInnerXml = null)
    {
        string contentXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<office:document-content " + NamespaceDeclarations + " office:version=\"1.3\">" +
            (automaticStylesXml.Length == 0
                ? string.Empty
                : "<office:automatic-styles>" + automaticStylesXml + "</office:automatic-styles>") +
            "<office:body><office:text>" + bodyXml + "</office:text></office:body>" +
            "</office:document-content>";

        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["META-INF/manifest.xml"] =
                "<manifest:manifest xmlns:manifest=\"urn:oasis:names:tc:opendocument:xmlns:manifest:1.0\" " +
                "manifest:version=\"1.3\">" +
                (manifestInnerXml ??
                    "<manifest:file-entry manifest:full-path=\"/\" manifest:media-type=\"" +
                    TextMediaType + "\"/>" +
                    "<manifest:file-entry manifest:full-path=\"content.xml\" manifest:media-type=\"text/xml\"/>") +
                "</manifest:manifest>",
            ["content.xml"] = contentXml,
        };

        if (stylesPartInnerXml is not null)
        {
            parts["styles.xml"] =
                "<office:document-styles " + NamespaceDeclarations + " office:version=\"1.3\">" +
                stylesPartInnerXml +
                "</office:document-styles>";
        }

        if (extraParts is not null)
        {
            foreach (KeyValuePair<string, string> part in extraParts)
                parts[part.Key] = part.Value;
        }

        var binary = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> part in parts)
            binary[part.Key] = Encoding.UTF8.GetBytes(part.Value);

        if (binaryParts is not null)
        {
            foreach (KeyValuePair<string, byte[]> part in binaryParts)
                binary[part.Key] = part.Value;
        }

        return Zip(mediaType, binary);
    }

    /// <summary>Reads a package built by <see cref="FromBody"/> through the public codec.</summary>
    public static DocumentReadResult ReadBody(
        string bodyXml,
        string automaticStylesXml = "",
        DocumentReadOptions? options = null)
    {
        using var stream = new MemoryStream(FromBody(bodyXml, automaticStylesXml), writable: false);
        return new OdtDocumentCodec().Read(stream, options);
    }

    /// <summary>Reads a package carrying a <c>styles.xml</c> alongside the body.</summary>
    public static DocumentReadResult ReadStyled(
        string bodyXml,
        string stylesPartInnerXml,
        string automaticStylesXml = "")
    {
        using var stream = new MemoryStream(
            FromBody(bodyXml, automaticStylesXml, stylesPartInnerXml),
            writable: false);
        return new OdtDocumentCodec().Read(stream);
    }

    /// <summary>Reads a package whose <c>Pictures</c> entries a frame refers to.</summary>
    public static DocumentReadResult ReadWithPictures(
        string bodyXml,
        IReadOnlyDictionary<string, byte[]> pictures,
        string? manifestInnerXml = null,
        DocumentReadOptions? options = null)
    {
        using var stream = new MemoryStream(
            FromBody(bodyXml, binaryParts: pictures, manifestInnerXml: manifestInnerXml),
            writable: false);
        return new OdtDocumentCodec().Read(stream, options);
    }

    /// <summary>A <c>style:style</c> definition; pass the inner property elements.</summary>
    public static string Style(
        string name,
        string propertiesXml,
        string family = "paragraph",
        string? parent = null) =>
        "<style:style style:name=\"" + name + "\" style:family=\"" + family + "\"" +
        (parent is null ? string.Empty : " style:parent-style-name=\"" + parent + "\"") + ">" +
        propertiesXml +
        "</style:style>";

    /// <summary>A <c>text:p</c> holding plain text.</summary>
    public static string Paragraph(string text) => "<text:p>" + Escape(text) + "</text:p>";

    /// <summary>A <c>text:p</c> that names a paragraph style.</summary>
    public static string StyledParagraph(string styleName, string text) =>
        "<text:p text:style-name=\"" + styleName + "\">" + Escape(text) + "</text:p>";

    /// <summary>A table whose rows are given as arrays of cell contents.</summary>
    public static string Table(params string[][] rows)
    {
        var builder = new StringBuilder("<table:table table:name=\"T\"><table:table-column/>");
        foreach (string[] row in rows)
        {
            builder.Append("<table:table-row>");
            foreach (string cell in row)
                builder.Append("<table:table-cell>").Append(cell).Append("</table:table-cell>");
            builder.Append("</table:table-row>");
        }

        return builder.Append("</table:table>").ToString();
    }

    /// <summary>A bullet list style declaring the levels a test needs.</summary>
    public static string BulletListStyle(string name, int levels = 3)
    {
        var builder = new StringBuilder("<text:list-style style:name=\"" + name + "\">");
        for (int level = 1; level <= levels; level++)
        {
            builder.Append("<text:list-level-style-bullet text:level=\"")
                .Append(level)
                .Append("\" text:bullet-char=\"*\"/>");
        }

        return builder.Append("</text:list-style>").ToString();
    }

    /// <summary>A numbered list style declaring the levels a test needs.</summary>
    public static string NumberListStyle(string name, int levels = 3)
    {
        var builder = new StringBuilder("<text:list-style style:name=\"" + name + "\">");
        for (int level = 1; level <= levels; level++)
        {
            builder.Append("<text:list-level-style-number text:level=\"")
                .Append(level)
                .Append("\" style:num-format=\"1\"/>");
        }

        return builder.Append("</text:list-style>").ToString();
    }

    public static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    /// <summary>
    /// Zips the parts with the <c>mimetype</c> entry first and stored, the way
    /// ODF requires and the way the probe expects to find it.
    /// </summary>
    private static byte[] Zip(string? mediaType, IReadOnlyDictionary<string, byte[]> parts)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (mediaType is not null)
            {
                ZipArchiveEntry mimetype = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
                using Stream stream = mimetype.Open();
                byte[] bytes = Encoding.ASCII.GetBytes(mediaType);
                stream.Write(bytes, 0, bytes.Length);
            }

            foreach (KeyValuePair<string, byte[]> part in parts)
            {
                ZipArchiveEntry entry = archive.CreateEntry(part.Key, CompressionLevel.NoCompression);
                using Stream stream = entry.Open();
                stream.Write(part.Value, 0, part.Value.Length);
            }
        }

        return buffer.ToArray();
    }
}
