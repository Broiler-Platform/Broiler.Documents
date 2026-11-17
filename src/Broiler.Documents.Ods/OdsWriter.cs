using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;

namespace Broiler.Documents.Ods;

internal static class OdsWriter
{
    private static readonly DateTimeOffset ZipTimestamp = new(new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    public static DocumentWriteResult Write(DocumentWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        long startPos = request.Destination.CanSeek ? request.Destination.Position : 0;
        SpreadsheetWorkbook workbook = SpreadsheetWorkbook.FromRichTextDocument(request.Document);
        var diagnostics = new List<DocumentDiagnostic>();
        var diagnosticOnce = new HashSet<string>(StringComparer.Ordinal);

        using (var archive = new ZipArchive(request.Destination, ZipArchiveMode.Create, leaveOpen: true))
        {
            // 1. mimetype (MUST be first entry, uncompressed)
            WriteMimeType(archive);

            // 2. META-INF/manifest.xml
            WriteManifestXml(archive, workbook);

            // 3. styles.xml
            WriteStylesXml(archive);

            // 4. content.xml
            WriteContentXml(archive, workbook, diagnostics, diagnosticOnce);

            // 5. settings.xml (view settings, freeze panes, gridlines)
            WriteSettingsXml(archive, workbook);
        }

        long bytesWritten = request.Destination.CanSeek ? Math.Max(0, request.Destination.Position - startPos) : 0;
        return new DocumentWriteResult(bytesWritten, diagnostics, DocumentWriteResult.StatusFrom(diagnostics));
    }

    private static void WriteMimeType(ZipArchive archive)
    {
        ZipArchiveEntry entry = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
        entry.LastWriteTime = ZipTimestamp;
        using Stream stream = entry.Open();
        byte[] bytes = Encoding.ASCII.GetBytes(OdsNamespaces.PackageMediaType);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteManifestXml(ZipArchive archive, SpreadsheetWorkbook workbook)
    {
        var root = new XElement(OdsNamespaces.Manifest + "manifest",
            new XAttribute(XNamespace.Xmlns + "manifest", OdsNamespaces.Manifest.NamespaceName),
            new XElement(OdsNamespaces.Manifest + "file-entry",
                new XAttribute(OdsNamespaces.Manifest + "full-path", "/"),
                new XAttribute(OdsNamespaces.Manifest + "media-type", OdsNamespaces.PackageMediaType)),
            new XElement(OdsNamespaces.Manifest + "file-entry",
                new XAttribute(OdsNamespaces.Manifest + "full-path", "content.xml"),
                new XAttribute(OdsNamespaces.Manifest + "media-type", "text/xml")),
            new XElement(OdsNamespaces.Manifest + "file-entry",
                new XAttribute(OdsNamespaces.Manifest + "full-path", "styles.xml"),
                new XAttribute(OdsNamespaces.Manifest + "media-type", "text/xml")));

        if (workbook.Sheets.Any(s => !s.ShowGridlines || s.FrozenRows > 0 || s.FrozenColumns > 0 || s.SplitRows > 0 || s.SplitColumns > 0))
        {
            root.Add(new XElement(OdsNamespaces.Manifest + "file-entry",
                new XAttribute(OdsNamespaces.Manifest + "full-path", "settings.xml"),
                new XAttribute(OdsNamespaces.Manifest + "media-type", "text/xml")));
        }

        WriteXmlEntry(archive, "META-INF/manifest.xml", new XDocument(root));
    }

    private static void WriteSettingsXml(ZipArchive archive, SpreadsheetWorkbook workbook)
    {
        if (workbook.Sheets.All(s => s.ShowGridlines && s.FrozenRows == 0 && s.FrozenColumns == 0 && s.SplitRows == 0 && s.SplitColumns == 0))
            return;

        var tableMap = new XElement(OdsNamespaces.Config + "config-item-map-named",
            new XAttribute(OdsNamespaces.Config + "name", "Tables"));

        foreach (var sheet in workbook.Sheets)
        {
            var sheetEntry = new XElement(OdsNamespaces.Config + "config-item-map-entry",
                new XAttribute(OdsNamespaces.Config + "name", sheet.Name),
                new XElement(OdsNamespaces.Config + "config-item",
                    new XAttribute(OdsNamespaces.Config + "name", "ShowGrid"),
                    new XAttribute(OdsNamespaces.Config + "type", "boolean"),
                    sheet.ShowGridlines ? "true" : "false"));

            if (sheet.FrozenRows > 0)
            {
                sheetEntry.Add(
                    new XElement(OdsNamespaces.Config + "config-item",
                        new XAttribute(OdsNamespaces.Config + "name", "VerticalSplitMode"),
                        new XAttribute(OdsNamespaces.Config + "type", "short"),
                        "2"),
                    new XElement(OdsNamespaces.Config + "config-item",
                        new XAttribute(OdsNamespaces.Config + "name", "VerticalSplitPosition"),
                        new XAttribute(OdsNamespaces.Config + "type", "int"),
                        sheet.FrozenRows.ToString(CultureInfo.InvariantCulture)));
            }
            else if (sheet.SplitRows > 0)
            {
                sheetEntry.Add(
                    new XElement(OdsNamespaces.Config + "config-item",
                        new XAttribute(OdsNamespaces.Config + "name", "VerticalSplitMode"),
                        new XAttribute(OdsNamespaces.Config + "type", "short"),
                        "1"),
                    new XElement(OdsNamespaces.Config + "config-item",
                        new XAttribute(OdsNamespaces.Config + "name", "VerticalSplitPosition"),
                        new XAttribute(OdsNamespaces.Config + "type", "int"),
                        sheet.SplitRows.ToString(CultureInfo.InvariantCulture)));
            }

            if (sheet.FrozenColumns > 0)
            {
                sheetEntry.Add(
                    new XElement(OdsNamespaces.Config + "config-item",
                        new XAttribute(OdsNamespaces.Config + "name", "HorizontalSplitMode"),
                        new XAttribute(OdsNamespaces.Config + "type", "short"),
                        "2"),
                    new XElement(OdsNamespaces.Config + "config-item",
                        new XAttribute(OdsNamespaces.Config + "name", "HorizontalSplitPosition"),
                        new XAttribute(OdsNamespaces.Config + "type", "int"),
                        sheet.FrozenColumns.ToString(CultureInfo.InvariantCulture)));
            }
            else if (sheet.SplitColumns > 0)
            {
                sheetEntry.Add(
                    new XElement(OdsNamespaces.Config + "config-item",
                        new XAttribute(OdsNamespaces.Config + "name", "HorizontalSplitMode"),
                        new XAttribute(OdsNamespaces.Config + "type", "short"),
                        "1"),
                    new XElement(OdsNamespaces.Config + "config-item",
                        new XAttribute(OdsNamespaces.Config + "name", "HorizontalSplitPosition"),
                        new XAttribute(OdsNamespaces.Config + "type", "int"),
                        sheet.SplitColumns.ToString(CultureInfo.InvariantCulture)));
            }

            tableMap.Add(sheetEntry);
        }

        var viewsEntry = new XElement(OdsNamespaces.Config + "config-item-map-entry", tableMap);
        var viewsMap = new XElement(OdsNamespaces.Config + "config-item-map-indexed",
            new XAttribute(OdsNamespaces.Config + "name", "Views"),
            viewsEntry);
        var viewSettings = new XElement(OdsNamespaces.Config + "config-item-set",
            new XAttribute(OdsNamespaces.Config + "name", "ooo:view-settings"),
            viewsMap);
        var settingsElem = new XElement(OdsNamespaces.Office + "settings", viewSettings);
        var root = new XElement(OdsNamespaces.Office + "document-settings",
            new XAttribute(XNamespace.Xmlns + "office", OdsNamespaces.Office.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "config", OdsNamespaces.Config.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "ooo", OdsNamespaces.Ooo.NamespaceName),
            settingsElem);

        WriteXmlEntry(archive, "settings.xml", new XDocument(root));
    }

    private static void WriteStylesXml(ZipArchive archive)
    {
        var root = new XElement(OdsNamespaces.Office + "document-styles",
            new XAttribute(XNamespace.Xmlns + "office", OdsNamespaces.Office.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "style", OdsNamespaces.Style.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "number", OdsNamespaces.Number.NamespaceName),
            new XElement(OdsNamespaces.Office + "styles"));

        WriteXmlEntry(archive, "styles.xml", new XDocument(root));
    }

    private static void WriteContentXml(
        ZipArchive archive,
        SpreadsheetWorkbook workbook,
        List<DocumentDiagnostic> diagnostics,
        HashSet<string> diagnosticOnce)
    {
        var styles = new OdsWriteStyles(() => AddDiagnosticOnce(diagnostics, diagnosticOnce,
            OdsDiagnosticCodes.NumberFormatUnsupported, "An unsupported number format was omitted."));
        var spreadsheetElem = new XElement(OdsNamespaces.Office + "spreadsheet");

        // Content validations
        var valMap = new Dictionary<(string SheetName, int Row, int Col), string>();
        var contentValidationsElem = new XElement(OdsNamespaces.Table + "content-validations");
        int valCounter = 0;

        foreach (SpreadsheetWorksheet sheet in workbook.Sheets)
        {
            if (sheet.DataValidations is not { Count: > 0 } dvs)
                continue;

            foreach (DataValidation dv in dvs)
            {
                if (dv.Ranges.Count == 0)
                    continue;

                string valName = $"val_{valCounter++}";
                string condition = FormatOdsValidationCondition(dv);

                var cvElem = new XElement(OdsNamespaces.Table + "content-validation",
                    new XAttribute(OdsNamespaces.Table + "name", valName),
                    new XAttribute(OdsNamespaces.Table + "condition", condition),
                    new XAttribute(OdsNamespaces.Table + "allow-empty-cell", dv.AllowBlank ? "true" : "false"),
                    new XAttribute(OdsNamespaces.Table + "display-list", dv.ShowDropdown ? "show" : "none"),
                    new XAttribute(OdsNamespaces.Calcext + "target-range-address", string.Join(" ", dv.Ranges.Select(r => r.Format()))));

                if (dv.ShowInputMessage && (!string.IsNullOrEmpty(dv.InputTitle) || !string.IsNullOrEmpty(dv.InputMessage)))
                {
                    var helpElem = new XElement(OdsNamespaces.Table + "help-message",
                        new XAttribute(OdsNamespaces.Table + "display", "true"));
                    if (!string.IsNullOrEmpty(dv.InputTitle))
                        helpElem.Add(new XAttribute(OdsNamespaces.Table + "title", XmlText(dv.InputTitle, diagnostics, diagnosticOnce)));
                    if (!string.IsNullOrEmpty(dv.InputMessage))
                        helpElem.Add(new XElement(OdsNamespaces.Text + "p", XmlText(dv.InputMessage, diagnostics, diagnosticOnce)));
                    cvElem.Add(helpElem);
                }

                if (dv.ShowErrorMessage && (!string.IsNullOrEmpty(dv.ErrorTitle) || !string.IsNullOrEmpty(dv.ErrorMessage)))
                {
                    string msgType = dv.ErrorStyle switch
                    {
                        DataValidationErrorStyle.Warning => "warning",
                        DataValidationErrorStyle.Information => "information",
                        _ => "stop"
                    };
                    var errElem = new XElement(OdsNamespaces.Table + "error-message",
                        new XAttribute(OdsNamespaces.Table + "display", "true"),
                        new XAttribute(OdsNamespaces.Table + "message-type", msgType));
                    if (!string.IsNullOrEmpty(dv.ErrorTitle))
                        errElem.Add(new XAttribute(OdsNamespaces.Table + "title", XmlText(dv.ErrorTitle, diagnostics, diagnosticOnce)));
                    if (!string.IsNullOrEmpty(dv.ErrorMessage))
                        errElem.Add(new XElement(OdsNamespaces.Text + "p", XmlText(dv.ErrorMessage, diagnostics, diagnosticOnce)));
                    cvElem.Add(errElem);
                }

                contentValidationsElem.Add(cvElem);

                foreach (var r in dv.Ranges)
                {
                    for (int row = r.StartRow; row <= r.EndRow; row++)
                    {
                        for (int col = r.StartCol; col <= r.EndCol; col++)
                        {
                            valMap[(sheet.Name, row, col)] = valName;
                        }
                    }
                }
            }
        }

        if (contentValidationsElem.HasElements)
            spreadsheetElem.Add(contentValidationsElem);

        foreach (SpreadsheetWorksheet sheet in workbook.Sheets)
        {
            var tableElem = new XElement(OdsNamespaces.Table + "table",
                new XAttribute(OdsNamespaces.Table + "name", XmlText(sheet.Name, diagnostics, diagnosticOnce)));
            if (!sheet.SummaryRowsBelow)
                tableElem.Add(new XAttribute(OdsNamespaces.Table + "summary-rows-below", "false"));
            if (!sheet.SummaryColumnsRight)
                tableElem.Add(new XAttribute(OdsNamespaces.Table + "summary-columns-right", "false"));

            if (sheet.Columns != null && sheet.Columns.Count > 0)
            {
                int maxCol = Math.Max(1, Math.Max(sheet.ColumnCount, sheet.Columns.Max(c => c.ColumnIndex) + 1));
                var colsDict = sheet.Columns.ToDictionary(c => c.ColumnIndex);
                var colStack = new Stack<XElement>();
                colStack.Push(tableElem);
                int currentColDepth = 0;

                for (int colIdx = 0; colIdx < maxCol; colIdx++)
                {
                    SpreadsheetColumn? colObj = null;
                    colsDict.TryGetValue(colIdx, out colObj);
                    int targetDepth = colObj?.OutlineLevel ?? 0;
                    bool collapsed = colObj?.Collapsed ?? false;

                    while (currentColDepth < targetDepth)
                    {
                        var groupElem = new XElement(OdsNamespaces.Table + "table-column-group");
                        if (collapsed)
                            groupElem.Add(new XAttribute(OdsNamespaces.Table + "display", "false"));
                        colStack.Peek().Add(groupElem);
                        colStack.Push(groupElem);
                        currentColDepth++;
                    }
                    while (currentColDepth > targetDepth)
                    {
                        colStack.Pop();
                        currentColDepth--;
                    }

                    double? w = colObj?.Width ?? (colIdx < sheet.ColumnWidths.Count ? sheet.ColumnWidths[colIdx] : null);
                    var colElem = new XElement(OdsNamespaces.Table + "table-column");
                    if (w.HasValue)
                        colElem.Add(styles.Dimension("table-column", "column-width", w.Value));
                    if (colObj?.Hidden == true)
                        colElem.Add(new XAttribute(OdsNamespaces.Table + "visibility", "collapse"));

                    colStack.Peek().Add(colElem);
                }

                while (currentColDepth > 0)
                {
                    colStack.Pop();
                    currentColDepth--;
                }
            }
            else
            {
                int maxCol = Math.Max(1, sheet.ColumnCount);
                int widthCount = Math.Min(maxCol, sheet.ColumnWidths.Count);
                for (int column = 0; column < widthCount; column++)
                    tableElem.Add(new XElement(OdsNamespaces.Table + "table-column",
                        styles.Dimension("table-column", "column-width", sheet.ColumnWidths[column])));
                if (widthCount < maxCol)
                    tableElem.Add(new XElement(OdsNamespaces.Table + "table-column",
                        new XAttribute(OdsNamespaces.Table + "number-columns-repeated", maxCol - widthCount)));
            }

            // Track covered cells from merges
            var covered = new HashSet<CellAddress>();
            var rowStack = new Stack<XElement>();
            rowStack.Push(tableElem);
            int currentRowDepth = 0;

            foreach (SpreadsheetRow row in sheet.Rows)
            {
                int targetDepth = row.OutlineLevel;
                while (currentRowDepth < targetDepth)
                {
                    var groupElem = new XElement(OdsNamespaces.Table + "table-row-group");
                    if (row.Collapsed)
                        groupElem.Add(new XAttribute(OdsNamespaces.Table + "display", "false"));
                    rowStack.Peek().Add(groupElem);
                    rowStack.Push(groupElem);
                    currentRowDepth++;
                }
                while (currentRowDepth > targetDepth)
                {
                    rowStack.Pop();
                    currentRowDepth--;
                }

                var rowElem = new XElement(OdsNamespaces.Table + "table-row",
                    styles.Dimension("table-row", "row-height", row.Height));

                if (row.Hidden)
                    rowElem.Add(new XAttribute(OdsNamespaces.Table + "visibility", "collapse"));

                int column = 0;
                foreach (SpreadsheetCell cell in row.Cells.OrderBy(cell => cell.Address.Column))
                {
                    while (column < cell.Address.Column)
                        AppendEmptySlot(rowElem, covered.Contains(new CellAddress(row.RowIndex, column++)));

                    if (column != cell.Address.Column || covered.Contains(cell.Address))
                        continue;

                    column++;
                    var cellElem = new XElement(OdsNamespaces.Table + "table-cell", styles.Cell(cell, cell.Font.FontFamily is { } family ? XmlText(family, diagnostics, diagnosticOnce) : null));

                    if (valMap.TryGetValue((sheet.Name, row.RowIndex, cell.Address.Column), out string? cvName))
                        cellElem.Add(new XAttribute(OdsNamespaces.Table + "content-validation-name", cvName));

                    if (cell.ColumnSpan > 1)
                        cellElem.Add(new XAttribute(OdsNamespaces.Table + "number-columns-spanned", cell.ColumnSpan));
                    if (cell.RowSpan > 1)
                        cellElem.Add(new XAttribute(OdsNamespaces.Table + "number-rows-spanned", cell.RowSpan));

                    if (cell.ColumnSpan > 1 || cell.RowSpan > 1)
                    {
                        for (int r = 0; r < cell.RowSpan; r++)
                        {
                            for (int c = 0; c < cell.ColumnSpan; c++)
                            {
                                if (r != 0 || c != 0)
                                    covered.Add(new CellAddress(cell.Address.Row + r, cell.Address.Column + c));
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(cell.Formula))
                    {
                        string f = XmlText(cell.Formula.TrimStart('='), diagnostics, diagnosticOnce);
                        cellElem.Add(new XAttribute(OdsNamespaces.Table + "formula", $"of:={f}"));
                    }

                    if (cell.Value.IsNumber)
                    {
                        double serial = cell.Value.Number;
                        if (serial < 60 && OdsNumberStyles.IsDate(cell.NumberFormat))
                            serial++;
                        cellElem.Add(new XAttribute(OdsNamespaces.Office + "value-type", "float"));
                        cellElem.Add(new XAttribute(OdsNamespaces.Office + "value", serial.ToString(CultureInfo.InvariantCulture)));
                    }
                    else if (cell.Value.IsBoolean)
                    {
                        cellElem.Add(new XAttribute(OdsNamespaces.Office + "value-type", "boolean"));
                        cellElem.Add(new XAttribute(OdsNamespaces.Office + "boolean-value", cell.Value.Boolean ? "true" : "false"));
                    }
                    else if (cell.Value.IsError)
                    {
                        cellElem.Add(new XAttribute(OdsNamespaces.Office + "value-type", "string"));
                        cellElem.Add(new XAttribute(OdsNamespaces.Calcext + "value-type", "error"));
                    }
                    else
                    {
                        cellElem.Add(new XAttribute(OdsNamespaces.Office + "value-type", "string"));
                    }

                    string rawText = !string.IsNullOrEmpty(cell.CachedText) ? cell.CachedText : (cell.Value.Text ?? string.Empty);
                    if (cell.Hyperlink is { } hl)
                    {
                        if (rawText.Contains('\n'))
                        {
                            string[] lines = rawText.Split('\n');
                            foreach (string line in lines)
                            {
                                string cleanLine = XmlText(line.TrimEnd('\r'), diagnostics, diagnosticOnce);
                                var aElem = new XElement(OdsNamespaces.Text + "a",
                                    new XAttribute(OdsNamespaces.XLink + "type", "simple"),
                                    new XAttribute(OdsNamespaces.XLink + "href", hl.Target),
                                    cleanLine);
                                if (!string.IsNullOrEmpty(hl.Tooltip))
                                    aElem.Add(new XAttribute(OdsNamespaces.Office + "title", XmlText(hl.Tooltip, diagnostics, diagnosticOnce)));
                                cellElem.Add(new XElement(OdsNamespaces.Text + "p", aElem));
                            }
                        }
                        else
                        {
                            string text = XmlText(rawText, diagnostics, diagnosticOnce);
                            var aElem = new XElement(OdsNamespaces.Text + "a",
                                new XAttribute(OdsNamespaces.XLink + "type", "simple"),
                                new XAttribute(OdsNamespaces.XLink + "href", hl.Target),
                                text);
                            if (!string.IsNullOrEmpty(hl.Tooltip))
                                aElem.Add(new XAttribute(OdsNamespaces.Office + "title", XmlText(hl.Tooltip, diagnostics, diagnosticOnce)));
                            cellElem.Add(new XElement(OdsNamespaces.Text + "p", aElem));
                        }
                    }
                    else
                    {
                        if (rawText.Contains('\n'))
                        {
                            string[] lines = rawText.Split('\n');
                            foreach (string line in lines)
                            {
                                string cleanLine = XmlText(line.TrimEnd('\r'), diagnostics, diagnosticOnce);
                                cellElem.Add(new XElement(OdsNamespaces.Text + "p", cleanLine));
                            }
                        }
                        else
                        {
                            string text = XmlText(rawText, diagnostics, diagnosticOnce);
                            cellElem.Add(new XElement(OdsNamespaces.Text + "p", text));
                        }
                    }

                    if (cell.Note is not null)
                    {
                        var annotation = new XElement(OdsNamespaces.Office + "annotation");
                        if (!string.IsNullOrEmpty(cell.Note.Author))
                            annotation.Add(new XElement(OdsNamespaces.Dc + "creator", XmlText(cell.Note.Author, diagnostics, diagnosticOnce)));
                        if (cell.Note.Timestamp is { } ts)
                            annotation.Add(new XElement(OdsNamespaces.Dc + "date", ts.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
                        string[] noteLines = (cell.Note.Text ?? string.Empty).Split('\n');
                        foreach (string noteLine in noteLines)
                            annotation.Add(new XElement(OdsNamespaces.Text + "p", XmlText(noteLine.TrimEnd('\r'), diagnostics, diagnosticOnce)));
                        cellElem.Add(annotation);
                    }

                    rowElem.Add(cellElem);
                }

                while (covered.Contains(new CellAddress(row.RowIndex, column++)))
                    AppendEmptySlot(rowElem, covered: true);

                rowStack.Peek().Add(rowElem);
            }

            while (currentRowDepth > 0)
            {
                rowStack.Pop();
                currentRowDepth--;
            }

            if (sheet.ConditionalFormats is { Count: > 0 } conditionalFormats)
            {
                var cfContainer = new XElement(OdsNamespaces.Calcext + "conditional-formats");
                foreach (ConditionalFormat cf in conditionalFormats)
                {
                    if (cf.Ranges.Count == 0 || cf.Rules.Count == 0)
                        continue;

                    string target = string.Join(" ", cf.Ranges.Select(r => $"{sheet.Name}.{r.Format()}"));
                    var cfElem = new XElement(OdsNamespaces.Calcext + "conditional-format",
                        new XAttribute(OdsNamespaces.Calcext + "target-range-address", target));

                    string firstCell = cf.Ranges.Count > 0 ? $"{sheet.Name}.{cf.Ranges[0].Start.Format()}" : $"{sheet.Name}.A1";

                    foreach (ConditionalFormatRule rule in cf.Rules)
                    {
                        if (rule.Type == ConditionalFormatRuleType.ColorScale && rule.ColorScale is { } cs)
                        {
                            var csElem = new XElement(OdsNamespaces.Calcext + "color-scale");
                            csElem.Add(new XElement(OdsNamespaces.Calcext + "color-scale-entry",
                                new XAttribute(OdsNamespaces.Calcext + "type", "min"),
                                new XAttribute(OdsNamespaces.Calcext + "color", $"#{cs.MinColor.R:X2}{cs.MinColor.G:X2}{cs.MinColor.B:X2}")));
                            if (cs.MidColor is { } mid)
                            {
                                csElem.Add(new XElement(OdsNamespaces.Calcext + "color-scale-entry",
                                    new XAttribute(OdsNamespaces.Calcext + "type", "percentile"),
                                    new XAttribute(OdsNamespaces.Calcext + "value", "50"),
                                    new XAttribute(OdsNamespaces.Calcext + "color", $"#{mid.R:X2}{mid.G:X2}{mid.B:X2}")));
                            }
                            csElem.Add(new XElement(OdsNamespaces.Calcext + "color-scale-entry",
                                new XAttribute(OdsNamespaces.Calcext + "type", "max"),
                                new XAttribute(OdsNamespaces.Calcext + "color", $"#{cs.MaxColor.R:X2}{cs.MaxColor.G:X2}{cs.MaxColor.B:X2}")));
                            cfElem.Add(csElem);
                        }
                        else
                        {
                            string? styleName = rule.Style is not null ? styles.ConditionalFormatStyle(rule.Style) : null;
                            string condValue = FormatOdsCondition(rule);
                            var condElem = new XElement(OdsNamespaces.Calcext + "condition",
                                styleName is not null ? new XAttribute(OdsNamespaces.Calcext + "apply-style-name", styleName) : null,
                                new XAttribute(OdsNamespaces.Calcext + "value", condValue),
                                new XAttribute(OdsNamespaces.Calcext + "base-cell-address", firstCell));
                            cfElem.Add(condElem);
                        }
                    }

                    cfContainer.Add(cfElem);
                }
                tableElem.Add(cfContainer);
            }

            spreadsheetElem.Add(tableElem);
        }

        var dbRangesElem = new XElement(OdsNamespaces.Table + "database-ranges");
        int dbRangeIdx = 1;
        foreach (SpreadsheetWorksheet sheet in workbook.Sheets)
        {
            if (sheet.AutoFilter is { } af)
            {
                string targetRange = $"{sheet.Name}.{af.Range.Start.Format()}:{sheet.Name}.{af.Range.End.Format()}";
                var dbRangeElem = new XElement(OdsNamespaces.Table + "database-range",
                    new XAttribute(OdsNamespaces.Table + "name", $"__Anonymous_Sheet_DB__{dbRangeIdx++}"),
                    new XAttribute(OdsNamespaces.Table + "target-range-address", targetRange),
                    new XAttribute(OdsNamespaces.Table + "display-filter-buttons", "true"));

                if (af.Columns.Count > 0)
                {
                    var filterElem = new XElement(OdsNamespaces.Table + "filter");
                    foreach (var col in af.Columns)
                    {
                        int relCol = col.ColumnIndex - af.Range.Start.Column;
                        foreach (string val in col.Values)
                        {
                            filterElem.Add(new XElement(OdsNamespaces.Table + "filter-condition",
                                new XAttribute(OdsNamespaces.Table + "field-number", relCol),
                                new XAttribute(OdsNamespaces.Table + "value", XmlText(val, diagnostics, diagnosticOnce)),
                                new XAttribute(OdsNamespaces.Table + "operator", "=")));
                        }
                    }
                    dbRangeElem.Add(filterElem);
                }

                dbRangesElem.Add(dbRangeElem);
            }
        }
        if (dbRangesElem.HasElements)
            spreadsheetElem.Add(dbRangesElem);

        var root = new XElement(OdsNamespaces.Office + "document-content",
            new XAttribute(XNamespace.Xmlns + "office", OdsNamespaces.Office.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "table", OdsNamespaces.Table.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "text", OdsNamespaces.Text.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "style", OdsNamespaces.Style.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "fo", OdsNamespaces.Fo.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "number", OdsNamespaces.Number.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "of", "urn:oasis:names:tc:opendocument:xmlns:of:1.2"),
            new XAttribute(XNamespace.Xmlns + "calcext", OdsNamespaces.Calcext.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "dc", OdsNamespaces.Dc.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xlink", OdsNamespaces.XLink.NamespaceName),
            styles.Definitions.HasElements ? styles.Definitions : null,
            new XElement(OdsNamespaces.Office + "body", spreadsheetElem));

        WriteXmlEntry(archive, "content.xml", new XDocument(root));
    }

    private static void AppendEmptySlot(XElement row, bool covered) =>
        row.Add(new XElement(OdsNamespaces.Table + (covered ? "covered-table-cell" : "table-cell")));

    private static void WriteXmlEntry(ZipArchive archive, string entryName, XDocument document)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        entry.LastWriteTime = ZipTimestamp;
        using Stream stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = false,
            Indent = false
        });
        document.WriteTo(writer);
    }

    private static void AddDiagnosticOnce(List<DocumentDiagnostic> diagnostics, HashSet<string> seen, string code, string message)
    {
        if (seen.Add(code))
            diagnostics.Add(DocumentDiagnostic.Warning(code, message));
    }

    private static string XmlText(string value, List<DocumentDiagnostic> diagnostics, HashSet<string> seen)
    {
        int first = -1;
        for (int i = 0; i < value.Length; i++)
        {
            if (!XmlConvert.IsXmlChar(value[i]))
            {
                first = i;
                break;
            }
        }

        if (first < 0)
            return value;

        var clean = new StringBuilder(value.Length);
        clean.Append(value, 0, first);

        for (int i = first; i < value.Length; i++)
        {
            char character = value[i];

            if (char.IsHighSurrogate(character) &&
                i + 1 < value.Length &&
                char.IsLowSurrogate(value[i + 1]))
            {
                clean.Append(character).Append(value[i + 1]);
                i++;
                continue;
            }

            if (XmlConvert.IsXmlChar(character))
            {
                clean.Append(character);
            }
            else
            {
                AddDiagnosticOnce(diagnostics, seen, OdsDiagnosticCodes.ControlCharacterDropped, "A control character that XML cannot represent was dropped.");
            }
        }

        return clean.ToString();
    }

    private static string FormatOdsCondition(ConditionalFormatRule rule) => rule.Type switch
    {
        ConditionalFormatRuleType.CellIs => rule.Operator switch
        {
            ConditionalFormatOperator.Equal => $"cell-content()={rule.Formula1}",
            ConditionalFormatOperator.NotEqual => $"cell-content()!={rule.Formula1}",
            ConditionalFormatOperator.GreaterThan => $"cell-content()>{rule.Formula1}",
            ConditionalFormatOperator.GreaterThanOrEqual => $"cell-content()>={rule.Formula1}",
            ConditionalFormatOperator.LessThan => $"cell-content()<{rule.Formula1}",
            ConditionalFormatOperator.LessThanOrEqual => $"cell-content()<={rule.Formula1}",
            ConditionalFormatOperator.Between => $"cell-content-is-between({rule.Formula1},{rule.Formula2})",
            ConditionalFormatOperator.NotBetween => $"cell-content-is-not-between({rule.Formula1},{rule.Formula2})",
            _ => $"cell-content()={rule.Formula1}"
        },
        ConditionalFormatRuleType.ContainsText => $"is-text-contains(\"{rule.Formula1}\")",
        ConditionalFormatRuleType.NotContainsText => $"not-is-text-contains(\"{rule.Formula1}\")",
        ConditionalFormatRuleType.BeginsWith => $"is-begins-with(\"{rule.Formula1}\")",
        ConditionalFormatRuleType.EndsWith => $"is-ends-with(\"{rule.Formula1}\")",
        ConditionalFormatRuleType.DuplicateValues => "is-duplicate",
        ConditionalFormatRuleType.UniqueValues => "is-unique",
        ConditionalFormatRuleType.Blanks => "is-empty",
        ConditionalFormatRuleType.NoBlanks => "is-not-empty",
        ConditionalFormatRuleType.Expression => $"is-true-formula({rule.Formula1?.TrimStart('=')})",
        _ => $"cell-content()={rule.Formula1}"
    };

    private static string FormatOdsValidationCondition(DataValidation dv)
    {
        return dv.Type switch
        {
            DataValidationType.List => $"cell-content-is-in-list({dv.Formula1})",
            DataValidationType.WholeNumber => $"cell-content-is-whole-number() and cell-content-is-{FormatOdsOperator(dv.Operator, dv.Formula1, dv.Formula2)}",
            DataValidationType.Decimal => $"cell-content-is-decimal() and cell-content-is-{FormatOdsOperator(dv.Operator, dv.Formula1, dv.Formula2)}",
            DataValidationType.Date => $"cell-content-is-date() and cell-content-is-{FormatOdsOperator(dv.Operator, dv.Formula1, dv.Formula2)}",
            DataValidationType.TextLength => $"cell-content-text-length-is-{FormatOdsOperator(dv.Operator, dv.Formula1, dv.Formula2)}",
            DataValidationType.Custom => dv.Formula1 ?? string.Empty,
            _ => "cell-content-is-any()"
        };
    }

    private static string FormatOdsOperator(DataValidationOperator op, string? f1, string? f2) => op switch
    {
        DataValidationOperator.Between => $"between({f1},{f2})",
        DataValidationOperator.NotBetween => $"not-between({f1},{f2})",
        DataValidationOperator.Equal => $"equal({f1})",
        DataValidationOperator.NotEqual => $"not-equal({f1})",
        DataValidationOperator.GreaterThan => $"greater-than({f1})",
        DataValidationOperator.LessThan => $"less-than({f1})",
        DataValidationOperator.GreaterThanOrEqual => $"greater-than-or-equal({f1})",
        DataValidationOperator.LessThanOrEqual => $"less-than-or-equal({f1})",
        _ => $"between({f1},{f2})"
    };
}
