namespace Broiler.Documents.Xlsx;

/// <summary>
/// The diagnostic codes the XLSX codec reports.
/// </summary>
public static class XlsxDiagnosticCodes
{
    /// <summary>The XLSX ZIP package is corrupt or could not be read.</summary>
    public const string Corrupt = "xlsx.corrupt";

    /// <summary>The package contains no workbook part.</summary>
    public const string MissingWorkbook = "xlsx.missing-workbook";

    /// <summary>The workbook XML part is invalid or empty.</summary>
    public const string WorkbookXmlInvalid = "xlsx.xml";

    /// <summary>The shared strings part is invalid or empty.</summary>
    public const string SharedStringsInvalid = "xlsx.sst";

    /// <summary>The styles part is invalid or empty.</summary>
    public const string StylesInvalid = "xlsx.styles";

    /// <summary>A worksheet XML part is invalid or empty.</summary>
    public const string SheetXmlInvalid = "xlsx.sheet";

    /// <summary>A package relationships part is invalid or empty.</summary>
    public const string RelationshipsInvalid = "xlsx.rels";
}
