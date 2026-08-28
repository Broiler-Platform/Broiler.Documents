using System.Xml.Linq;

namespace Broiler.Documents.Odt;

/// <summary>
/// The OpenDocument XML namespaces and package constants this codec uses. The
/// names are the OASIS ODF 1.2/1.3 ones; ODF 1.0/1.1 packages declare the same
/// URIs, so one set covers every version the reader accepts.
/// </summary>
internal static class OdtNamespaces
{
    public static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    public static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    public static readonly XNamespace Style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    public static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    public static readonly XNamespace Draw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
    public static readonly XNamespace Manifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
    public static readonly XNamespace Meta = "urn:oasis:names:tc:opendocument:xmlns:meta:1.0";

    /// <summary>The XSL-FO compatible namespace: most character and paragraph properties live here.</summary>
    public static readonly XNamespace Fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";

    /// <summary>The SVG compatible namespace: frame extents, titles, and font family names.</summary>
    public static readonly XNamespace Svg = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";

    public static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";

    public static readonly XNamespace DublinCore = "http://purl.org/dc/elements/1.1/";

    public static readonly XNamespace Xml = "http://www.w3.org/XML/1998/namespace";

    /// <summary>The package media type, and the payload of the <c>mimetype</c> entry.</summary>
    public const string PackageMediaType = "application/vnd.oasis.opendocument.text";

    /// <summary>
    /// The text <em>template</em> media type. A template holds the same body a
    /// document does, so the reader accepts one rather than refusing a file it
    /// can read perfectly well; the descriptor still claims only <c>.odt</c>.
    /// </summary>
    public const string TemplateMediaType = "application/vnd.oasis.opendocument.text-template";

    /// <summary>The ODF version this codec writes.</summary>
    public const string WrittenVersion = "1.3";

    public const string ContentPart = "content.xml";
    public const string StylesPart = "styles.xml";
    public const string MetaPart = "meta.xml";
    public const string ManifestPart = "META-INF/manifest.xml";
    public const string MimeTypePart = "mimetype";
    public const string PicturesDirectory = "Pictures/";
}
