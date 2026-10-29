using System.Collections.Generic;
using System.Xml.Linq;
using Broiler.Documents.Packaging;

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
    public static readonly XNamespace Number = "urn:oasis:names:tc:opendocument:xmlns:datastyle:1.0";
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

    /// <summary>The form namespace: the controls a <c>draw:control</c> places on the page.</summary>
    public static readonly XNamespace Form = "urn:oasis:names:tc:opendocument:xmlns:form:1.0";

    /// <summary>The settings namespace: <c>config:config-item</c> in <c>settings.xml</c>.</summary>
    public static readonly XNamespace Config = "urn:oasis:names:tc:opendocument:xmlns:config:1.0";

    /// <summary>
    /// The prefixes LibreOffice declares on the root of every document it
    /// writes, taken as bound when a document uses one without declaring it.
    /// </summary>
    /// <remarks>
    /// Hand-written test documents leave declarations out: LibreOffice's own
    /// <c>tdf142130.fodt</c> uses <c>fo:</c> and <c>style:</c> and declares
    /// neither, and <c>tdf136453.fodt</c> forgets <c>loext:</c>. LibreOffice
    /// reads them as if the declarations were there, and an XML reader refuses
    /// the whole file, so the reader parses once more with these bound.
    /// </remarks>
    public static readonly XmlImpliedPrefixes ImpliedPrefixes = new(
        new Dictionary<string, string>
        {
            ["office"] = Office.NamespaceName,
            ["style"] = Style.NamespaceName,
            ["text"] = Text.NamespaceName,
            ["table"] = Table.NamespaceName,
            ["draw"] = Draw.NamespaceName,
            ["fo"] = Fo.NamespaceName,
            ["xlink"] = XLink.NamespaceName,
            ["dc"] = DublinCore.NamespaceName,
            ["meta"] = Meta.NamespaceName,
            ["number"] = "urn:oasis:names:tc:opendocument:xmlns:datastyle:1.0",
            ["svg"] = Svg.NamespaceName,
            ["chart"] = "urn:oasis:names:tc:opendocument:xmlns:chart:1.0",
            ["dr3d"] = "urn:oasis:names:tc:opendocument:xmlns:dr3d:1.0",
            ["math"] = "http://www.w3.org/1998/Math/MathML",
            ["form"] = "urn:oasis:names:tc:opendocument:xmlns:form:1.0",
            ["script"] = "urn:oasis:names:tc:opendocument:xmlns:script:1.0",
            ["config"] = "urn:oasis:names:tc:opendocument:xmlns:config:1.0",
            ["of"] = "urn:oasis:names:tc:opendocument:xmlns:of:1.2",
            ["ooo"] = "http://openoffice.org/2004/office",
            ["ooow"] = "http://openoffice.org/2004/writer",
            ["oooc"] = "http://openoffice.org/2004/calc",
            ["officeooo"] = "http://openoffice.org/2009/office",
            ["tableooo"] = "http://openoffice.org/2009/table",
            ["drawooo"] = "http://openoffice.org/2010/draw",
            ["rpt"] = "http://openoffice.org/2005/report",
            ["loext"] = "urn:org:documentfoundation:names:experimental:office:xmlns:loext:1.0",
            ["calcext"] = "urn:org:documentfoundation:names:experimental:calc:xmlns:calcext:1.0",
            ["field"] = "urn:openoffice:names:experimental:ooo-ms-interop:xmlns:field:1.0",
            ["formx"] = "urn:openoffice:names:experimental:ooxml-odf-interop:xmlns:form:1.0",
            ["css3t"] = "http://www.w3.org/TR/css3-text/",
            ["dom"] = "http://www.w3.org/2001/xml-events",
            ["xforms"] = "http://www.w3.org/2002/xforms",
            ["xsd"] = "http://www.w3.org/2001/XMLSchema",
            ["xsi"] = "http://www.w3.org/2001/XMLSchema-instance",
            ["xhtml"] = "http://www.w3.org/1999/xhtml",
            ["grddl"] = "http://www.w3.org/2003/g/data-view#",
        },
        "odt.xml.prefix",
        "uses namespace prefixes it does not declare; they were read as the ones LibreOffice declares.");

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
    public const string SettingsPart = "settings.xml";
    public const string ManifestPart = "META-INF/manifest.xml";
    public const string MimeTypePart = "mimetype";
    public const string PicturesDirectory = "Pictures/";
}
