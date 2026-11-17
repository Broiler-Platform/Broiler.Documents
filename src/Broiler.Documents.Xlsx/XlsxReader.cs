using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;
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
            string? themeTarget = workbookRels.FirstOrDefault(r => r.Type.EndsWith("/theme", StringComparison.OrdinalIgnoreCase))?.Target;
            XElement? theme = themeTarget is not null && FindEntry(archive, themeTarget) is { } themeEntry
                ? DocumentPackage.LoadEntryXml(themeEntry, options.Limits.MaxBinBytes, diagnostics,
                    XlsxDiagnosticCodes.StylesInvalid, "Theme")?.Descendants().FirstOrDefault(e => e.Name.LocalName == "clrScheme")
                : null;
            double digitWidth = 7;
            var customFormats = new Dictionary<int, string>();
            var cellStyles = new List<(int NumberFormatId, BColor Fill, CellVerticalAlignment Vertical, TextAlignment Horizontal, InlineStyle Font, CellBorders Borders, bool WrapText)>();
            var dxfStyles = new List<ConditionalFormatStyle>();
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

                        XNamespace ns = XlsxNamespaces.Spreadsheet;
                        XElement? indexed = stylesXml.Root.Element(ns + "colors")?.Element(ns + "indexedColors");
                        var fills = new List<BColor>();
                        foreach (XElement fill in stylesXml.Root.Element(ns + "fills")?.Elements(ns + "fill") ?? [])
                        {
                            XElement? pattern = fill.Element(ns + "patternFill");
                            fills.Add((string?)pattern?.Attribute("patternType") == "solid"
                                ? XlsxCellAppearance.ReadColor(pattern?.Element(ns + "fgColor"), theme, indexed)
                                : default);
                        }

                        InlineStyle[] fonts = [.. (stylesXml.Root.Element(ns + "fonts")?.Elements(ns + "font") ?? [])
                            .Select(font => XlsxCellAppearance.ReadFont(font, theme, indexed))];
                        CellBorders[] borders = [.. (stylesXml.Root.Element(ns + "borders")?.Elements(ns + "border") ?? [])
                            .Select(border => XlsxCellAppearance.ReadBorders(border, theme, indexed))];

                        string? normalFontId = (string?)stylesXml.Root.Element(ns + "cellStyleXfs")?
                            .Elements(ns + "xf").FirstOrDefault()?.Attribute("fontId");
                        int.TryParse(normalFontId, out int normalFont);
                        if ((uint)normalFont < (uint)fonts.Length && fonts[normalFont].FontSize is float size)
                            digitWidth = Math.Clamp(Math.Round(7d * size / 11), 1, 255);

                        XElement? cellXfs = stylesXml.Root.Element(XlsxNamespaces.Spreadsheet + "cellXfs");
                        if (cellXfs is not null)
                        {
                            foreach (XElement xf in cellXfs.Elements(XlsxNamespaces.Spreadsheet + "xf"))
                            {
                                int numFmtId = 0;
                                if (int.TryParse((string?)xf.Attribute("numFmtId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedId))
                                    numFmtId = parsedId;
                                int.TryParse((string?)xf.Attribute("fillId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int fillId);
                                var alignElem = xf.Element(ns + "alignment");
                                var vertical = (string?)alignElem?.Attribute("vertical") switch
                                {
                                    "center" => CellVerticalAlignment.Center,
                                    "bottom" => CellVerticalAlignment.Bottom,
                                    _ => CellVerticalAlignment.Top,
                                };
                                var horizontal = (string?)alignElem?.Attribute("horizontal") switch
                                {
                                    "center" => TextAlignment.Center,
                                    "right" => TextAlignment.Right,
                                    "justify" => TextAlignment.Justify,
                                    _ => TextAlignment.Left,
                                };
                                bool wrapText = (string?)alignElem?.Attribute("wrapText") is "1" or "true";
                                int.TryParse((string?)xf.Attribute("fontId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int fontId);
                                int.TryParse((string?)xf.Attribute("borderId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int borderId);
                                cellStyles.Add((numFmtId, (uint)fillId < (uint)fills.Count ? fills[fillId] : default, vertical,
                                    horizontal, (uint)fontId < (uint)fonts.Length ? fonts[fontId] : default,
                                    (uint)borderId < (uint)borders.Length ? borders[borderId] : default, wrapText));
                            }
                        }

                        XElement? dxfsElem = stylesXml.Root.Element(ns + "dxfs");
                        if (dxfsElem is not null)
                        {
                            foreach (XElement dxf in dxfsElem.Elements(ns + "dxf"))
                            {
                                bool? bold = null;
                                bool? italic = null;
                                bool? underline = null;
                                bool? strikethrough = null;
                                BColor? foreground = null;
                                XElement? fontElem = dxf.Element(ns + "font");
                                if (fontElem is not null)
                                {
                                    if (fontElem.Element(ns + "b") is { } b)
                                        bold = (string?)b.Attribute("val") is not ("0" or "false");
                                    if (fontElem.Element(ns + "i") is { } i)
                                        italic = (string?)i.Attribute("val") is not ("0" or "false");
                                    if (fontElem.Element(ns + "u") is { } u)
                                        underline = (string?)u.Attribute("val") is not ("0" or "false" or "none");
                                    if (fontElem.Element(ns + "strike") is { } s)
                                        strikethrough = (string?)s.Attribute("val") is not ("0" or "false");
                                    if (fontElem.Element(ns + "color") is { } c)
                                    {
                                        var clr = XlsxCellAppearance.ReadColor(c, theme, indexed);
                                        if (clr.A > 0) foreground = clr;
                                    }
                                }

                                BColor? shading = null;
                                XElement? fillElem = dxf.Element(ns + "fill");
                                if (fillElem is not null)
                                {
                                    XElement? pattern = fillElem.Element(ns + "patternFill");
                                    if (pattern is not null)
                                    {
                                        var fillClr = XlsxCellAppearance.ReadColor(pattern.Element(ns + "bgColor") ?? pattern.Element(ns + "fgColor"), theme, indexed);
                                        if (fillClr.A > 0)
                                            shading = fillClr;
                                    }
                                }

                                CellBorders? dxfBorders = null;
                                XElement? borderElem = dxf.Element(ns + "border");
                                if (borderElem is not null)
                                {
                                    dxfBorders = XlsxCellAppearance.ReadBorders(borderElem, theme, indexed);
                                }

                                dxfStyles.Add(new ConditionalFormatStyle(shading, foreground, bold, italic, underline, strikethrough, dxfBorders));
                            }
                        }
                    }
                }
            }

            // 5. Read sheets listed in workbook.xml
            var sheets = new List<SpreadsheetWorksheet>();
            XElement? sheetsElem = workbookXml.Root.Element(XlsxNamespaces.Spreadsheet + "sheets");
            bool use1904 = (string?)workbookXml.Root.Element(XlsxNamespaces.Spreadsheet + "workbookPr")?
                .Attribute("date1904") is "1" or "true";
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

                    ZipArchiveEntry? commEntry = FindEntry(archive, $"xl/comments{sheets.Count + 1}.xml") ?? FindEntry(archive, $"comments{sheets.Count + 1}.xml");
                    var sheetNotes = new Dictionary<CellAddress, CellNote>();
                    if (commEntry is not null)
                    {
                        XDocument? commentsXml = DocumentPackage.LoadEntryXml(commEntry, options.Limits.MaxBinBytes, diagnostics, XlsxDiagnosticCodes.SheetXmlInvalid, $"Comments {sheetName}");
                        if (commentsXml?.Root is not null)
                        {
                            var authorsList = commentsXml.Root.Element(XlsxNamespaces.Spreadsheet + "authors")?.Elements(XlsxNamespaces.Spreadsheet + "author").Select(a => a.Value).ToList() ?? [];
                            XElement? commentListElem = commentsXml.Root.Element(XlsxNamespaces.Spreadsheet + "commentList");
                            if (commentListElem is not null)
                            {
                                foreach (XElement c in commentListElem.Elements(XlsxNamespaces.Spreadsheet + "comment"))
                                {
                                    string? refAttr = (string?)c.Attribute("ref");
                                    if (!string.IsNullOrEmpty(refAttr) && CellAddress.TryParse(refAttr, out CellAddress addr))
                                    {
                                        int.TryParse((string?)c.Attribute("authorId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int authorId);
                                        string? author = (authorId >= 0 && authorId < authorsList.Count) ? authorsList[authorId] : null;
                                        XElement? textElem = c.Element(XlsxNamespaces.Spreadsheet + "text");
                                        string noteText = textElem is not null ? string.Concat(textElem.Descendants(XlsxNamespaces.Spreadsheet + "t").Select(t => t.Value)) : string.Empty;
                                        sheetNotes[addr] = new CellNote(noteText, author);
                                    }
                                }
                            }
                        }
                    }

                    string sheetBaseDir = GetBaseDirectory(sheetPartTarget);
                    string sheetRelsPath = GetRelationshipsPartPath(sheetPartTarget);
                    var sheetRels = ReadRelationships(archive, sheetRelsPath, sheetBaseDir, options.Limits, diagnostics);

                    var sheetHyperlinks = new Dictionary<CellAddress, CellHyperlink>();
                    XElement? hyperlinksElem = sheetXml.Root.Element(XlsxNamespaces.Spreadsheet + "hyperlinks");
                    if (hyperlinksElem is not null)
                    {
                        foreach (XElement hl in hyperlinksElem.Elements(XlsxNamespaces.Spreadsheet + "hyperlink"))
                        {
                            string? refAttr = (string?)hl.Attribute("ref");
                            if (string.IsNullOrEmpty(refAttr) || !CellAddress.TryParse(refAttr, out CellAddress addr))
                                continue;

                            string? rId = (string?)hl.Attribute(XlsxNamespaces.Relationships + "id")
                                ?? (string?)hl.Attribute("id");
                            string? location = (string?)hl.Attribute("location");
                            string? tooltip = (string?)hl.Attribute("tooltip");
                            string? target = null;
                            if (!string.IsNullOrEmpty(rId))
                            {
                                target = sheetRels.FirstOrDefault(r => r.Id == rId)?.Target;
                            }
                            if (string.IsNullOrEmpty(target) && !string.IsNullOrEmpty(location))
                            {
                                target = location;
                            }
                            if (!string.IsNullOrEmpty(target))
                            {
                                sheetHyperlinks[addr] = new CellHyperlink(target, tooltip);
                            }
                        }
                    }

                    SpreadsheetWorksheet sheet = ReadWorksheet(sheetName, sheetXml.Root, sharedStrings, cellStyles, customFormats, use1904, digitWidth, dxfStyles, sheetNotes, sheetHyperlinks);
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
        IReadOnlyList<(int NumberFormatId, BColor Fill, CellVerticalAlignment Vertical, TextAlignment Horizontal, InlineStyle Font, CellBorders Borders, bool WrapText)> cellStyles,
        IReadOnlyDictionary<int, string> customFormats, bool use1904, double digitWidth,
        IReadOnlyList<ConditionalFormatStyle>? dxfStyles = null,
        IReadOnlyDictionary<CellAddress, CellNote>? sheetNotes = null,
        IReadOnlyDictionary<CellAddress, CellHyperlink>? sheetHyperlinks = null)
    {
        // Parse sheetPr (summary directions)
        bool summaryBelow = true;
        bool summaryRight = true;
        XElement? sheetPr = worksheetRoot.Element(XlsxNamespaces.Spreadsheet + "sheetPr");
        if (sheetPr is not null)
        {
            XElement? outlinePr = sheetPr.Element(XlsxNamespaces.Spreadsheet + "outlinePr");
            if (outlinePr is not null)
            {
                if ((string?)outlinePr.Attribute("summaryBelow") is "0" or "false")
                    summaryBelow = false;
                if ((string?)outlinePr.Attribute("summaryRight") is "0" or "false")
                    summaryRight = false;
            }
        }

        // Parse column widths and outline properties
        var columnWidths = new List<double>();
        var columns = new List<SpreadsheetColumn>();
        XElement? colsElem = worksheetRoot.Element(XlsxNamespaces.Spreadsheet + "cols");
        if (colsElem is not null)
        {
            foreach (XElement col in colsElem.Elements(XlsxNamespaces.Spreadsheet + "col"))
            {
                if (int.TryParse((string?)col.Attribute("min"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int min) &&
                    int.TryParse((string?)col.Attribute("max"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int max))
                {
                    double? ptWidth = null;
                    if (double.TryParse((string?)col.Attribute("width"), NumberStyles.Float, CultureInfo.InvariantCulture, out double width))
                    {
                        ptWidth = XlsxColumnWidth.ToPoints(width, digitWidth);
                        while (columnWidths.Count < max)
                            columnWidths.Add(ptWidth.Value);
                    }

                    bool colHidden = (string?)col.Attribute("hidden") is "1" or "true";
                    int colOutline = 0;
                    if (int.TryParse((string?)col.Attribute("outlineLevel"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedOl))
                        colOutline = parsedOl;
                    bool colCollapsed = (string?)col.Attribute("collapsed") is "1" or "true";

                    for (int c = min - 1; c <= max - 1; c++)
                    {
                        columns.Add(new SpreadsheetColumn(c, ptWidth, colHidden, colOutline, colCollapsed));
                    }
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

                bool hidden = false;
                string? hiddenAttr = (string?)rowElem.Attribute("hidden");
                if (hiddenAttr is "1" or "true")
                    hidden = true;

                int rowOutline = 0;
                if (int.TryParse((string?)rowElem.Attribute("outlineLevel"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedRol))
                    rowOutline = parsedRol;
                bool rowCollapsed = (string?)rowElem.Attribute("collapsed") is "1" or "true";

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

                    var (numFmtId, fill, vertical, horizontal, font, borders, wrapText) = (uint)styleIdx < (uint)cellStyles.Count ? cellStyles[styleIdx] : default;
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
                            if (use1904 && HasCalendarDate(numFmtId, formatCode))
                            {
                                // Normalize to the model's 1900 system before any export.
                                double serial = numVal + 1462;
                                cellVal = CellValue.FromNumber(serial < 61 ? serial - 1 : serial);
                            }
                            DateTime dt = CellDateSerial.FromSerial(cellVal.Number);
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

                    CellHyperlink? hyperlink = sheetHyperlinks is not null && sheetHyperlinks.TryGetValue(cellAddr, out var sh) ? sh : null;
                    if (hyperlink is not null)
                    {
                        if (font == InlineStyle.Default)
                            font = font with { Foreground = new BColor(17, 85, 204), Underline = true, LinkHref = hyperlink.Target };
                        else if (!font.IsLink)
                            font = font with { LinkHref = hyperlink.Target };
                    }

                    cells.Add(new SpreadsheetCell(
                        cellAddr,
                        cellVal,
                        formula: formula,
                        numberFormat: formatCode,
                        cachedText: cachedText,
                        columnSpan: colSpan,
                        rowSpan: rowSpan, shading: fill)
                    {
                        VerticalAlignment = vertical,
                        Alignment = horizontal,
                        WrapText = wrapText,
                        Font = font,
                        Borders = borders,
                        Note = sheetNotes is not null && sheetNotes.TryGetValue(cellAddr, out var sn) ? sn : null,
                        Hyperlink = hyperlink
                    });
                }

                rows.Add(new SpreadsheetRow(rowIndex, cells, height, hidden, rowOutline, rowCollapsed));
            }
        }

        // Parse sheet views (gridlines, freeze panes, split panes)
        bool showGridlines = true;
        int frozenRows = 0;
        int frozenColumns = 0;
        int splitRows = 0;
        int splitColumns = 0;
        XElement? sheetViewsElem = worksheetRoot.Element(XlsxNamespaces.Spreadsheet + "sheetViews");
        if (sheetViewsElem is not null)
        {
            var sheetView = sheetViewsElem.Element(XlsxNamespaces.Spreadsheet + "sheetView");
            if (sheetView is not null)
            {
                string? gridAttr = (string?)sheetView.Attribute("showGridLines");
                if (gridAttr is "0" or "false")
                    showGridlines = false;

                var pane = sheetView.Element(XlsxNamespaces.Spreadsheet + "pane");
                if (pane is not null)
                {
                    int yVal = 0;
                    int xVal = 0;
                    if (int.TryParse((string?)pane.Attribute("ySplit"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int ySplit))
                        yVal = Math.Max(0, ySplit);
                    if (int.TryParse((string?)pane.Attribute("xSplit"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int xSplit))
                        xVal = Math.Max(0, xSplit);

                    string? state = (string?)pane.Attribute("state");
                    if (string.Equals(state, "split", StringComparison.OrdinalIgnoreCase))
                    {
                        splitRows = yVal;
                        splitColumns = xVal;
                    }
                    else
                    {
                        frozenRows = yVal;
                        frozenColumns = xVal;
                    }
                }
            }
        }

        var conditionalFormats = new List<ConditionalFormat>();
        foreach (XElement cfElem in worksheetRoot.Elements(XlsxNamespaces.Spreadsheet + "conditionalFormatting"))
        {
            string? sqref = (string?)cfElem.Attribute("sqref");
            if (string.IsNullOrWhiteSpace(sqref))
                continue;

            var ranges = new List<CellRangeAddress>();
            foreach (string token in sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (CellRangeAddress.TryParse(token, out CellRangeAddress range))
                    ranges.Add(range);
            }

            if (ranges.Count == 0)
                continue;

            var rules = new List<ConditionalFormatRule>();
            foreach (XElement ruleElem in cfElem.Elements(XlsxNamespaces.Spreadsheet + "cfRule"))
            {
                string? typeStr = (string?)ruleElem.Attribute("type");
                string? operatorStr = (string?)ruleElem.Attribute("operator");
                int priority = int.TryParse((string?)ruleElem.Attribute("priority"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int prio) ? prio : 0;
                bool stopIfTrue = (string?)ruleElem.Attribute("stopIfTrue") is "1" or "true";

                ConditionalFormatStyle? ruleStyle = null;
                if (int.TryParse((string?)ruleElem.Attribute("dxfId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int dxfId) &&
                    dxfStyles is not null && dxfId >= 0 && dxfId < dxfStyles.Count)
                {
                    ruleStyle = dxfStyles[dxfId];
                }

                var formulas = ruleElem.Elements(XlsxNamespaces.Spreadsheet + "formula").Select(f => f.Value).ToList();
                string? formula1 = formulas.Count > 0 ? formulas[0] : null;
                string? formula2 = formulas.Count > 1 ? formulas[1] : null;

                var ruleType = ParseRuleType(typeStr);
                var ruleOp = ParseOperator(operatorStr);

                ColorScaleConfiguration? colorScale = null;
                if (ruleType == ConditionalFormatRuleType.ColorScale)
                {
                    XElement? csElem = ruleElem.Element(XlsxNamespaces.Spreadsheet + "colorScale");
                    if (csElem is not null)
                    {
                        var colors = csElem.Elements(XlsxNamespaces.Spreadsheet + "color")
                            .Select(c => XlsxCellAppearance.ReadColor(c))
                            .ToList();
                        if (colors.Count == 2)
                            colorScale = new ColorScaleConfiguration(colors[0], colors[1]);
                        else if (colors.Count >= 3)
                            colorScale = new ColorScaleConfiguration(colors[0], colors[^1], MidColor: colors[1]);
                    }
                }

                string? textAttr = (string?)ruleElem.Attribute("text");
                if (!string.IsNullOrEmpty(textAttr) && (string.IsNullOrEmpty(formula1) || ruleType == ConditionalFormatRuleType.ContainsText || ruleType == ConditionalFormatRuleType.BeginsWith || ruleType == ConditionalFormatRuleType.EndsWith))
                {
                    formula1 = textAttr;
                }

                rules.Add(new ConditionalFormatRule(
                    ruleType,
                    ruleOp,
                    formula1,
                    formula2,
                    ruleStyle,
                    colorScale,
                    priority,
                    stopIfTrue));
            }

            if (rules.Count > 0)
                conditionalFormats.Add(new ConditionalFormat(ranges, rules));
        }

        var dataValidations = new List<DataValidation>();
        XElement? dvsElem = worksheetRoot.Element(XlsxNamespaces.Spreadsheet + "dataValidations");
        if (dvsElem is not null)
        {
            foreach (XElement dvElem in dvsElem.Elements(XlsxNamespaces.Spreadsheet + "dataValidation"))
            {
                string? sqref = (string?)dvElem.Attribute("sqref");
                if (string.IsNullOrWhiteSpace(sqref))
                    continue;

                var ranges = new List<CellRangeAddress>();
                foreach (string token in sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (CellRangeAddress.TryParse(token, out CellRangeAddress range))
                        ranges.Add(range);
                }

                if (ranges.Count == 0)
                    continue;

                DataValidationType type = ParseDataValidationType((string?)dvElem.Attribute("type"));
                DataValidationOperator op = ParseDataValidationOperator((string?)dvElem.Attribute("operator"));
                DataValidationErrorStyle errStyle = ParseDataValidationErrorStyle((string?)dvElem.Attribute("errorStyle"));

                bool allowBlank = (string?)dvElem.Attribute("allowBlank") is "1" or "true";
                bool showDropDown = (string?)dvElem.Attribute("showDropDown") != "1"; // "1" suppresses dropdown
                bool showInput = (string?)dvElem.Attribute("showInputMessage") is "1" or "true";
                bool showError = (string?)dvElem.Attribute("showErrorMessage") is "1" or "true";

                string? inputTitle = (string?)dvElem.Attribute("promptTitle");
                string? inputMsg = (string?)dvElem.Attribute("prompt");
                string? errorTitle = (string?)dvElem.Attribute("errorTitle");
                string? errorMsg = (string?)dvElem.Attribute("error");

                string? f1 = dvElem.Element(XlsxNamespaces.Spreadsheet + "formula1")?.Value;
                string? f2 = dvElem.Element(XlsxNamespaces.Spreadsheet + "formula2")?.Value;

                dataValidations.Add(new DataValidation(
                    ranges,
                    type,
                    op,
                    Formula1: f1,
                    Formula2: f2,
                    AllowBlank: allowBlank,
                    ShowDropdown: showDropDown,
                    ShowInputMessage: showInput,
                    InputTitle: inputTitle,
                    InputMessage: inputMsg,
                    ShowErrorMessage: showError,
                    ErrorTitle: errorTitle,
                    ErrorMessage: errorMsg,
                    ErrorStyle: errStyle));
            }
        }

        SpreadsheetAutoFilter? autoFilter = null;
        XElement? afElem = worksheetRoot.Element(XlsxNamespaces.Spreadsheet + "autoFilter");
        if (afElem is not null)
        {
            string? refAttr = (string?)afElem.Attribute("ref");
            if (!string.IsNullOrEmpty(refAttr) && CellRangeAddress.TryParse(refAttr, out CellRangeAddress afRange))
            {
                var filterColumns = new List<SpreadsheetFilterColumn>();
                foreach (XElement colElem in afElem.Elements(XlsxNamespaces.Spreadsheet + "filterColumn"))
                {
                    if (int.TryParse((string?)colElem.Attribute("colId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int colId))
                    {
                        int absCol = afRange.Start.Column + colId;
                        XElement? filtersElem = colElem.Element(XlsxNamespaces.Spreadsheet + "filters");
                        bool includeBlanks = true;
                        var values = new List<string>();
                        if (filtersElem is not null)
                        {
                            if ((string?)filtersElem.Attribute("blank") is "0" or "false")
                                includeBlanks = false;

                            foreach (XElement f in filtersElem.Elements(XlsxNamespaces.Spreadsheet + "filter"))
                            {
                                string? val = (string?)f.Attribute("val");
                                if (val is not null)
                                    values.Add(val);
                            }
                        }

                        filterColumns.Add(new SpreadsheetFilterColumn(absCol, values, includeBlanks));
                    }
                }

                autoFilter = new SpreadsheetAutoFilter(afRange, filterColumns);
            }
        }

        return new SpreadsheetWorksheet(sheetName, rows, columnWidths)
        {
            Columns = columns.Count > 0 ? columns : null,
            SummaryRowsBelow = summaryBelow,
            SummaryColumnsRight = summaryRight,
            ShowGridlines = showGridlines,
            FrozenRows = frozenRows,
            FrozenColumns = frozenColumns,
            SplitRows = splitRows,
            SplitColumns = splitColumns,
            ConditionalFormats = conditionalFormats.Count > 0 ? conditionalFormats : null,
            DataValidations = dataValidations.Count > 0 ? dataValidations : null,
            AutoFilter = autoFilter
        };
    }

    private static ConditionalFormatRuleType ParseRuleType(string? type) => type switch
    {
        "cellIs" => ConditionalFormatRuleType.CellIs,
        "containsText" => ConditionalFormatRuleType.ContainsText,
        "notContainsText" => ConditionalFormatRuleType.NotContainsText,
        "beginsWith" => ConditionalFormatRuleType.BeginsWith,
        "endsWith" => ConditionalFormatRuleType.EndsWith,
        "expression" => ConditionalFormatRuleType.Expression,
        "duplicateValues" => ConditionalFormatRuleType.DuplicateValues,
        "uniqueValues" => ConditionalFormatRuleType.UniqueValues,
        "containsBlanks" => ConditionalFormatRuleType.Blanks,
        "notContainsBlanks" => ConditionalFormatRuleType.NoBlanks,
        "colorScale" => ConditionalFormatRuleType.ColorScale,
        _ => ConditionalFormatRuleType.CellIs
    };

    private static ConditionalFormatOperator ParseOperator(string? op) => op switch
    {
        "equal" => ConditionalFormatOperator.Equal,
        "notEqual" => ConditionalFormatOperator.NotEqual,
        "greaterThan" => ConditionalFormatOperator.GreaterThan,
        "greaterThanOrEqual" => ConditionalFormatOperator.GreaterThanOrEqual,
        "lessThan" => ConditionalFormatOperator.LessThan,
        "lessThanOrEqual" => ConditionalFormatOperator.LessThanOrEqual,
        "between" => ConditionalFormatOperator.Between,
        "notBetween" => ConditionalFormatOperator.NotBetween,
        _ => ConditionalFormatOperator.None
    };

    private static DataValidationType ParseDataValidationType(string? type) => type switch
    {
        "whole" => DataValidationType.WholeNumber,
        "decimal" => DataValidationType.Decimal,
        "list" => DataValidationType.List,
        "date" => DataValidationType.Date,
        "time" => DataValidationType.Time,
        "textLength" => DataValidationType.TextLength,
        "custom" => DataValidationType.Custom,
        _ => DataValidationType.None
    };

    private static DataValidationOperator ParseDataValidationOperator(string? op) => op switch
    {
        "notBetween" => DataValidationOperator.NotBetween,
        "equal" => DataValidationOperator.Equal,
        "notEqual" => DataValidationOperator.NotEqual,
        "greaterThan" => DataValidationOperator.GreaterThan,
        "lessThan" => DataValidationOperator.LessThan,
        "greaterThanOrEqual" => DataValidationOperator.GreaterThanOrEqual,
        "lessThanOrEqual" => DataValidationOperator.LessThanOrEqual,
        _ => DataValidationOperator.Between
    };

    private static DataValidationErrorStyle ParseDataValidationErrorStyle(string? style) => style switch
    {
        "warning" => DataValidationErrorStyle.Warning,
        "information" => DataValidationErrorStyle.Information,
        _ => DataValidationErrorStyle.Stop
    };

    private static bool HasCalendarDate(int numFmtId, string? formatCode)
    {
        if (numFmtId is >= 14 and <= 17 or 22)
            return true;
        if (string.IsNullOrEmpty(formatCode))
            return false;

        string clean = Regex.Replace(formatCode, "\"[^\"]*\"|\\[[^\\]]*\\]|\\\\.", string.Empty);
        return clean.Contains('y', StringComparison.OrdinalIgnoreCase) ||
            clean.Contains('d', StringComparison.OrdinalIgnoreCase);
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
                string? targetMode = (string?)rel.Attribute("TargetMode");
                bool isExternal = string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase)
                    || target.Contains("://") || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
                string resolved = isExternal ? target : NormalizePath(baseDir, target);
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
