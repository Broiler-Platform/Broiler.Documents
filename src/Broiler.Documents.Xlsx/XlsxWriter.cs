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
using Broiler.Graphics.Color;
using Broiler.Documents.Model.Spreadsheets;

namespace Broiler.Documents.Xlsx;

internal static class XlsxWriter
{
    private static readonly DateTimeOffset ZipTimestamp = new(new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    public static DocumentWriteResult Write(DocumentWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        long startPos = request.Destination.CanSeek ? request.Destination.Position : 0;
        SpreadsheetWorkbook workbook = SpreadsheetWorkbook.FromRichTextDocument(request.Document);
        var diagnostics = new List<DocumentDiagnostic>();
        var diagnosticOnce = new HashSet<string>(StringComparer.Ordinal);

        // Collect unique shared strings
        var styles = new Dictionary<CellStyle, int> { [default] = 0 };
        var sharedStrings = new List<string>();
        var stringMap = new Dictionary<string, int>(StringComparer.Ordinal);
        var dxfStyles = new Dictionary<ConditionalFormatStyle, int>();

        foreach (SpreadsheetWorksheet sheet in workbook.Sheets)
        {
            if (sheet.ConditionalFormats is not null)
            {
                foreach (ConditionalFormat cf in sheet.ConditionalFormats)
                {
                    foreach (ConditionalFormatRule rule in cf.Rules)
                    {
                        if (rule.Style is not null)
                            dxfStyles.TryAdd(rule.Style, dxfStyles.Count);
                    }
                }
            }

            foreach (SpreadsheetRow row in sheet.Rows)
            {
                foreach (SpreadsheetCell cell in row.Cells)
                {
                    styles.TryAdd(GetStyle(cell, diagnostics, diagnosticOnce), styles.Count);
                    if (string.IsNullOrEmpty(cell.Formula) &&
                        (cell.Value.IsText || (!cell.Value.IsNumber && !cell.Value.IsBoolean && !cell.Value.IsError && !string.IsNullOrEmpty(cell.CachedText))))
                    {
                        string str = XmlText(cell.Value.IsText ? (cell.Value.Text ?? string.Empty) : cell.CachedText, diagnostics, diagnosticOnce);
                        if (!stringMap.ContainsKey(str))
                        {
                            stringMap[str] = sharedStrings.Count;
                            sharedStrings.Add(str);
                        }
                    }
                }
            }
        }

        using (var archive = new ZipArchive(request.Destination, ZipArchiveMode.Create, leaveOpen: true))
        {
            // 1. [Content_Types].xml
            WriteContentTypes(archive, workbook);

            // 2. _rels/.rels
            WriteRootRelationships(archive);

            // 3. xl/_rels/workbook.xml.rels
            WriteWorkbookRelationships(archive, workbook.Sheets.Count);

            // 4. xl/workbook.xml
            WriteWorkbookXml(archive, workbook, diagnostics, diagnosticOnce);

            // 5. xl/styles.xml
            WriteStylesXml(archive, styles, dxfStyles);

            // 6. xl/sharedStrings.xml
            WriteSharedStringsXml(archive, sharedStrings);

            // 7. xl/worksheets/sheet{i}.xml
            for (int i = 0; i < workbook.Sheets.Count; i++)
            {
                WriteWorksheetXml(archive, workbook.Sheets[i], i + 1, stringMap, styles, dxfStyles, diagnostics, diagnosticOnce);
            }
        }

        long bytesWritten = request.Destination.CanSeek ? Math.Max(0, request.Destination.Position - startPos) : 0;
        return new DocumentWriteResult(bytesWritten, diagnostics, DocumentWriteResult.StatusFrom(diagnostics));
    }

    private static void WriteContentTypes(ZipArchive archive, SpreadsheetWorkbook workbook)
    {
        int sheetCount = workbook.Sheets.Count;
        var root = new XElement(XlsxNamespaces.ContentTypes + "Types",
            new XElement(XlsxNamespaces.ContentTypes + "Default",
                new XAttribute("Extension", "rels"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(XlsxNamespaces.ContentTypes + "Default",
                new XAttribute("Extension", "xml"),
                new XAttribute("ContentType", "application/xml")),
            new XElement(XlsxNamespaces.ContentTypes + "Override",
                new XAttribute("PartName", "/xl/workbook.xml"),
                new XAttribute("ContentType", XlsxNamespaces.ContentTypeWorkbook)),
            new XElement(XlsxNamespaces.ContentTypes + "Override",
                new XAttribute("PartName", "/xl/styles.xml"),
                new XAttribute("ContentType", XlsxNamespaces.ContentTypeStyles)),
            new XElement(XlsxNamespaces.ContentTypes + "Override",
                new XAttribute("PartName", "/xl/sharedStrings.xml"),
                new XAttribute("ContentType", XlsxNamespaces.ContentTypeSharedStrings)));

        for (int i = 1; i <= sheetCount; i++)
        {
            root.Add(new XElement(XlsxNamespaces.ContentTypes + "Override",
                new XAttribute("PartName", $"/xl/worksheets/sheet{i}.xml"),
                new XAttribute("ContentType", XlsxNamespaces.ContentTypeWorksheet)));

            if (workbook.Sheets[i - 1].Rows.SelectMany(r => r.Cells).Any(c => c.Note is not null))
            {
                root.Add(new XElement(XlsxNamespaces.ContentTypes + "Override",
                    new XAttribute("PartName", $"/xl/comments{i}.xml"),
                    new XAttribute("ContentType", XlsxNamespaces.ContentTypeComments)));
            }
        }

        WriteXmlEntry(archive, "[Content_Types].xml", new XDocument(root));
    }

    private static void WriteContentTypesOld(ZipArchive archive, int sheetCount)
    {
        var root = new XElement(XlsxNamespaces.ContentTypes + "Types",
            new XElement(XlsxNamespaces.ContentTypes + "Default",
                new XAttribute("Extension", "rels"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(XlsxNamespaces.ContentTypes + "Default",
                new XAttribute("Extension", "xml"),
                new XAttribute("ContentType", "application/xml")),
            new XElement(XlsxNamespaces.ContentTypes + "Override",
                new XAttribute("PartName", "/xl/workbook.xml"),
                new XAttribute("ContentType", XlsxNamespaces.ContentTypeWorkbook)),
            new XElement(XlsxNamespaces.ContentTypes + "Override",
                new XAttribute("PartName", "/xl/styles.xml"),
                new XAttribute("ContentType", XlsxNamespaces.ContentTypeStyles)),
            new XElement(XlsxNamespaces.ContentTypes + "Override",
                new XAttribute("PartName", "/xl/sharedStrings.xml"),
                new XAttribute("ContentType", XlsxNamespaces.ContentTypeSharedStrings)));

        for (int i = 1; i <= sheetCount; i++)
        {
            root.Add(new XElement(XlsxNamespaces.ContentTypes + "Override",
                new XAttribute("PartName", $"/xl/worksheets/sheet{i}.xml"),
                new XAttribute("ContentType", XlsxNamespaces.ContentTypeWorksheet)));
        }

        WriteXmlEntry(archive, "[Content_Types].xml", new XDocument(root));
    }

    private static void WriteRootRelationships(ZipArchive archive)
    {
        var root = new XElement(XlsxNamespaces.PackageRelationships + "Relationships",
            new XElement(XlsxNamespaces.PackageRelationships + "Relationship",
                new XAttribute("Id", "rId1"),
                new XAttribute("Type", XlsxNamespaces.OfficeDocumentRelationship),
                new XAttribute("Target", "xl/workbook.xml")));

        WriteXmlEntry(archive, "_rels/.rels", new XDocument(root));
    }

    private static void WriteWorkbookRelationships(ZipArchive archive, int sheetCount)
    {
        var root = new XElement(XlsxNamespaces.PackageRelationships + "Relationships");

        for (int i = 1; i <= sheetCount; i++)
        {
            root.Add(new XElement(XlsxNamespaces.PackageRelationships + "Relationship",
                new XAttribute("Id", $"rIdSheet{i}"),
                new XAttribute("Type", XlsxNamespaces.WorksheetRelationship),
                new XAttribute("Target", $"worksheets/sheet{i}.xml")));
        }

        root.Add(new XElement(XlsxNamespaces.PackageRelationships + "Relationship",
            new XAttribute("Id", "rIdStyles"),
            new XAttribute("Type", XlsxNamespaces.StylesRelationship),
            new XAttribute("Target", "styles.xml")));

        root.Add(new XElement(XlsxNamespaces.PackageRelationships + "Relationship",
            new XAttribute("Id", "rIdSharedStrings"),
            new XAttribute("Type", XlsxNamespaces.SharedStringsRelationship),
            new XAttribute("Target", "sharedStrings.xml")));

        WriteXmlEntry(archive, "xl/_rels/workbook.xml.rels", new XDocument(root));
    }

    private static void WriteWorkbookXml(
        ZipArchive archive,
        SpreadsheetWorkbook workbook,
        List<DocumentDiagnostic> diagnostics,
        HashSet<string> diagnosticOnce)
    {
        var sheetsElem = new XElement(XlsxNamespaces.Spreadsheet + "sheets");

        for (int i = 0; i < workbook.Sheets.Count; i++)
        {
            sheetsElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "sheet",
                new XAttribute("name", XmlText(workbook.Sheets[i].Name, diagnostics, diagnosticOnce)),
                new XAttribute("sheetId", i + 1),
                new XAttribute(XlsxNamespaces.Relationships + "id", $"rIdSheet{i + 1}")));
        }

        var root = new XElement(XlsxNamespaces.Spreadsheet + "workbook",
            new XAttribute("xmlns", XlsxNamespaces.Spreadsheet.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", XlsxNamespaces.Relationships.NamespaceName),
            sheetsElem);

        WriteXmlEntry(archive, "xl/workbook.xml", new XDocument(root));
    }

    private readonly record struct CellStyle(string? Format, BColor Fill, CellVerticalAlignment Vertical,
        TextAlignment Horizontal, InlineStyle Font, CellBorders Borders, bool WrapText = false);

    private static CellStyle GetStyle(
        SpreadsheetCell cell, List<DocumentDiagnostic> diagnostics, HashSet<string> diagnosticOnce)
    {
        string? format = cell.NumberFormat is null ? null : XmlText(cell.NumberFormat, diagnostics, diagnosticOnce);
        BColor fill = cell.Shading.A == 0 ? default : cell.Shading;
        InlineStyle font = new()
        {
            FontFamily = cell.Font.FontFamily is { } family ? XmlText(family, diagnostics, diagnosticOnce) : null,
            FontSize = cell.Font.FontSize is float size && float.IsFinite(size) && size > 0 ? size : null,
            Bold = cell.Font.Bold, Italic = cell.Font.Italic, Underline = cell.Font.Underline,
            Strikethrough = cell.Font.Strikethrough, Foreground = cell.Font.Foreground,
        };
        return new CellStyle(string.IsNullOrWhiteSpace(format) || format == "General" ? null : format,
            fill, cell.VerticalAlignment, cell.Alignment, font, cell.Borders, cell.WrapText);
    }

    private static void WriteStylesXml(ZipArchive archive, Dictionary<CellStyle, int> styles, Dictionary<ConditionalFormatStyle, int> dxfStyles)
    {
        var root = new XElement(XlsxNamespaces.Spreadsheet + "styleSheet",
            new XAttribute("xmlns", XlsxNamespaces.Spreadsheet.NamespaceName),
            new XElement(XlsxNamespaces.Spreadsheet + "fonts",
                new XAttribute("count", "1"),
                new XElement(XlsxNamespaces.Spreadsheet + "font",
                    new XElement(XlsxNamespaces.Spreadsheet + "sz", new XAttribute("val", "11")),
                    new XElement(XlsxNamespaces.Spreadsheet + "name", new XAttribute("val", "Calibri")))),
            new XElement(XlsxNamespaces.Spreadsheet + "fills",
                new XAttribute("count", "2"),
                new XElement(XlsxNamespaces.Spreadsheet + "fill",
                    new XElement(XlsxNamespaces.Spreadsheet + "patternFill", new XAttribute("patternType", "none"))),
                new XElement(XlsxNamespaces.Spreadsheet + "fill",
                    new XElement(XlsxNamespaces.Spreadsheet + "patternFill", new XAttribute("patternType", "gray125")))),
            new XElement(XlsxNamespaces.Spreadsheet + "borders",
                new XAttribute("count", "1"),
                new XElement(XlsxNamespaces.Spreadsheet + "border",
                    new XElement(XlsxNamespaces.Spreadsheet + "left"),
                    new XElement(XlsxNamespaces.Spreadsheet + "right"),
                    new XElement(XlsxNamespaces.Spreadsheet + "top"),
                    new XElement(XlsxNamespaces.Spreadsheet + "bottom"))),
            new XElement(XlsxNamespaces.Spreadsheet + "cellStyleXfs",
                new XAttribute("count", "1"),
                new XElement(XlsxNamespaces.Spreadsheet + "xf",
                    new XAttribute("numFmtId", "0"),
                    new XAttribute("fontId", "0"),
                    new XAttribute("fillId", "0"),
                    new XAttribute("borderId", "0"))),
            new XElement(XlsxNamespaces.Spreadsheet + "cellXfs",
                new XAttribute("count", "1"),
                new XElement(XlsxNamespaces.Spreadsheet + "xf",
                    new XAttribute("numFmtId", "0"),
                    new XAttribute("fontId", "0"),
                    new XAttribute("fillId", "0"),
                    new XAttribute("borderId", "0"),
                    new XAttribute("xfId", "0"),
                    new XAttribute("applyAlignment", 1),
                    new XElement(XlsxNamespaces.Spreadsheet + "alignment", new XAttribute("vertical", "top")))));

        XNamespace ns = XlsxNamespaces.Spreadsheet;
        var numberFormats = new XElement(ns + "numFmts");
        XElement fills = root.Element(ns + "fills")!;
        XElement cellFormats = root.Element(ns + "cellXfs")!;
        var formatIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var fillIds = new Dictionary<BColor, int>();
        var fontIds = new Dictionary<InlineStyle, int> { [InlineStyle.Default] = 0 };
        var borderIds = new Dictionary<CellBorders, int> { [default] = 0 };

        foreach (var (style, index) in styles)
        {
            if (index == 0)
                continue;

            int formatId = 0;
            if (style.Format is { } format && !formatIds.TryGetValue(format, out formatId))
            {
                formatId = 164 + formatIds.Count;
                formatIds.Add(format, formatId);
                numberFormats.Add(new XElement(ns + "numFmt",
                    new XAttribute("numFmtId", formatId), new XAttribute("formatCode", format)));
            }

            int fillId = 0;
            if (style.Fill.A != 0 && !fillIds.TryGetValue(style.Fill, out fillId))
            {
                fillId = 2 + fillIds.Count;
                fillIds.Add(style.Fill, fillId);
                string argb = string.Create(CultureInfo.InvariantCulture,
                    $"{style.Fill.A:X2}{style.Fill.R:X2}{style.Fill.G:X2}{style.Fill.B:X2}");
                fills.Add(new XElement(ns + "fill",
                    new XElement(ns + "patternFill", new XAttribute("patternType", "solid"),
                        new XElement(ns + "fgColor", new XAttribute("rgb", argb)),
                        new XElement(ns + "bgColor", new XAttribute("indexed", 64)))));
            }

            int fontId = Register(style.Font, fontIds, root.Element(ns + "fonts")!, XlsxCellAppearance.WriteFont);
            int borderId = Register(style.Borders, borderIds, root.Element(ns + "borders")!, XlsxCellAppearance.WriteBorders);
            cellFormats.Add(new XElement(ns + "xf",
                new XAttribute("numFmtId", formatId), new XAttribute("fontId", fontId),
                new XAttribute("fillId", fillId), new XAttribute("borderId", borderId), new XAttribute("xfId", 0),
                formatId == 0 ? null : new XAttribute("applyNumberFormat", 1),
                fillId == 0 ? null : new XAttribute("applyFill", 1),
                new XAttribute("applyAlignment", 1),
                new XElement(ns + "alignment", new XAttribute("vertical", style.Vertical switch
                {
                    CellVerticalAlignment.Center => "center",
                    CellVerticalAlignment.Bottom => "bottom",
                    _ => "top",
                }), style.Horizontal == TextAlignment.Left ? null :
                    new XAttribute("horizontal", style.Horizontal.ToString().ToLowerInvariant()),
                    style.WrapText ? new XAttribute("wrapText", "1") : null)));
        }

        if (formatIds.Count > 0)
        {
            numberFormats.SetAttributeValue("count", formatIds.Count);
            root.AddFirst(numberFormats);
        }
        fills.SetAttributeValue("count", 2 + fillIds.Count);
        cellFormats.SetAttributeValue("count", styles.Count);

        if (dxfStyles.Count > 0)
        {
            var dxfsElem = new XElement(ns + "dxfs", new XAttribute("count", dxfStyles.Count));
            foreach (var (dxfStyle, _) in dxfStyles.OrderBy(kv => kv.Value))
            {
                var dxf = new XElement(ns + "dxf");

                if (dxfStyle.Bold.HasValue || dxfStyle.Italic.HasValue || dxfStyle.Underline.HasValue ||
                    dxfStyle.Strikethrough.HasValue || (dxfStyle.Foreground.HasValue && dxfStyle.Foreground.Value.A > 0))
                {
                    var fontElem = new XElement(ns + "font");
                    if (dxfStyle.Bold == true)
                        fontElem.Add(new XElement(ns + "b"));
                    if (dxfStyle.Italic == true)
                        fontElem.Add(new XElement(ns + "i"));
                    if (dxfStyle.Underline == true)
                        fontElem.Add(new XElement(ns + "u"));
                    if (dxfStyle.Strikethrough == true)
                        fontElem.Add(new XElement(ns + "strike"));
                    if (dxfStyle.Foreground is { } fg && fg.A > 0)
                    {
                        string argb = string.Create(CultureInfo.InvariantCulture, $"{fg.A:X2}{fg.R:X2}{fg.G:X2}{fg.B:X2}");
                        fontElem.Add(new XElement(ns + "color", new XAttribute("rgb", argb)));
                    }
                    dxf.Add(fontElem);
                }

                if (dxfStyle.Shading is { } sh && sh.A > 0)
                {
                    string argb = string.Create(CultureInfo.InvariantCulture, $"{sh.A:X2}{sh.R:X2}{sh.G:X2}{sh.B:X2}");
                    dxf.Add(new XElement(ns + "fill",
                        new XElement(ns + "patternFill",
                            new XAttribute("patternType", "solid"),
                            new XElement(ns + "bgColor", new XAttribute("rgb", argb)),
                            new XElement(ns + "fgColor", new XAttribute("rgb", argb)))));
                }

                if (dxfStyle.Borders is { } borders && borders.IsVisible)
                {
                    dxf.Add(XlsxCellAppearance.WriteBorders(borders));
                }

                dxfsElem.Add(dxf);
            }
            root.Add(dxfsElem);
        }

        WriteXmlEntry(archive, "xl/styles.xml", new XDocument(root));
    }

    private static int Register<T>(T value, Dictionary<T, int> ids, XElement parent, Func<T, XElement> write)
        where T : notnull
    {
        if (!ids.TryGetValue(value, out int id))
        {
            id = ids.Count;
            ids.Add(value, id);
            parent.Add(write(value));
            parent.SetAttributeValue("count", ids.Count);
        }
        return id;
    }

    private static void WriteSharedStringsXml(ZipArchive archive, List<string> sharedStrings)
    {
        var root = new XElement(XlsxNamespaces.Spreadsheet + "sst",
            new XAttribute("xmlns", XlsxNamespaces.Spreadsheet.NamespaceName),
            new XAttribute("count", sharedStrings.Count),
            new XAttribute("uniqueCount", sharedStrings.Count));

        foreach (string str in sharedStrings)
        {
            var t = new XElement(XlsxNamespaces.Spreadsheet + "t", str);
            if (str.StartsWith(' ') || str.EndsWith(' ') || str.Contains('\n') || str.Contains('\r'))
                t.Add(new XAttribute(XNamespace.Xml + "space", "preserve"));

            root.Add(new XElement(XlsxNamespaces.Spreadsheet + "si", t));
        }

        WriteXmlEntry(archive, "xl/sharedStrings.xml", new XDocument(root));
    }

    private static void WriteWorksheetXml(
        ZipArchive archive,
        SpreadsheetWorksheet sheet,
        int sheetNumber,
        Dictionary<string, int> stringMap,
        Dictionary<CellStyle, int> styles,
        Dictionary<ConditionalFormatStyle, int> dxfStyles,
        List<DocumentDiagnostic> diagnostics,
        HashSet<string> diagnosticOnce)
    {
        var root = new XElement(XlsxNamespaces.Spreadsheet + "worksheet",
            new XAttribute("xmlns", XlsxNamespaces.Spreadsheet.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", XlsxNamespaces.Relationships.NamespaceName));

        // Sheet properties (outlines, summary directions)
        bool hasOutlines = sheet.Rows.Any(r => r.OutlineLevel > 0) || (sheet.Columns != null && sheet.Columns.Any(c => c.OutlineLevel > 0));
        if (hasOutlines || !sheet.SummaryRowsBelow || !sheet.SummaryColumnsRight)
        {
            var sheetPr = new XElement(XlsxNamespaces.Spreadsheet + "sheetPr");
            var outlinePr = new XElement(XlsxNamespaces.Spreadsheet + "outlinePr",
                new XAttribute("summaryBelow", sheet.SummaryRowsBelow ? "1" : "0"),
                new XAttribute("summaryRight", sheet.SummaryColumnsRight ? "1" : "0"));
            sheetPr.Add(outlinePr);
            root.Add(sheetPr);
        }

        // Sheet views (gridlines, freeze panes, split panes)
        if (!sheet.ShowGridlines || sheet.FrozenRows > 0 || sheet.FrozenColumns > 0 || sheet.SplitRows > 0 || sheet.SplitColumns > 0)
        {
            var sheetViews = new XElement(XlsxNamespaces.Spreadsheet + "sheetViews");
            var sheetView = new XElement(XlsxNamespaces.Spreadsheet + "sheetView",
                new XAttribute("workbookViewId", 0));
            if (!sheet.ShowGridlines)
                sheetView.Add(new XAttribute("showGridLines", "0"));

            if (sheet.FrozenRows > 0 || sheet.FrozenColumns > 0)
            {
                var pane = new XElement(XlsxNamespaces.Spreadsheet + "pane",
                    new XAttribute("state", "frozen"));
                if (sheet.FrozenColumns > 0)
                    pane.Add(new XAttribute("xSplit", sheet.FrozenColumns));
                if (sheet.FrozenRows > 0)
                    pane.Add(new XAttribute("ySplit", sheet.FrozenRows));

                string topLeft = new CellAddress(sheet.FrozenRows, sheet.FrozenColumns).Format();
                pane.Add(new XAttribute("topLeftCell", topLeft));
                string activePane = (sheet.FrozenRows > 0 && sheet.FrozenColumns > 0) ? "bottomRight" :
                                    sheet.FrozenRows > 0 ? "bottomLeft" : "topRight";
                pane.Add(new XAttribute("activePane", activePane));
                sheetView.Add(pane);

                var sel = new XElement(XlsxNamespaces.Spreadsheet + "selection",
                    new XAttribute("pane", activePane),
                    new XAttribute("activeCell", topLeft),
                    new XAttribute("sqref", topLeft));
                sheetView.Add(sel);
            }
            else if (sheet.SplitRows > 0 || sheet.SplitColumns > 0)
            {
                var pane = new XElement(XlsxNamespaces.Spreadsheet + "pane",
                    new XAttribute("state", "split"));
                if (sheet.SplitColumns > 0)
                    pane.Add(new XAttribute("xSplit", sheet.SplitColumns));
                if (sheet.SplitRows > 0)
                    pane.Add(new XAttribute("ySplit", sheet.SplitRows));

                string topLeft = new CellAddress(sheet.SplitRows, sheet.SplitColumns).Format();
                pane.Add(new XAttribute("topLeftCell", topLeft));
                string activePane = (sheet.SplitRows > 0 && sheet.SplitColumns > 0) ? "bottomRight" :
                                    sheet.SplitRows > 0 ? "bottomLeft" : "topRight";
                pane.Add(new XAttribute("activePane", activePane));
                sheetView.Add(pane);

                var sel = new XElement(XlsxNamespaces.Spreadsheet + "selection",
                    new XAttribute("pane", activePane),
                    new XAttribute("activeCell", topLeft),
                    new XAttribute("sqref", topLeft));
                sheetView.Add(sel);
            }
            sheetViews.Add(sheetView);
            root.Add(sheetViews);
        }

        // Column widths & outlines
        int maxCol = Math.Max(sheet.ColumnWidths.Count, sheet.Columns != null && sheet.Columns.Count > 0 ? sheet.Columns.Max(c => c.ColumnIndex) + 1 : 0);
        if (maxCol > 0)
        {
            var colsDict = sheet.Columns?.ToDictionary(c => c.ColumnIndex);
            var colsElem = new XElement(XlsxNamespaces.Spreadsheet + "cols");
            for (int c = 0; c < maxCol; c++)
            {
                SpreadsheetColumn? colObj = null;
                colsDict?.TryGetValue(c, out colObj);
                double? colWidth = colObj?.Width ?? (c < sheet.ColumnWidths.Count ? sheet.ColumnWidths[c] : null);
                bool isHidden = colObj?.Hidden ?? false;
                int outlineLevel = colObj?.OutlineLevel ?? 0;
                bool isCollapsed = colObj?.Collapsed ?? false;

                if (colWidth.HasValue || isHidden || outlineLevel > 0 || isCollapsed)
                {
                    double w = colWidth.HasValue ? XlsxColumnWidth.FromPoints(colWidth.Value) : 8.43;
                    var colElem = new XElement(XlsxNamespaces.Spreadsheet + "col",
                        new XAttribute("min", c + 1),
                        new XAttribute("max", c + 1),
                        new XAttribute("width", w.ToString(CultureInfo.InvariantCulture)));
                    if (colWidth.HasValue)
                        colElem.Add(new XAttribute("customWidth", "1"));
                    if (isHidden)
                        colElem.Add(new XAttribute("hidden", "1"));
                    if (outlineLevel > 0)
                        colElem.Add(new XAttribute("outlineLevel", outlineLevel));
                    if (isCollapsed)
                        colElem.Add(new XAttribute("collapsed", "1"));

                    colsElem.Add(colElem);
                }
            }
            if (colsElem.HasElements)
                root.Add(colsElem);
        }

        var sheetData = new XElement(XlsxNamespaces.Spreadsheet + "sheetData");
        var merges = new List<string>();

        foreach (SpreadsheetRow row in sheet.Rows)
        {
            var rowElem = new XElement(XlsxNamespaces.Spreadsheet + "row",
                new XAttribute("r", row.RowIndex + 1));

            if (row.Hidden)
                rowElem.Add(new XAttribute("hidden", "1"));

            if (row.OutlineLevel > 0)
                rowElem.Add(new XAttribute("outlineLevel", row.OutlineLevel));

            if (row.Collapsed)
                rowElem.Add(new XAttribute("collapsed", "1"));

            if (row.Height.HasValue)
            {
                rowElem.Add(new XAttribute("ht", row.Height.Value.ToString("F2", CultureInfo.InvariantCulture)));
                rowElem.Add(new XAttribute("customHeight", "1"));
            }

            foreach (SpreadsheetCell cell in row.Cells)
            {
                string a1 = cell.Address.Format();
                var cElem = new XElement(XlsxNamespaces.Spreadsheet + "c",
                    new XAttribute("r", a1));
                int styleIndex = styles[GetStyle(cell, diagnostics, diagnosticOnce)];
                if (styleIndex != 0)
                    cElem.Add(new XAttribute("s", styleIndex));

                if (!string.IsNullOrEmpty(cell.Formula))
                {
                    string f = XmlText(cell.Formula.TrimStart('='), diagnostics, diagnosticOnce);
                    cElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "f", f));
                    if (cell.Value.IsNumber)
                        cElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "v", cell.Value.Number.ToString(CultureInfo.InvariantCulture)));
                    else if (!string.IsNullOrEmpty(cell.CachedText))
                        cElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "v", XmlText(cell.CachedText, diagnostics, diagnosticOnce)));
                }
                else if (cell.Value.IsNumber)
                {
                    cElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "v",
                        cell.Value.Number.ToString(CultureInfo.InvariantCulture)));
                }
                else if (cell.Value.IsBoolean)
                {
                    cElem.Add(new XAttribute("t", "b"));
                    cElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "v", cell.Value.Boolean ? "1" : "0"));
                }
                else if (cell.Value.IsError)
                {
                    cElem.Add(new XAttribute("t", "e"));
                    cElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "v", cell.Value.Error.ToErrorString()));
                }
                else
                {
                    string str = XmlText(cell.Value.IsText ? (cell.Value.Text ?? string.Empty) : cell.CachedText, diagnostics, diagnosticOnce);
                    if (!string.IsNullOrEmpty(str))
                    {
                        if (stringMap.TryGetValue(str, out int sstIdx))
                        {
                            cElem.Add(new XAttribute("t", "s"));
                            cElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "v", sstIdx));
                        }
                        else
                        {
                            cElem.Add(new XAttribute("t", "inlineStr"));
                            cElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "is",
                                new XElement(XlsxNamespaces.Spreadsheet + "t", str)));
                        }
                    }
                }

                rowElem.Add(cElem);

                if (cell.ColumnSpan > 1 || cell.RowSpan > 1)
                {
                    var endAddr = new CellAddress(cell.Address.Row + cell.RowSpan - 1, cell.Address.Column + cell.ColumnSpan - 1);
                    merges.Add($"{a1}:{endAddr.Format()}");
                }
            }

            sheetData.Add(rowElem);
        }

        root.Add(sheetData);

        if (sheet.AutoFilter is { } autoFilter)
        {
            var afElem = new XElement(XlsxNamespaces.Spreadsheet + "autoFilter",
                new XAttribute("ref", autoFilter.Range.Format()));

            foreach (var col in autoFilter.Columns)
            {
                int colId = col.ColumnIndex - autoFilter.Range.Start.Column;
                if (colId >= 0)
                {
                    var colElem = new XElement(XlsxNamespaces.Spreadsheet + "filterColumn",
                        new XAttribute("colId", colId));

                    var filtersElem = new XElement(XlsxNamespaces.Spreadsheet + "filters");
                    if (!col.IncludeBlanks)
                        filtersElem.Add(new XAttribute("blank", "0"));

                    foreach (string val in col.Values)
                    {
                        filtersElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "filter",
                            new XAttribute("val", XmlText(val, diagnostics, diagnosticOnce))));
                    }

                    colElem.Add(filtersElem);
                    afElem.Add(colElem);
                }
            }

            root.Add(afElem);
        }

        if (merges.Count > 0)
        {
            var mergeCellsElem = new XElement(XlsxNamespaces.Spreadsheet + "mergeCells",
                new XAttribute("count", merges.Count));
            foreach (string mergeRef in merges)
            {
                mergeCellsElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "mergeCell",
                    new XAttribute("ref", mergeRef)));
            }
            root.Add(mergeCellsElem);
        }

        if (sheet.ConditionalFormats is { Count: > 0 } conditionalFormats)
        {
            int rulePriority = 1;
            foreach (ConditionalFormat cf in conditionalFormats)
            {
                if (cf.Ranges.Count == 0 || cf.Rules.Count == 0)
                    continue;

                string sqref = string.Join(" ", cf.Ranges.Select(r => r.Format()));
                var cfElem = new XElement(XlsxNamespaces.Spreadsheet + "conditionalFormatting",
                    new XAttribute("sqref", sqref));

                string firstCell = cf.Ranges[0].Start.Format();

                foreach (ConditionalFormatRule rule in cf.Rules)
                {
                    int prio = rule.Priority > 0 ? rule.Priority : rulePriority++;
                    var ruleElem = new XElement(XlsxNamespaces.Spreadsheet + "cfRule",
                        new XAttribute("type", FormatRuleType(rule.Type)),
                        new XAttribute("priority", prio));

                    if (rule.StopIfTrue)
                        ruleElem.Add(new XAttribute("stopIfTrue", "1"));

                    if (rule.Style is not null && dxfStyles.TryGetValue(rule.Style, out int dxfId))
                        ruleElem.Add(new XAttribute("dxfId", dxfId));

                    if (rule.Operator != ConditionalFormatOperator.None)
                        ruleElem.Add(new XAttribute("operator", FormatOperator(rule.Operator)));

                    switch (rule.Type)
                    {
                        case ConditionalFormatRuleType.CellIs:
                            if (!string.IsNullOrEmpty(rule.Formula1))
                                ruleElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "formula", XmlText(rule.Formula1, diagnostics, diagnosticOnce)));
                            if (!string.IsNullOrEmpty(rule.Formula2))
                                ruleElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "formula", XmlText(rule.Formula2, diagnostics, diagnosticOnce)));
                            break;

                        case ConditionalFormatRuleType.ContainsText:
                            ruleElem.SetAttributeValue("operator", "containsText");
                            ruleElem.SetAttributeValue("text", rule.Formula1 ?? string.Empty);
                            ruleElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "formula",
                                $"NOT(ISERROR(SEARCH(\"{rule.Formula1}\",{firstCell})))"));
                            break;

                        case ConditionalFormatRuleType.NotContainsText:
                            ruleElem.SetAttributeValue("operator", "notContains");
                            ruleElem.SetAttributeValue("text", rule.Formula1 ?? string.Empty);
                            ruleElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "formula",
                                $"ISERROR(SEARCH(\"{rule.Formula1}\",{firstCell}))"));
                            break;

                        case ConditionalFormatRuleType.BeginsWith:
                            ruleElem.SetAttributeValue("operator", "beginsWith");
                            ruleElem.SetAttributeValue("text", rule.Formula1 ?? string.Empty);
                            ruleElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "formula",
                                $"LEFT({firstCell},LEN(\"{rule.Formula1}\"))=\"{rule.Formula1}\""));
                            break;

                        case ConditionalFormatRuleType.EndsWith:
                            ruleElem.SetAttributeValue("operator", "endsWith");
                            ruleElem.SetAttributeValue("text", rule.Formula1 ?? string.Empty);
                            ruleElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "formula",
                                $"RIGHT({firstCell},LEN(\"{rule.Formula1}\"))=\"{rule.Formula1}\""));
                            break;

                        case ConditionalFormatRuleType.Expression:
                            if (!string.IsNullOrEmpty(rule.Formula1))
                                ruleElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "formula", XmlText(rule.Formula1.TrimStart('='), diagnostics, diagnosticOnce)));
                            break;

                        case ConditionalFormatRuleType.DuplicateValues:
                        case ConditionalFormatRuleType.UniqueValues:
                            break;

                        case ConditionalFormatRuleType.Blanks:
                            ruleElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "formula", $"LEN(TRIM({firstCell}))=0"));
                            break;

                        case ConditionalFormatRuleType.NoBlanks:
                            ruleElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "formula", $"LEN(TRIM({firstCell}))>0"));
                            break;

                        case ConditionalFormatRuleType.ColorScale:
                            if (rule.ColorScale is { } cs)
                            {
                                var csElem = new XElement(XlsxNamespaces.Spreadsheet + "colorScale");
                                if (cs.MidColor is null)
                                {
                                    csElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "cfvo", new XAttribute("type", "min")));
                                    csElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "cfvo", new XAttribute("type", "max")));
                                    csElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "color", new XAttribute("rgb", FormatColor(cs.MinColor))));
                                    csElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "color", new XAttribute("rgb", FormatColor(cs.MaxColor))));
                                }
                                else
                                {
                                    csElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "cfvo", new XAttribute("type", "min")));
                                    csElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "cfvo", new XAttribute("type", "percentile"), new XAttribute("val", "50")));
                                    csElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "cfvo", new XAttribute("type", "max")));
                                    csElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "color", new XAttribute("rgb", FormatColor(cs.MinColor))));
                                    csElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "color", new XAttribute("rgb", FormatColor(cs.MidColor.Value))));
                                    csElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "color", new XAttribute("rgb", FormatColor(cs.MaxColor))));
                                }
                                ruleElem.Add(csElem);
                            }
                            break;
                    }

                    cfElem.Add(ruleElem);
                }

                root.Add(cfElem);
            }
        }

        if (sheet.DataValidations is { Count: > 0 } dataValidations)
        {
            var validDvs = dataValidations.Where(dv => dv.Ranges.Count > 0).ToList();
            if (validDvs.Count > 0)
            {
                var dvsElem = new XElement(XlsxNamespaces.Spreadsheet + "dataValidations",
                    new XAttribute("count", validDvs.Count));

                foreach (DataValidation dv in validDvs)
                {
                    string sqref = string.Join(" ", dv.Ranges.Select(r => r.Format()));
                    var dvElem = new XElement(XlsxNamespaces.Spreadsheet + "dataValidation",
                        new XAttribute("sqref", sqref));

                    if (dv.Type != DataValidationType.None)
                        dvElem.Add(new XAttribute("type", FormatValidationType(dv.Type)));

                    if (dv.ErrorStyle != DataValidationErrorStyle.Stop)
                        dvElem.Add(new XAttribute("errorStyle", FormatValidationErrorStyle(dv.ErrorStyle)));

                    if (dv.Type is not (DataValidationType.List or DataValidationType.Custom) &&
                        dv.Operator != DataValidationOperator.Between)
                    {
                        dvElem.Add(new XAttribute("operator", FormatValidationOperator(dv.Operator)));
                    }

                    dvElem.Add(new XAttribute("allowBlank", dv.AllowBlank ? "1" : "0"));

                    // In OpenXML, showDropDown="1" suppresses the in-cell dropdown
                    if (!dv.ShowDropdown)
                        dvElem.Add(new XAttribute("showDropDown", "1"));

                    if (dv.ShowInputMessage)
                        dvElem.Add(new XAttribute("showInputMessage", "1"));

                    if (dv.ShowErrorMessage)
                        dvElem.Add(new XAttribute("showErrorMessage", "1"));

                    if (!string.IsNullOrEmpty(dv.InputTitle))
                        dvElem.Add(new XAttribute("promptTitle", XmlText(dv.InputTitle, diagnostics, diagnosticOnce)));

                    if (!string.IsNullOrEmpty(dv.InputMessage))
                        dvElem.Add(new XAttribute("prompt", XmlText(dv.InputMessage, diagnostics, diagnosticOnce)));

                    if (!string.IsNullOrEmpty(dv.ErrorTitle))
                        dvElem.Add(new XAttribute("errorTitle", XmlText(dv.ErrorTitle, diagnostics, diagnosticOnce)));

                    if (!string.IsNullOrEmpty(dv.ErrorMessage))
                        dvElem.Add(new XAttribute("error", XmlText(dv.ErrorMessage, diagnostics, diagnosticOnce)));

                    if (!string.IsNullOrEmpty(dv.Formula1))
                        dvElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "formula1", XmlText(dv.Formula1, diagnostics, diagnosticOnce)));

                    if (!string.IsNullOrEmpty(dv.Formula2))
                        dvElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "formula2", XmlText(dv.Formula2, diagnostics, diagnosticOnce)));

                    dvsElem.Add(dvElem);
                }

                root.Add(dvsElem);
            }
        }

        var notes = new List<(CellAddress Address, CellNote Note)>();
        var hyperlinks = new List<(CellAddress Address, CellHyperlink Hyperlink, string? RelId)>();
        var externalLinks = new List<(string RelId, string Target)>();
        int relIdCounter = 1;

        foreach (SpreadsheetRow row in sheet.Rows)
        {
            foreach (SpreadsheetCell cell in row.Cells)
            {
                if (cell.Note is not null)
                    notes.Add((cell.Address, cell.Note));
                if (cell.Hyperlink is not null)
                {
                    if (cell.Hyperlink.IsInternal)
                    {
                        hyperlinks.Add((cell.Address, cell.Hyperlink, null));
                    }
                    else
                    {
                        string rId = $"rId{relIdCounter++}";
                        hyperlinks.Add((cell.Address, cell.Hyperlink, rId));
                        externalLinks.Add((rId, cell.Hyperlink.Target));
                    }
                }
            }
        }

        string? commentsRelId = notes.Count > 0 ? $"rId{relIdCounter++}" : null;

        if (hyperlinks.Count > 0)
        {
            var hyperlinksElem = new XElement(XlsxNamespaces.Spreadsheet + "hyperlinks");
            foreach (var (addr, hl, relId) in hyperlinks)
            {
                var hlElem = new XElement(XlsxNamespaces.Spreadsheet + "hyperlink",
                    new XAttribute("ref", addr.Format()));
                if (relId is not null)
                {
                    hlElem.Add(new XAttribute(XlsxNamespaces.Relationships + "id", relId));
                }
                else
                {
                    string loc = hl.Target.TrimStart('#');
                    hlElem.Add(new XAttribute("location", loc));
                }
                if (!string.IsNullOrEmpty(hl.Tooltip))
                {
                    hlElem.Add(new XAttribute("tooltip", XmlText(hl.Tooltip, diagnostics, diagnosticOnce)));
                }
                hlElem.Add(new XAttribute("display", XmlText(addr.Format(), diagnostics, diagnosticOnce)));
                hyperlinksElem.Add(hlElem);
            }
            root.Add(hyperlinksElem);
        }

        WriteXmlEntry(archive, $"xl/worksheets/sheet{sheetNumber}.xml", new XDocument(root));

        if (externalLinks.Count > 0 || commentsRelId is not null)
        {
            WriteWorksheetRelationships(archive, sheetNumber, commentsRelId, externalLinks);
        }

        if (notes.Count > 0)
        {
            WriteWorksheetComments(archive, sheetNumber, notes, diagnostics, diagnosticOnce);
        }
    }

    private static void WriteWorksheetRelationships(
        ZipArchive archive,
        int sheetNumber,
        string? commentsRelId,
        List<(string RelId, string Target)> externalLinks)
    {
        var root = new XElement(XlsxNamespaces.PackageRelationships + "Relationships");
        if (commentsRelId is not null)
        {
            root.Add(new XElement(XlsxNamespaces.PackageRelationships + "Relationship",
                new XAttribute("Id", commentsRelId),
                new XAttribute("Type", XlsxNamespaces.CommentsRelationship),
                new XAttribute("Target", $"../comments{sheetNumber}.xml")));
        }
        foreach (var (rId, target) in externalLinks)
        {
            root.Add(new XElement(XlsxNamespaces.PackageRelationships + "Relationship",
                new XAttribute("Id", rId),
                new XAttribute("Type", XlsxNamespaces.HyperlinkRelationship),
                new XAttribute("Target", target),
                new XAttribute("TargetMode", "External")));
        }
        WriteXmlEntry(archive, $"xl/worksheets/_rels/sheet{sheetNumber}.xml.rels", new XDocument(root));
    }

    private static void WriteWorksheetComments(
        ZipArchive archive,
        int sheetNumber,
        List<(CellAddress Address, CellNote Note)> notes,
        List<DocumentDiagnostic> diagnostics,
        HashSet<string> diagnosticOnce)
    {
        var authors = notes.Select(n => n.Note.Author ?? "Author").Distinct().ToList();
        var authorsElem = new XElement(XlsxNamespaces.Spreadsheet + "authors");
        foreach (string author in authors)
            authorsElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "author", XmlText(author, diagnostics, diagnosticOnce)));

        var commentListElem = new XElement(XlsxNamespaces.Spreadsheet + "commentList");
        foreach (var (addr, note) in notes)
        {
            int authorId = authors.IndexOf(note.Author ?? "Author");
            var commentElem = new XElement(XlsxNamespaces.Spreadsheet + "comment",
                new XAttribute("ref", addr.Format()),
                new XAttribute("authorId", Math.Max(0, authorId)));

            var textElem = new XElement(XlsxNamespaces.Spreadsheet + "text");
            string cleanText = XmlText(note.Text, diagnostics, diagnosticOnce);
            textElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "r",
                new XElement(XlsxNamespaces.Spreadsheet + "t", cleanText)));
            commentElem.Add(textElem);
            commentListElem.Add(commentElem);
        }

        var root = new XElement(XlsxNamespaces.Spreadsheet + "comments",
            new XAttribute("xmlns", XlsxNamespaces.Spreadsheet.NamespaceName),
            authorsElem,
            commentListElem);

        WriteXmlEntry(archive, $"xl/comments{sheetNumber}.xml", new XDocument(root));
    }

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
                AddDiagnosticOnce(diagnostics, seen, XlsxDiagnosticCodes.ControlCharacterDropped, "A control character that XML cannot represent was dropped.");
            }
        }

        return clean.ToString();
    }

    private static string FormatColor(BColor color) =>
        string.Create(CultureInfo.InvariantCulture, $"{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");

    private static string FormatRuleType(ConditionalFormatRuleType type) => type switch
    {
        ConditionalFormatRuleType.CellIs => "cellIs",
        ConditionalFormatRuleType.ContainsText => "containsText",
        ConditionalFormatRuleType.NotContainsText => "notContainsText",
        ConditionalFormatRuleType.BeginsWith => "beginsWith",
        ConditionalFormatRuleType.EndsWith => "endsWith",
        ConditionalFormatRuleType.Expression => "expression",
        ConditionalFormatRuleType.DuplicateValues => "duplicateValues",
        ConditionalFormatRuleType.UniqueValues => "uniqueValues",
        ConditionalFormatRuleType.Blanks => "containsBlanks",
        ConditionalFormatRuleType.NoBlanks => "notContainsBlanks",
        ConditionalFormatRuleType.ColorScale => "colorScale",
        _ => "cellIs"
    };

    private static string FormatOperator(ConditionalFormatOperator op) => op switch
    {
        ConditionalFormatOperator.Equal => "equal",
        ConditionalFormatOperator.NotEqual => "notEqual",
        ConditionalFormatOperator.GreaterThan => "greaterThan",
        ConditionalFormatOperator.GreaterThanOrEqual => "greaterThanOrEqual",
        ConditionalFormatOperator.LessThan => "lessThan",
        ConditionalFormatOperator.LessThanOrEqual => "lessThanOrEqual",
        ConditionalFormatOperator.Between => "between",
        ConditionalFormatOperator.NotBetween => "notBetween",
        _ => string.Empty
    };

    private static string FormatValidationType(DataValidationType type) => type switch
    {
        DataValidationType.WholeNumber => "whole",
        DataValidationType.Decimal => "decimal",
        DataValidationType.List => "list",
        DataValidationType.Date => "date",
        DataValidationType.Time => "time",
        DataValidationType.TextLength => "textLength",
        DataValidationType.Custom => "custom",
        _ => "none"
    };

    private static string FormatValidationErrorStyle(DataValidationErrorStyle style) => style switch
    {
        DataValidationErrorStyle.Warning => "warning",
        DataValidationErrorStyle.Information => "information",
        _ => "stop"
    };

    private static string FormatValidationOperator(DataValidationOperator op) => op switch
    {
        DataValidationOperator.NotBetween => "notBetween",
        DataValidationOperator.Equal => "equal",
        DataValidationOperator.NotEqual => "notEqual",
        DataValidationOperator.GreaterThan => "greaterThan",
        DataValidationOperator.LessThan => "lessThan",
        DataValidationOperator.GreaterThanOrEqual => "greaterThanOrEqual",
        DataValidationOperator.LessThanOrEqual => "lessThanOrEqual",
        _ => "between"
    };
}
