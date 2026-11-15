using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Broiler.Documents.Model.Spreadsheets;
using Xunit;

namespace Broiler.Documents.Ods.Tests;

/// <summary>
/// Office conformance issue 249: calc-data-source.ods gives every grid column
/// Calc's <c>Default</c> cell style (Calibri, bottom aligned) and every row a
/// height. Read as formatting, that kept all 1024 columns and the trailing
/// empty rows, and the CSV export carried a thousand commas a row. LibreOffice
/// prints and exports only the cells with content or visible attributes.
/// </summary>
public sealed class OdsIssue249Tests
{
    private const string Styles =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<office:document-styles xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
        "xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" " +
        "xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" office:version=\"1.3\">" +
        "<office:styles><style:style style:name=\"Default\" style:family=\"table-cell\">" +
        "<style:table-cell-properties style:vertical-align=\"bottom\"/>" +
        "<style:text-properties fo:font-family=\"Calibri\" fo:font-size=\"11pt\"/></style:style>" +
        "<style:style style:name=\"Boxed\" style:family=\"table-cell\">" +
        "<style:table-cell-properties fo:border=\"0.06pt solid #000000\"/></style:style>" +
        "<style:style style:name=\"Big\" style:family=\"table-cell\">" +
        "<style:text-properties fo:font-size=\"20pt\"/></style:style>" +
        "</office:styles></office:document-styles>";

    private static string Content(string extraRow) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<office:document-content xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
        "xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" " +
        "xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" " +
        "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" office:version=\"1.3\">" +
        "<office:automatic-styles><style:style style:name=\"ro1\" style:family=\"table-row\">" +
        "<style:table-row-properties style:row-height=\"0.2083in\"/></style:style></office:automatic-styles>" +
        "<office:body><office:spreadsheet><table:table table:name=\"Sheet1\">" +
        "<table:table-column table:number-columns-repeated=\"1024\" table:default-cell-style-name=\"Default\"/>" +
        "<table:table-row table:style-name=\"ro1\"><table:table-cell office:value-type=\"string\"><text:p>firstname</text:p></table:table-cell>" +
        "<table:table-cell office:value-type=\"string\"><text:p>lastname</text:p></table:table-cell>" +
        "<table:table-cell table:number-columns-repeated=\"1022\"/></table:table-row>" +
        "<table:table-row table:style-name=\"ro1\"><table:table-cell office:value-type=\"string\"><text:p>Rebecca</text:p></table:table-cell>" +
        "<table:table-cell office:value-type=\"string\"><text:p>Lopez</text:p></table:table-cell>" +
        "<table:table-cell table:number-columns-repeated=\"1022\"/></table:table-row>" +
        extraRow +
        "<table:table-row table:style-name=\"ro1\" table:number-rows-repeated=\"1048572\"><table:table-cell table:number-columns-repeated=\"1024\"/></table:table-row>" +
        "<table:table-row table:style-name=\"ro1\"><table:table-cell table:number-columns-repeated=\"1024\"/></table:table-row>" +
        "</table:table></office:spreadsheet></office:body></office:document-content>";

    private static SpreadsheetWorksheet Read(string extraRow = "")
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (Stream mime = archive.CreateEntry("mimetype", CompressionLevel.NoCompression).Open())
                mime.Write(Encoding.ASCII.GetBytes("application/vnd.oasis.opendocument.spreadsheet"));
            using (Stream styles = archive.CreateEntry("styles.xml").Open())
                styles.Write(Encoding.UTF8.GetBytes(Styles));
            using (Stream content = archive.CreateEntry("content.xml").Open())
                content.Write(Encoding.UTF8.GetBytes(Content(extraRow)));
        }

        stream.Position = 0;
        return Assert.Single(((OdsReadResult)new OdsDocumentCodec().Read(stream)).Workbook.Sheets);
    }

    [Fact]
    public void A_Column_Default_Style_And_Row_Heights_Do_Not_Widen_The_Used_Range()
    {
        SpreadsheetWorksheet sheet = Read();

        Assert.Equal(2, sheet.ColumnCount);
        Assert.Equal(2, sheet.RowCount);
        Assert.All(sheet.Rows, row => Assert.Equal(2, row.Cells.Count));
        Assert.Equal(2, sheet.ColumnWidths.Count);
        // The inherited style still formats the cells that are kept.
        Assert.Equal("Calibri", sheet[0, 0]?.Font.FontFamily);
    }

    [Fact]
    public void A_Visible_Border_Past_The_Data_Still_Widens_The_Used_Range()
    {
        SpreadsheetWorksheet sheet = Read(
            "<table:table-row table:style-name=\"ro1\"><table:table-cell table:number-columns-repeated=\"3\"/>" +
            "<table:table-cell table:style-name=\"Boxed\"/><table:table-cell table:number-columns-repeated=\"1020\"/></table:table-row>");

        Assert.Equal(4, sheet.ColumnCount);
        Assert.Equal(3, sheet.RowCount);
        Assert.True(sheet[2, 3]?.Borders.IsVisible);
    }

    [Fact]
    public void A_Number_Format_Or_Font_Alone_Past_The_Data_Does_Not_Widen_The_Used_Range()
    {
        // 4_v01.ods: rows past the data hold a cell styled only with a number
        // format; LibreOffice's CSV ends at the last address.
        SpreadsheetWorksheet sheet = Read(
            "<table:table-row table:style-name=\"ro1\" table:number-rows-repeated=\"3\"><table:table-cell/>" +
            "<table:table-cell table:style-name=\"Big\"/><table:table-cell table:number-columns-repeated=\"1022\"/></table:table-row>");

        Assert.Equal(2, sheet.RowCount);
        Assert.Equal(2, sheet.ColumnCount);
    }

    [Fact]
    public void A_Blank_Cell_Between_Values_Keeps_Its_Own_Style()
    {
        SpreadsheetWorksheet sheet = Read(
            "<table:table-row table:style-name=\"ro1\"><table:table-cell table:style-name=\"Big\"/>" +
            "<table:table-cell office:value-type=\"string\"><text:p>x</text:p></table:table-cell>" +
            "<table:table-cell table:number-columns-repeated=\"1022\"/></table:table-row>");

        Assert.Equal(3, sheet.RowCount);
        Assert.Equal(20f, sheet[2, 0]?.Font.FontSize);
    }
}
