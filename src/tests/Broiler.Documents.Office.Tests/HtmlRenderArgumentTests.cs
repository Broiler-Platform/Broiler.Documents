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
}
