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

    /// <summary>A control character that XML cannot represent was dropped.</summary>
    public const string ControlCharacterDropped = "ods.text.control";
    /// <summary>A cell number format could not be translated to or from ODF.</summary>
    public const string NumberFormatUnsupported = "ods.number-format";

    /// <summary>The optional styles.xml entry could not be read.</summary>
    public const string StylesInvalid = "ods.styles";
}
