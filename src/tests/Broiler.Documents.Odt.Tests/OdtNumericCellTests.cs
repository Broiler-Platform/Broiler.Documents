using System.Globalization;

namespace Broiler.Documents.Odt.Tests;

public sealed class OdtNumericCellTests
{
    private static RichTextDocument Read(string value, string cached, string type = "float", string numberProperties = "", bool rich = false, string culture = "en-US")
    {
        string format = "<number:number-style xmlns:number=\"urn:oasis:names:tc:opendocument:xmlns:datastyle:1.0\" style:name=\"N0\">" +
            "<number:number number:min-integer-digits=\"1\" " + numberProperties + "/></number:number-style>";
        string styles = "<office:styles>" + format +
            "<style:style style:name=\"base\" style:family=\"table-cell\" style:data-style-name=\"N0\"/>" +
            "<style:style style:name=\"cell\" style:family=\"table-cell\" style:parent-style-name=\"base\"/>" +
            "<style:style style:name=\"P\" style:family=\"paragraph\"><style:text-properties fo:font-weight=\"bold\"/></style:style></office:styles>";
        string content = rich ? "<text:span>" + cached + "</text:span>" : cached;
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            return OdtTestPackage.ReadStyled("<table:table><table:table-row><table:table-cell table:style-name=\"cell\" office:value-type=\"" + type +
                "\" office:value=\"" + value + "\"><text:p text:style-name=\"P\">" + content + "</text:p></table:table-cell></table:table-row></table:table>", styles).Document;
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Theory]
    [InlineData("12.1", "12,1", "12.1")]
    [InlineData("-1234.5", "-1.234,5", "-1234.5")]
    [InlineData("0", "0,00", "0")]
    public void General_Numeric_Cells_Display_Their_Stored_Value_And_Keep_Paragraph_Formatting(string value, string cached, string expected)
    {
        var paragraph = Assert.Single(Read(value, cached).Paragraphs);
        Assert.Equal(expected, paragraph.Text);
        Assert.True(paragraph.Runs[0].Style.Bold);
    }

    [Fact]
    public void General_Formatting_Uses_The_Applications_Locale_Instead_Of_Forcing_A_Dot()
    {
        Assert.Equal("12,1", Assert.Single(Read("12.1", "cached", culture: "de-DE").Paragraphs).Text);
    }

    [Fact]
    public void Explicit_Decimal_Formats_Keep_Their_Cached_Display()
    {
        Assert.Equal("12,10", Assert.Single(Read("12.1", "12,10", numberProperties: "number:decimal-places=\"2\"").Paragraphs).Text);
    }

    [Fact]
    public void String_Values_Are_Not_Reformatted_As_Numbers()
    {
        Assert.Equal("12,1", Assert.Single(Read("12.1", "12,1", type: "string").Paragraphs).Text);
    }

    [Fact]
    public void Rich_Cell_Content_Is_Not_Flattened_By_The_Numeric_Fallback()
    {
        Assert.Equal("12,1", Assert.Single(Read("12.1", "12,1", rich: true).Paragraphs).Text);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("bad")]
    public void Invalid_Or_Nonfinite_Numeric_Values_Keep_The_Cache(string value)
    {
        Assert.Equal("cached", Assert.Single(Read(value, "cached").Paragraphs).Text);
    }
}
