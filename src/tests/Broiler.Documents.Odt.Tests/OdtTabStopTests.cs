namespace Broiler.Documents.Odt.Tests;

public sealed class OdtTabStopTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_Child_Tab_List_Replaces_Its_Parents_List_And_Round_Trips(bool empty)
    {
        string styles = "<style:style style:name=\"base\" style:family=\"paragraph\"><style:paragraph-properties>" +
            "<style:tab-stops><style:tab-stop style:position=\"36pt\"/></style:tab-stops></style:paragraph-properties></style:style>" +
            "<style:style style:name=\"child\" style:family=\"paragraph\" style:parent-style-name=\"base\"><style:paragraph-properties>" +
            "<style:tab-stops>" + (empty ? "" : "<style:tab-stop style:position=\"2in\" style:type=\"right\" style:leader-text=\".\"/>" +
                "<style:tab-stop style:position=\"200pt\" style:type=\"char\" style:char=\",\"/>") +
            "</style:tab-stops></style:paragraph-properties></style:style>";
        var document = OdtTestPackage.ReadBody("<text:p text:style-name=\"child\">title<text:tab/>12</text:p>", styles).Document;
        var tabs = Assert.IsType<ParagraphTabStops>(Assert.Single(document.Paragraphs).Style.TabStops);
        Assert.Equal(empty ? 0 : 2, tabs.Count);
        if (!empty)
        {
            Assert.Equal(new ParagraphTabStop(144, TabAlignment.Right, "."), tabs[0]);
            Assert.Equal(new ParagraphTabStop(200, TabAlignment.Decimal, DecimalCharacter: ','), tabs[1]);
        }
        using var written = new MemoryStream(OdtDocumentCodec.WriteToArray(document));
        Assert.Equal(tabs, Assert.Single(new OdtDocumentCodec().Read(written).Document.Paragraphs).Style.TabStops);
    }
}
