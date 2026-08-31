using System.Text;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// The page an RTF states. Its section control words — \paperw, \margl and the
/// rest — were not read at all, so a letter whose left margin exists to hold a
/// letterhead was laid out on whatever page the renderer chose.
/// </summary>
public sealed class RtfPageGeometryTests
{
    private static readonly PageGeometry A4Letterhead =
        new(595.3, 841.9, 127.55, 56.7, 56.7, 56.7, 36.15, 56.7);

    private static string Ascii(byte[] bytes) => Encoding.ASCII.GetString(bytes);

    [Fact(Timeout = 600000)]
    public void Reads_The_Section_Control_Words()
    {
        // A4 in twips, with a 4.5cm left margin.
        RichTextDocument document = RtfReader.Read(
            "{\\rtf1\\paperw11906\\paperh16838\\margl2551\\margr1134\\margt1134\\margb1134 body\\par}"u8.ToArray())
            .Document;

        PageGeometry geometry = Assert.IsType<PageGeometry>(document.PageGeometry);

        Assert.Equal(595.3, geometry.Width, 1);
        Assert.Equal(841.9, geometry.Height, 1);
        Assert.Equal(127.55, geometry.MarginLeft, 1);
    }

    [Fact(Timeout = 600000)]
    public void Reads_The_Header_And_Footer_Distances()
    {
        RichTextDocument document = RtfReader.Read(Encoding.ASCII.GetBytes(
            "{\\rtf1\\paperw11906\\paperh16838\\margl1440\\margr1440\\margt1440\\margb1440" +
            "\\headery723\\footery1134 body\\par}"))
            .Document;

        PageGeometry geometry = Assert.IsType<PageGeometry>(document.PageGeometry);

        Assert.Equal(36.15, geometry.HeaderDistance, 1);
        Assert.Equal(56.7, geometry.FooterDistance, 1);
    }

    [Fact(Timeout = 600000)]
    public void A_Document_Stating_No_Paper_Has_No_Page()
    {
        Assert.Null(RtfReader.Read("{\\rtf1 body\\par}"u8.ToArray()).Document.PageGeometry);
    }

    [Fact(Timeout = 600000)]
    public void Margins_That_Leave_No_Column_Are_Refused_And_Reported()
    {
        DocumentReadResult result = RtfReader.Read(
            "{\\rtf1\\paperw2000\\paperh2000\\margl1500\\margr1500 body\\par}"u8.ToArray());

        Assert.Null(result.Document.PageGeometry);
        Assert.Contains(result.Diagnostics, d => d.Code == "rtf.page.geometry");
    }

    [Fact(Timeout = 600000)]
    public void The_Page_Survives_A_Round_Trip()
    {
        RichTextDocument source = RichTextDocument.FromPlainText("body").WithPageGeometry(A4Letterhead);

        PageGeometry geometry = Assert.IsType<PageGeometry>(
            RtfReader.Read(RtfWriter.WriteToArray(source)).Document.PageGeometry);

        Assert.Equal(A4Letterhead.Width, geometry.Width, 1);
        Assert.Equal(A4Letterhead.MarginLeft, geometry.MarginLeft, 1);
        Assert.Equal(A4Letterhead.HeaderDistance, geometry.HeaderDistance, 1);
    }

    [Fact(Timeout = 600000)]
    public void A_Document_Without_A_Page_Writes_No_Section_Words()
    {
        string rtf = Ascii(RtfWriter.WriteToArray(RichTextDocument.FromPlainText("body")));

        // Inventing a paper size would put words in the author's mouth.
        Assert.DoesNotContain("paperw", rtf, StringComparison.Ordinal);
        Assert.DoesNotContain("margl", rtf, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void The_Section_Words_Precede_The_Body()
    {
        string rtf = Ascii(RtfWriter.WriteToArray(
            RichTextDocument.FromPlainText("body").WithPageGeometry(A4Letterhead)));

        int paper = rtf.IndexOf("paperw", StringComparison.Ordinal);
        int body = rtf.IndexOf("body", StringComparison.Ordinal);

        Assert.True(paper > 0 && body > 0);
        Assert.True(paper < body, "a reader takes these as section properties, so they come first");
    }
}
