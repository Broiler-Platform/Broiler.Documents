namespace Broiler.Documents.Pdf.Tests;

public sealed class CellRelativeLineWriterTests
{
    [Fact]
    public void Native_Pdf_Reports_Unsupported_Cell_Relative_Line_Placement()
    {
        var document = RichTextDocument.FromParagraphs([RichTextParagraph.Empty, RichTextParagraph.Empty])
            .WithTables([new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])], [100, 100])])
            .WithShapes([new DocumentShape(1, 20, 20, 60, 0, outline: Broiler.Graphics.Color.BColor.Black,
                geometry: ShapeGeometry.Line, offsetXFromCell: true)]);
        using var stream = new MemoryStream();
        var result = new PdfDocumentCodec().Write(document, stream);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("Cell-relative horizontal line placement"));
    }
}
