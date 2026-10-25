using System.IO.Compression;
using System.Xml.Linq;

namespace Broiler.Documents.Odt.Tests;

public sealed class OdtInheritedCellPaddingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(5.4)]
    [InlineData(9)]
    public void Effective_Table_Padding_Is_Written_And_Cell_Overrides_Win(double padding)
    {
        var table = new DocumentTable(0, 2,
            [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1, padding: new TableCellPadding(2, 3, 4, 5))])],
            [100, 100], cellPadding: padding);
        var document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("first"), RichTextParagraph.Plain("second")])
            .WithTables([table]);
        using var stream = new MemoryStream();
        new OdtDocumentCodec().Write(document, stream);
        stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            using var styles = zip.GetEntry("content.xml")!.Open();
            var xml = XDocument.Load(styles);
            XNamespace style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
            XNamespace fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
            Assert.All(xml.Descendants(style + "table-cell-properties"), properties =>
                Assert.NotNull(properties.Attribute(fo + "padding-left")));
            Assert.NotEmpty(xml.Descendants(style + "table-cell-properties"));
        }
        stream.Position = 0;
        var restored = Assert.Single(new OdtDocumentCodec().Read(stream).Document.Tables);
        Assert.Equal(table.Rows[0].Cells[0].PaddingFor(table), restored.Rows[0].Cells[0].PaddingFor(restored));
        Assert.Equal(table.Rows[0].Cells[1].PaddingFor(table), restored.Rows[0].Cells[1].PaddingFor(restored));
    }
}
