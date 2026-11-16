namespace Broiler.Documents.Office.Tests;

/// <summary>
/// An HTML sample is rendered on LibreOffice's HTML page and in its default
/// serif (office issue #238), and no other format is.
/// </summary>
public sealed class HtmlRenderArgumentTests
{
    private static List<string> Arguments(string extension, string format) =>
        ExtrasConformance.RenderArguments(
            new ExtrasSample("sample" + extension, extension, format, "sample" + extension, null),
            "out", 96, 10, [], []);

    [Fact]
    public void An_Html_Sample_Asks_For_LibreOffices_Html_Page_And_Face()
    {
        List<string> arguments = Arguments(".html", "html");

        int margin = arguments.IndexOf("--default-margin");
        Assert.True(margin >= 0);
        Assert.Equal("1cm,2cm", arguments[margin + 1]);
        int font = arguments.IndexOf("--font");
        Assert.True(font >= 0);
        Assert.Equal("serif", arguments[font + 1]);

        // The document's own @page still wins: the harness never overrides it.
        Assert.DoesNotContain("--margin", arguments);
    }

    [Theory]
    [InlineData(".docx", "docx")]
    [InlineData(".rtf", "rtf")]
    [InlineData(".odt", "odt")]
    public void Other_Formats_Keep_Their_Own_Page_And_Face(string extension, string format)
    {
        List<string> arguments = Arguments(extension, format);

        Assert.DoesNotContain("--default-margin", arguments);
        Assert.DoesNotContain("--font", arguments);
    }

    [Theory]
    [InlineData(".html", "html", true)]
    [InlineData(".odt", "odt", true)]
    [InlineData(".fodt", "odt", true)]
    [InlineData(".ott", "odt", true)]
    [InlineData(".docx", "docx", false)]
    [InlineData(".rtf", "rtf", false)]
    public void Only_Html_And_Odt_Samples_May_Load_Pictures_Beside_Themselves(string extension, string format, bool allowed)
    {
        // LibreOffice draws an ODT package's ../name link from the document's
        // own directory (globalfilter's document_with_linked_graphic.odt), as
        // it draws an HTML sibling image (office issue #249).
        List<string> arguments = Arguments(extension, format);

        Assert.Equal(allowed, arguments.Contains("--allow-local-images"));
    }
}
