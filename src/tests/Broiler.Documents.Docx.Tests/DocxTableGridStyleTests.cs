using System.IO.Compression;
using System.Xml.Linq;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Docx.Tests;

public sealed class DocxTableGridStyleTests
{
    private static string Cell(string text, string properties = "") => "<w:tc><w:tcPr>" + properties + "</w:tcPr>" + DocxTestPackage.Paragraph(text) + "</w:tc>";
    private static string Row(string cells, string properties = "", string exceptions = "") => "<w:tr><w:tblPrEx>" + exceptions + "</w:tblPrEx><w:trPr>" + properties + "</w:trPr>" + cells + "</w:tr>";
    private static string Table(string rows, string properties = "", int columns = 3) => "<w:tbl><w:tblPr><w:tblStyle w:val='s'/>" + properties + "</w:tblPr><w:tblGrid>" + string.Concat(Enumerable.Repeat("<w:gridCol w:w='1000'/>", columns)) + "</w:tblGrid>" + rows + "</w:tbl>";
    private static string Style(string properties) => "<w:style w:type='table' w:styleId='s'>" + properties + "</w:style>";
    private static string Conditional(string kind, string fill) => "<w:tblStylePr w:type='" + kind + "'><w:tcPr><w:shd w:fill='" + fill + "'/></w:tcPr></w:tblStylePr>";
    private static DocumentReadResult Read(string table, string styles = "") => DocxTestPackage.ReadStyled(table, styles);
    private static readonly BColor Red = BColor.FromArgb(255, 0, 0), Blue = BColor.FromArgb(0, 0, 255);

    [Fact]
    public void Leading_Grid_Omissions_Place_The_First_Real_Cell_In_Its_Actual_Column()
    {
        var table = Assert.Single(Read(Table(Row(Cell("a") + Cell("b"), "<w:gridBefore w:val='1'/>"))).Document.Tables);
        Assert.Equal(new[] { 1, 2 }, table.Rows[0].Cells.Select(c => c.ColumnIndex));
    }

    private static RichTextDocument SparseModel() => RichTextDocument.FromPlainText("cell").WithTables(
        [new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 1)])], [50, 50, 50])]);
    private static XDocument Xml(RichTextDocument document)
    {
        using var bytes = new MemoryStream(DocxDocumentCodec.WriteToArray(document));
        using var zip = new ZipArchive(bytes, ZipArchiveMode.Read);
        using var stream = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(stream);
    }

    [Fact]
    public void Leading_Grid_Omissions_Are_Exported_And_Round_Trip()
    {
        var source = SparseModel(); XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        Assert.Equal("1", (string?)Xml(source).Descendants(w + "gridBefore").Single().Attribute(w + "val"));
        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(source));
        Assert.Equal(1, Assert.Single(new DocxDocumentCodec().Read(stream).Document.Tables).Rows[0].Cells[0].ColumnIndex);
    }

    [Fact]
    public void Trailing_Grid_Omissions_Are_Explicit_In_Export()
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        Assert.Equal("1", (string?)Xml(SparseModel()).Descendants(w + "gridAfter").Single().Attribute(w + "val"));
        var read = Read(Table(Row(Cell("only"), "<w:gridAfter w:val='2'/>"), columns: 0)).Document;
        Assert.Equal(3, Assert.Single(read.Tables).ColumnWidths.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Horizontal_Bands_Use_Their_Declared_Row_Count(int band)
    {
        string rows = string.Concat(Enumerable.Range(0, 4).Select(i => Row(Cell(i.ToString()))));
        var document = Read(Table(rows, "<w:tblLook w:val='0000'/>"), Style("<w:tblPr><w:tblLook w:val='0000'/><w:tblStyleRowBandSize w:val='" + band + "'/></w:tblPr>" +
            Conditional("band1Horz", "FF0000") + Conditional("band2Horz", "0000FF"))).Document;
        var expected = band == 1 ? new[] { Red, Blue, Red, Blue } : new[] { Red, Red, Blue, Blue };
        Assert.Equal(expected, document.Tables[0].Rows.Select(r => r.Cells[0].Shading));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Vertical_Bands_Use_Their_Declared_Grid_Width(int band)
    {
        var document = Read(Table(Row(Cell("a") + Cell("b") + Cell("c") + Cell("d")), "<w:tblLook w:val='0000'/>", columns: 4),
            Style("<w:tblPr><w:tblLook w:val='0000'/><w:tblStyleColBandSize w:val='" + band + "'/></w:tblPr>" +
                Conditional("band1Vert", "FF0000") + Conditional("band2Vert", "0000FF"))).Document;
        Assert.Equal(band == 1 ? new[] { Red, Blue, Red, Blue } : new[] { Red, Red, Blue, Blue }, document.Tables[0].Rows[0].Cells.Select(c => c.Shading));
    }

    [Fact]
    public void Row_Exception_Shading_Is_Local_And_Cell_Shading_Still_Wins()
    {
        string rows = Row(Cell("red") + Cell("blue", "<w:shd w:fill='0000FF'/>"), exceptions: "<w:shd w:fill='FF0000'/>") + Row(Cell("base"));
        var table = Read(Table(rows, "<w:shd w:fill='00FF00'/>" )).Document.Tables[0];
        Assert.Equal(Red, table.Rows[0].Cells[0].Shading); Assert.Equal(Blue, table.Rows[0].Cells[1].Shading);
        Assert.Equal(BColor.FromArgb(0, 255, 0), table.Rows[1].Cells[0].Shading);
    }

    [Fact]
    public void Row_Exception_Cell_Margins_Are_Per_Edge_And_Local()
    {
        string own = "<w:tcMar><w:right w:w='600' w:type='dxa'/></w:tcMar>";
        string exceptions = "<w:tblCellMar><w:left w:w='400' w:type='dxa'/><w:top w:w='0' w:type='dxa'/></w:tblCellMar>";
        var table = Read(Table(Row(Cell("a", own), exceptions: exceptions) + Row(Cell("b")),
            "<w:tblCellMar><w:left w:w='100' w:type='dxa'/><w:top w:w='200' w:type='dxa'/></w:tblCellMar>")).Document.Tables[0];
        Assert.Equal(new TableCellPadding(20, 0, 30, 0), table.Rows[0].Cells[0].Padding);
        Assert.Equal(5, table.Rows[1].Cells[0].Padding!.Value.Left); Assert.Equal(10, table.Rows[1].Cells[0].Padding!.Value.Top);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_Grids_Are_Reconstructed_From_Cell_And_Omitted_Widths(bool gaps)
    {
        string cells = gaps ? Cell("cell", "<w:tcW w:type='dxa' w:w='3000'/>") :
            Cell("small", "<w:tcW w:type='dxa' w:w='1000'/>") + Cell("large", "<w:tcW w:type='dxa' w:w='3000'/>");
        string row = gaps ? "<w:gridBefore w:val='1'/><w:gridAfter w:val='1'/><w:wBefore w:type='dxa' w:w='2000'/><w:wAfter w:type='dxa' w:w='1000'/>" : "";
        var table = Read(Table(Row(cells, row), columns: 0)).Document.Tables[0];
        Assert.Equal(gaps ? new[] { 100d, 150d, 50d } : new[] { 50d, 150d }, table.ColumnWidths);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Vertical_Merges_Require_The_Same_Grid_Span(int continuationSpan)
    {
        string rows = Row(Cell("top", "<w:gridSpan w:val='2'/><w:vMerge w:val='restart'/>")) +
            Row(Cell("following", "<w:gridSpan w:val='" + continuationSpan + "'/><w:vMerge/>"));
        var table = Read(Table(rows)).Document.Tables[0];
        Assert.Equal(continuationSpan == 2 ? 2 : 1, table.Rows[0].Cells[0].RowSpan);
        Assert.Equal(continuationSpan == 2, table.Rows[1].Cells[0].IsRowSpanContinuation);
    }

    [Fact]
    public void Omitted_Edge_Cells_Do_Not_Give_Their_First_Or_Last_Column_Format_To_The_Middle_Cell()
    {
        var table = Read(Table(Row(Cell("middle"), "<w:gridBefore w:val='1'/><w:gridAfter w:val='1'/>"), "<w:tblLook w:val='0180'/>"),
            Style(Conditional("firstCol", "FF0000") + Conditional("lastCol", "0000FF"))).Document.Tables[0];
        Assert.Equal(BColor.Empty, table.Rows[0].Cells[0].Shading);
    }

    [Fact]
    public void Banding_After_A_Merged_Cell_Keeps_The_Native_Cell_Count_Rule()
    {
        var table = Read(Table(Row(Cell("merged", "<w:gridSpan w:val='2'/>") + Cell("next")), "<w:tblLook w:val='0000'/>", columns: 3),
            Style("<w:tblPr><w:tblLook w:val='0000'/></w:tblPr>" + Conditional("band1Vert", "FF0000") + Conditional("band2Vert", "0000FF"))).Document.Tables[0];
        Assert.Equal(new[] { Red, Blue }, table.Rows[0].Cells.Select(c => c.Shading));
    }

    [Fact]
    public void Extreme_Grid_Counts_Are_Bounded_And_Diagnosed()
    {
        var result = Read(Table(Row(Cell("a"), "<w:gridBefore w:val='2147483647'/><w:gridAfter w:val='2147483647'/>"), columns: 0));
        Assert.Equal(4096, result.Document.Tables[0].Rows[0].Cells[0].ColumnIndex);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.table.grid");
    }
}
