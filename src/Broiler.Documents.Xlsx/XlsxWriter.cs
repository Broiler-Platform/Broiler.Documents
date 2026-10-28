using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;

namespace Broiler.Documents.Xlsx;

internal static class XlsxWriter
{
    public static DocumentWriteResult Write(DocumentWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        long startPos = request.Destination.CanSeek ? request.Destination.Position : 0;
        SpreadsheetWorkbook workbook = SpreadsheetWorkbook.FromRichTextDocument(request.Document);

        // Collect unique shared strings
        var sharedStrings = new List<string>();
        var stringMap = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (SpreadsheetWorksheet sheet in workbook.Sheets)
        {
            foreach (SpreadsheetRow row in sheet.Rows)
            {
                foreach (SpreadsheetCell cell in row.Cells)
                {
                    if (string.IsNullOrEmpty(cell.Formula) &&
                        (cell.Value.IsText || (!cell.Value.IsNumber && !cell.Value.IsBoolean && !cell.Value.IsError && !string.IsNullOrEmpty(cell.CachedText))))
                    {
                        string str = cell.Value.IsText ? (cell.Value.Text ?? string.Empty) : cell.CachedText;
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
            WriteContentTypes(archive, workbook.Sheets.Count);

            // 2. _rels/.rels
            WriteRootRelationships(archive);

            // 3. xl/_rels/workbook.xml.rels
            WriteWorkbookRelationships(archive, workbook.Sheets.Count);

            // 4. xl/workbook.xml
            WriteWorkbookXml(archive, workbook);

            // 5. xl/styles.xml
            WriteStylesXml(archive);

            // 6. xl/sharedStrings.xml
            WriteSharedStringsXml(archive, sharedStrings);

            // 7. xl/worksheets/sheet{i}.xml
            for (int i = 0; i < workbook.Sheets.Count; i++)
            {
                WriteWorksheetXml(archive, workbook.Sheets[i], i + 1, stringMap);
            }
        }

        long bytesWritten = request.Destination.CanSeek ? Math.Max(0, request.Destination.Position - startPos) : 0;
        return new DocumentWriteResult(bytesWritten, null, DocumentResultStatus.Success);
    }

    private static void WriteContentTypes(ZipArchive archive, int sheetCount)
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

    private static void WriteWorkbookXml(ZipArchive archive, SpreadsheetWorkbook workbook)
    {
        var sheetsElem = new XElement(XlsxNamespaces.Spreadsheet + "sheets");

        for (int i = 0; i < workbook.Sheets.Count; i++)
        {
            sheetsElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "sheet",
                new XAttribute("name", workbook.Sheets[i].Name),
                new XAttribute("sheetId", i + 1),
                new XAttribute(XlsxNamespaces.Relationships + "id", $"rIdSheet{i + 1}")));
        }

        var root = new XElement(XlsxNamespaces.Spreadsheet + "workbook",
            new XAttribute("xmlns", XlsxNamespaces.Spreadsheet.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", XlsxNamespaces.Relationships.NamespaceName),
            sheetsElem);

        WriteXmlEntry(archive, "xl/workbook.xml", new XDocument(root));
    }

    private static void WriteStylesXml(ZipArchive archive)
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
                    new XAttribute("xfId", "0"))));

        WriteXmlEntry(archive, "xl/styles.xml", new XDocument(root));
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
            if (str.StartsWith(' ') || str.EndsWith(' '))
                t.Add(new XAttribute(XNamespace.Xml + "space", "preserve"));

            root.Add(new XElement(XlsxNamespaces.Spreadsheet + "si", t));
        }

        WriteXmlEntry(archive, "xl/sharedStrings.xml", new XDocument(root));
    }

    private static void WriteWorksheetXml(
        ZipArchive archive,
        SpreadsheetWorksheet sheet,
        int sheetNumber,
        Dictionary<string, int> stringMap)
    {
        var root = new XElement(XlsxNamespaces.Spreadsheet + "worksheet",
            new XAttribute("xmlns", XlsxNamespaces.Spreadsheet.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", XlsxNamespaces.Relationships.NamespaceName));

        // Column widths
        if (sheet.ColumnWidths.Count > 0)
        {
            var colsElem = new XElement(XlsxNamespaces.Spreadsheet + "cols");
            for (int c = 0; c < sheet.ColumnWidths.Count; c++)
            {
                double width = Math.Max(1.0, sheet.ColumnWidths[c] / 7.5);
                colsElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "col",
                    new XAttribute("min", c + 1),
                    new XAttribute("max", c + 1),
                    new XAttribute("width", width.ToString("F2", CultureInfo.InvariantCulture)),
                    new XAttribute("customWidth", "1")));
            }
            root.Add(colsElem);
        }

        var sheetData = new XElement(XlsxNamespaces.Spreadsheet + "sheetData");
        var merges = new List<string>();

        foreach (SpreadsheetRow row in sheet.Rows)
        {
            var rowElem = new XElement(XlsxNamespaces.Spreadsheet + "row",
                new XAttribute("r", row.RowIndex + 1));

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

                if (!string.IsNullOrEmpty(cell.Formula))
                {
                    string f = cell.Formula.TrimStart('=');
                    cElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "f", f));
                    if (!string.IsNullOrEmpty(cell.CachedText))
                        cElem.Add(new XElement(XlsxNamespaces.Spreadsheet + "v", cell.CachedText));
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
                    string str = cell.Value.IsText ? (cell.Value.Text ?? string.Empty) : cell.CachedText;
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

        WriteXmlEntry(archive, $"xl/worksheets/sheet{sheetNumber}.xml", new XDocument(root));
    }

    private static void WriteXmlEntry(ZipArchive archive, string entryName, XDocument document)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = false,
            Indent = false
        });
        document.WriteTo(writer);
    }
}
