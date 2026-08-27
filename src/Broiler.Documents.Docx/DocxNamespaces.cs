using System.Xml.Linq;

namespace Broiler.Documents.Docx;

internal static class DocxNamespaces
{
    public static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    public static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    public static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static readonly XNamespace Wordprocessing = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    public static readonly XNamespace Xml = "http://www.w3.org/XML/1998/namespace";

    /// <summary>Markup compatibility, used by <c>mc:AlternateContent</c> blocks.</summary>
    public static readonly XNamespace MarkupCompatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    /// <summary>DrawingML, used by the theme part's font scheme and by picture fills.</summary>
    public static readonly XNamespace Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";

    /// <summary>WordprocessingML drawing, the <c>wp:inline</c>/<c>wp:anchor</c> wrapper around a picture.</summary>
    public static readonly XNamespace WordDrawing =
        "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";

    /// <summary>DrawingML picture, the <c>pic:pic</c> shape a drawing embeds.</summary>
    public static readonly XNamespace Picture = "http://schemas.openxmlformats.org/drawingml/2006/picture";

    /// <summary>VML, used by the legacy <c>w:pict</c> image shape.</summary>
    public static readonly XNamespace Vml = "urn:schemas-microsoft-com:vml";

    public const string OfficeDocumentRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";

    public const string HyperlinkRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";

    public const string NumberingRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering";

    public const string StylesRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";

    public const string ThemeRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme";

    public const string HeaderRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/header";

    public const string FooterRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer";

    public const string ImageRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";

    public const string DocumentContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";

    public const string NumberingContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml";

    public const string PackageContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
}
