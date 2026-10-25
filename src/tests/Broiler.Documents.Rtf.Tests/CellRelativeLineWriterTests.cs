namespace Broiler.Documents.Rtf.Tests;

public sealed class CellRelativeLineWriterTests
{
    [Fact]
    public void Rtf_Reports_Loss_Of_A_Cell_Relative_Line_When_Tables_Are_Flattened()
    {
        var document = RichTextDocument.FromParagraphs([RichTextParagraph.Empty, RichTextParagraph.Empty])
            .WithTables([new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])], [100, 100])])
            .WithShapes([new DocumentShape(1, 20, 20, 60, 0, outline: Broiler.Graphics.Color.BColor.Black,
                geometry: ShapeGeometry.Line, offsetXFromCell: true)]);
        using var stream = new MemoryStream();
        var result = new RtfDocumentCodec().Write(document, stream);
        Assert.Contains(result.Diagnostics, d => d.Code == "rtf.shape.cell-anchor");
    }
}
