namespace Broiler.Documents.Ods;

/// <summary>
/// The diagnostic codes the ODS codec reports.
/// </summary>
public static class OdsDiagnosticCodes
{
    /// <summary>The ODS ZIP package is corrupt or could not be read.</summary>
    public const string Corrupt = "ods.corrupt";

    /// <summary>The package contains no content.xml entry.</summary>
    public const string MissingContent = "ods.missing-content";

    /// <summary>The content.xml entry is invalid or empty.</summary>
    public const string ContentXmlInvalid = "ods.content";

    /// <summary>content.xml contains no office:spreadsheet element.</summary>
    public const string MissingSpreadsheet = "ods.missing-spreadsheet";
}
