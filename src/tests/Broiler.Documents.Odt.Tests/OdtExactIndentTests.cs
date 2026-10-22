namespace Broiler.Documents.Odt.Tests;

public sealed class OdtExactIndentTests
{
    [Theory]
    [InlineData("5.25pt", 5.25f)]
    [InlineData("-5.25pt", -5.25f)]
    public void Exact_Indents_Inherit_Override_And_Round_Trip(string first, float expectedFirst)
    {
        string styles = "<style:style style:name=\"base\" style:family=\"paragraph\"><style:paragraph-properties " +
            "fo:margin-left=\"25pt\" fo:margin-right=\"9.5pt\"/></style:style>" +
            "<style:style style:name=\"child\" style:family=\"paragraph\" style:parent-style-name=\"base\">" +
            "<style:paragraph-properties fo:margin-left=\"0pt\" fo:text-indent=\"" + first + "\"/></style:style>";
        var document = OdtTestPackage.ReadBody("<text:p text:style-name=\"child\">body</text:p>", styles).Document;
        var style = Assert.Single(document.Paragraphs).Style;
        Assert.Equal(0, style.LeftIndent);
        Assert.Equal(9.5f, style.RightIndent);
        Assert.Equal(expectedFirst, style.FirstLineIndent);
        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(document));
        Assert.Equal(style, Assert.Single(new OdtDocumentCodec().Read(stream).Document.Paragraphs).Style);
    }
}
