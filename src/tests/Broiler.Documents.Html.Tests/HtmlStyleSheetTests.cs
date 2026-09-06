using System.Text;
using Broiler.Graphics;

namespace Broiler.Documents.Html.Tests;

/// <summary>
/// The document's own stylesheet, in the one form this codec reads it: a rule
/// whose selector is a bare element name.
/// </summary>
/// <remarks>
/// <para>
/// The reader applied CSS from <c>style</c> attributes and nowhere else, and the
/// office conformance suite reported HTML scoring below its own DOCX and ODT
/// twins because of it. The measurement that narrowed the fix is worth keeping
/// here: converting DOCX to HTML, LibreOffice writes a <c>p { ... }</c> rule and
/// then an inline style on every paragraph that overrides it, so the sheet does
/// not matter; converting HTML to HTML it writes bare <c>&lt;p&gt;</c> elements
/// and the rule says everything. The second shape is the one that was lost, and
/// it was lost in silence, because a declaration the reader never looked at
/// leaves nothing behind for it to report.
/// </para>
/// <para>
/// So these tests are as much about what is not implemented as what is. A class
/// rule still selects nothing here, and the reason it never will is that
/// ordering a class rule against an id rule against a descendant rule is the
/// cascade, and the cascade is a browser.
/// </para>
/// </remarks>
public sealed class HtmlStyleSheetTests
{
    [Fact(Timeout = 600000)]
    public void A_Type_Rule_Reaches_An_Element_That_States_No_Style_Of_Its_Own()
    {
        RichTextDocument document = Read(
            "<html><head><style>p { text-align: center }</style></head>" +
            "<body><p>text</p></body></html>");

        Assert.Equal(TextAlignment.Center, Assert.Single(document.Paragraphs).Style.Alignment);
    }

    [Fact(Timeout = 600000)]
    public void An_Inline_Style_Wins_Over_The_Type_Rule_Under_It()
    {
        // The whole of this codec's specificity: one comparison with one answer.
        RichTextDocument document = Read(
            "<html><head><style>p { text-align: center }</style></head>" +
            "<body><p style='text-align: right'>text</p></body></html>");

        Assert.Equal(TextAlignment.Right, Assert.Single(document.Paragraphs).Style.Alignment);
    }

    [Fact(Timeout = 600000)]
    public void A_Property_The_Inline_Style_Does_Not_State_Still_Applies()
    {
        // The half of "inline wins" that is easy to get wrong: overriding one
        // property must not discard the rest of the rule. A reader that took the
        // inline style whole whenever there was one would drop the line spacing
        // here and report nothing.
        RichTextDocument document = Read(
            "<html><head><style>p { text-align: center; line-height: 200% }</style></head>" +
            "<body><p style='text-align: right'>text</p></body></html>");

        RichTextParagraph paragraph = Assert.Single(document.Paragraphs);
        Assert.Equal(TextAlignment.Right, paragraph.Style.Alignment);
        Assert.Equal(2f, paragraph.Style.LineSpacing);
    }

    [Fact(Timeout = 600000)]
    public void A_Type_Rule_Reaches_An_Inline_Element_Too()
    {
        RichTextDocument document = Read(
            "<html><head><style>span { font-weight: bold; color: #336699 }</style></head>" +
            "<body><p>plain <span>styled</span></p></body></html>");

        RichTextParagraph paragraph = Assert.Single(document.Paragraphs);
        Assert.False(paragraph.StyleAt(0).Bold);
        Assert.True(paragraph.StyleAt(6).Bold);
        Assert.Equal(BColor.FromArgb(0x33, 0x66, 0x99), paragraph.StyleAt(6).Foreground);
    }

    [Fact(Timeout = 600000)]
    public void A_Type_Rule_On_A_Container_Feeds_The_Descent_This_Reader_Already_Had()
    {
        // Not a computed-style inheritance pass. The rule contributes to the
        // `div` it names, and what happens after that is the descent that already
        // carried a container's own style attribute into the blocks inside it.
        RichTextDocument document = Read(
            "<html><head><style>div { text-align: center }</style></head>" +
            "<body><div><p>one</p><p>two</p></div></body></html>");

        Assert.Equal(2, document.ParagraphCount);
        Assert.All(
            document.Paragraphs,
            paragraph => Assert.Equal(TextAlignment.Center, paragraph.Style.Alignment));
    }

    [Theory(Timeout = 600000)]
    // Everything on the far side of the line. Each of these selects something a
    // browser can work out and this codec deliberately cannot.
    [InlineData(".lead")]
    [InlineData("#first")]
    [InlineData("div p")]
    [InlineData("div > p")]
    [InlineData("p:first-child")]
    [InlineData("p[align]")]
    [InlineData("p.lead")]
    [InlineData("*")]
    public void A_Selector_This_Codec_Does_Not_Implement_Selects_Nothing(string selector)
    {
        DocumentReadResult result = ReadResult(
            "<html><head><style>" + selector + " { text-align: center }</style></head>" +
            "<body><div><p class='lead' id='first'>text</p></div></body></html>");

        Assert.Equal(TextAlignment.Left, Assert.Single(result.Document.Paragraphs).Style.Alignment);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "html.css.rule");
    }

    [Fact(Timeout = 600000)]
    public void A_Selector_List_Of_Type_Selectors_Applies_To_Each_Of_Them()
    {
        RichTextDocument document = Read(
            "<html><head><style>h1, h2, h3 { color: #336699 }</style></head>" +
            "<body><h1>one</h1><h2>two</h2><h3>three</h3><p>four</p></body></html>");

        var blue = BColor.FromArgb(0x33, 0x66, 0x99);
        Assert.Equal(blue, document.Paragraphs[0].StyleAt(0).Foreground);
        Assert.Equal(blue, document.Paragraphs[1].StyleAt(0).Foreground);
        Assert.Equal(blue, document.Paragraphs[2].StyleAt(0).Foreground);
        Assert.NotEqual(blue, document.Paragraphs[3].StyleAt(0).Foreground);
    }

    [Fact(Timeout = 600000)]
    public void A_Selector_List_With_One_Item_This_Codec_Cannot_Match_Is_Skipped_Whole()
    {
        // Not applied to the h1 and dropped for the .lead. Two elements an author
        // styled together would come back one styled and one not, and nothing in
        // the result would say which had happened to which.
        DocumentReadResult result = ReadResult(
            "<html><head><style>h1, .lead { color: #336699 }</style></head>" +
            "<body><h1>one</h1></body></html>");

        Assert.NotEqual(
            BColor.FromArgb(0x33, 0x66, 0x99),
            Assert.Single(result.Document.Paragraphs).StyleAt(0).Foreground);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "html.css.rule");
    }

    [Fact(Timeout = 600000)]
    public void A_Rule_For_An_Element_The_Document_Does_Not_Have_Changes_Nothing()
    {
        DocumentReadResult result = ReadResult(
            "<html><head><style>blockquote { text-align: center; line-height: 300% }</style></head>" +
            "<body><p>text</p></body></html>");

        RichTextParagraph paragraph = Assert.Single(result.Document.Paragraphs);
        Assert.Equal(ParagraphStyle.Default.Alignment, paragraph.Style.Alignment);
        Assert.Equal(ParagraphStyle.Default.LineSpacing, paragraph.Style.LineSpacing);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "html.css.rule");
    }

    [Fact(Timeout = 600000)]
    public void An_Ordinary_Document_With_Type_Rules_Reports_Nothing()
    {
        DocumentReadResult result = ReadResult(
            "<html><head><style>@page { size: a4 } p { line-height: 115% }</style></head>" +
            "<body><p>text</p></body></html>");

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "html.css.rule");
    }

    [Fact(Timeout = 600000)]
    public void The_Later_Of_Two_Rules_Wins_The_Property_They_Share()
    {
        // Source order, which is what CSS says for two rules of one specificity -
        // and unlike the class case, saying so here needs no comparison at all.
        RichTextDocument document = Read(
            "<html><head><style>p { text-align: center } p { text-align: right }</style></head>" +
            "<body><p>text</p></body></html>");

        Assert.Equal(TextAlignment.Right, Assert.Single(document.Paragraphs).Style.Alignment);
    }

    [Fact(Timeout = 600000)]
    public void Rules_In_Separate_Style_Elements_Are_Both_Read()
    {
        // A producer may split its sheet, which is already why the page is looked
        // for across every style element rather than the first.
        RichTextDocument document = Read(
            "<html><head><style>p { text-align: center }</style>" +
            "<style>p { line-height: 200% }</style></head>" +
            "<body><p>text</p></body></html>");

        RichTextParagraph paragraph = Assert.Single(document.Paragraphs);
        Assert.Equal(TextAlignment.Center, paragraph.Style.Alignment);
        Assert.Equal(2f, paragraph.Style.LineSpacing);
    }

    [Fact(Timeout = 600000)]
    public void A_Rule_Inside_A_Media_Query_Is_Not_Applied()
    {
        // @media asks about the device, and this codec is not one. The block is
        // stepped over whole so that the rule nested in it does not leak out and
        // become an unconditional rule, which would be worse than not reading it.
        DocumentReadResult result = ReadResult(
            "<html><head><style>@media print { p { text-align: center } }</style></head>" +
            "<body><p>text</p></body></html>");

        Assert.Equal(TextAlignment.Left, Assert.Single(result.Document.Paragraphs).Style.Alignment);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "html.css.rule");
    }

    [Fact(Timeout = 600000)]
    public void A_Statement_At_Rule_Does_Not_Swallow_The_Rule_After_It()
    {
        // @charset ends at its semicolon and has no block. Scanning to the next
        // brace instead would take `p` into the at-rule's prelude and lose the
        // rule with it.
        RichTextDocument document = Read(
            "<html><head><style>@charset \"utf-8\"; p { text-align: center }</style></head>" +
            "<body><p>text</p></body></html>");

        Assert.Equal(TextAlignment.Center, Assert.Single(document.Paragraphs).Style.Alignment);
    }

    [Fact(Timeout = 600000)]
    public void A_Comment_Does_Not_Take_The_Rule_Beside_It()
    {
        RichTextDocument document = Read(
            "<html><head><style>/* the body text */ p { text-align: center } /* ends */</style></head>" +
            "<body><p>text</p></body></html>");

        Assert.Equal(TextAlignment.Center, Assert.Single(document.Paragraphs).Style.Alignment);
    }

    [Fact(Timeout = 600000)]
    public void A_Sheet_Wrapped_In_Html_Comment_Delimiters_Is_Still_Read()
    {
        // The habit of hiding a stylesheet from a browser too old to know the
        // element, which is what a word processor still writes. Left in place the
        // first selector reads `<!-- p`, which is not a type selector - so the
        // exact case this was built for would have been skipped as unimplemented.
        RichTextDocument document = Read(
            "<html><head><style type=\"text/css\">\n<!--\n" +
            "p { text-align: center }\n-->\n</style></head>" +
            "<body><p>text</p></body></html>");

        Assert.Equal(TextAlignment.Center, Assert.Single(document.Paragraphs).Style.Alignment);
    }

    [Fact(Timeout = 600000)]
    public void A_Nested_Block_Does_Not_Swallow_The_Declaration_After_It()
    {
        // The trap the @page reader documents, met again: the declaration splitter
        // divides on semicolons and then the first colon, so a nested block left
        // in the body eats the declaration that follows it and says nothing.
        RichTextDocument document = Read(
            "<html><head><style>p { line-height: 200%; & span { color: red } text-align: center }" +
            "</style></head><body><p>text</p></body></html>");

        RichTextParagraph paragraph = Assert.Single(document.Paragraphs);
        Assert.Equal(2f, paragraph.Style.LineSpacing);
        Assert.Equal(TextAlignment.Center, paragraph.Style.Alignment);
    }

    [Fact(Timeout = 600000)]
    public void The_At_Page_Rule_Is_Still_Read_Beside_The_Type_Rules()
    {
        // Both halves come out of one gathering of the style elements, and the
        // type-rule scan has to step over `@page` rather than mistake it for a
        // selector.
        RichTextDocument document = Read(
            "<html><head><style>@page { size: letter; margin: 2.54cm } p { text-align: center }" +
            "</style></head><body><p>text</p></body></html>");

        PageGeometry page = Assert.IsType<PageGeometry>(document.PageGeometry);
        Assert.Equal(612, page.Width, 1);
        Assert.Equal(TextAlignment.Center, Assert.Single(document.Paragraphs).Style.Alignment);
    }

    [Fact(Timeout = 600000)]
    public void The_Style_Element_Is_Still_Not_Read_As_Text()
    {
        // Reading the sheet is not letting it into the prose. The skip that hid
        // these rules is still the right skip.
        RichTextDocument document = Read(
            "<html><head><style>p { text-align: center }</style></head>" +
            "<body><p>text</p></body></html>");

        Assert.Equal("text", document.PlainText);
    }

    /// <summary>
    /// The shape LibreOffice writes when it converts HTML to HTML, end to end:
    /// a bare paragraph and a stylesheet that says everything about it. This is
    /// the document the office conformance suite scored HTML down on, and every
    /// property in it used to arrive as the model default.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void The_Shape_Libreoffice_Writes_Arrives_Whole()
    {
        DocumentReadResult result = ReadResult(
            "<html><head><meta charset=\"utf-8\"><title>styled</title>" +
            "<style type=\"text/css\">\n<!--\n" +
            "\t\t@page { size: 21.59cm 27.94cm; margin: 2cm }\n" +
            "\t\tp { line-height: 115%; margin-bottom: 0.25cm; background: transparent }\n" +
            "-->\n</style></head>" +
            "<body lang=\"en-GB\"><p>The paragraph the stylesheet describes.</p></body></html>");

        RichTextParagraph paragraph = Assert.Single(result.Document.Paragraphs);
        Assert.Equal("The paragraph the stylesheet describes.", paragraph.Text);
        Assert.Equal(1.15f, paragraph.Style.LineSpacing, 3);
        Assert.Equal(7.087f, paragraph.Style.SpacingAfter, 2);

        PageGeometry page = Assert.IsType<PageGeometry>(result.Document.PageGeometry);
        Assert.Equal(612, page.Width, 1);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "html.css.rule");
    }

    private static RichTextDocument Read(string html) => ReadResult(html).Document;

    private static DocumentReadResult ReadResult(string html)
    {
        var codec = new HtmlDocumentCodec();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        return codec.Read(stream);
    }
}
