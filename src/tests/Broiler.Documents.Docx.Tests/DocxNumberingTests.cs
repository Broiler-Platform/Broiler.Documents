namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Covers <c>word/numbering.xml</c> (ECMA-376 §17.9): which list a paragraph is
/// numbered in, how its levels count, and the label each item gets.
/// </summary>
public sealed class DocxNumberingTests
{
    [Fact]
    public void A_List_Starts_At_Its_Start_And_Counts_On_Across_Unnumbered_Paragraphs()
    {
        RichTextDocument document = Read(
            Item(1, 0, "four") + Plain("between") + Item(1, 0, "five"),
            Abstract(0, Level(0, "decimal", "%1.", start: 4)) + Num(1, 0));

        Assert.Equal(["4.", null, "5."], Labels(document));
        Assert.Equal(ListKind.Numbered, document.Paragraphs[0].Style.ListKind);
        Assert.Equal(ListKind.None, document.Paragraphs[1].Style.ListKind);
    }

    [Fact]
    public void A_Level_Template_Names_The_Numbers_Of_The_Levels_Above_It()
    {
        RichTextDocument document = Read(
            Item(1, 0, "a") + Item(1, 1, "b") + Item(1, 1, "c") + Item(1, 0, "d") + Item(1, 1, "e"),
            Abstract(0, Level(0, "decimal", "%1.") + Level(1, "decimal", "%1.%2.")) + Num(1, 0));

        Assert.Equal(["1.", "1.1.", "1.2.", "2.", "2.1."], Labels(document));
    }

    [Fact]
    public void A_Level_That_Never_Restarts_Counts_On_Under_A_New_Parent()
    {
        RichTextDocument document = Read(
            Item(1, 0, "a") + Item(1, 1, "b") + Item(1, 0, "c") + Item(1, 1, "d"),
            Abstract(0, Level(0, "decimal", "%1.") + Level(1, "lowerLetter", "%2)", extra: "<w:lvlRestart w:val=\"0\"/>")) + Num(1, 0));

        Assert.Equal(["1.", "a)", "2.", "b)"], Labels(document));
    }

    [Fact]
    public void A_Legal_Level_Writes_Every_Number_In_Decimal()
    {
        RichTextDocument document = Read(
            Item(1, 0, "chapter") + Item(1, 1, "section"),
            Abstract(0, Level(0, "upperRoman", "%1") + Level(1, "decimal", "%1.%2", extra: "<w:isLgl/>")) + Num(1, 0));

        Assert.Equal(["I", "1.1"], Labels(document));
    }

    [Fact]
    public void Lists_Naming_One_Abstract_Numbering_Count_Together_Unless_One_Overrides_Its_Start()
    {
        string numbering =
            Abstract(0, Level(0, "decimal", "%1.")) +
            Num(1, 0) +
            Num(2, 0) +
            "<w:num w:numId=\"3\"><w:abstractNumId w:val=\"0\"/>" +
            "<w:lvlOverride w:ilvl=\"0\"><w:startOverride w:val=\"7\"/></w:lvlOverride></w:num>";

        RichTextDocument document = Read(
            Item(1, 0, "a") + Item(2, 0, "b") + Item(3, 0, "c") + Item(3, 0, "d") + Item(1, 0, "e"),
            numbering);

        Assert.Equal(["1.", "2.", "7.", "8.", "3."], Labels(document));
    }

    [Fact]
    public void A_Style_Names_The_List_And_The_Abstract_Numbering_Names_The_Level()
    {
        string styles = DocxTestPackage.Style("Heading2", "<w:pPr><w:numPr><w:numId w:val=\"1\"/></w:numPr></w:pPr>");
        string numbering = Abstract(0,
            Level(0, "decimal", "%1") +
            Level(1, "decimal", "%1.%2", extra: "<w:pStyle w:val=\"Heading2\"/>")) + Num(1, 0);

        RichTextDocument document = Read(DocxTestPackage.StyledParagraph("Heading2", "Scope"), numbering, styles);

        ListReference list = Assert.IsType<ListReference>(Assert.Single(document.Paragraphs).Style.List);
        Assert.Equal(1, list.Level);
        Assert.Equal(["1.1"], Labels(document));
    }

    [Fact]
    public void A_Paragraph_With_NumId_Nought_Leaves_The_List_Its_Style_Put_It_In()
    {
        string styles = DocxTestPackage.Style("Clause", "<w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr>");
        string body =
            DocxTestPackage.StyledParagraph("Clause", "numbered") +
            "<w:p><w:pPr><w:pStyle w:val=\"Clause\"/><w:numPr><w:numId w:val=\"0\"/></w:numPr></w:pPr><w:r><w:t>plain</w:t></w:r></w:p>";

        RichTextDocument document = Read(body, Abstract(0, Level(0, "decimal", "%1.")) + Num(1, 0), styles);

        Assert.Equal(["1.", null], Labels(document));
        Assert.Null(document.Paragraphs[1].Style.List);
    }

    [Fact]
    public void An_Abstract_Numbering_Linked_To_A_Numbering_Style_Takes_Its_Levels()
    {
        string numbering =
            "<w:abstractNum w:abstractNumId=\"0\"><w:styleLink w:val=\"Outline\"/>" + Level(0, "upperLetter", "%1)") + "</w:abstractNum>" +
            "<w:abstractNum w:abstractNumId=\"1\"><w:numStyleLink w:val=\"Outline\"/></w:abstractNum>" +
            Num(1, 1);

        RichTextDocument document = Read(Item(1, 0, "a") + Item(1, 0, "b"), numbering);

        Assert.Equal(["A)", "B)"], Labels(document));
    }

    [Fact]
    public void A_Level_Numbered_None_Counts_And_Draws_Nothing()
    {
        RichTextDocument document = Read(
            Item(1, 0, "unlabelled") + Item(1, 1, "child") + Item(1, 0, "unlabelled") + Item(1, 1, "child"),
            Abstract(0, Level(0, "none", "") + Level(1, "decimal", "%1(%2)")) + Num(1, 0));

        // The unnumbered level still restarts the one below it, and writes as
        // nothing where a label names it.
        Assert.Equal([null, "(1)", null, "(1)"], Labels(document));
        Assert.Equal(ListKind.None, document.Paragraphs[0].Style.ListKind);
    }

    [Fact]
    public void A_Symbol_Font_Bullet_Is_Read_As_The_Bullet_It_Draws()
    {
        string symbolBullet = ((char)0xF0B7).ToString();
        RichTextDocument document = Read(
            Item(1, 0, "point"),
            Abstract(0, Level(0, "bullet", symbolBullet)) + Num(1, 0));

        Assert.Equal([((char)0x2022).ToString()], Labels(document));
        Assert.Equal(ListKind.Bullet, document.Paragraphs[0].Style.ListKind);
    }

    [Fact]
    public void An_Empty_Paragraph_That_Only_Ends_A_Section_Takes_No_Number()
    {
        string styles = DocxTestPackage.Style("Heading1", "<w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr>");
        string body =
            "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/><w:sectPr/></w:pPr></w:p>" +
            DocxTestPackage.StyledParagraph("Heading1", "Einleitung");

        RichTextDocument document = Read(body, Abstract(0, Level(0, "decimal", "%1")) + Num(1, 0), styles);

        Assert.Equal([null, "1"], Labels(document));
    }

    [Fact]
    public void A_Level_Indent_Places_The_Label_Where_It_Hangs_Unless_The_Paragraph_Says_Otherwise()
    {
        string numbering = Abstract(0,
            Level(0, "decimal", "%1.", extra: "<w:pPr><w:ind w:left=\"1440\" w:hanging=\"360\"/></w:pPr>")) + Num(1, 0);
        string body =
            Item(1, 0, "level indent") +
            "<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr><w:ind w:hanging=\"720\"/></w:pPr>" +
            "<w:r><w:t>own hanging</w:t></w:r></w:p>";

        RichTextDocument document = Read(body, numbering);

        Assert.Equal(3, document.Paragraphs[0].Style.IndentLevel);
        Assert.Equal(2, document.Paragraphs[1].Style.IndentLevel);
    }

    [Fact]
    public void A_List_Round_Trips_Through_The_Writer_With_Its_Labels()
    {
        var definition = new ListDefinition(
        [
            new ListLevel { Format = ListNumberFormat.Decimal, Template = "(%1)", Start = 4 },
            new ListLevel { Format = ListNumberFormat.LowerRoman, Template = "%1.%2", Legal = true },
        ]);
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            Numbered("four", definition, 0),
            RichTextParagraph.Plain("between"),
            Numbered("five", definition, 0),
            Numbered("five-one", definition, 1),
        ]);

        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(document));
        RichTextDocument read = new DocxDocumentCodec().Read(stream).Document;

        Assert.Equal(["(4)", null, "(5)", "5.1"], Labels(read));
        Assert.Equal(Labels(document), Labels(read));
    }

    private static RichTextParagraph Numbered(string text, ListDefinition definition, int level)
    {
        var reference = new ListReference(definition, level);
        return RichTextParagraph.Create(
            text,
            InlineStyle.Default,
            ParagraphStyle.Default with { List = reference, ListKind = reference.Kind, IndentLevel = level + 1 });
    }

    private static List<string?> Labels(RichTextDocument document)
    {
        var numberer = new ListNumberer();
        return document.Paragraphs
            .Select(paragraph => paragraph.Style.List is null ? null : numberer.Advance(paragraph.Style))
            .ToList();
    }

    private static RichTextDocument Read(string body, string numberingInner, string? stylesInner = null)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/numbering.xml"] = "<w:numbering " + DocxTestPackage.BodyNamespaceDeclarations + ">" + numberingInner + "</w:numbering>",
        };
        if (stylesInner is not null)
            parts["word/styles.xml"] = "<w:styles " + DocxTestPackage.BodyNamespaceDeclarations + ">" + stylesInner + "</w:styles>";

        using var stream = new MemoryStream(DocxTestPackage.FromBody(body, parts), writable: false);
        return new DocxDocumentCodec().Read(stream).Document;
    }

    private static string Item(int numId, int level, string text) =>
        "<w:p><w:pPr><w:numPr><w:ilvl w:val=\"" + level + "\"/><w:numId w:val=\"" + numId + "\"/></w:numPr></w:pPr>" +
        "<w:r><w:t>" + text + "</w:t></w:r></w:p>";

    private static string Plain(string text) => "<w:p><w:r><w:t>" + text + "</w:t></w:r></w:p>";

    private static string Abstract(int id, string levels) =>
        "<w:abstractNum w:abstractNumId=\"" + id + "\">" + levels + "</w:abstractNum>";

    private static string Num(int numId, int abstractId) =>
        "<w:num w:numId=\"" + numId + "\"><w:abstractNumId w:val=\"" + abstractId + "\"/></w:num>";

    private static string Level(int ilvl, string format, string text, int start = 1, string extra = "") =>
        "<w:lvl w:ilvl=\"" + ilvl + "\"><w:start w:val=\"" + start + "\"/><w:numFmt w:val=\"" + format + "\"/>" +
        extra +
        "<w:lvlText w:val=\"" + text + "\"/>" +
        "</w:lvl>";
}
