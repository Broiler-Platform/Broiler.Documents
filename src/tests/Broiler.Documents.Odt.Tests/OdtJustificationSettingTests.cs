namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// LibreOffice's <c>JustifyLinesWithShrinking</c> setting, which an ODT saved
/// from a Word 2013 document keeps: its justified lines shrink their spaces.
/// </summary>
public sealed class OdtJustificationSettingTests
{
    private static string Settings(string set, string name, string value) =>
        "<office:document-settings xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
        "xmlns:config=\"urn:oasis:names:tc:opendocument:xmlns:config:1.0\"><office:settings>" +
        "<config:config-item-set config:name=\"" + set + "\">" +
        "<config:config-item config:name=\"" + name + "\" config:type=\"boolean\">" + value +
        "</config:config-item></config:config-item-set></office:settings></office:document-settings>";

    private static RichTextDocument Read(string settings)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal) { ["settings.xml"] = settings };
        using var stream = new MemoryStream(OdtTestPackage.FromBody(OdtTestPackage.Paragraph("x"), extraParts: parts), writable: false);
        return new OdtDocumentCodec().Read(stream).Document;
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void The_Configuration_Setting_Decides_Whether_Justified_Lines_Shrink(string value, bool shrinks)
    {
        Assert.Equal(shrinks, Read(Settings("ooo:configuration-settings", "JustifyLinesWithShrinking", value)).StyleDefaults.ShrinksJustifiedLines);
    }

    [Fact]
    public void A_Package_That_States_Nothing_Does_Not_Shrink()
    {
        Assert.False(OdtTestPackage.ReadBody(OdtTestPackage.Paragraph("x")).Document.StyleDefaults.ShrinksJustifiedLines);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_Setting_Is_Written_Back_Beside_Hidden_Changes(bool showsRevisions)
    {
        RichTextDocument source = RichTextDocument.FromPlainText("body")
            .WithStyleDefaults(DocumentStyleDefaults.Default with { ShrinksJustifiedLines = true })
            .WithShowsRevisions(showsRevisions);

        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(source), writable: false);
        RichTextDocument back = new OdtDocumentCodec().Read(stream).Document;

        Assert.True(back.StyleDefaults.ShrinksJustifiedLines);
        Assert.Equal(showsRevisions, back.ShowsRevisions);
    }
}
