using System.Collections.Generic;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;

namespace Broiler.Documents.Xlsx;

/// <summary>
/// The result of reading an Open XML spreadsheet document, carrying both the
/// projected <see cref="RichTextDocument"/> and the strongly-typed <see cref="SpreadsheetWorkbook"/>.
/// </summary>
public sealed class XlsxReadResult : DocumentReadResult
{
    /// <summary>The strongly-typed workbook model extracted from the spreadsheet.</summary>
    public SpreadsheetWorkbook Workbook { get; }

    /// <summary>Initializes a new instance of <see cref="XlsxReadResult"/>.</summary>
    public XlsxReadResult(
        RichTextDocument document,
        SpreadsheetWorkbook workbook,
        IEnumerable<DocumentDiagnostic>? diagnostics = null,
        DocumentResultStatus status = DocumentResultStatus.Success)
        : base(document, diagnostics, status)
    {
        Workbook = workbook ?? new SpreadsheetWorkbook([]);
    }
}
