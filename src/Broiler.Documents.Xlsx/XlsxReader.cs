using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;
using Broiler.Documents.Packaging;

namespace Broiler.Documents.Xlsx;

internal sealed record XlsxRelationship(string Id, string Type, string Target);

internal static class XlsxReader
{
    private static readonly Dictionary<int, string> BuiltinNumberFormats = new()
    {
        { 0, "General" },
        { 1, "0" },
        { 2, "0.00" },
        { 3, "#,##0" },
        { 4, "#,##0.00" },
        { 9, "0%" },
        { 10, "0.00%" },
        { 11, "0.00E+00" },
        { 12, "# ?/?" },
        { 13, "# ??/??" },
        { 14, "yyyy-mm-dd" },
        { 15, "d-mmm-yy" },
        { 16, "d-mmm" },
        { 17, "mmm-yy" },
        { 18, "h:mm AM/PM" },
        { 19, "h:mm:ss AM/PM" },
        { 20, "h:mm" },
        { 21, "h:mm:ss" },
        { 22, "yyyy-mm-dd hh:mm" },
        { 37, "#,##0 ;(#,##0)" },
        { 38, "#,##0 ;[Red](#,##0)" },
        { 39, "#,##0.00;(#,##0.00)" },
        { 40, "#,##0.00;[Red](#,##0.00)" },
        { 44, "_($* #,##0.00_)" },
        { 45, "mm:ss" },
        { 46, "[h]:mm:ss" },
        { 47, "mm:ss.0" },
        { 48, "##0.0E+0" },
        { 49, "@" }
    };

    public static XlsxReadResult Read(Stream stream, DocumentReadOptions options)
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
            diagnostics.Add(DocumentDiagnostic.Error(XlsxDiagnosticCodes.Corrupt, "Failed to open XLSX package: " + ex.Message));
            return new XlsxReadResult(RichTextDocument.Empty, new SpreadsheetWorkbook([]), diagnostics, DocumentResultStatus.Rejected);
        }

        using (archive)
        {
            // 1. Locate workbook part from _rels/.rels
            string? workbookPartPath = FindWorkbookPartPath(archive);
            if (workbookPartPath is null)
            {
                diagnostics.Add(DocumentDiagnostic.Error(XlsxDiagnosticCodes.MissingWorkbook, "Package contains no workbook part."));
                return new XlsxReadResult(RichTextDocument.Empty, new SpreadsheetWorkbook([]), diagnostics, DocumentResultStatus.Rejected);
            }

            ZipArchiveEntry? workbookEntry = FindEntry(archive, workbookPartPath);
            if (workbookEntry is null)
            {
                diagnostics.Add(DocumentDiagnostic.Error(XlsxDiagnosticCodes.MissingWorkbook, $"Workbook part '{workbookPartPath}' not found in package."));
                return new XlsxReadResult(RichTextDocument.Empty, new SpreadsheetWorkbook([]), diagnostics, DocumentResultStatus.Rejected);
            }

            XDocument? workbookXml = DocumentPackage.LoadEntryXml(workbookEntry, options.Limits.MaxBinBytes, diagnostics, XlsxDiagnosticCodes.WorkbookXmlInvalid, "Workbook XML");
            if (workbookXml?.Root is null)
            {
                return new XlsxReadResult(RichTextDocument.Empty, new SpreadsheetWorkbook([]), diagnostics, DocumentResultStatus.Rejected);
            }

            // 2. Read workbook relationships
            string workbookBaseDir = GetBaseDirectory(workbookPartPath);
            string workbookRelsPath = GetRelationshipsPartPath(workbookPartPath);
            var workbookRels = ReadRelationships(archive, workbookRelsPath, workbookBaseDir, options.Limits, diagnostics);

            // 3. Read shared strings
            var sharedStrings = new List<string>();
            string? sharedStringsRelTarget = workbookRels.FirstOrDefault(r => r.Type.EndsWith("/sharedStrings", StringComparison.OrdinalIgnoreCase))?.Target;
            if (sharedStringsRelTarget is not null)
            {
                ZipArchiveEntry? sstEntry = FindEntry(archive, sharedStringsRelTarget);
                if (sstEntry is not null)
                {
                    XDocument? sstXml = DocumentPackage.LoadEntryXml(sstEntry, options.Limits.MaxBinBytes, diagnostics, XlsxDiagnosticCodes.SharedStringsInvalid, "Shared Strings");
                    if (sstXml?.Root is not null)
                    {
                        foreach (XElement si in sstXml.Root.Elements(XlsxNamespaces.Spreadsheet + "si"))
                        {
                            sharedStrings.Add(ExtractStringFromSi(si));
                        }
                    }
                }
            }

            // 4. Read styles & number formats
            var customFormats = new Dictionary<int, string>();
            var styleToNumFmt = new List<int>();
            string? stylesRelTarget = workbookRels.FirstOrDefault(r => r.Type.EndsWith("/styles", StringComparison.OrdinalIgnoreCase))?.Target;
            if (stylesRelTarget is not null)
            {
                ZipArchiveEntry? stylesEntry = FindEntry(archive, stylesRelTarget);
                if (stylesEntry is not null)
                {
                    XDocument? stylesXml = DocumentPackage.LoadEntryXml(stylesEntry, options.Limits.MaxBinBytes, diagnostics, XlsxDiagnosticCodes.StylesInvalid, "Styles");
                    if (stylesXml?.Root is not null)
                    {
                        XElement? numFmts = stylesXml.Root.Element(XlsxNamespaces.Spreadsheet + "numFmts");
                        if (numFmts is not null)
                        {
                            foreach (XElement nf in numFmts.Elements(XlsxNamespaces.Spreadsheet + "numFmt"))
                            {
                                if (int.TryParse((string?)nf.Attribute("numFmtId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                                {
                                    string? code = (string?)nf.Attribute("formatCode");
                                    if (!string.IsNullOrEmpty(code))
                                        customFormats[id] = code;
                                }
                            }
                        }

                        XElement? cellXfs = stylesXml.Root.Element(XlsxNamespaces.Spreadsheet + "cellXfs");
                        if (cellXfs is not null)
                        {
                            foreach (XElement xf in cellXfs.Elements(XlsxNamespaces.Spreadsheet + "xf"))
                            {
                                int numFmtId = 0;
                                if (int.TryParse((string?)xf.Attribute("numFmtId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedId))
                                    numFmtId = parsedId;
                                styleToNumFmt.Add(numFmtId);
                            }
                        }
                    }
                }
            }

            // 5. Read sheets listed in workbook.xml
            var sheets = new List<SpreadsheetWorksheet>();
            XElement? sheetsElem = workbookXml.Root.Element(XlsxNamespaces.Spreadsheet + "sheets");
            if (sheetsElem is not null)
            {
                foreach (XElement sheetElem in sheetsElem.Elements(XlsxNamespaces.Spreadsheet + "sheet"))
                {
                    string sheetName = (string?)sheetElem.Attribute("name") ?? $"Sheet{sheets.Count + 1}";
                    string? relId = (string?)sheetElem.Attribute(XlsxNamespaces.Relationships + "id");
                    if (string.IsNullOrEmpty(relId))
                        continue;

                    string? sheetPartTarget = workbookRels.FirstOrDefault(r => r.Id == relId)?.Target;
                    if (string.IsNullOrEmpty(sheetPartTarget))
                        continue;

                    ZipArchiveEntry? sheetEntry = FindEntry(archive, sheetPartTarget);
                    if (sheetEntry is null)
                        continue;

                    XDocument? sheetXml = DocumentPackage.LoadEntryXml(sheetEntry, options.Limits.MaxBinBytes, diagnostics, XlsxDiagnosticCodes.SheetXmlInvalid, $"Worksheet {sheetName}");
                    if (sheetXml?.Root is null)
                        continue;

                    SpreadsheetWorksheet sheet = ReadWorksheet(sheetName, sheetXml.Root, sharedStrings, styleToNumFmt, customFormats);
                    sheets.Add(sheet);
                }
            }

            if (sheets.Count == 0)
                sheets.Add(new SpreadsheetWorksheet("Sheet1", []));

            var workbook = new SpreadsheetWorkbook(sheets);
            RichTextDocument document = workbook.ToRichTextDocument();

            return new XlsxReadResult(document, workbook, diagnostics, DocumentResultStatus.Success);
        }
    }

    private static SpreadsheetWorksheet ReadWorksheet(
        string sheetName,
        XElement worksheetRoot,
        IReadOnlyList<string> sharedStrings,
        IReadOnlyList<int> styleToNumFmt,
        IReadOnlyDictionary<int, string> customFormats)
    {
        // Parse column widths
        var columnWidths = new List<double>();
        XElement? colsElem = worksheetRoot.Element(XlsxNamespaces.Spreadsheet + "cols");
        if (colsElem is not null)
        {
            foreach (XElement col in colsElem.Elements(XlsxNamespaces.Spreadsheet + "col"))
            {
                if (int.TryParse((string?)col.Attribute("min"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int min) &&
                    int.TryParse((string?)col.Attribute("max"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int max) &&
                    double.TryParse((string?)col.Attribute("width"), NumberStyles.Float, CultureInfo.InvariantCulture, out double width))
                {
                    double ptWidth = Math.Round(width * 7.5, 2);
                    while (columnWidths.Count < max)
                        columnWidths.Add(ptWidth);
                }
            }
        }

        // Parse merged cells
        var mergeMap = new Dictionary<CellAddress, (int ColSpan, int RowSpan)>();
        var coveredCells = new HashSet<CellAddress>();
        XElement? mergeCellsElem = worksheetRoot.Element(XlsxNamespaces.Spreadsheet + "mergeCells");
        if (mergeCellsElem is not null)
        {
            foreach (XElement mergeCell in mergeCellsElem.Elements(XlsxNamespaces.Spreadsheet + "mergeCell"))
            {
                string? refAttr = (string?)mergeCell.Attribute("ref");
                if (!string.IsNullOrEmpty(refAttr) && CellRangeAddress.TryParse(refAttr, out CellRangeAddress range))
                {
                    var topLeft = new CellAddress(range.StartRow, range.StartCol);
                    int colSpan = range.EndCol - range.StartCol + 1;
                    int rowSpan = range.EndRow - range.StartRow + 1;
                    mergeMap[topLeft] = (colSpan, rowSpan);

                    for (int r = range.StartRow; r <= range.EndRow; r++)
                    {
                        for (int c = range.StartCol; c <= range.EndCol; c++)
                        {
                            if (r != range.StartRow || c != range.StartCol)
                                coveredCells.Add(new CellAddress(r, c));
                        }
                    }
                }
            }
        }

        // Parse rows and cells
        var rows = new List<SpreadsheetRow>();
        XElement? sheetData = worksheetRoot.Element(XlsxNamespaces.Spreadsheet + "sheetData");
        if (sheetData is not null)
        {
            int currentSeqRow = 0;
            foreach (XElement rowElem in sheetData.Elements(XlsxNamespaces.Spreadsheet + "row"))
            {
                int rowIndex = currentSeqRow;
                if (int.TryParse((string?)rowElem.Attribute("r"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedRow))
                    rowIndex = parsedRow - 1;
                currentSeqRow = rowIndex + 1;

                double? height = null;
                if (double.TryParse((string?)rowElem.Attribute("ht"), NumberStyles.Float, CultureInfo.InvariantCulture, out double h))
                    height = h;

                var cells = new List<SpreadsheetCell>();
                int currentSeqCol = 0;

                foreach (XElement cElem in rowElem.Elements(XlsxNamespaces.Spreadsheet + "c"))
                {
                    int colIndex = currentSeqCol;
                    string? rAttr = (string?)cElem.Attribute("r");
                    if (!string.IsNullOrEmpty(rAttr) && CellAddress.TryParse(rAttr, out CellAddress addr))
                    {
                        colIndex = addr.Column;
                    }
                    currentSeqCol = colIndex + 1;

                    var cellAddr = new CellAddress(rowIndex, colIndex);
                    if (coveredCells.Contains(cellAddr))
                        continue;

                    string? formula = null;
                    XElement? fElem = cElem.Element(XlsxNamespaces.Spreadsheet + "f");
                    if (fElem is not null && !string.IsNullOrWhiteSpace(fElem.Value))
                    {
                        string fVal = fElem.Value.Trim();
                        formula = fVal.StartsWith('=') ? fVal : "=" + fVal;
                    }

                    string? typeAttr = (string?)cElem.Attribute("t");
                    string? valStr = cElem.Element(XlsxNamespaces.Spreadsheet + "v")?.Value;

                    int styleIdx = 0;
                    if (int.TryParse((string?)cElem.Attribute("s"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int s))
                        styleIdx = s;

                    int numFmtId = styleIdx < styleToNumFmt.Count ? styleToNumFmt[styleIdx] : 0;
                    string? formatCode = customFormats.TryGetValue(numFmtId, out string? customCode)
                        ? customCode
                        : (BuiltinNumberFormats.TryGetValue(numFmtId, out string? builtin) ? builtin : null);

                    CellValue cellVal;
                    string cachedText;

                    if (typeAttr == "s" && int.TryParse(valStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sstIdx) && sstIdx >= 0 && sstIdx < sharedStrings.Count)
                    {
                        string strVal = sharedStrings[sstIdx];
                        cellVal = CellValue.FromText(strVal);
                        cachedText = strVal;
                    }
                    else if (typeAttr == "inlineStr")
                    {
                        XElement? isElem = cElem.Element(XlsxNamespaces.Spreadsheet + "is");
                        string strVal = isElem is not null ? ExtractStringFromSi(isElem) : string.Empty;
                        cellVal = CellValue.FromText(strVal);
                        cachedText = strVal;
                    }
                    else if (typeAttr == "b")
                    {
                        bool b = valStr == "1" || string.Equals(valStr, "true", StringComparison.OrdinalIgnoreCase);
                        cellVal = CellValue.FromBoolean(b);
                        cachedText = b ? "TRUE" : "FALSE";
                    }
                    else if (typeAttr == "e")
                    {
                        FormulaError err = FormulaError.Syntax;
                        if (!string.IsNullOrEmpty(valStr))
                            FormulaErrorExtensions.TryParse(valStr, out err);
                        cellVal = CellValue.FromError(err);
                        cachedText = valStr ?? "#ERROR!";
                    }
                    else if (typeAttr == "str")
                    {
                        string strVal = valStr ?? string.Empty;
                        cellVal = CellValue.FromText(strVal);
                        cachedText = strVal;
                    }
                    else if (!string.IsNullOrEmpty(valStr) && double.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double numVal))
                    {
                        cellVal = CellValue.FromNumber(numVal);
                        if (IsDateFormat(numFmtId, formatCode))
                        {
                            DateTime dt = CellDateSerial.FromSerial(numVal);
                            cachedText = dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            cachedText = valStr;
                        }
                    }
                    else
                    {
                        cellVal = string.IsNullOrEmpty(valStr) ? CellValue.Empty : CellValue.FromText(valStr);
                        cachedText = valStr ?? string.Empty;
                    }

                    int colSpan = 1;
                    int rowSpan = 1;
                    if (mergeMap.TryGetValue(cellAddr, out var span))
                    {
                        colSpan = span.ColSpan;
                        rowSpan = span.RowSpan;
                    }

                    cells.Add(new SpreadsheetCell(
                        cellAddr,
                        cellVal,
                        formula: formula,
                        numberFormat: formatCode,
                        cachedText: cachedText,
                        columnSpan: colSpan,
                        rowSpan: rowSpan));
                }

                rows.Add(new SpreadsheetRow(rowIndex, cells, height));
            }
        }

        return new SpreadsheetWorksheet(sheetName, rows, columnWidths);
    }

    private static bool IsDateFormat(int numFmtId, string? formatCode)
    {
        if (numFmtId is >= 14 and <= 22 or >= 45 and <= 47)
            return true;

        if (string.IsNullOrEmpty(formatCode))
            return false;

        string clean = Regex.Replace(formatCode, "\"[^\"]*\"|\\[[^\\]]*\\]", string.Empty);
        return clean.Contains('y', StringComparison.OrdinalIgnoreCase) ||
               clean.Contains('d', StringComparison.OrdinalIgnoreCase) ||
               clean.Contains('h', StringComparison.OrdinalIgnoreCase) ||
               clean.Contains('s', StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractStringFromSi(XElement si)
    {
        XElement? t = si.Element(XlsxNamespaces.Spreadsheet + "t");
        if (t is not null)
            return t.Value;

        var sb = new System.Text.StringBuilder();
        foreach (XElement r in si.Elements(XlsxNamespaces.Spreadsheet + "r"))
        {
            XElement? rt = r.Element(XlsxNamespaces.Spreadsheet + "t");
            if (rt is not null)
                sb.Append(rt.Value);
        }
        return sb.ToString();
    }

    private static string? FindWorkbookPartPath(ZipArchive archive)
    {
        ZipArchiveEntry? rootRelsEntry = FindEntry(archive, "_rels/.rels");
        if (rootRelsEntry is not null)
        {
            using Stream s = rootRelsEntry.Open();
            XDocument relsDoc = XDocument.Load(s);
            if (relsDoc.Root is not null)
            {
                foreach (XElement rel in relsDoc.Root.Elements(XlsxNamespaces.PackageRelationships + "Relationship"))
                {
                    string? type = (string?)rel.Attribute("Type");
                    if (type == XlsxNamespaces.OfficeDocumentRelationship)
                    {
                        string? target = (string?)rel.Attribute("Target");
                        if (!string.IsNullOrEmpty(target))
                            return target.TrimStart('/');
                    }
                }
            }
        }

        if (FindEntry(archive, "xl/workbook.xml") is not null)
            return "xl/workbook.xml";

        return null;
    }

    private static List<XlsxRelationship> ReadRelationships(
        ZipArchive archive,
        string relsPath,
        string baseDir,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics)
    {
        var result = new List<XlsxRelationship>();
        ZipArchiveEntry? entry = FindEntry(archive, relsPath);
        if (entry is null)
            return result;

        XDocument? relsXml = DocumentPackage.LoadEntryXml(entry, limits.MaxBinBytes, diagnostics, XlsxDiagnosticCodes.RelationshipsInvalid, "Relationships");
        if (relsXml?.Root is null)
            return result;

        foreach (XElement rel in relsXml.Root.Elements(XlsxNamespaces.PackageRelationships + "Relationship"))
        {
            string? id = (string?)rel.Attribute("Id");
            string? type = (string?)rel.Attribute("Type");
            string? target = (string?)rel.Attribute("Target");

            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(target))
            {
                string resolved = NormalizePath(baseDir, target);
                result.Add(new XlsxRelationship(id, type, resolved));
            }
        }

        return result;
    }

    private static string NormalizePath(string baseDir, string target)
    {
        target = target.Replace('\\', '/');
        if (target.StartsWith('/'))
            return target.TrimStart('/');

        if (string.IsNullOrEmpty(baseDir))
            return target;

        string combined = baseDir.TrimEnd('/') + "/" + target;
        string[] parts = combined.Split('/');
        var stack = new List<string>();
        foreach (string part in parts)
        {
            if (part is "" or ".")
                continue;
            if (part == "..")
            {
                if (stack.Count > 0)
                    stack.RemoveAt(stack.Count - 1);
            }
            else
            {
                stack.Add(part);
            }
        }
        return string.Join('/', stack);
    }

    private static string GetBaseDirectory(string partPath)
    {
        string normalized = partPath.TrimStart('/').Replace('\\', '/');
        int slash = normalized.LastIndexOf('/');
        return slash < 0 ? string.Empty : normalized[..slash];
    }

    private static string GetRelationshipsPartPath(string partPath)
    {
        string normalized = partPath.TrimStart('/').Replace('\\', '/');
        int slash = normalized.LastIndexOf('/');
        if (slash < 0)
            return "_rels/" + normalized + ".rels";
        return normalized[..slash] + "/_rels/" + normalized[(slash + 1)..] + ".rels";
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string path)
    {
        string normalized = path.TrimStart('/').Replace('\\', '/');
        return archive.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }
}
