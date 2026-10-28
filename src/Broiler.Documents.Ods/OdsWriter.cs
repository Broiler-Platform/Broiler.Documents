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

namespace Broiler.Documents.Ods;

internal static class OdsWriter
{
    public static DocumentWriteResult Write(DocumentWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        long startPos = request.Destination.CanSeek ? request.Destination.Position : 0;
        SpreadsheetWorkbook workbook = SpreadsheetWorkbook.FromRichTextDocument(request.Document);

        using (var archive = new ZipArchive(request.Destination, ZipArchiveMode.Create, leaveOpen: true))
        {
            // 1. mimetype (MUST be first entry, uncompressed)
            WriteMimeType(archive);

            // 2. META-INF/manifest.xml
            WriteManifestXml(archive);

            // 3. styles.xml
            WriteStylesXml(archive);

            // 4. content.xml
            WriteContentXml(archive, workbook);
        }

        long bytesWritten = request.Destination.CanSeek ? Math.Max(0, request.Destination.Position - startPos) : 0;
        return new DocumentWriteResult(bytesWritten, null, DocumentResultStatus.Success);
    }

    private static void WriteMimeType(ZipArchive archive)
    {
        ZipArchiveEntry entry = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
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

    private static void WriteContentXml(ZipArchive archive, SpreadsheetWorkbook workbook)
    {
        var spreadsheetElem = new XElement(OdsNamespaces.Office + "spreadsheet");

        foreach (SpreadsheetWorksheet sheet in workbook.Sheets)
        {
            var tableElem = new XElement(OdsNamespaces.Table + "table",
                new XAttribute(OdsNamespaces.Table + "name", sheet.Name));

            int maxCol = Math.Max(1, sheet.ColumnCount);
            tableElem.Add(new XElement(OdsNamespaces.Table + "table-column",
                new XAttribute(OdsNamespaces.Table + "number-columns-repeated", maxCol)));

            // Track covered cells from merges
            var covered = new HashSet<CellAddress>();

            foreach (SpreadsheetRow row in sheet.Rows)
            {
                var rowElem = new XElement(OdsNamespaces.Table + "table-row");

                foreach (SpreadsheetCell cell in row.Cells)
                {
                    if (covered.Contains(cell.Address))
                    {
                        rowElem.Add(new XElement(OdsNamespaces.Table + "covered-table-cell"));
                        continue;
                    }

                    var cellElem = new XElement(OdsNamespaces.Table + "table-cell");

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
                        string f = cell.Formula.TrimStart('=');
                        cellElem.Add(new XAttribute(OdsNamespaces.Table + "formula", $"of:={f}"));
                    }

                    if (cell.Value.IsNumber)
                    {
                        cellElem.Add(new XAttribute(OdsNamespaces.Office + "value-type", "float"));
                        cellElem.Add(new XAttribute(OdsNamespaces.Office + "value", cell.Value.Number.ToString(CultureInfo.InvariantCulture)));
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

                    string text = !string.IsNullOrEmpty(cell.CachedText) ? cell.CachedText : (cell.Value.Text ?? string.Empty);
                    cellElem.Add(new XElement(OdsNamespaces.Text + "p", text));

                    rowElem.Add(cellElem);
                }

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
            new XElement(OdsNamespaces.Office + "body", spreadsheetElem));

        WriteXmlEntry(archive, "content.xml", new XDocument(root));
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
