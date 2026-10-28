using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;
using Broiler.Documents.Packaging;

namespace Broiler.Documents.Ods;

internal static class OdsReader
{
    public static OdsReadResult Read(Stream stream, DocumentReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);

        var diagnostics = new List<DocumentDiagnostic>();

        if (stream.CanSeek && stream.Length > options.Limits.MaxDocumentBytes)
        {
            throw new DocumentLimitExceededException(
                $"Document size {stream.Length} bytes exceeds limit of {options.Limits.MaxDocumentBytes} bytes.");
        }

        ZipArchive archive;
        try
        {
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException ex)
        {
            diagnostics.Add(DocumentDiagnostic.Error(OdsDiagnosticCodes.Corrupt, "Failed to open ODS package: " + ex.Message));
            return new OdsReadResult(RichTextDocument.Empty, new SpreadsheetWorkbook([]), diagnostics, DocumentResultStatus.Rejected);
        }

        using (archive)
        {
            ZipArchiveEntry? contentEntry = FindEntry(archive, "content.xml");
            if (contentEntry is null)
            {
                diagnostics.Add(DocumentDiagnostic.Error(OdsDiagnosticCodes.MissingContent, "Package contains no content.xml entry."));
                return new OdsReadResult(RichTextDocument.Empty, new SpreadsheetWorkbook([]), diagnostics, DocumentResultStatus.Rejected);
            }

            XDocument? contentXml = DocumentPackage.LoadEntryXml(contentEntry, options.Limits.MaxBinBytes, diagnostics, OdsDiagnosticCodes.ContentXmlInvalid, "Content XML");
            if (contentXml?.Root is null)
            {
                return new OdsReadResult(RichTextDocument.Empty, new SpreadsheetWorkbook([]), diagnostics, DocumentResultStatus.Rejected);
            }

            XElement? body = contentXml.Root.Element(OdsNamespaces.Office + "body");
            XElement? spreadsheet = body?.Element(OdsNamespaces.Office + "spreadsheet");
            if (spreadsheet is null)
            {
                diagnostics.Add(DocumentDiagnostic.Error(OdsDiagnosticCodes.MissingSpreadsheet, "content.xml contains no office:spreadsheet element."));
                return new OdsReadResult(RichTextDocument.Empty, new SpreadsheetWorkbook([]), diagnostics, DocumentResultStatus.Rejected);
            }

            var sheets = new List<SpreadsheetWorksheet>();
            foreach (XElement tableElem in spreadsheet.Elements(OdsNamespaces.Table + "table"))
            {
                string sheetName = (string?)tableElem.Attribute(OdsNamespaces.Table + "name") ?? $"Sheet{sheets.Count + 1}";
                SpreadsheetWorksheet sheet = ReadSheet(sheetName, tableElem);
                sheets.Add(sheet);
            }

            if (sheets.Count == 0)
                sheets.Add(new SpreadsheetWorksheet("Sheet1", []));

            var workbook = new SpreadsheetWorkbook(sheets);
            RichTextDocument document = workbook.ToRichTextDocument();

            return new OdsReadResult(document, workbook, diagnostics, DocumentResultStatus.Success);
        }
    }

    private static SpreadsheetWorksheet ReadSheet(string sheetName, XElement tableElem)
    {
        // Column widths
        var columnWidths = new List<double>();
        foreach (XElement col in tableElem.Elements(OdsNamespaces.Table + "table-column"))
        {
            int repeated = 1;
            if (int.TryParse((string?)col.Attribute(OdsNamespaces.Table + "number-columns-repeated"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r))
                repeated = Math.Clamp(r, 1, 1024);

            for (int i = 0; i < repeated; i++)
                columnWidths.Add(72.0); // Default 1 inch if style lookup omitted
        }

        var rows = new List<SpreadsheetRow>();
        int currentRowIndex = 0;

        foreach (XElement rowElem in tableElem.Elements(OdsNamespaces.Table + "table-row"))
        {
            int rowRepeated = 1;
            if (int.TryParse((string?)rowElem.Attribute(OdsNamespaces.Table + "number-rows-repeated"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int rr))
                rowRepeated = Math.Max(1, rr);

            var cellElements = rowElem.Elements().ToList();
            bool hasNonEmptyCell = false;
            foreach (XElement c in cellElements)
            {
                if (c.Name == OdsNamespaces.Table + "table-cell")
                {
                    if (c.Attribute(OdsNamespaces.Office + "value") is not null ||
                        c.Attribute(OdsNamespaces.Office + "boolean-value") is not null ||
                        c.Attribute(OdsNamespaces.Office + "date-value") is not null ||
                        c.Attribute(OdsNamespaces.Table + "formula") is not null ||
                        c.Elements(OdsNamespaces.Text + "p").Any(p => !string.IsNullOrEmpty(p.Value)))
                    {
                        hasNonEmptyCell = true;
                        break;
                    }
                }
            }

            // Cap repeated rows if empty to avoid allocating millions of empty rows
            if (!hasNonEmptyCell && rowRepeated > 10)
                rowRepeated = 1;
            else if (rowRepeated > 256)
                rowRepeated = 256;

            for (int ri = 0; ri < rowRepeated; ri++)
            {
                int rIdx = currentRowIndex++;
                var cells = new List<SpreadsheetCell>();
                int currentColIndex = 0;

                foreach (XElement cElem in cellElements)
                {
                    bool isCovered = cElem.Name == OdsNamespaces.Table + "covered-table-cell";
                    int colRepeated = 1;
                    if (int.TryParse((string?)cElem.Attribute(OdsNamespaces.Table + "number-columns-repeated"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cr))
                        colRepeated = Math.Clamp(cr, 1, 1024);

                    if (isCovered)
                    {
                        currentColIndex += colRepeated;
                        continue;
                    }

                    int colSpan = 1;
                    if (int.TryParse((string?)cElem.Attribute(OdsNamespaces.Table + "number-columns-spanned"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cs))
                        colSpan = Math.Max(1, cs);

                    int rowSpan = 1;
                    if (int.TryParse((string?)cElem.Attribute(OdsNamespaces.Table + "number-rows-spanned"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int rs))
                        rowSpan = Math.Max(1, rs);

                    string? formula = NormalizeOdsFormula((string?)cElem.Attribute(OdsNamespaces.Table + "formula"));
                    string? valueType = (string?)cElem.Attribute(OdsNamespaces.Office + "value-type");
                    string? valueAttr = (string?)cElem.Attribute(OdsNamespaces.Office + "value");
                    string? boolAttr = (string?)cElem.Attribute(OdsNamespaces.Office + "boolean-value");
                    string? dateAttr = (string?)cElem.Attribute(OdsNamespaces.Office + "date-value");

                    var sb = new StringBuilder();
                    foreach (XElement p in cElem.Elements(OdsNamespaces.Text + "p"))
                    {
                        if (sb.Length > 0) sb.Append('\n');
                        sb.Append(p.Value);
                    }
                    string textContent = sb.ToString();

                    CellValue cellVal;
                    string cachedText;

                    if (valueType is "float" or "percentage" or "currency" &&
                        double.TryParse(valueAttr, NumberStyles.Float, CultureInfo.InvariantCulture, out double numVal))
                    {
                        cellVal = CellValue.FromNumber(numVal);
                        cachedText = !string.IsNullOrEmpty(textContent) ? textContent : numVal.ToString(CultureInfo.InvariantCulture);
                    }
                    else if (valueType == "boolean")
                    {
                        bool b = string.Equals(boolAttr, "true", StringComparison.OrdinalIgnoreCase);
                        cellVal = CellValue.FromBoolean(b);
                        cachedText = !string.IsNullOrEmpty(textContent) ? textContent : (b ? "TRUE" : "FALSE");
                    }
                    else if (valueType == "date")
                    {
                        cachedText = !string.IsNullOrEmpty(textContent) ? textContent : (dateAttr ?? string.Empty);
                        cellVal = CellValue.FromText(cachedText);
                    }
                    else
                    {
                        cachedText = textContent;
                        cellVal = string.IsNullOrEmpty(textContent) ? CellValue.Empty : CellValue.FromText(textContent);
                    }

                    for (int rep = 0; rep < colRepeated; rep++)
                    {
                        int cIdx = currentColIndex++;
                        if (rep > 0 && cellVal.IsEmpty && string.IsNullOrEmpty(formula))
                            continue; // Avoid storing thousands of repeated trailing empty cells

                        var addr = new CellAddress(rIdx, cIdx);
                        cells.Add(new SpreadsheetCell(
                            addr,
                            cellVal,
                            formula: formula,
                            cachedText: cachedText,
                            columnSpan: colSpan,
                            rowSpan: rowSpan));
                    }
                }

                if (cells.Count > 0 || hasNonEmptyCell)
                    rows.Add(new SpreadsheetRow(rIdx, cells));
            }
        }

        return new SpreadsheetWorksheet(sheetName, rows, columnWidths);
    }

    private static string? NormalizeOdsFormula(string? formula)
    {
        if (string.IsNullOrWhiteSpace(formula))
            return null;

        string trimmed = formula.Trim();

        // Strip prefix: of:= or oooc:= or msoxl:=
        if (trimmed.StartsWith("of:=", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[4..];
        else if (trimmed.StartsWith("oooc:=", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[6..];
        else if (trimmed.StartsWith("msoxl:=", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[7..];

        // Strip OpenFormula brackets: [.A1:.B2] -> A1:B2, [.A1] -> A1
        trimmed = Regex.Replace(trimmed, @"\[\.([A-Za-z0-9]+)(:\.([A-Za-z0-9]+))?\]", match =>
        {
            if (match.Groups[3].Success)
                return $"{match.Groups[1].Value}:{match.Groups[3].Value}";
            return match.Groups[1].Value;
        });

        if (!trimmed.StartsWith('='))
            trimmed = "=" + trimmed;

        return trimmed;
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string path)
    {
        string normalized = path.TrimStart('/').Replace('\\', '/');
        return archive.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }
}
