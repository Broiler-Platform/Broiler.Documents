using System.Globalization;

namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Office conformance issue 242, ODT text: table cells that show the value
/// they hold through their data style (fdo60842.odt), and date fields shown
/// through theirs (ooo96040-2.odt), as LibreOffice 24.2 shows them with an
/// en-US system locale.
/// </summary>
public sealed class OdtIssue242OdtTextTests
{
    private const string Number = "xmlns:number=\"urn:oasis:names:tc:opendocument:xmlns:datastyle:1.0\"";

    private static T InEnglish<T>(Func<T> read)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            return read();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    /// <summary>One table cell with the attributes given, styled <c>ce</c>, which names data style <c>N</c>.</summary>
    private static string CellText(string dataStyle, string cellAttributes, string content = "<text:p>cached</text:p>")
    {
        string styles = "<office:automatic-styles>" + dataStyle +
            "<style:style style:name=\"ce\" style:family=\"table-cell\" style:data-style-name=\"N\"/></office:automatic-styles>";
        string body = "<table:table><table:table-column/><table:table-row><table:table-cell table:style-name=\"ce\" " + cellAttributes + ">" +
            content + "</table:table-cell></table:table-row></table:table>";
        return InEnglish(() => string.Join("\n", OdtTestPackage.ReadStyled(body, styles).Document.Paragraphs.Select(paragraph => paragraph.Text)));
    }

    private static string DateStyle(string attributes, string parts) =>
        "<number:date-style " + Number + " style:name=\"N\" " + attributes + ">" + parts + "</number:date-style>";

    private const string DayMonthYear =
        "<number:day/><number:text>/</number:text><number:month/><number:text>/</number:text><number:year/>";

    private const string DayTextualMonthYear =
        "<number:day/><number:text>. </number:text><number:month number:textual=\"true\"/><number:text> </number:text><number:year number:style=\"long\"/>";

    [Fact]
    public void A_Date_Cell_Shows_Its_Value_In_The_Locale_Short_Date_Format()
    {
        // fdo60842.odt: an empty cell holding 2012-02-18, styled day/month/year
        // in fr-LU, in automatic order from the locale: its short date format.
        string style = DateStyle("number:language=\"fr\" number:country=\"LU\" number:automatic-order=\"true\" number:format-source=\"language\"", DayMonthYear);

        Assert.Equal("18/02/2012", CellText(style, "office:value-type=\"date\" office:date-value=\"2012-02-18T00:00:00.0Z\"", "<text:p/>"));
    }

    [Fact]
    public void A_Number_Cell_Shows_Its_Value_Grouped_In_The_Style_Locale()
    {
        string style = "<number:number-style " + Number + " style:name=\"N\" number:language=\"fr\" number:country=\"LU\">" +
            "<number:number number:min-integer-digits=\"1\" number:grouping=\"true\" number:decimal-places=\"2\"/></number:number-style>";

        Assert.Equal("500 000,00", CellText(style, "office:value-type=\"float\" office:value=\"500000.00\""));
    }

    [Fact]
    public void A_String_Value_Replaces_The_Cell_Text()
    {
        string style = "<number:text-style " + Number + " style:name=\"N\"><number:text-content/></number:text-style>";

        Assert.Equal("USD", CellText(style, "office:value-type=\"string\" office:string-value=\"USD\"", "<text:p>This text should not appear</text:p>"));
    }

    [Fact]
    public void A_Style_Without_A_Language_Formats_In_The_System_Locale()
    {
        string style = "<number:number-style " + Number + " style:name=\"N\">" +
            "<number:text>[</number:text><number:number number:min-integer-digits=\"3\" number:decimal-places=\"1\"/><number:text> kg]</number:text></number:number-style>";

        Assert.Equal("-[012.5 kg]", CellText(style, "office:value-type=\"float\" office:value=\"-12.46\""));
    }

    [Fact]
    public void A_Currency_Style_Takes_The_Section_Its_Map_Names()
    {
        // The style that maps is the section for negative values: it writes the
        // minus as text and shows the value without a sign of its own.
        string styles =
            "<number:currency-style " + Number + " style:name=\"NP0\" number:language=\"de\" number:country=\"DE\">" +
            "<number:number number:decimal-places=\"2\" number:min-integer-digits=\"1\" number:grouping=\"true\"/>" +
            "<number:text> </number:text><number:currency-symbol>€</number:currency-symbol></number:currency-style>" +
            "<number:currency-style " + Number + " style:name=\"N\" number:language=\"de\" number:country=\"DE\">" +
            "<number:text>-</number:text><number:number number:decimal-places=\"2\" number:min-integer-digits=\"1\" number:grouping=\"true\"/>" +
            "<number:text> </number:text><number:currency-symbol>€</number:currency-symbol>" +
            "<style:map style:condition=\"value()&gt;=0\" style:apply-style-name=\"NP0\"/></number:currency-style>";

        Assert.Equal("2.580,00 €", CellText(styles, "office:value-type=\"currency\" office:value=\"2580\""));
        Assert.Equal("-1.302,50 €", CellText(styles, "office:value-type=\"currency\" office:value=\"-1302.5\""));
    }

    [Fact]
    public void A_General_Cell_Of_Value_Zero_Keeps_Its_Label()
    {
        string style = "<number:number-style " + Number + " style:name=\"N\"><number:number number:min-integer-digits=\"1\"/></number:number-style>";

        Assert.Equal("Apple", CellText(style, "office:value-type=\"float\" office:value=\"0\"", "<text:p>Apple</text:p>"));
        Assert.Equal("12.1", CellText(style, "office:value-type=\"float\" office:value=\"12.1\"", "<text:p>Apple</text:p>"));
    }

    [Theory]
    [InlineData("<text:p>a</text:p><text:p>b</text:p>", "a\nb")]
    [InlineData("<text:p><text:page-number>1</text:page-number></text:p>", "1")]
    public void A_Cell_Of_Several_Paragraphs_Or_A_Field_Keeps_Its_Content(string content, string expected)
    {
        string style = "<number:number-style " + Number + " style:name=\"N\"><number:number number:decimal-places=\"2\" number:min-integer-digits=\"1\"/></number:number-style>";

        Assert.Equal(expected, CellText(style, "office:value-type=\"float\" office:value=\"3\"", content));
    }

    [Fact]
    public void A_Locale_This_Reader_Does_Not_Hold_Keeps_The_Cached_Text()
    {
        string style = DateStyle("number:language=\"ja\" number:country=\"JP\" number:automatic-order=\"true\"", DayTextualMonthYear);

        Assert.Equal("cached", CellText(style, "office:value-type=\"date\" office:date-value=\"2008-11-10T00:00:00\""));
    }

    [Fact]
    public void A_Formula_Cell_Keeps_Its_Cached_Result()
    {
        string style = "<number:number-style " + Number + " style:name=\"N\"><number:number number:decimal-places=\"2\" number:min-integer-digits=\"1\"/></number:number-style>";

        Assert.Equal("cached", CellText(style, "table:formula=\"ooow:1+2\" office:value-type=\"float\" office:value=\"3\""));
    }

    private static string FieldText(string field, string dataStyle, string? meta = null)
    {
        string styles = "<office:automatic-styles>" + dataStyle + "</office:automatic-styles>";
        var extra = new Dictionary<string, string>(StringComparer.Ordinal);
        if (meta is not null)
        {
            extra["meta.xml"] = "<office:document-meta xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
                "xmlns:meta=\"urn:oasis:names:tc:opendocument:xmlns:meta:1.0\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><office:meta>" +
                meta + "</office:meta></office:document-meta>";
        }
        byte[] package = OdtTestPackage.FromBody("<text:p>" + field + "</text:p>", stylesPartInnerXml: styles, extraParts: extra);
        return InEnglish(() =>
        {
            using var stream = new MemoryStream(package, writable: false);
            return Assert.Single(new OdtDocumentCodec().Read(stream).Document.Paragraphs).Text;
        });
    }

    [Fact]
    public void A_Modification_Date_Shows_The_Document_Date_In_Automatic_Order()
    {
        // ooo96040-2.odt: "10. Nov 2008" cached, "Nov 10, 2008" in en-US.
        string style = DateStyle("number:automatic-order=\"true\"", DayTextualMonthYear);

        Assert.Equal("Nov 10, 2008", FieldText(
            "<text:modification-date style:data-style-name=\"N\">10. Nov 2008</text:modification-date>", style,
            "<dc:date>2008-11-10T12:09:27</dc:date>"));
    }

    [Fact]
    public void A_Modification_Date_Without_A_Document_Date_Keeps_Its_Text()
    {
        string style = DateStyle("number:automatic-order=\"true\"", DayTextualMonthYear);

        Assert.Equal("10. Nov 2008", FieldText(
            "<text:modification-date style:data-style-name=\"N\">10. Nov 2008</text:modification-date>", style));
    }

    [Fact]
    public void A_Fixed_Date_Shows_Its_Value_In_The_Order_It_States()
    {
        string style = DateStyle("number:language=\"de\" number:country=\"DE\"",
            "<number:day-of-week number:style=\"long\"/><number:text>, </number:text>" + DayTextualMonthYear);

        Assert.Equal("Donnerstag, 5. Feb 2009", FieldText(
            "<text:date style:data-style-name=\"N\" text:date-value=\"2009-02-05T10:00:00\" text:fixed=\"true\">X</text:date>", style));
    }

    [Fact]
    public void A_Date_That_Is_Not_Fixed_Keeps_Its_Text()
    {
        // It shows the day the document is opened.
        Assert.Equal("X", FieldText(
            "<text:date style:data-style-name=\"N\" text:date-value=\"2009-02-05T10:00:00\">X</text:date>", DateStyle("", DayMonthYear)));
    }

    [Theory]
    [InlineData("en", "US", "", "Feb 5, 2009")]
    [InlineData("en", "GB", "", "5 Feb 2009")]
    [InlineData("de", "AT", "", "5. Feb 2009")]
    [InlineData("de", "AT", " number:format-source=\"language\"", "05.02.2009")]
    [InlineData("fr", "FR", "", "5 févr. 2009")]
    [InlineData("en", "US", " number:format-source=\"language\"", "2/5/09")]
    public void Automatic_Order_Takes_The_Locale_Built_In_Format(string language, string country, string source, string expected)
    {
        string style = DateStyle("number:language=\"" + language + "\" number:country=\"" + country + "\" number:automatic-order=\"true\"" + source, DayTextualMonthYear);

        Assert.Equal(expected, FieldText(
            "<text:date style:data-style-name=\"N\" text:date-value=\"2009-02-05T10:00:00\" text:fixed=\"true\">X</text:date>", style));
    }

    [Fact]
    public void Automatic_Order_Keeps_A_Format_With_No_Built_In_Counterpart()
    {
        // Short day and month: no row of the table has them, so the stated
        // order stands.
        string style = DateStyle("number:language=\"en\" number:country=\"US\" number:automatic-order=\"true\"", DayMonthYear);

        Assert.Equal("5/2/09", FieldText(
            "<text:date style:data-style-name=\"N\" text:date-value=\"2009-02-05T10:00:00\" text:fixed=\"true\">X</text:date>", style));
    }

    [Fact]
    public void A_Data_Style_In_Styles_Xml_Keeps_The_Space_It_States()
    {
        // styles.xml is read with its white space where it is text: the
        // currency style's single space between the number and the symbol.
        string styles = "<office:automatic-styles><number:currency-style " + Number + " style:name=\"N\" number:language=\"de\" number:country=\"DE\">" +
            "<number:number number:decimal-places=\"2\" number:min-integer-digits=\"1\"/><number:text> </number:text>" +
            "<number:currency-symbol>€</number:currency-symbol></number:currency-style>" +
            "<style:style style:name=\"ce\" style:family=\"table-cell\" style:data-style-name=\"N\"/></office:automatic-styles>";
        string body = "<table:table><table:table-column/><table:table-row><table:table-cell table:style-name=\"ce\" " +
            "office:value-type=\"currency\" office:value=\"100\"><text:p/></table:table-cell></table:table-row></table:table>";

        Assert.Equal("100,00 €", Assert.Single(OdtTestPackage.ReadStyled(body, styles).Document.Paragraphs).Text);
    }
}
