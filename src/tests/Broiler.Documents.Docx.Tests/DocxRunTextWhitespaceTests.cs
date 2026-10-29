namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// White space in run text as Word and LibreOffice read it: a line feed is a
/// space, and without <c>xml:space="preserve"</c> the ends are trimmed and a
/// tab is a space (LibreOffice's <c>OOXMLFastContextHandler::text</c>,
/// tdf#108806 and tdf#108995). <c>tdf123163-1.docx</c> starts every
/// <c>w:t</c> with a line feed, and each one was read as a line break.
/// </summary>
public sealed class DocxRunTextWhitespaceTests
{
    private static string Text(string bodyXml) =>
        Assert.Single(DocxTestPackage.ReadBody(bodyXml).Document.Paragraphs).Text;

    [Fact]
    public void A_Line_Feed_In_Preserved_Run_Text_Is_A_Space()
    {
        string text = Text(
            "<w:p><w:r><w:t xml:space=\"preserve\">\nxxx xx</w:t></w:r>" +
            "<w:r><w:t xml:space=\"preserve\">\n2</w:t></w:r>" +
            "<w:r><w:t xml:space=\"preserve\">\n xxxx</w:t></w:r></w:p>");

        Assert.Equal(" xxx xx 2  xxxx", text);
    }

    [Fact]
    public void A_Written_Carriage_Return_And_Line_Feed_Is_One_Space()
    {
        Assert.Equal("one two", Text("<w:p><w:r><w:t xml:space=\"preserve\">one\r\ntwo</w:t></w:r></w:p>"));
    }

    [Fact]
    public void Unpreserved_Run_Text_Loses_Its_End_Spaces_And_Its_Tabs_Are_Spaces()
    {
        Assert.Equal("a b c", Text("<w:p><w:r><w:t>\n  a\tb\nc \t</w:t></w:r></w:p>"));
    }

    [Fact]
    public void Preserve_Stated_On_An_Ancestor_Keeps_The_Spaces()
    {
        Assert.Equal(" a\tb ", Text("<w:p xml:space=\"preserve\"><w:r><w:t> a\tb </w:t></w:r></w:p>"));
    }

    [Fact]
    public void Default_Stated_Below_A_Preserving_Ancestor_Trims_Again()
    {
        Assert.Equal("a", Text("<w:p xml:space=\"preserve\"><w:r><w:t xml:space=\"default\"> a </w:t></w:r></w:p>"));
    }

    [Fact]
    public void Deleted_Run_Text_Follows_The_Same_Rule()
    {
        string text = Text(
            "<w:p><w:del w:id=\"1\" w:author=\"a\"><w:r>" +
            "<w:delText xml:space=\"preserve\">\ngone</w:delText></w:r></w:del></w:p>");

        Assert.Equal(" gone", text);
    }
}
