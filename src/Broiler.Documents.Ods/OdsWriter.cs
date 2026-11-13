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
            WriteManifestXml(archive);

            // 3. styles.xml
            WriteStylesXml(archive);

            // 4. content.xml
            WriteContentXml(archive, workbook, diagnostics, diagnosticOnce);
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

    private static void WriteManifestXml(ZipArchive archive)
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

        WriteXmlEntry(archive, "META-INF/manifest.xml", new XDocument(root));
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

        foreach (SpreadsheetWorksheet sheet in workbook.Sheets)
        {
            var tableElem = new XElement(OdsNamespaces.Table + "table",
                new XAttribute(OdsNamespaces.Table + "name", XmlText(sheet.Name, diagnostics, diagnosticOnce)));

            int maxCol = Math.Max(1, sheet.ColumnCount);
            int widthCount = Math.Min(maxCol, sheet.ColumnWidths.Count);
            for (int column = 0; column < widthCount; column++)
                tableElem.Add(new XElement(OdsNamespaces.Table + "table-column",
                    styles.Dimension("table-column", "column-width", sheet.ColumnWidths[column])));
            if (widthCount < maxCol)
                tableElem.Add(new XElement(OdsNamespaces.Table + "table-column",
                    new XAttribute(OdsNamespaces.Table + "number-columns-repeated", maxCol - widthCount)));

            // Track covered cells from merges
            var covered = new HashSet<CellAddress>();

            foreach (SpreadsheetRow row in sheet.Rows)
            {
                var rowElem = new XElement(OdsNamespaces.Table + "table-row",
                    styles.Dimension("table-row", "row-height", row.Height));

                int column = 0;
                foreach (SpreadsheetCell cell in row.Cells.OrderBy(cell => cell.Address.Column))
                {
                    while (column < cell.Address.Column)
                        AppendEmptySlot(rowElem, covered.Contains(new CellAddress(row.RowIndex, column++)));

                    if (column != cell.Address.Column || covered.Contains(cell.Address))
                        continue;

                    column++;
                    var cellElem = new XElement(OdsNamespaces.Table + "table-cell", styles.Cell(cell, cell.Font.FontFamily is { } family ? XmlText(family, diagnostics, diagnosticOnce) : null));

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
                    else
                    {
                        cellElem.Add(new XAttribute(OdsNamespaces.Office + "value-type", "string"));
                    }

                    string text = XmlText(!string.IsNullOrEmpty(cell.CachedText) ? cell.CachedText : (cell.Value.Text ?? string.Empty), diagnostics, diagnosticOnce);
                    cellElem.Add(new XElement(OdsNamespaces.Text + "p", text));

                    rowElem.Add(cellElem);
                }

                while (covered.Contains(new CellAddress(row.RowIndex, column++)))
                    AppendEmptySlot(rowElem, covered: true);

                tableElem.Add(rowElem);
            }

            spreadsheetElem.Add(tableElem);
        }

        var root = new XElement(OdsNamespaces.Office + "document-content",
            new XAttribute(XNamespace.Xmlns + "office", OdsNamespaces.Office.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "table", OdsNamespaces.Table.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "text", OdsNamespaces.Text.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "style", OdsNamespaces.Style.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "fo", OdsNamespaces.Fo.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "number", OdsNamespaces.Number.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "of", "urn:oasis:names:tc:opendocument:xmlns:of:1.2"),
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
}
