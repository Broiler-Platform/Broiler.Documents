using System.Xml.Linq;

namespace Broiler.Documents.Xlsx;

internal static class XlsxNamespaces
{
    public static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    public static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    public static readonly XNamespace Xml = "http://www.w3.org/XML/1998/namespace";

    public const string OfficeDocumentRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    public const string WorksheetRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet";
    public const string SharedStringsRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings";
    public const string StylesRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";

    public const string ContentTypeWorkbook = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
    public const string ContentTypeWorksheet = "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml";
    public const string ContentTypeSharedStrings = "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml";
    public const string ContentTypeStyles = "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml";
}
