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

            var cellStyles = new OdsCellStyles(shared, contentXml.Root, styles);
            bool unsupportedFormat = false;

            var validationsByName = new Dictionary<string, OdsValidationDef>();
            var cvsContainer = spreadsheet.Element(OdsNamespaces.Table + "content-validations");
            if (cvsContainer is not null)
            {
                foreach (XElement cvElem in cvsContainer.Elements(OdsNamespaces.Table + "content-validation"))
                {
                    string? name = (string?)cvElem.Attribute(OdsNamespaces.Table + "name");
                    if (string.IsNullOrEmpty(name))
                        continue;

                    string? cond = (string?)cvElem.Attribute(OdsNamespaces.Table + "condition");
                    var (valType, op, f1, f2) = ParseOdsValidationCondition(cond);

                    bool allowBlank = (string?)cvElem.Attribute(OdsNamespaces.Table + "allow-empty-cell") != "false";
                    bool showDropdown = (string?)cvElem.Attribute(OdsNamespaces.Table + "display-list") != "none";

                    XElement? helpElem = cvElem.Element(OdsNamespaces.Table + "help-message");
                    bool showInput = (string?)helpElem?.Attribute(OdsNamespaces.Table + "display") == "true";
                    string? inputTitle = (string?)helpElem?.Attribute(OdsNamespaces.Table + "title");
                    string? inputMsg = helpElem?.Element(OdsNamespaces.Text + "p")?.Value;

                    XElement? errElem = cvElem.Element(OdsNamespaces.Table + "error-message");
                    bool showError = (string?)errElem?.Attribute(OdsNamespaces.Table + "display") == "true";
                    string? errorTitle = (string?)errElem?.Attribute(OdsNamespaces.Table + "title");
                    string? errorMsg = errElem?.Element(OdsNamespaces.Text + "p")?.Value;
                    DataValidationErrorStyle errStyle = (string?)errElem?.Attribute(OdsNamespaces.Table + "message-type") switch
                    {
                        "warning" => DataValidationErrorStyle.Warning,
                        "information" => DataValidationErrorStyle.Information,
                        _ => DataValidationErrorStyle.Stop
                    };

                    var def = new OdsValidationDef
                    {
                        Name = name,
                        Type = valType,
                        Operator = op,
                        Formula1 = f1,
                        Formula2 = f2,
                        AllowBlank = allowBlank,
                        ShowDropdown = showDropdown,
                        ShowInputMessage = showInput,
                        InputTitle = inputTitle,
                        InputMessage = inputMsg,
                        ShowErrorMessage = showError,
                        ErrorTitle = errorTitle,
                        ErrorMessage = errorMsg,
                        ErrorStyle = errStyle
                    };

                    string? targetRange = (string?)cvElem.Attribute(OdsNamespaces.Calcext + "target-range-address");
                    if (!string.IsNullOrWhiteSpace(targetRange))
                    {
                        foreach (string token in targetRange.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            string rangeToken = token;
                            int dotIdx = rangeToken.IndexOf('.');
                            if (dotIdx >= 0) rangeToken = rangeToken.Substring(dotIdx + 1);
                            if (CellRangeAddress.TryParse(rangeToken, out var r))
                                def.Ranges.Add(r);
                        }
                    }

                    validationsByName[name] = def;
                }
            }

            var sheets = new List<SpreadsheetWorksheet>();
            var tableStyles = new List<XElement?>();
            foreach (XElement tableElem in spreadsheet.Elements(OdsNamespaces.Table + "table"))
            {
                string sheetName = (string?)tableElem.Attribute(OdsNamespaces.Table + "name") ?? $"Sheet{sheets.Count + 1}";
                SpreadsheetWorksheet sheet = ReadSheet(sheetName, tableElem, styles, cellStyles, numberStyles, dateBase, ref unsupportedFormat, validationsByName);
                sheets.Add(sheet);
                tableStyles.Add(styles.GetValueOrDefault(("table", (string?)tableElem.Attribute(OdsNamespaces.Table + "style-name") ?? string.Empty)));
            }

            var dbRangesElem = spreadsheet.Element(OdsNamespaces.Table + "database-ranges");
            if (dbRangesElem is not null)
            {
                foreach (XElement dbRange in dbRangesElem.Elements(OdsNamespaces.Table + "database-range"))
                {
                    string? target = (string?)dbRange.Attribute(OdsNamespaces.Table + "target-range-address");
                    if (string.IsNullOrEmpty(target))
                        continue;

                    string sheetPart = string.Empty;
                    string rangePart = target;
                    int dotIdx = target.IndexOf('.');
                    if (dotIdx >= 0)
                    {
                        sheetPart = target[..dotIdx];
                        int colonIdx = target.IndexOf(':');
                        if (colonIdx > dotIdx)
                        {
                            string startA1 = target[(dotIdx + 1)..colonIdx];
                            int secondDot = target.IndexOf('.', colonIdx);
                            string endA1 = secondDot >= 0 ? target[(secondDot + 1)..] : target[(colonIdx + 1)..];
                            rangePart = $"{startA1}:{endA1}";
                        }
                        else
                        {
                            rangePart = target[(dotIdx + 1)..];
                        }
                    }

                    if (!CellRangeAddress.TryParse(rangePart, out CellRangeAddress afRange))
                        continue;

                    var filterCols = new List<SpreadsheetFilterColumn>();
                    XElement? filterElem = dbRange.Element(OdsNamespaces.Table + "filter");
                    if (filterElem is not null)
                    {
                        var colsMap = new Dictionary<int, List<string>>();
                        foreach (XElement cond in filterElem.Elements(OdsNamespaces.Table + "filter-condition"))
                        {
                            if (int.TryParse((string?)cond.Attribute(OdsNamespaces.Table + "field-number"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int fieldNum))
                            {
                                int absCol = afRange.Start.Column + fieldNum;
                                string? val = (string?)cond.Attribute(OdsNamespaces.Table + "value");
                                if (val is not null)
                                {
                                    if (!colsMap.TryGetValue(absCol, out var list))
                                        colsMap[absCol] = list = [];
                                    list.Add(val);
                                }
                            }
                        }
                        foreach (var kvp in colsMap)
                            filterCols.Add(new SpreadsheetFilterColumn(kvp.Key, kvp.Value));
                    }

                    var autoFilter = new SpreadsheetAutoFilter(afRange, filterCols);

                    for (int i = 0; i < sheets.Count; i++)
                    {
                        if (string.IsNullOrEmpty(sheetPart) || string.Equals(sheets[i].Name, sheetPart, StringComparison.OrdinalIgnoreCase))
                        {
                            var s = sheets[i];
                            sheets[i] = new SpreadsheetWorksheet(s.Name, s.Rows, s.ColumnWidths)
                            {
                                Columns = s.Columns,
                                SummaryRowsBelow = s.SummaryRowsBelow,
                                SummaryColumnsRight = s.SummaryColumnsRight,
                                ShowGridlines = s.ShowGridlines,
                                FrozenRows = s.FrozenRows,
                                FrozenColumns = s.FrozenColumns,
                                SplitRows = s.SplitRows,
                                SplitColumns = s.SplitColumns,
                                ConditionalFormats = s.ConditionalFormats,
                                DataValidations = s.DataValidations,
                                AutoFilter = autoFilter
                            };
                            break;
                        }
                    }
                }
            }

            // Read settings.xml for view state (ShowGrid, frozen rows/columns)
            if (FindEntry(archive, "settings.xml") is { } settingsEntry &&
                DocumentPackage.LoadEntryXml(settingsEntry, options.Limits.MaxBinBytes, diagnostics,
                    "ODS_SETTINGS_INVALID", "Settings XML")?.Root is { } settingsRoot)
            {
                var tableMap = settingsRoot.Descendants(OdsNamespaces.Config + "config-item-map-named")
                    .FirstOrDefault(e => (string?)e.Attribute(OdsNamespaces.Config + "name") == "Tables");
                if (tableMap is not null)
                {
                    for (int i = 0; i < sheets.Count; i++)
                    {
                        var s = sheets[i];
                        var sheetEntry = tableMap.Elements(OdsNamespaces.Config + "config-item-map-entry")
                            .FirstOrDefault(e => (string?)e.Attribute(OdsNamespaces.Config + "name") == s.Name);
                        if (sheetEntry is not null)
                        {
                            bool showGrid = true;
                            int vSplitPos = 0;
                            int hSplitPos = 0;
                            short vSplitMode = 2;
                            short hSplitMode = 2;
                            foreach (var item in sheetEntry.Elements(OdsNamespaces.Config + "config-item"))
                            {
                                string? name = (string?)item.Attribute(OdsNamespaces.Config + "name");
                                if (name == "ShowGrid" && bool.TryParse(item.Value, out bool sg))
                                    showGrid = sg;
                                else if (name == "VerticalSplitMode" && short.TryParse(item.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out short vm))
                                    vSplitMode = vm;
                                else if (name == "HorizontalSplitMode" && short.TryParse(item.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out short hm))
                                    hSplitMode = hm;
                                else if (name == "VerticalSplitPosition" && int.TryParse(item.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                                    vSplitPos = Math.Max(0, v);
                                else if (name == "HorizontalSplitPosition" && int.TryParse(item.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h))
                                    hSplitPos = Math.Max(0, h);
                            }
                            int frozenRows = vSplitMode == 2 ? vSplitPos : 0;
                            int splitRows = vSplitMode == 1 ? vSplitPos : 0;
                            int frozenCols = hSplitMode == 2 ? hSplitPos : 0;
                            int splitCols = hSplitMode == 1 ? hSplitPos : 0;
                            sheets[i] = new SpreadsheetWorksheet(s.Name, s.Rows, s.ColumnWidths)
                            {
                                Columns = s.Columns,
                                SummaryRowsBelow = s.SummaryRowsBelow,
                                SummaryColumnsRight = s.SummaryColumnsRight,
                                ShowGridlines = showGrid,
                                FrozenRows = frozenRows,
                                FrozenColumns = frozenCols,
                                SplitRows = splitRows,
                                SplitColumns = splitCols,
                                ConditionalFormats = s.ConditionalFormats,
                                DataValidations = s.DataValidations,
                                AutoFilter = s.AutoFilter,
                            };
                        }
                    }
                }
            }

            if (sheets.Count == 0)
                sheets.Add(new SpreadsheetWorksheet("Sheet1", []));

            var workbook = new SpreadsheetWorkbook(sheets);
            RichTextDocument document = Printed(workbook.ToRichTextDocument(), workbook, tableStyles,
                new OdsPrintSetup(shared, contentXml.Root), cellStyles, ReadTitle(archive, options));

            if (unsupportedFormat)
                diagnostics.Add(DocumentDiagnostic.Warning(OdsDiagnosticCodes.NumberFormatUnsupported,
                    "An unsupported number format was omitted."));
            return new OdsReadResult(document, workbook, diagnostics,
                diagnostics.Count == 0 ? DocumentResultStatus.Success : DocumentResultStatus.Partial);
        }
    }

    /// <summary>
    /// The projected workbook as Calc prints it: each sheet on the page its
    /// page style states, with that style's header and footer, its text in the
    /// cell defaults Calc's pool states (<c>style:default-style</c>, else
    /// Liberation Sans at 10 pt), and its tables printed as sheets
    /// (<see cref="DocumentStyleDefaults.PrintsSheets"/>). A sheet after the
    /// first starts a page, as Calc starts one for every sheet.
    /// </summary>
    private static RichTextDocument Printed(RichTextDocument document, SpreadsheetWorkbook workbook,
        IReadOnlyList<XElement?> tableStyles, OdsPrintSetup print, OdsCellStyles cellStyles, string? title)
    {
        var pages = new List<(PageGeometry Page, RunningContent Running)>();
        for (int i = 0; i < workbook.Sheets.Count; i++)
            pages.Add(print.For(i < tableStyles.Count ? tableStyles[i] : null, workbook.Sheets[i].Name, title));

        document = document
            .WithPageGeometry(pages[0].Page)
            .WithRunningContent(pages[0].Running)
            .WithStyleDefaults(document.StyleDefaults with
            {
                FontFamily = cellStyles.DefaultFontFamily,
                FontSizePoints = cellStyles.DefaultFontSize,
                PrintsSheets = true,
            });

        if (document.Tables.Count == workbook.Sheets.Count && workbook.Sheets.Count > 1)
        {
            var sections = new List<DocumentSection>(document.Tables.Count);
            for (int i = 0; i < document.Tables.Count; i++)
            {
                DocumentTable table = document.Tables[i];
                sections.Add(new DocumentSection(table.ParagraphIndex, table.ParagraphCount, 1, 0, pages[i].Page, pages[i].Running,
                    breakType: i == 0 ? SectionBreakType.Continuous : SectionBreakType.NextPage));
            }

            document = document.WithSections(sections);
        }

        return document;
    }

    /// <summary>The document's title (<c>dc:title</c> in <c>meta.xml</c>), for the header field that shows it.</summary>
    private static string? ReadTitle(ZipArchive archive, DocumentReadOptions options)
    {
        if (FindEntry(archive, "meta.xml") is not { } entry)
            return null;

        XDocument? meta = DocumentPackage.LoadEntryXml(entry, options.Limits.MaxBinBytes, [],
            OdsDiagnosticCodes.StylesInvalid, "Meta XML");
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        return meta?.Root?.Descendants(dc + "title").FirstOrDefault()?.Value;
    }

    private static double? ReadDimension(XElement? style, string family, string property)
    {
        string? length = (string?)style?.Element(OdsNamespaces.Style + (family + "-properties"))?
            .Attribute(OdsNamespaces.Style + property);
        return TryParseLength(length, out double points) && double.IsFinite(points) && points > 0 ? points : null;
    }

    /// <summary>
    /// Whether a cell widens the sheet's used range: a value, a formula, a span,
    /// or a visible attribute (fill or border). A number format, font or
    /// alignment alone does not, wherever it comes from: Calc writes every grid
    /// column with the <c>Default</c> cell style, and LibreOffice prints and
    /// exports only the range holding content or visible attributes
    /// (<c>ScTable::GetPrintArea</c>, <c>ScPatternAttr::IsVisible</c>).
    /// </summary>
    private static bool MarksUsedRange(SpreadsheetCell cell) =>
        !cell.Value.IsEmpty || !string.IsNullOrEmpty(cell.Formula) || cell.Shading.A != 0 ||
        cell.Borders.IsVisible || cell.ColumnSpan > 1 || cell.RowSpan > 1 || cell.Note is not null;

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
        Dictionary<(string Family, string Name), XElement> styles, OdsCellStyles cellStyles,
        Dictionary<string, string?> numberStyles, DateTime dateBase, ref bool unsupportedFormat,
        Dictionary<string, OdsValidationDef>? validationsByName = null)
    {
        bool localUnsupportedFormat = false;
        var referencedValidations = new HashSet<string>();
        // Column widths and outlines
        var columnWidths = new List<double>();
        var columnCellStyles = new List<string?>();
        var columns = new List<SpreadsheetColumn>();

        void ReadColumns(XElement container, int depth, bool parentCollapsed)
        {
            foreach (XElement child in container.Elements())
            {
                if (child.Name == OdsNamespaces.Table + "table-column-group")
                {
                    string? display = (string?)child.Attribute(OdsNamespaces.Table + "display");
                    bool collapsed = parentCollapsed || (display is "false");
                    ReadColumns(child, depth + 1, collapsed);
                }
                else if (child.Name == OdsNamespaces.Table + "table-column")
                {
                    int repeated = 1;
                    if (int.TryParse((string?)child.Attribute(OdsNamespaces.Table + "number-columns-repeated"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r))
                        repeated = Math.Clamp(r, 1, 1024);

                    string? styleName = (string?)child.Attribute(OdsNamespaces.Table + "style-name");
                    double width = ReadDimension(styles.GetValueOrDefault(("table-column", styleName ?? string.Empty)),
                        "table-column", "column-width") ?? 72.0;
                    string? defaultCellStyle = (string?)child.Attribute(OdsNamespaces.Table + "default-cell-style-name");
                    string? visibility = (string?)child.Attribute(OdsNamespaces.Table + "visibility");
                    bool hidden = visibility is "collapse" or "filter";

                    for (int i = 0; i < repeated && columnWidths.Count < 1024; i++)
                    {
                        int colIdx = columnWidths.Count;
                        columnWidths.Add(width);
                        columnCellStyles.Add(defaultCellStyle);
                        if (depth > 0 || hidden || parentCollapsed)
                        {
                            columns.Add(new SpreadsheetColumn(colIdx, width, hidden, depth, parentCollapsed));
                        }
                    }
                }
            }
        }
        ReadColumns(tableElem, 0, false);

        var rows = new List<SpreadsheetRow>();
        int currentRowIndex = 0;

        void ReadRows(XElement container, int depth, bool parentCollapsed)
        {
            foreach (XElement child in container.Elements())
            {
                if (child.Name == OdsNamespaces.Table + "table-row-group")
                {
                    string? display = (string?)child.Attribute(OdsNamespaces.Table + "display");
                    bool collapsed = parentCollapsed || (display is "false");
                    ReadRows(child, depth + 1, collapsed);
                }
                else if (child.Name == OdsNamespaces.Table + "table-row")
                {
                    ReadRow(child, depth, parentCollapsed);
                }
            }
        }

        void ReadRow(XElement rowElem, int depth, bool parentCollapsed)
        {
            int rowRepeated = 1;
            if (int.TryParse((string?)rowElem.Attribute(OdsNamespaces.Table + "number-rows-repeated"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int rr))
                rowRepeated = Math.Max(1, rr);

            string rowStyle = (string?)rowElem.Attribute(OdsNamespaces.Table + "style-name") ?? string.Empty;
            double? height = ReadDimension(styles.GetValueOrDefault(("table-row", rowStyle)), "table-row", "row-height");
            string? visibility = (string?)rowElem.Attribute(OdsNamespaces.Table + "visibility");
            bool hidden = visibility is "collapse" or "filter";
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
                        c.Attribute(OdsNamespaces.Table + "content-validation-name") is not null ||
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
                    CellHyperlink? cellHyperlink = null;
                    foreach (XElement p in cElem.Elements(OdsNamespaces.Text + "p"))
                    {
                        if (sb.Length > 0) sb.Append('\n');
                        sb.Append(p.Value);
                        if (cellHyperlink is null)
                        {
                            XElement? aElem = p.Element(OdsNamespaces.Text + "a")
                                ?? p.Descendants(OdsNamespaces.Text + "a").FirstOrDefault();
                            if (aElem is not null)
                            {
                                string? href = (string?)aElem.Attribute(OdsNamespaces.XLink + "href")
                                    ?? (string?)aElem.Attribute("href");
                                if (!string.IsNullOrEmpty(href))
                                {
                                    string? title = (string?)aElem.Attribute(OdsNamespaces.Office + "title")
                                        ?? (string?)aElem.Attribute(OdsNamespaces.Table + "title")
                                        ?? (string?)aElem.Attribute("title");
                                    cellHyperlink = new CellHyperlink(href, title);
                                }
                            }
                        }
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
                    else if ((string?)cElem.Attribute(OdsNamespaces.Calcext + "value-type") == "error" &&
                             FormulaErrorExtensions.TryParse(textContent, out var formulaErr))
                    {
                        cachedText = textContent;
                        cellVal = CellValue.FromError(formulaErr);
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
                        string? ownStyleName = (string?)cElem.Attribute(OdsNamespaces.Table + "style-name");
                        string cellStyleName = ownStyleName ??
                        (string?)rowElem.Attribute(OdsNamespaces.Table + "default-cell-style-name") ??
                        (cIdx < columnCellStyles.Count ? columnCellStyles[cIdx] : null) ?? string.Empty;
                        XElement? cellStyle = styles.GetValueOrDefault(("table-cell", cellStyleName));
                        string? dataStyleName = (string?)cellStyle?.Attribute(OdsNamespaces.Style + "data-style-name");
                        string? numberFormat = dataStyleName is null ? null : numberStyles.GetValueOrDefault(dataStyleName);
                        localUnsupportedFormat |= dataStyleName is not null && numberFormat is null;
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

                        // The cell's style with what its parents state beneath it,
                        // and Calc's standard alignment where it states none:
                        // numbers, dates and truth values to the right, text to
                        // the left (ScOutputData's SvxCellHorJustify::Standard).
                        XElement? resolvedStyle = cellStyles.Resolve(cellStyleName);
                        OdsCellAppearance appearance = OdsCellAppearance.Read(resolvedStyle);
                        if (!OdsCellStyles.StatesAlignment(resolvedStyle))
                        {
                            appearance = appearance with
                            {
                                Alignment = valueType is "float" or "percentage" or "currency" or "date" or "time" or "boolean"
                                    ? TextAlignment.Right
                                    : TextAlignment.Left,
                            };
                        }
                        CellNote? cellNote = null;
                        if (rep == 0)
                        {
                            XElement? annotation = cElem.Element(OdsNamespaces.Office + "annotation");
                            if (annotation is not null)
                            {
                                string? creator = (string?)annotation.Element(OdsNamespaces.Dc + "creator");
                                DateTimeOffset? date = null;
                                if (DateTimeOffset.TryParse((string?)annotation.Element(OdsNamespaces.Dc + "date"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                                    date = parsedDate;
                                string noteText = string.Join("\n", annotation.Elements(OdsNamespaces.Text + "p").Select(p => p.Value));
                                if (!string.IsNullOrEmpty(noteText) || !string.IsNullOrEmpty(creator))
                                    cellNote = new CellNote(noteText, creator, date);
                            }
                        }

                        var addr = new CellAddress(rIdx, cIdx);
                        var font = appearance.Font;
                        if (cellHyperlink is not null)
                        {
                            if (font == InlineStyle.Default)
                                font = font with { Foreground = new BColor(17, 85, 204), Underline = true, LinkHref = cellHyperlink.Target };
                            else if (!font.IsLink)
                                font = font with { LinkHref = cellHyperlink.Target };
                        }

                        var cell = new SpreadsheetCell(
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
                            Font = font,
                            WrapText = appearance.WrapText,
                            Note = cellNote,
                            Hyperlink = cellHyperlink,
                        };

                        string? cvName = (string?)cElem.Attribute(OdsNamespaces.Table + "content-validation-name");
                        if (cvName is not null && validationsByName is not null && validationsByName.TryGetValue(cvName, out var vDef))
                        {
                            referencedValidations.Add(cvName);
                            if (vDef.Ranges.Count == 0)
                                vDef.Ranges.Add(new CellRangeAddress(addr, addr));
                        }

                        // Avoid storing thousands of repeated empty cells; a
                        // style of the cell's own is kept between other cells.
                        if (rep > 0 && ownStyleName is null && !MarksUsedRange(cell))
                            continue;

                        cells.Add(cell);
                    }
                }

                int minCells = hasNonEmptyCell ? 0 : 1;
                while (cells.Count > minCells && !MarksUsedRange(cells[^1]))
                    cells.RemoveAt(cells.Count - 1);

                if (cells.Count > 0 || hasNonEmptyCell || height.HasValue || hidden || depth > 0)
                    rows.Add(new SpreadsheetRow(rIdx, cells, height, hidden, depth, parentCollapsed));
            }
        }

        ReadRows(tableElem, 0, false);

        // A row height alone does not extend the used range either: Calc saves
        // a row style for the rows past the data, and LibreOffice prints none.
        while (rows.Count > 0 && !rows[^1].Cells.Any(MarksUsedRange))
            rows.RemoveAt(rows.Count - 1);

        var sheet = new SpreadsheetWorksheet(sheetName, rows, columnWidths);
        int usedColumns = sheet.ColumnCount;
        if (columns.Count > 0)
            usedColumns = Math.Max(usedColumns, columns.Max(c => c.ColumnIndex) + 1);
        if (columnWidths.Count > usedColumns)
        {
            columnWidths.RemoveRange(usedColumns, columnWidths.Count - usedColumns);
            sheet = new SpreadsheetWorksheet(sheetName, rows, columnWidths);
        }

        var conditionalFormats = new List<ConditionalFormat>();
        var cfContainer = tableElem.Element(OdsNamespaces.Calcext + "conditional-formats") ??
                          tableElem.Element(OdsNamespaces.Table + "conditional-formats");
        if (cfContainer is not null)
        {
            var cfElements = cfContainer.Elements(OdsNamespaces.Calcext + "conditional-format")
                .Concat(cfContainer.Elements(OdsNamespaces.Table + "conditional-format"));

            foreach (XElement cfElem in cfElements)
            {
                string? target = (string?)cfElem.Attribute(OdsNamespaces.Calcext + "target-range-address") ??
                                 (string?)cfElem.Attribute(OdsNamespaces.Table + "target-range-address");
                if (string.IsNullOrWhiteSpace(target))
                    continue;

                var ranges = new List<CellRangeAddress>();
                foreach (string token in target.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    int dotIdx = token.LastIndexOf('.');
                    string rangeStr = dotIdx >= 0 ? token[(dotIdx + 1)..] : token;
                    if (CellRangeAddress.TryParse(rangeStr, out CellRangeAddress range))
                        ranges.Add(range);
                }

                if (ranges.Count == 0)
                    continue;

                var rules = new List<ConditionalFormatRule>();

                // Check color scale
                foreach (XElement csElem in cfElem.Elements(OdsNamespaces.Calcext + "color-scale"))
                {
                    var entries = csElem.Elements(OdsNamespaces.Calcext + "color-scale-entry").ToList();
                    var colors = new List<BColor>();
                    foreach (var entry in entries)
                    {
                        string? clrStr = (string?)entry.Attribute(OdsNamespaces.Calcext + "color");
                        if (!string.IsNullOrEmpty(clrStr) && TryParseOdsColor(clrStr, out var clr))
                            colors.Add(clr);
                    }
                    if (colors.Count == 2)
                        rules.Add(new ConditionalFormatRule(ConditionalFormatRuleType.ColorScale, ColorScale: new ColorScaleConfiguration(colors[0], colors[1])));
                    else if (colors.Count >= 3)
                        rules.Add(new ConditionalFormatRule(ConditionalFormatRuleType.ColorScale, ColorScale: new ColorScaleConfiguration(colors[0], colors[^1], MidColor: colors[1])));
                }

                // Check conditions
                var condElements = cfElem.Elements(OdsNamespaces.Calcext + "condition")
                    .Concat(cfElem.Elements(OdsNamespaces.Table + "condition"));

                foreach (XElement condElem in condElements)
                {
                    string? styleName = (string?)condElem.Attribute(OdsNamespaces.Calcext + "apply-style-name") ??
                                        (string?)condElem.Attribute(OdsNamespaces.Table + "apply-style-name");
                    ConditionalFormatStyle? ruleStyle = null;
                    if (!string.IsNullOrEmpty(styleName) && styles.TryGetValue(("table-cell", styleName), out XElement? styleDef))
                    {
                        ruleStyle = ParseOdsConditionalFormatStyle(styleDef);
                    }

                    string? condVal = (string?)condElem.Attribute(OdsNamespaces.Calcext + "value") ??
                                      (string?)condElem.Attribute(OdsNamespaces.Table + "value") ??
                                      (string?)condElem.Attribute(OdsNamespaces.Table + "condition");

                    var (ruleType, op, f1, f2) = ParseOdsCondition(condVal);
                    rules.Add(new ConditionalFormatRule(ruleType, op, f1, f2, ruleStyle));
                }

                if (rules.Count > 0)
                    conditionalFormats.Add(new ConditionalFormat(ranges, rules));
            }
        }

        var sheetValidations = new List<DataValidation>();
        if (validationsByName is not null)
        {
            foreach (string vName in referencedValidations)
            {
                if (validationsByName.TryGetValue(vName, out var vDef) && vDef.Ranges.Count > 0)
                {
                    sheetValidations.Add(new DataValidation(
                        vDef.Ranges,
                        vDef.Type,
                        vDef.Operator,
                        Formula1: vDef.Formula1,
                        Formula2: vDef.Formula2,
                        AllowBlank: vDef.AllowBlank,
                        ShowDropdown: vDef.ShowDropdown,
                        ShowInputMessage: vDef.ShowInputMessage,
                        InputTitle: vDef.InputTitle,
                        InputMessage: vDef.InputMessage,
                        ShowErrorMessage: vDef.ShowErrorMessage,
                        ErrorTitle: vDef.ErrorTitle,
                        ErrorMessage: vDef.ErrorMessage,
                        ErrorStyle: vDef.ErrorStyle));
                }
            }
        }

        bool summaryRowsBelow = true;
        if (string.Equals((string?)tableElem.Attribute(OdsNamespaces.Table + "summary-rows-below"), "false", StringComparison.OrdinalIgnoreCase))
            summaryRowsBelow = false;

        bool summaryColumnsRight = true;
        if (string.Equals((string?)tableElem.Attribute(OdsNamespaces.Table + "summary-columns-right"), "false", StringComparison.OrdinalIgnoreCase))
            summaryColumnsRight = false;

        unsupportedFormat |= localUnsupportedFormat;
        return new SpreadsheetWorksheet(sheet.Name, sheet.Rows, sheet.ColumnWidths)
        {
            Columns = columns.Count > 0 ? columns : null,
            SummaryRowsBelow = summaryRowsBelow,
            SummaryColumnsRight = summaryColumnsRight,
            ShowGridlines = sheet.ShowGridlines,
            FrozenRows = sheet.FrozenRows,
            FrozenColumns = sheet.FrozenColumns,
            SplitRows = sheet.SplitRows,
            SplitColumns = sheet.SplitColumns,
            ConditionalFormats = conditionalFormats.Count > 0 ? conditionalFormats : null,
            DataValidations = sheetValidations.Count > 0 ? sheetValidations : null,
            AutoFilter = sheet.AutoFilter
        };
    }

    private static ConditionalFormatStyle ParseOdsConditionalFormatStyle(XElement styleDef)
    {
        XElement? cellProp = styleDef.Element(OdsNamespaces.Style + "table-cell-properties");
        XElement? textProp = styleDef.Element(OdsNamespaces.Style + "text-properties");

        BColor? shading = null;
        if ((string?)cellProp?.Attribute(OdsNamespaces.Fo + "background-color") is string bgStr &&
            TryParseOdsColor(bgStr, out var bgClr))
        {
            shading = bgClr;
        }

        BColor? fgColor = null;
        if ((string?)textProp?.Attribute(OdsNamespaces.Fo + "color") is string fgStr &&
            TryParseOdsColor(fgStr, out var fgClr))
        {
            fgColor = fgClr;
        }

        bool? bold = null;
        if ((string?)textProp?.Attribute(OdsNamespaces.Fo + "font-weight") is string fw)
            bold = fw is "bold" or "bolder" or "700" or "800" or "900";

        bool? italic = null;
        if ((string?)textProp?.Attribute(OdsNamespaces.Fo + "font-style") is string fs)
            italic = fs is "italic" or "oblique";

        bool? underline = null;
        if ((string?)textProp?.Attribute(OdsNamespaces.Style + "text-underline-style") is string us)
            underline = us is not "none";

        bool? strikethrough = null;
        if ((string?)textProp?.Attribute(OdsNamespaces.Style + "text-line-through-style") is string ls)
            strikethrough = ls is not "none";

        return new ConditionalFormatStyle(shading, fgColor, bold, italic, underline, strikethrough);
    }

    private static bool TryParseOdsColor(string text, out BColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string t = text.Trim().TrimStart('#');
        if (t.Length == 6 && uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
        {
            color = BColor.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            return true;
        }
        if (t.Length == 8 && uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
        {
            color = BColor.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            return true;
        }
        return false;
    }

    private static (ConditionalFormatRuleType Type, ConditionalFormatOperator Operator, string? Formula1, string? Formula2) ParseOdsCondition(string? condVal)
    {
        if (string.IsNullOrWhiteSpace(condVal))
            return (ConditionalFormatRuleType.CellIs, ConditionalFormatOperator.None, null, null);

        string s = condVal.Trim();
        if (s.StartsWith("cell-content-is-between(", StringComparison.OrdinalIgnoreCase) && s.EndsWith(')'))
        {
            string inner = s[24..^1];
            int comma = inner.IndexOf(',');
            if (comma >= 0)
                return (ConditionalFormatRuleType.CellIs, ConditionalFormatOperator.Between, inner[..comma].Trim(), inner[(comma + 1)..].Trim());
        }
        if (s.StartsWith("cell-content-is-not-between(", StringComparison.OrdinalIgnoreCase) && s.EndsWith(')'))
        {
            string inner = s[28..^1];
            int comma = inner.IndexOf(',');
            if (comma >= 0)
                return (ConditionalFormatRuleType.CellIs, ConditionalFormatOperator.NotBetween, inner[..comma].Trim(), inner[(comma + 1)..].Trim());
        }
        if (s.StartsWith("cell-content()>=", StringComparison.OrdinalIgnoreCase))
            return (ConditionalFormatRuleType.CellIs, ConditionalFormatOperator.GreaterThanOrEqual, s[16..].Trim(), null);
        if (s.StartsWith("cell-content()>", StringComparison.OrdinalIgnoreCase))
            return (ConditionalFormatRuleType.CellIs, ConditionalFormatOperator.GreaterThan, s[15..].Trim(), null);
        if (s.StartsWith("cell-content()<=", StringComparison.OrdinalIgnoreCase))
            return (ConditionalFormatRuleType.CellIs, ConditionalFormatOperator.LessThanOrEqual, s[16..].Trim(), null);
        if (s.StartsWith("cell-content()<", StringComparison.OrdinalIgnoreCase))
            return (ConditionalFormatRuleType.CellIs, ConditionalFormatOperator.LessThan, s[15..].Trim(), null);
        if (s.StartsWith("cell-content()!=", StringComparison.OrdinalIgnoreCase))
            return (ConditionalFormatRuleType.CellIs, ConditionalFormatOperator.NotEqual, s[16..].Trim(), null);
        if (s.StartsWith("cell-content()<>", StringComparison.OrdinalIgnoreCase))
            return (ConditionalFormatRuleType.CellIs, ConditionalFormatOperator.NotEqual, s[16..].Trim(), null);
        if (s.StartsWith("cell-content()=", StringComparison.OrdinalIgnoreCase))
            return (ConditionalFormatRuleType.CellIs, ConditionalFormatOperator.Equal, s[15..].Trim(), null);

        if (s.StartsWith("is-text-contains(\"", StringComparison.OrdinalIgnoreCase) && s.EndsWith("\")"))
            return (ConditionalFormatRuleType.ContainsText, ConditionalFormatOperator.None, s[18..^2], null);
        if (s.StartsWith("not-is-text-contains(\"", StringComparison.OrdinalIgnoreCase) && s.EndsWith("\")"))
            return (ConditionalFormatRuleType.NotContainsText, ConditionalFormatOperator.None, s[22..^2], null);
        if (s.StartsWith("is-begins-with(\"", StringComparison.OrdinalIgnoreCase) && s.EndsWith("\")"))
            return (ConditionalFormatRuleType.BeginsWith, ConditionalFormatOperator.None, s[16..^2], null);
        if (s.StartsWith("is-ends-with(\"", StringComparison.OrdinalIgnoreCase) && s.EndsWith("\")"))
            return (ConditionalFormatRuleType.EndsWith, ConditionalFormatOperator.None, s[14..^2], null);

        if (s.Equals("is-duplicate", StringComparison.OrdinalIgnoreCase) || s.StartsWith("is-duplicate()", StringComparison.OrdinalIgnoreCase))
            return (ConditionalFormatRuleType.DuplicateValues, ConditionalFormatOperator.None, null, null);
        if (s.Equals("is-unique", StringComparison.OrdinalIgnoreCase) || s.StartsWith("is-unique()", StringComparison.OrdinalIgnoreCase))
            return (ConditionalFormatRuleType.UniqueValues, ConditionalFormatOperator.None, null, null);
        if (s.Equals("is-empty", StringComparison.OrdinalIgnoreCase) || s.StartsWith("is-empty()", StringComparison.OrdinalIgnoreCase))
            return (ConditionalFormatRuleType.Blanks, ConditionalFormatOperator.None, null, null);
        if (s.Equals("is-not-empty", StringComparison.OrdinalIgnoreCase) || s.StartsWith("is-not-empty()", StringComparison.OrdinalIgnoreCase))
            return (ConditionalFormatRuleType.NoBlanks, ConditionalFormatOperator.None, null, null);

        if (s.StartsWith("is-true-formula(", StringComparison.OrdinalIgnoreCase) && s.EndsWith(')'))
            return (ConditionalFormatRuleType.Expression, ConditionalFormatOperator.None, s[16..^1].Trim(), null);

        return (ConditionalFormatRuleType.Expression, ConditionalFormatOperator.None, s, null);
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

    private sealed class OdsValidationDef
    {
        public string Name { get; init; } = "";
        public DataValidationType Type { get; init; }
        public DataValidationOperator Operator { get; init; }
        public string? Formula1 { get; init; }
        public string? Formula2 { get; init; }
        public bool AllowBlank { get; init; } = true;
        public bool ShowDropdown { get; init; } = true;
        public bool ShowInputMessage { get; init; } = true;
        public string? InputTitle { get; init; }
        public string? InputMessage { get; init; }
        public bool ShowErrorMessage { get; init; } = true;
        public string? ErrorTitle { get; init; }
        public string? ErrorMessage { get; init; }
        public DataValidationErrorStyle ErrorStyle { get; init; } = DataValidationErrorStyle.Stop;
        public List<CellRangeAddress> Ranges { get; } = [];
    }

    private static (DataValidationType Type, DataValidationOperator Operator, string? F1, string? F2) ParseOdsValidationCondition(string? cond)
    {
        if (string.IsNullOrWhiteSpace(cond))
            return (DataValidationType.None, DataValidationOperator.Between, null, null);

        if (cond.Contains("cell-content-is-in-list", StringComparison.OrdinalIgnoreCase))
        {
            int start = cond.IndexOf('(');
            int end = cond.LastIndexOf(')');
            string? f1 = (start >= 0 && end > start) ? cond.Substring(start + 1, end - start - 1) : null;
            return (DataValidationType.List, DataValidationOperator.Between, f1, null);
        }

        DataValidationType valType = DataValidationType.None;
        if (cond.Contains("whole-number", StringComparison.OrdinalIgnoreCase))
            valType = DataValidationType.WholeNumber;
        else if (cond.Contains("decimal", StringComparison.OrdinalIgnoreCase))
            valType = DataValidationType.Decimal;
        else if (cond.Contains("date", StringComparison.OrdinalIgnoreCase))
            valType = DataValidationType.Date;
        else if (cond.Contains("text-length", StringComparison.OrdinalIgnoreCase))
            valType = DataValidationType.TextLength;
        else
            valType = DataValidationType.Custom;

        DataValidationOperator op = DataValidationOperator.Between;
        string? f1Val = null, f2Val = null;

        if (cond.Contains("not-between", StringComparison.OrdinalIgnoreCase))
        {
            op = DataValidationOperator.NotBetween;
            ExtractTwoArgs(cond, "not-between", out f1Val, out f2Val);
        }
        else if (cond.Contains("between", StringComparison.OrdinalIgnoreCase))
        {
            op = DataValidationOperator.Between;
            ExtractTwoArgs(cond, "between", out f1Val, out f2Val);
        }
        else if (cond.Contains("not-equal", StringComparison.OrdinalIgnoreCase))
        {
            op = DataValidationOperator.NotEqual;
            ExtractOneArg(cond, "not-equal", out f1Val);
        }
        else if (cond.Contains("equal", StringComparison.OrdinalIgnoreCase))
        {
            op = DataValidationOperator.Equal;
            ExtractOneArg(cond, "equal", out f1Val);
        }
        else if (cond.Contains("greater-than-or-equal", StringComparison.OrdinalIgnoreCase))
        {
            op = DataValidationOperator.GreaterThanOrEqual;
            ExtractOneArg(cond, "greater-than-or-equal", out f1Val);
        }
        else if (cond.Contains("greater-than", StringComparison.OrdinalIgnoreCase))
        {
            op = DataValidationOperator.GreaterThan;
            ExtractOneArg(cond, "greater-than", out f1Val);
        }
        else if (cond.Contains("less-than-or-equal", StringComparison.OrdinalIgnoreCase))
        {
            op = DataValidationOperator.LessThanOrEqual;
            ExtractOneArg(cond, "less-than-or-equal", out f1Val);
        }
        else if (cond.Contains("less-than", StringComparison.OrdinalIgnoreCase))
        {
            op = DataValidationOperator.LessThan;
            ExtractOneArg(cond, "less-than", out f1Val);
        }

        return (valType, op, f1Val, f2Val);
    }

    private static void ExtractTwoArgs(string text, string keyword, out string? a, out string? b)
    {
        a = null; b = null;
        int idx = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return;
        int start = text.IndexOf('(', idx);
        int end = text.IndexOf(')', start >= 0 ? start : idx);
        if (start >= 0 && end > start)
        {
            string inside = text.Substring(start + 1, end - start - 1);
            string[] parts = inside.Split(',', 2);
            a = parts[0].Trim();
            if (parts.Length > 1) b = parts[1].Trim();
        }
    }

    private static void ExtractOneArg(string text, string keyword, out string? a)
    {
        a = null;
        int idx = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return;
        int start = text.IndexOf('(', idx);
        int end = text.IndexOf(')', start >= 0 ? start : idx);
        if (start >= 0 && end > start)
            a = text.Substring(start + 1, end - start - 1).Trim();
    }
}
