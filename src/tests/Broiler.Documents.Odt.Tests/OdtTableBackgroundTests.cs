using Broiler.Graphics.Color;

namespace Broiler.Documents.Odt.Tests;

public sealed class OdtTableBackgroundTests
{
    [Fact]
    public void Transparent_Cells_Reveal_The_Row_And_Table_Backgrounds()
    {
        const string styles = "<style:style style:name=\"table\" style:family=\"table\"><style:table-properties fo:background-color=\"#d9d9d9\"/></style:style>" +
            "<style:style style:name=\"row\" style:family=\"table-row\"><style:table-row-properties fo:background-color=\"#ff0000\"/></style:style>" +
            "<style:style style:name=\"clear\" style:family=\"table-cell\"><style:table-cell-properties fo:background-color=\"transparent\"/></style:style>" +
            "<style:style style:name=\"blue\" style:family=\"table-cell\"><style:table-cell-properties fo:background-color=\"#0000ff\"/></style:style>";
        const string body = "<table:table table:style-name=\"table\"><table:table-row>" +
            "<table:table-cell table:style-name=\"clear\"><text:p>a</text:p></table:table-cell></table:table-row>" +
            "<table:table-row table:style-name=\"row\"><table:table-cell><text:p>b</text:p></table:table-cell>" +
            "<table:table-cell table:style-name=\"blue\"><text:p>c</text:p></table:table-cell></table:table-row></table:table>";
        var document = OdtTestPackage.ReadBody(body, styles).Document;
        DocumentTable table = Assert.Single(document.Tables);
        Assert.Equal(new BColor(217, 217, 217), table.Rows[0].Cells[0].Shading);
        Assert.Equal(BColor.Red, table.Rows[1].Cells[0].Shading);
        Assert.Equal(new BColor(0, 0, 255), table.Rows[1].Cells[1].Shading);
        using var written = new MemoryStream(OdtDocumentCodec.WriteToArray(document));
        Assert.Equal(table.Rows[0].Cells[0].Shading,
            Assert.Single(new OdtDocumentCodec().Read(written).Document.Tables).Rows[0].Cells[0].Shading);
    }
}
