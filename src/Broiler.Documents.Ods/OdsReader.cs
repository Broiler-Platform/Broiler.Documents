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

            Dictionary<string, double> columnStyleWidths = ReadColumnStyleWidths(contentXml.Root);
            var sheets = new List<SpreadsheetWorksheet>();
            foreach (XElement tableElem in spreadsheet.Elements(OdsNamespaces.Table + "table"))
            {
                string sheetName = (string?)tableElem.Attribute(OdsNamespaces.Table + "name") ?? $"Sheet{sheets.Count + 1}";
                SpreadsheetWorksheet sheet = ReadSheet(sheetName, tableElem, columnStyleWidths);
                sheets.Add(sheet);
            }

            if (sheets.Count == 0)
                sheets.Add(new SpreadsheetWorksheet("Sheet1", []));

            var workbook = new SpreadsheetWorkbook(sheets);
            RichTextDocument document = workbook.ToRichTextDocument();

            return new OdsReadResult(document, workbook, diagnostics, DocumentResultStatus.Success);
        }
    }

    /// <summary>
    /// The <c>style:column-width</c> of each automatic table-column style, in
    /// points, by style name.
    /// </summary>
    private static Dictionary<string, double> ReadColumnStyleWidths(XElement root)
    {
        var widths = new Dictionary<string, double>(StringComparer.Ordinal);
        XElement? automatic = root.Element(OdsNamespaces.Office + "automatic-styles");
        if (automatic is null)
            return widths;

        foreach (XElement style in automatic.Elements(OdsNamespaces.Style + "style"))
        {
            if ((string?)style.Attribute(OdsNamespaces.Style + "family") != "table-column" ||
                (string?)style.Attribute(OdsNamespaces.Style + "name") is not string name)
                continue;

            string? width = (string?)style.Element(OdsNamespaces.Style + "table-column-properties")?
                .Attribute(OdsNamespaces.Style + "column-width");
            if (TryParseLength(width, out double points) && points > 0)
                widths[name] = points;
        }

        return widths;
    }

    /// <summary>An ODF length (in, cm, mm, pt, pc, px) in points.</summary>
    private static bool TryParseLength(string? value, out double points)
    {
        points = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string text = value.Trim();
        int unitStart = text.Length;
        while (unitStart > 0 && char.IsLetter(text[unitStart - 1]))
            unitStart--;

        double scale = text[unitStart..].ToLowerInvariant() switch
        {
            "in" => 72.0,
            "cm" => 72.0 / 2.54,
            "mm" => 72.0 / 25.4,
            "pt" => 1.0,
            "pc" => 12.0,
            "px" => 0.75,
            _ => double.NaN,
        };
        if (double.IsNaN(scale) ||
            !double.TryParse(text[..unitStart], NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ||
            !double.IsFinite(number))
            return false;

        points = number * scale;
        return true;
    }

    private static SpreadsheetWorksheet ReadSheet(string sheetName, XElement tableElem, IReadOnlyDictionary<string, double> columnStyleWidths)
    {
        // Column widths, from each column's automatic style; a column whose style
        // states none keeps one inch. Only the used columns are kept (below):
        // a sheet declares all 1024 of its grid's columns, and LibreOffice prints
        // only the used range.
        var columnWidths = new List<double>();
        foreach (XElement col in tableElem.Elements(OdsNamespaces.Table + "table-column"))
        {
            int repeated = 1;
            if (int.TryParse((string?)col.Attribute(OdsNamespaces.Table + "number-columns-repeated"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r))
                repeated = Math.Clamp(r, 1, 1024);

            string? styleName = (string?)col.Attribute(OdsNamespaces.Table + "style-name");
            double width = styleName is not null && columnStyleWidths.TryGetValue(styleName, out double stated) ? stated : 72.0;
            for (int i = 0; i < repeated && columnWidths.Count < 1024; i++)
                columnWidths.Add(width);
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

                int minCells = hasNonEmptyCell ? 0 : 1;
                while (cells.Count > minCells && cells[^1].Value.IsEmpty && string.IsNullOrEmpty(cells[^1].Formula))
                    cells.RemoveAt(cells.Count - 1);

                if (cells.Count > 0 || hasNonEmptyCell)
                    rows.Add(new SpreadsheetRow(rIdx, cells));
            }
        }

        while (rows.Count > 0 && rows[^1].Cells.All(c => c.Value.IsEmpty && string.IsNullOrEmpty(c.Formula)))
            rows.RemoveAt(rows.Count - 1);

        var sheet = new SpreadsheetWorksheet(sheetName, rows, columnWidths);
        int usedColumns = sheet.ColumnCount;
        if (columnWidths.Count > usedColumns)
        {
            columnWidths.RemoveRange(usedColumns, columnWidths.Count - usedColumns);
            sheet = new SpreadsheetWorksheet(sheetName, rows, columnWidths);
        }

        return sheet;
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
