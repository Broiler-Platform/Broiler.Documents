using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Broiler.Documents.Model;
using Xunit;

namespace Broiler.Documents.Ods.Tests;

/// <summary>
/// Office conformance issue 234: calc-data-source.ods declares all 1024 columns
/// of its grid and fills two. LibreOffice prints the used range; the projected
/// table had 1024 one-inch columns squeezed onto one page, which drew nothing.
/// </summary>
public sealed class OdsIssue234Tests
{
    private const string Content =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<office:document-content xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
        "xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" " +
        "xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" " +
        "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" office:version=\"1.3\">" +
        "<office:automatic-styles><style:style style:name=\"co1\" style:family=\"table-column\">" +
        "<style:table-column-properties style:column-width=\"0.6634in\"/></style:style></office:automatic-styles>" +
        "<office:body><office:spreadsheet><table:table table:name=\"Sheet1\">" +
        "<table:table-column table:style-name=\"co1\" table:number-columns-repeated=\"1024\"/>" +
        "<table:table-row><table:table-cell office:value-type=\"string\"><text:p>firstname</text:p></table:table-cell>" +
        "<table:table-cell office:value-type=\"string\"><text:p>lastname</text:p></table:table-cell>" +
        "<table:table-cell table:number-columns-repeated=\"1022\"/></table:table-row>" +
        "<table:table-row table:number-rows-repeated=\"1048575\"><table:table-cell table:number-columns-repeated=\"1024\"/></table:table-row>" +
        "</table:table></office:spreadsheet></office:body></office:document-content>";

    [Fact]
    public void A_Sheet_Keeps_Only_The_Used_Columns_At_Their_Stated_Width()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (Stream mime = archive.CreateEntry("mimetype", CompressionLevel.NoCompression).Open())
                mime.Write(Encoding.ASCII.GetBytes("application/vnd.oasis.opendocument.spreadsheet"));
            using (Stream content = archive.CreateEntry("content.xml").Open())
                content.Write(Encoding.UTF8.GetBytes(Content));
        }

        stream.Position = 0;
        var result = (OdsReadResult)new OdsDocumentCodec().Read(stream);

        Assert.Equal(2, result.Workbook.Sheets[0].ColumnWidths.Count);
        Assert.All(result.Workbook.Sheets[0].ColumnWidths, width => Assert.Equal(0.6634 * 72, width, 3));
        DocumentTable table = Assert.Single(result.Document.Tables);
        Assert.Equal(2, table.ColumnWidths.Count);
    }
}
