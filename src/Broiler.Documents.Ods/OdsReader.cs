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
using Broiler.Graphics.Color;

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

            XElement? shared = FindEntry(archive, "styles.xml") is { } stylesEntry
                ? DocumentPackage.LoadEntryXml(stylesEntry, options.Limits.MaxBinBytes, diagnostics,
                    OdsDiagnosticCodes.StylesInvalid, "Styles XML")?.Root
                : null;
            var styles = new Dictionary<(string Family, string Name), XElement>();
            var numberStyles = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (XElement? root in new[] { shared, contentXml.Root })
            {
                foreach (XElement group in root?.Elements().Where(e =>
                    e.Name == OdsNamespaces.Office + "styles" || e.Name == OdsNamespaces.Office + "automatic-styles") ?? [])
                {
                    foreach (XElement definition in group.Elements())
                    {
                        if ((string?)definition.Attribute(OdsNamespaces.Style + "name") is not string name)
                            continue;
                        if (definition.Name.Namespace == OdsNamespaces.Number)
                            numberStyles[name] = OdsNumberStyles.Read(definition);
                        else if (definition.Name == OdsNamespaces.Style + "style" &&
                            (string?)definition.Attribute(OdsNamespaces.Style + "family") is string family)
                            styles[(family, name)] = definition;
                    }
                }
            }

            string? nullDate = (string?)spreadsheet.Element(OdsNamespaces.Table + "calculation-settings")?
                .Element(OdsNamespaces.Table + "null-date")?.Attribute(OdsNamespaces.Table + "date-value");
            if (!DateTime.TryParseExact(nullDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime dateBase))
                dateBase = OdsNumberStyles.DefaultDateBase;

            bool unsupportedFormat = false;
            var sheets = new List<SpreadsheetWorksheet>();
            foreach (XElement tableElem in spreadsheet.Elements(OdsNamespaces.Table + "table"))
            {
                string sheetName = (string?)tableElem.Attribute(OdsNamespaces.Table + "name") ?? $"Sheet{sheets.Count + 1}";
                SpreadsheetWorksheet sheet = ReadSheet(sheetName, tableElem, styles, numberStyles, dateBase, ref unsupportedFormat);
                sheets.Add(sheet);
            }

            if (sheets.Count == 0)
                sheets.Add(new SpreadsheetWorksheet("Sheet1", []));

            var workbook = new SpreadsheetWorkbook(sheets);
            RichTextDocument document = workbook.ToRichTextDocument();

            if (unsupportedFormat)
                diagnostics.Add(DocumentDiagnostic.Warning(OdsDiagnosticCodes.NumberFormatUnsupported,
                    "An unsupported number format was omitted."));
            return new OdsReadResult(document, workbook, diagnostics,
                diagnostics.Count == 0 ? DocumentResultStatus.Success : DocumentResultStatus.Partial);
        }
    }

    private static double? ReadDimension(XElement? style, string family, string property)
    {
        string? length = (string?)style?.Element(OdsNamespaces.Style + (family + "-properties"))?
            .Attribute(OdsNamespaces.Style + property);
        return TryParseLength(length, out double points) && double.IsFinite(points) && points > 0 ? points : null;
    }

    private static bool HasCellContent(SpreadsheetCell cell) =>
        !cell.Value.IsEmpty || !string.IsNullOrEmpty(cell.Formula) || cell.Shading.A != 0 ||
        cell.NumberFormat is not null || cell.Borders.IsVisible || cell.Font != InlineStyle.Default ||
        cell.Alignment != TextAlignment.Left || cell.VerticalAlignment != CellVerticalAlignment.Top || cell.ColumnSpan > 1 || cell.RowSpan > 1;

    /// <summary>An ODF length (in, cm, mm, pt, pc, px) in points.</summary>
    internal static bool TryParseLength(string? value, out double points)
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

    private static SpreadsheetWorksheet ReadSheet(string sheetName, XElement tableElem,
        Dictionary<(string Family, string Name), XElement> styles,
        Dictionary<string, string?> numberStyles, DateTime dateBase, ref bool unsupportedFormat)
    {
        // Column widths, from each column's automatic style; a column whose style
        // states none keeps one inch. Only the used columns are kept (below):
        // a sheet declares all 1024 of its grid's columns, and LibreOffice prints
        // only the used range.
        var columnWidths = new List<double>();
        var columnCellStyles = new List<string?>();
        foreach (XElement col in tableElem.Elements(OdsNamespaces.Table + "table-column"))
        {
            int repeated = 1;
            if (int.TryParse((string?)col.Attribute(OdsNamespaces.Table + "number-columns-repeated"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r))
                repeated = Math.Clamp(r, 1, 1024);

            string? styleName = (string?)col.Attribute(OdsNamespaces.Table + "style-name");
            double width = ReadDimension(styles.GetValueOrDefault(("table-column", styleName ?? string.Empty)),
                "table-column", "column-width") ?? 72.0;
            string? defaultCellStyle = (string?)col.Attribute(OdsNamespaces.Table + "default-cell-style-name");
            for (int i = 0; i < repeated && columnWidths.Count < 1024; i++)
            {
                columnWidths.Add(width);
                columnCellStyles.Add(defaultCellStyle);
            }
        }

        var rows = new List<SpreadsheetRow>();
        int currentRowIndex = 0;

        foreach (XElement rowElem in tableElem.Elements(OdsNamespaces.Table + "table-row"))
        {
            int rowRepeated = 1;
            if (int.TryParse((string?)rowElem.Attribute(OdsNamespaces.Table + "number-rows-repeated"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int rr))
                rowRepeated = Math.Max(1, rr);

            string rowStyle = (string?)rowElem.Attribute(OdsNamespaces.Table + "style-name") ?? string.Empty;
            double? height = ReadDimension(styles.GetValueOrDefault(("table-row", rowStyle)), "table-row", "row-height");
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
                        CellValue projectedValue = cellVal;
                        string cellStyleName = (string?)cElem.Attribute(OdsNamespaces.Table + "style-name") ??
                        (string?)rowElem.Attribute(OdsNamespaces.Table + "default-cell-style-name") ??
                        (cIdx < columnCellStyles.Count ? columnCellStyles[cIdx] : null) ?? string.Empty;
                        XElement? cellStyle = styles.GetValueOrDefault(("table-cell", cellStyleName));
                        string? dataStyleName = (string?)cellStyle?.Attribute(OdsNamespaces.Style + "data-style-name");
                        string? numberFormat = dataStyleName is null ? null : numberStyles.GetValueOrDefault(dataStyleName);
                        unsupportedFormat |= dataStyleName is not null && numberFormat is null;
                        if (OdsNumberStyles.IsDate(numberFormat))
                        {
                        if (projectedValue.IsNumber)
                        {
                            double serial = projectedValue.Number + (dateBase - OdsNumberStyles.DefaultDateBase).TotalDays;
                            projectedValue = CellValue.FromNumber(serial < 61 ? serial - 1 : serial);
                        }
                        else if (valueType == "date" && DateTime.TryParse(dateAttr, CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out DateTime date))
                        {
                            projectedValue = CellValue.FromNumber(CellDateSerial.ToSerial(date));
                        }
                        }

                        OdsCellAppearance appearance = OdsCellAppearance.Read(cellStyle);
                        if (rep > 0 && cellVal.IsEmpty && string.IsNullOrEmpty(formula) &&
                            appearance == default && numberFormat is null)
                            continue; // Avoid storing thousands of repeated trailing empty cells

                        var addr = new CellAddress(rIdx, cIdx);
                        cells.Add(new SpreadsheetCell(
                            addr,
                            projectedValue,
                            formula: formula,
                            cachedText: cachedText,
                            columnSpan: colSpan,
                            rowSpan: rowSpan, numberFormat: numberFormat, shading: appearance.Fill)
                        {
                            Borders = appearance.Borders,
                            VerticalAlignment = appearance.Vertical,
                            Alignment = appearance.Alignment,
                            Font = appearance.Font,
                        });
                    }
                }

                int minCells = hasNonEmptyCell ? 0 : 1;
                while (cells.Count > minCells && !HasCellContent(cells[^1]))
                    cells.RemoveAt(cells.Count - 1);

                if (cells.Count > 0 || hasNonEmptyCell || height.HasValue)
                    rows.Add(new SpreadsheetRow(rIdx, cells, height));
            }
        }

        while (rows.Count > 0 && rows[^1].Height is null && !rows[^1].Cells.Any(HasCellContent))
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
