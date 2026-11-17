using System.Xml.Linq;

namespace Broiler.Documents.Ods;

internal static class OdsNamespaces
{
    public static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    public static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    public static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    public static readonly XNamespace Style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    public static readonly XNamespace Fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
    public static readonly XNamespace Number = "urn:oasis:names:tc:opendocument:xmlns:datastyle:1.0";
    public static readonly XNamespace Manifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
    public static readonly XNamespace Config = "urn:oasis:names:tc:opendocument:xmlns:config:1.0";
    public static readonly XNamespace Ooo = "http://openoffice.org/2004/office";
    public static readonly XNamespace Calcext = "urn:org:documentfoundation:names:experimental:calc:xmlns:calcext:1.0";
    public static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    public static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";

    public const string PackageMediaType = "application/vnd.oasis.opendocument.spreadsheet";
    public const string TemplateMediaType = "application/vnd.oasis.opendocument.spreadsheet-template";
}
