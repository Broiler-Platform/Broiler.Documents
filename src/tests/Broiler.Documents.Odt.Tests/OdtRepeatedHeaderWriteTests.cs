using System.IO;
using Broiler.Documents.Model;

namespace Broiler.Documents.Odt.Tests;

public sealed class OdtRepeatedHeaderWriteTests
{
    [Fact]
    public void Leading_Headers_Survive_Writing_Without_Changing_Row_Order()
    {
        var document = RichTextDocument.FromPlainText("First\nSecond\nBody").WithTables([
            new DocumentTable(0, 3, [
                new TableRow([new TableCell(0, 1, 0)], isHeader: true),
                new TableRow([new TableCell(1, 1, 0)], isHeader: true),
                new TableRow([new TableCell(2, 1, 0)])], [100])]);
        using var stream = new MemoryStream();
        var codec = new OdtDocumentCodec();
        codec.Write(document, stream);
        stream.Position = 0;
        var reopened = codec.Read(stream).Document;
        Assert.Equal(document.PlainText, reopened.PlainText);
        Assert.Equal(new[] { true, true, false }, Assert.Single(reopened.Tables).Rows.Select(row => row.IsHeader));
    }
}
