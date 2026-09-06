using System.Text;

namespace Broiler.Documents.Html.Tests;

/// <summary>
/// The page an HTML document states, in its <c>@page</c> rule.
/// </summary>
/// <remarks>
/// <para>
/// Neither half of this codec looked at it. The reader skipped every
/// <c>style</c> element - correctly, because its content is not text the
/// document says - and so threw away the one thing in a stylesheet the model has
/// somewhere to put. The writer emitted no rule at all, so a document that
/// arrived from DOCX or ODF stating US Letter left through HTML stating nothing,
/// and the loss was silent: no reader of the result could tell it from a
/// document that never stated a page.
/// </para>
/// <para>
/// Found by the office conformance suite, which reported a page geometry
/// disagreement on every one of the twelve HTML documents LibreOffice produced.
/// </para>
/// </remarks>
public sealed class HtmlPageGeometryTests
{
    [Fact(Timeout = 600000)]
    public void The_Page_Is_Read_From_An_At_Page_Rule()
    {
        // Centimetres, because that is what a word processor writing HTML emits
        // and what this codec's length parser did not know.
        RichTextDocument document = Read(
            "<html><head><style>@page { size: 21.59cm 27.94cm; margin: 2.54cm }</style></head>" +
            "<body><p>body</p></body></html>");

        PageGeometry page = Assert.IsType<PageGeometry>(document.PageGeometry);

        Assert.Equal(612, page.Width, 1);
        Assert.Equal(792, page.Height, 1);
        Assert.Equal(72, page.MarginTop, 1);
        Assert.Equal(72, page.MarginLeft, 1);
    }

    [Fact(Timeout = 600000)]
    public void A_Document_With_No_Rule_States_No_Page()
    {
        // The distinction the model keeps, and the reason this is not simply
        // defaulted: a document that says nothing about its paper is different
        // from one that says A4, and a renderer is entitled to know which it has.
        Assert.Null(Read("<p>body</p>").PageGeometry);
    }

    [Theory(Timeout = 600000)]
    [InlineData("a4", 595.276, 841.89)]
    [InlineData("letter", 612, 792)]
    [InlineData("legal", 612, 1008)]
    [InlineData("A4", 595.276, 841.89)]
    public void A_Named_Size_Is_The_Size_Css_Names(string name, double width, double height)
    {
        PageGeometry page = PageOf("@page { size: " + name + " }");

        Assert.Equal(width, page.Width, 1);
        Assert.Equal(height, page.Height, 1);
    }

    [Fact(Timeout = 600000)]
    public void Landscape_Turns_The_Page_It_Names()
    {
        PageGeometry page = PageOf("@page { size: a4 landscape }");

        Assert.Equal(841.89, page.Width, 1);
        Assert.Equal(595.276, page.Height, 1);
        Assert.True(page.IsLandscape);
    }

    [Fact(Timeout = 600000)]
    public void One_Length_Is_A_Square_Page()
    {
        PageGeometry page = PageOf("@page { size: 10in }");

        Assert.Equal(720, page.Width, 1);
        Assert.Equal(720, page.Height, 1);
    }

    [Theory(Timeout = 600000)]
    // top right bottom left, in each of the shorthand's four arities.
    [InlineData("10pt", 10, 10, 10, 10)]
    [InlineData("10pt 20pt", 10, 20, 10, 20)]
    [InlineData("10pt 20pt 30pt", 10, 20, 30, 20)]
    [InlineData("10pt 20pt 30pt 40pt", 10, 20, 30, 40)]
    public void The_Margin_Shorthand_Is_Read_In_Every_Arity(
        string margin, double top, double right, double bottom, double left)
    {
        PageGeometry page = PageOf("@page { size: a4; margin: " + margin + " }");

        Assert.Equal(top, page.MarginTop, 1);
        Assert.Equal(right, page.MarginRight, 1);
        Assert.Equal(bottom, page.MarginBottom, 1);
        Assert.Equal(left, page.MarginLeft, 1);
    }

    [Fact(Timeout = 600000)]
    public void A_Longhand_Overrides_The_Shorthand_Beside_It()
    {
        PageGeometry page = PageOf("@page { size: a4; margin: 10pt; margin-left: 99pt }");

        Assert.Equal(10, page.MarginTop, 1);
        Assert.Equal(99, page.MarginLeft, 1);
    }

    [Fact(Timeout = 600000)]
    public void Margins_Alone_Are_Honoured_Without_A_Size()
    {
        // What a document says when it cares about its margins and not its paper.
        PageGeometry page = PageOf("@page { margin: 36pt }");

        Assert.Equal(36, page.MarginTop, 1);
        Assert.Equal(PageGeometry.A4.Width, page.Width, 1);
    }

    [Fact(Timeout = 600000)]
    public void A_Rule_Leaving_No_Column_To_Write_In_Is_Refused()
    {
        // A producer stating nonsense is better ignored than honoured, which is
        // the judgement the ODT reader already makes about a page layout with no
        // room in it.
        Assert.Null(Read(
            "<html><head><style>@page { size: 100pt 100pt; margin: 200pt }</style></head>" +
            "<body><p>body</p></body></html>").PageGeometry);
    }

    [Fact(Timeout = 600000)]
    public void A_Size_This_Codec_Does_Not_Know_Leaves_The_Page_Unread()
    {
        // Half a size is not a size. Guessing would produce a page nobody stated
        // and no diagnostic to say so.
        Assert.Null(Read(
            "<html><head><style>@page { size: tabloid-extra-wide }</style></head>" +
            "<body><p>body</p></body></html>").PageGeometry);
    }

    [Fact(Timeout = 600000)]
    public void A_Named_Page_Selector_Is_Not_Read_As_The_Whole_Document()
    {
        // `@page cover` states the page for part of a document. Reading it as the
        // page for all of it would be a guess, and this model has one page.
        Assert.Null(Read(
            "<html><head><style>@page cover { size: a5 }</style></head>" +
            "<body><p>body</p></body></html>").PageGeometry);
    }

    [Fact(Timeout = 600000)]
    public void Margin_Boxes_Do_Not_End_The_Rule_Early()
    {
        // A real @page can nest rules inside it. A scan that stopped at the first
        // closing brace would read the size and miss the margin.
        PageGeometry page = PageOf(
            "@page { size: letter; @top-center { content: \"x\" } margin: 18pt }");

        Assert.Equal(612, page.Width, 1);
        Assert.Equal(18, page.MarginTop, 1);
    }

    [Fact(Timeout = 600000)]
    public void The_Writer_States_The_Page_The_Document_Has()
    {
        string html = Write(RichTextDocument.FromPlainText("body")
            .WithPageGeometry(new PageGeometry(612, 792, 72, 72, 72, 72)));

        Assert.Contains("@page", html, StringComparison.Ordinal);
        Assert.Contains("size: 612pt 792pt", html, StringComparison.Ordinal);
        Assert.Contains("margin: 72pt 72pt 72pt 72pt", html, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void The_Writer_States_Nothing_For_A_Document_That_States_Nothing()
    {
        Assert.DoesNotContain("@page", Write(RichTextDocument.FromPlainText("body")), StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void The_Page_Survives_A_Round_Trip()
    {
        // The half that makes the other half worth having. Reading the page and
        // never writing it would move the loss rather than fix it.
        var stated = new PageGeometry(841.89, 595.276, 20, 30, 40, 50);

        RichTextDocument back = Read(Write(RichTextDocument.FromPlainText("body").WithPageGeometry(stated)));
        PageGeometry page = Assert.IsType<PageGeometry>(back.PageGeometry);

        Assert.Equal(stated.Width, page.Width, 1);
        Assert.Equal(stated.Height, page.Height, 1);
        Assert.Equal(stated.MarginTop, page.MarginTop, 1);
        Assert.Equal(stated.MarginRight, page.MarginRight, 1);
        Assert.Equal(stated.MarginBottom, page.MarginBottom, 1);
        Assert.Equal(stated.MarginLeft, page.MarginLeft, 1);
    }

    [Fact(Timeout = 600000)]
    public void The_Style_Element_Is_Still_Not_Read_As_Text()
    {
        // The skip that hid the page is still the right skip. A stylesheet in the
        // middle of the prose would be a worse bug than the one being fixed.
        RichTextDocument document = Read(
            "<html><head><style>@page { size: a4 }</style></head><body><p>body</p></body></html>");

        Assert.Equal("body", document.PlainText);
    }

    private static PageGeometry PageOf(string rule) =>
        Assert.IsType<PageGeometry>(Read(
            "<html><head><style>" + rule + "</style></head><body><p>body</p></body></html>").PageGeometry);

    private static RichTextDocument Read(string html)
    {
        var codec = new HtmlDocumentCodec();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        return codec.Read(stream).Document;
    }

    private static string Write(RichTextDocument document) =>
        Encoding.UTF8.GetString(HtmlWriter.WriteToArray(document));
}
