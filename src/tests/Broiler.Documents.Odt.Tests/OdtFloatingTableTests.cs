using Broiler.Documents.Model;
using Xunit;

namespace Broiler.Documents.Odt.Tests;

public sealed class OdtFloatingTableTests
{
    [Fact]
    public void Reads_Frame_Holding_Table_As_Floating_Table()
    {
        string styles = """
            <style:style style:name="frameStyle" style:family="graphic">
                <style:graphic-properties
                    style:horizontal-pos="center"
                    style:horizontal-rel="page-content"
                    style:vertical-pos="top"
                    style:vertical-rel="paragraph"
                    fo:margin-left="10pt"
                    fo:margin-right="15pt"
                    fo:margin-top="5pt"
                    fo:margin-bottom="8pt"
                    style:wrap="parallel"/>
            </style:style>
            """;

        string body = """
            <draw:frame draw:name="FloatTable" draw:style-name="frameStyle" text:anchor-type="paragraph"
                        svg:x="20pt" svg:y="30pt" svg:width="300pt">
                <table:table table:name="T1">
                    <table:table-column/>
                    <table:table-row>
                        <table:table-cell><text:p>Cell text</text:p></table:table-cell>
                    </table:table-row>
                </table:table>
            </draw:frame>
            """;

        DocumentReadResult read = OdtTestPackage.ReadStyled(body, styles);
        RichTextDocument doc = read.Document;

        DocumentTable table = Assert.Single(doc.Tables);
        Assert.NotNull(table.Position);
        TablePosition pos = table.Position!;

        Assert.Equal(TableAnchor.Margin, pos.HorizontalAnchor);
        Assert.Equal(TableAnchor.Text, pos.VerticalAnchor);
        Assert.Equal(TableAlignment.Center, pos.HorizontalAlignment);
        Assert.Equal(TableAlignment.Start, pos.VerticalAlignment);
        Assert.Equal(20.0, pos.X);
        Assert.Equal(30.0, pos.Y);
        Assert.Equal(10.0, pos.LeftFromText);
        Assert.Equal(15.0, pos.RightFromText);
        Assert.Equal(5.0, pos.TopFromText);
        Assert.Equal(8.0, pos.BottomFromText);
        Assert.True(pos.AllowOverlap);
    }

    [Fact]
    public void Floating_Table_Round_Trips_Through_Codec()
    {
        var position = new TablePosition(
            HorizontalAnchor: TableAnchor.Margin,
            X: 25.0,
            VerticalAnchor: TableAnchor.Text,
            Y: 40.0,
            HorizontalAlignment: TableAlignment.Start,
            VerticalAlignment: TableAlignment.None,
            LeftFromText: 12.0,
            RightFromText: 14.0,
            TopFromText: 6.0,
            BottomFromText: 8.0,
            AllowOverlap: true,
            ZOrder: 3);

        var table = new DocumentTable(
            paragraphIndex: 0,
            paragraphCount: 1,
            rows: [new TableRow([new TableCell(0, 1, 0, 1)])],
            columnWidths: [250.0],
            position: position);

        var doc = RichTextDocument.FromParagraphs([
            RichTextParagraph.Plain("Floating cell content")
        ]).WithTables([table]);

        byte[] bytes = OdtDocumentCodec.WriteToArray(doc);
        var codec = new OdtDocumentCodec();
        using var stream = new MemoryStream(bytes, writable: false);
        DocumentReadResult read = codec.Read(stream);
        RichTextDocument restored = read.Document;

        DocumentTable restoredTable = Assert.Single(restored.Tables);
        Assert.NotNull(restoredTable.Position);
        TablePosition restoredPos = restoredTable.Position!;

        Assert.Equal(TableAnchor.Margin, restoredPos.HorizontalAnchor);
        Assert.Equal(TableAnchor.Text, restoredPos.VerticalAnchor);
        Assert.Equal(TableAlignment.Start, restoredPos.HorizontalAlignment);
        Assert.Equal(25.0, restoredPos.X);
        Assert.Equal(40.0, restoredPos.Y);
        Assert.Equal(12.0, restoredPos.LeftFromText);
        Assert.Equal(14.0, restoredPos.RightFromText);
        Assert.Equal(6.0, restoredPos.TopFromText);
        Assert.Equal(8.0, restoredPos.BottomFromText);
        Assert.True(restoredPos.AllowOverlap);
    }
}
