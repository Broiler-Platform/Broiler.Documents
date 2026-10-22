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

    [Fact]
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

    [Fact]
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

    [Fact]
    public void A_Document_Stating_No_Paper_Uses_LibreOffices_Letter_Default()
    {
        PageGeometry geometry = Assert.IsType<PageGeometry>(
            RtfReader.Read("{\\rtf1 body\\par}"u8.ToArray()).Document.PageGeometry);
        Assert.Equal(612, geometry.Width);
        Assert.Equal(792, geometry.Height);
        Assert.Equal(72, geometry.MarginLeft);
        Assert.Equal(72, geometry.MarginRight);
        Assert.Equal(72, geometry.MarginTop);
        Assert.Equal(72, geometry.MarginBottom);
    }

    [Fact]
    public void A_Margin_Without_Paper_Dimensions_Is_Kept_On_The_Default_Page()
    {
        PageGeometry geometry = Assert.IsType<PageGeometry>(
            RtfReader.Read("{\\rtf1\\margl1800\\margtsxn1000 body\\par}"u8.ToArray()).Document.PageGeometry);
        Assert.Equal(612, geometry.Width);
        Assert.Equal(792, geometry.Height);
        Assert.Equal(90, geometry.MarginLeft);
        Assert.Equal(50, geometry.MarginTop);
        Assert.Equal(72, geometry.MarginRight);
    }

    [Fact]
    public void Margins_That_Leave_No_Column_Are_Refused_And_Reported()
    {
        DocumentReadResult result = RtfReader.Read(
            "{\\rtf1\\paperw2000\\paperh2000\\margl1500\\margr1500 body\\par}"u8.ToArray());

        Assert.Null(result.Document.PageGeometry);
        Assert.Contains(result.Diagnostics, d => d.Code == "rtf.page.geometry");
    }

    [Fact]
    public void A_Section_States_Its_Own_Page_Over_The_Documents()
    {
        // fdo66565.rtf's section words, over a portrait document page. RTF 1.9.1
        // gives \pgwsxn and the rest the document's values as defaults, so what
        // the section states is the page; LibreOffice draws it landscape.
        PageGeometry geometry = Assert.IsType<PageGeometry>(RtfReader.Read(Encoding.ASCII.GetBytes(
            "{\\rtf1\\paperw11906\\paperh16838\\margl1800\\margr1800\\margt1440\\margb1440" +
            "\\sectd\\lndscpsxn\\pgwsxn16840\\pghsxn11907\\marglsxn1133\\margrsxn850\\margtsxn850\\margbsxn850" +
            " body\\par}")).Document.PageGeometry);

        Assert.Equal(842, geometry.Width, 1);
        Assert.Equal(595.35, geometry.Height, 1);
        Assert.Equal(56.65, geometry.MarginLeft, 1);
        Assert.Equal(42.5, geometry.MarginRight, 1);
        Assert.Equal(42.5, geometry.MarginTop, 1);
        Assert.Equal(42.5, geometry.MarginBottom, 1);
    }

    [Fact]
    public void A_Section_Page_Needs_No_Document_Paper()
    {
        PageGeometry geometry = Assert.IsType<PageGeometry>(RtfReader.Read(
            "{\\rtf1\\sectd\\pgwsxn16840\\pghsxn11907\\marglsxn1133\\margrsxn850 body\\par}"u8.ToArray())
            .Document.PageGeometry);

        Assert.Equal(842, geometry.Width, 1);
        Assert.Equal(56.65, geometry.MarginLeft, 1);
    }

    [Fact]
    public void What_A_Section_Does_Not_State_Is_The_Documents()
    {
        PageGeometry geometry = Assert.IsType<PageGeometry>(RtfReader.Read(
            "{\\rtf1\\paperw11906\\paperh16838\\margl2551\\margr1134\\margt1134\\margb1134\\sectd\\margrsxn850 body\\par}"u8.ToArray())
            .Document.PageGeometry);

        Assert.Equal(595.3, geometry.Width, 1);
        Assert.Equal(127.55, geometry.MarginLeft, 1);
        Assert.Equal(42.5, geometry.MarginRight, 1);
    }

    [Fact]
    public void Landscape_Does_Not_Swap_The_Stated_Size()
    {
        // LibreOffice draws this portrait: the size is the page, and the flag
        // only says which way up it is.
        PageGeometry geometry = Assert.IsType<PageGeometry>(RtfReader.Read(
            "{\\rtf1\\sectd\\lndscpsxn\\pgwsxn11907\\pghsxn16840 body\\par}"u8.ToArray()).Document.PageGeometry);

        Assert.Equal(595.35, geometry.Width, 1);
        Assert.Equal(842, geometry.Height, 1);
    }

    [Fact]
    public void A_Later_Sections_Page_Does_Not_Replace_The_First()
    {
        PageGeometry geometry = Assert.IsType<PageGeometry>(RtfReader.Read(
            "{\\rtf1\\paperw11906\\paperh16838 one\\par\\sect\\sectd\\pgwsxn16840\\pghsxn11907 two\\par}"u8.ToArray())
            .Document.PageGeometry);

        Assert.Equal(595.3, geometry.Width, 1);
        Assert.Equal(841.9, geometry.Height, 1);
    }

    [Fact]
    public void Section_Defaults_Clear_What_The_Section_Stated()
    {
        PageGeometry geometry = Assert.IsType<PageGeometry>(RtfReader.Read(
            "{\\rtf1\\paperw11906\\paperh16838\\pgwsxn16840\\pghsxn11907\\sectd body\\par}"u8.ToArray())
            .Document.PageGeometry);

        Assert.Equal(595.3, geometry.Width, 1);
    }

    [Fact]
    public void The_Page_Survives_A_Round_Trip()
    {
        RichTextDocument source = RichTextDocument.FromPlainText("body").WithPageGeometry(A4Letterhead);

        PageGeometry geometry = Assert.IsType<PageGeometry>(
            RtfReader.Read(RtfWriter.WriteToArray(source)).Document.PageGeometry);

        Assert.Equal(A4Letterhead.Width, geometry.Width, 1);
        Assert.Equal(A4Letterhead.MarginLeft, geometry.MarginLeft, 1);
        Assert.Equal(A4Letterhead.HeaderDistance, geometry.HeaderDistance, 1);
    }

    [Fact]
    public void A_Document_Without_A_Page_Writes_No_Section_Words()
    {
        string rtf = Ascii(RtfWriter.WriteToArray(RichTextDocument.FromPlainText("body")));

        // Inventing a paper size would put words in the author's mouth.
        Assert.DoesNotContain("paperw", rtf, StringComparison.Ordinal);
        Assert.DoesNotContain("margl", rtf, StringComparison.Ordinal);
    }

    [Fact]
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
