using System.IO;
using Broiler.Documents.Model;

namespace Broiler.Documents.Docx.Tests;

public sealed class DocxAuthoredShapeAnchorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Writing_Retains_Column_Versus_Text_Area_Relative_Placement(bool column)
    {
        var shape = new DocumentShape(0, 25, 10, 100, 50, fill: ShapeFill.Solid(Broiler.Graphics.Color.BColor.White)) { OffsetXFromColumn = column };
        var document = RichTextDocument.FromPlainText("Anchor").WithShapes([shape]);
        using var stream = new MemoryStream(); var codec = new DocxDocumentCodec();
        codec.Write(document, stream); stream.Position = 0;
        var reopened = Assert.Single(codec.Read(stream).Document.Shapes);
        Assert.Equal(column, reopened.OffsetXFromColumn);
        Assert.Equal(25, reopened.OffsetX);
    }
}
