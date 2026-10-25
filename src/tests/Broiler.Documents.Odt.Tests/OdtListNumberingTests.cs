namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Covers how ODF lists and the outline count and label their paragraphs
/// (ODF 1.3 §5.3, §16.30-16.33).
/// </summary>
public sealed class OdtListNumberingTests
{
    [Fact]
    public void Only_The_First_Paragraph_Of_An_Item_Has_A_Label_And_Nested_Items_Count_Separately()
    {
        string body = "<text:list text:style-name=\"N\"><text:list-item>" + P("one") + P("continued") +
            "<text:list><text:list-item>" + P("child") + P("child continued") +
            "</text:list-item></text:list>" + P("after child") +
            "</text:list-item><text:list-item>" + P("two") + "</text:list-item></text:list>";
        RichTextDocument document = Read(body, Numbered);
        Assert.Equal(["1.", null, "(a)", null, null, "2."], Labels(document));
        Assert.Equal(1, document.Paragraphs[1].Style.IndentLevel);
        Assert.Equal(2, document.Paragraphs[3].Style.IndentLevel);
    }

    [Theory]
    [InlineData("Outline")]
    [InlineData("Custom_20_Outline")]
    public void Lists_Can_Reference_The_Named_Outline_Style(string name)
    {
        string outline = "<office:styles><text:outline-style style:name='" + name + "'>" +
            "<text:outline-level-style text:level='1' style:num-format='1'/>" +
            "<text:outline-level-style text:level='2' style:num-format='1' text:display-levels='2'/>" +
            "</text:outline-style></office:styles>";
        string body = "<text:list text:style-name='" + name + "'><text:list-item>" + P("parent") +
            "<text:list><text:list-item>" + P("child") + "</text:list-item><text:list-item>" + P("next") +
            "</text:list-item></text:list></text:list-item></text:list>";
        var result = OdtTestPackage.ReadStyled(body, outline);
        Assert.Equal(["1", "1.1", "1.2"], Labels(result.Document));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "odt.styles.list-unknown");
        using var stream = new MemoryStream();
        new OdtDocumentCodec().Write(result.Document, stream);
        stream.Position = 0;
        Assert.Equal(Labels(result.Document), Labels(new OdtDocumentCodec().Read(stream).Document));
    }

    [Fact]
    public void Named_List_Styles_Take_Precedence_Over_The_Outline_And_Unknown_Names_Still_Warn()
    {
        string styles = "<office:styles><text:outline-style style:name='N'>" +
            "<text:outline-level-style text:level='1' style:num-format='1'/></text:outline-style>" +
            "<text:list-style style:name='N'><text:list-level-style-number text:level='1' style:num-format='a'/>" +
            "</text:list-style></office:styles>";
        var result = OdtTestPackage.ReadStyled(List("N", "", P("letter")) + List("missing", "", P("unknown")), styles);
        Assert.Equal("a", Labels(result.Document)[0]);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.styles.list-unknown");
    }

    private const string Numbered =
        "<text:list-style style:name=\"N\">" +
        "<text:list-level-style-number text:level=\"1\" style:num-format=\"1\" style:num-suffix=\".\"/>" +
        "<text:list-level-style-number text:level=\"2\" style:num-format=\"a\" style:num-prefix=\"(\" style:num-suffix=\")\"/>" +
        "<text:list-level-style-number text:level=\"3\" style:num-format=\"i\" text:display-levels=\"3\"/>" +
        "</text:list-style>";

    [Fact]
    public void A_Level_Reads_Its_Format_Prefix_Suffix_And_Displayed_Levels()
    {
        RichTextDocument document = Read(
            "<text:list text:style-name=\"N\"><text:list-item>" + P("one") +
            "<text:list><text:list-item>" + P("sub") +
            "<text:list><text:list-item>" + P("deep") + "</text:list-item></text:list>" +
            "</text:list-item></text:list></text:list-item></text:list>",
            Numbered);

        Assert.Equal(["1.", "(a)", "1.a.i"], Labels(document));
    }

    [Fact]
    public void Each_List_Counts_Afresh_Unless_It_Continues_One()
    {
        string body =
            List("N", "", P("a"), P("b")) +
            P("between") +
            List("N", "", P("fresh")) +
            List("N", " text:continue-numbering=\"true\"", P("continued")) +
            List("N", " xml:id=\"first\"", P("x")) +
            List("N", "", P("y")) +
            List("N", " text:continue-list=\"first\"", P("x again"));

        Assert.Equal(["1.", "2.", null, "1.", "2.", "1.", "1.", "2."], Labels(Read(body, Numbered)));
    }

    [Fact]
    public void A_List_Item_Start_Value_Restarts_The_Count_There()
    {
        string body =
            "<text:list text:style-name=\"N\">" +
            "<text:list-item>" + P("one") + "</text:list-item>" +
            "<text:list-item text:start-value=\"7\">" + P("seven") + P("same item") + "</text:list-item>" +
            "<text:list-item>" + P("eight") + "</text:list-item>" +
            "</text:list>";

        Assert.Equal(["1.", "7.", null, "8."], Labels(Read(body, Numbered)));
    }

    [Fact]
    public void A_Level_With_An_Empty_Format_Draws_No_Label()
    {
        string style =
            "<text:list-style style:name=\"E\">" +
            "<text:list-level-style-number text:level=\"1\" style:num-format=\"\"/>" +
            "</text:list-style>";

        RichTextDocument document = Read(List("E", "", P("plain step")), style);

        Assert.Equal([null], Labels(document));
        Assert.Equal(ListKind.None, document.Paragraphs[0].Style.ListKind);
    }

    [Fact]
    public void A_List_Header_Is_In_No_List()
    {
        RichTextDocument document = Read(
            "<text:list text:style-name=\"N\"><text:list-header>" + P("lead-in") + "</text:list-header>" +
            "<text:list-item>" + P("one") + "</text:list-item></text:list>",
            Numbered);

        Assert.Null(document.Paragraphs[0].Style.List);
        Assert.Equal([null, "1."], Labels(document));
    }

    [Fact]
    public void Headings_Are_Numbered_By_The_Outline_Style_From_A_Missing_Parent()
    {
        string outline =
            "<office:styles><text:outline-style style:name=\"Outline\">" +
            "<text:outline-level-style text:level=\"1\" style:num-format=\"1\"/>" +
            "<text:outline-level-style text:level=\"2\" style:num-format=\"1\" text:display-levels=\"2\"/>" +
            "</text:outline-style></office:styles>";
        string body =
            H(2, "first") + P("text") + H(2, "second") + H(1, "chapter") + H(2, "section") +
            "<text:h text:outline-level=\"1\" text:is-list-header=\"true\">unnumbered</text:h>";

        DocumentReadResult result = OdtTestPackage.ReadStyled(body, outline);

        // LibreOffice's numbering of tdf142694-1.odt.
        Assert.Equal(["1.1", null, "1.2", "2", "2.1", null], Labels(result.Document));
    }

    [Fact]
    public void Headings_Are_Not_Numbered_Where_The_Outline_Numbers_No_Level()
    {
        string outline =
            "<office:styles><text:outline-style style:name=\"Outline\">" +
            "<text:outline-level-style text:level=\"1\" style:num-format=\"\"/>" +
            "</text:outline-style></office:styles>";

        DocumentReadResult result = OdtTestPackage.ReadStyled(H(1, "Title"), outline);

        Assert.Null(Assert.Single(result.Document.Paragraphs).Style.List);
    }

    [Fact]
    public void A_List_Round_Trips_Through_The_Writer_With_Its_Labels()
    {
        var definition = new ListDefinition(
        [
            new ListLevel { Template = "(%1)", Start = 4 },
            new ListLevel { Template = "%1.%2.", Format = ListNumberFormat.UpperLetter },
        ]);
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            Item("four", definition, 0),
            RichTextParagraph.Plain("between"),
            Item("five", definition, 0),
            Item("five-a", definition, 1),
        ]);

        RichTextDocument read = OdtWriterTests.RoundTrip(document);

        Assert.Equal(["(4)", null, "(5)", "5.A."], Labels(read));
    }

    private static RichTextParagraph Item(string text, ListDefinition definition, int level)
    {
        var reference = new ListReference(definition, level);
        return RichTextParagraph.Create(
            text,
            InlineStyle.Default,
            ParagraphStyle.Default with { List = reference, ListKind = reference.Kind, IndentLevel = level + 1 });
    }

    private static RichTextDocument Read(string body, string listStyles) =>
        OdtTestPackage.ReadBody(body, listStyles).Document;

    private static string P(string text) => OdtTestPackage.Paragraph(text);

    private static string H(int level, string text) =>
        "<text:h text:outline-level=\"" + level + "\">" + OdtTestPackage.Escape(text) + "</text:h>";

    private static string List(string style, string attributes, params string[] items) =>
        "<text:list text:style-name=\"" + style + "\"" + attributes + ">" +
        string.Concat(items.Select(item => "<text:list-item>" + item + "</text:list-item>")) +
        "</text:list>";

    private static List<string?> Labels(RichTextDocument document)
    {
        var numberer = new ListNumberer();
        return document.Paragraphs
            .Select(paragraph => paragraph.Style.List is null ? null : numberer.Advance(paragraph.Style))
            .ToList();
    }
}
