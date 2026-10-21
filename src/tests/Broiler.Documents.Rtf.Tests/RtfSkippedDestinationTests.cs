using System.Text;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// A skipped destination is skipped with every group inside it. A destination
/// the reader does understand - a field, a header, a font table - used to take
/// effect one level down regardless, so a footnote's hyperlink reached the body
/// and a header inside a stylesheet reached the page.
/// </summary>
public sealed class RtfSkippedDestinationTests
{
    private static DocumentReadResult Read(string text) =>
        RtfReader.Read(Encoding.Latin1.GetBytes(text));

    public static TheoryData<string, string> HostsAndPayloads()
    {
        string[] hosts =
        [
            @"\annotation",
            @"\stylesheet",
            @"\info",
            @"\pict",
            @"\*\somethingunknown",
        ];
        string[] payloads =
        [
            @"{\field{\*\fldinst HYPERLINK ""https://example.test""}{\fldrslt hidden}}",
            @"{\field{\*\fldinst PAGE}{\fldrslt 7}}",
            @"{\header hidden\par}{\footer hidden\par}",
            @"{\headerf hidden\par}{\footerl hidden\par}",
            @"{\fonttbl{\f0 Hidden;}}",
            @"{\colortbl;\red255\green0\blue0;}",
            @"{\shp{\*\shpinst\shpleft0\shptop0\shpright1440\shpbottom1440{\sp{\sn fillColor}{\sv 255}}{\shptxt hidden}}}",
        ];

        var data = new TheoryData<string, string>();
        foreach (string host in hosts)
        {
            foreach (string payload in payloads)
                data.Add(host, payload);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(HostsAndPayloads))]
    public void Nested_Destinations_Cannot_Escape_A_Skipped_One(string host, string payload)
    {
        const string Prefix = @"{\rtf1{\fonttbl{\f0 Real;}}{\colortbl;\red0\green0\blue255;}\f0\cf1 Before";
        DocumentReadResult result = Read(Prefix + "{" + host + " " + payload + "}After}");
        RichTextDocument expected = Read(Prefix + "After}").Document;

        DocumentAssert.Equivalent(expected, result.Document);
        Assert.True(result.Document.RunningContent.IsEmpty);
        Assert.Empty(result.Document.Shapes);
        Assert.All(result.Diagnostics, d => Assert.Equal("rtf.embedded", d.Code));
    }

    /// <summary>
    /// A note is read now, not skipped, and what is stated inside it stays
    /// there: the body has the note's citation and nothing else of it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Payloads))]
    public void Nested_Destinations_Cannot_Escape_A_Note(string payload)
    {
        const string Prefix = @"{\rtf1{\fonttbl{\f0 Real;}}{\colortbl;\red0\green0\blue255;}\f0\cf1 Before";
        DocumentReadResult result = Read(Prefix + @"{\footnote " + payload + "}After}");

        RichTextParagraph paragraph = Assert.Single(result.Document.Paragraphs);
        Assert.Equal("Before" + DocumentNote.PlaceholderText + "After", paragraph.Text);
        Assert.NotNull(paragraph.StyleAt(6).Note);
        Assert.Equal("Real", paragraph.StyleAt(7).FontFamily);
        Assert.True(result.Document.RunningContent.IsEmpty);
        Assert.Empty(result.Document.Shapes);
    }

    public static TheoryData<string> Payloads()
    {
        var data = new TheoryData<string>();
        foreach (object[] row in HostsAndPayloads())
        {
            if ((string)row[0] == @"\annotation")
                data.Add((string)row[1]);
        }

        return data;
    }

    [Fact]
    public void A_Footnote_Keeps_Its_Hyperlink()
    {
        // LibreOffice reads this as "A1B": the link is in the footnote, under
        // the mark. This reader used to have "AlinkB", with the footnote's link
        // text in the sentence, and then "AB", with the footnote skipped.
        DocumentReadResult result = Read(
            @"{\rtf1 A{\footnote\pard {\field{\*\fldinst HYPERLINK ""https://example.test""}{\fldrslt link}}}B\par}");

        Assert.Equal("A" + DocumentNote.PlaceholderText + "B", result.Document.PlainText);
        Assert.Null(result.Document.Paragraphs[0].StyleAt(1).LinkHref);
        DocumentNote note = Assert.Single(result.Document.Notes);
        Assert.Equal("link", note.PlainText);
        Assert.Equal("https://example.test", note.Paragraphs[0].StyleAt(0).LinkHref);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void A_Font_Table_Inside_A_Skipped_Destination_Does_Not_Replace_The_Document_One()
    {
        RichTextDocument document = Read(
            @"{\rtf1{\fonttbl{\f0 Real;}}{\info{\fonttbl{\f0 Fake;}}}\f0 Body}").Document;

        Assert.Equal("Real", document.Paragraphs[0].StyleAt(0).FontFamily);
    }

    [Fact]
    public void A_Header_Inside_A_Stylesheet_Is_Not_The_Page_Header()
    {
        RichTextDocument document = Read(
            @"{\rtf1{\stylesheet{\s0 Normal;}{\header Head\par}}Body\par}").Document;

        Assert.True(document.RunningContent.IsEmpty);
        Assert.Equal("Body", document.PlainText);
    }

    [Fact]
    public void A_Picture_Inside_A_Skipped_Destination_Is_Still_Reported()
    {
        // Word wraps its pictures in {\*\shppict ...}. The picture is no less
        // lost for being inside a group the reader skips anyway.
        DocumentReadResult result = Read(@"{\rtf1 {\*\shppict{\pict\pngblip 89504e47}}Body}");

        Assert.Equal("Body", result.Document.PlainText);
        Assert.Equal("rtf.embedded", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Destinations_Outside_A_Skipped_One_Still_Take_Effect()
    {
        DocumentReadResult result = Read(
            @"{\rtf1{\annotation note}{\field{\*\fldinst HYPERLINK ""https://example.test""}{\fldrslt link}}{\header Head\par}}");

        Assert.Equal("link", result.Document.PlainText);
        Assert.Equal("https://example.test", result.Document.Paragraphs[0].StyleAt(0).LinkHref);
        Assert.False(result.Document.RunningContent.IsEmpty);
    }
}
