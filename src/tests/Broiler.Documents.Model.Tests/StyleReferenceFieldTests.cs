namespace Broiler.Documents.Model.Tests;

/// <summary>
/// What a <see cref="StyleReferenceField"/> shows on a page: Word's search, and
/// LibreOffice's since the tdf#163894 fixes, as <c>testTdf163894*</c> in
/// <c>sw/qa/extras/ooxmlexport/ooxmlexport14.cxx</c> pins it.
/// </summary>
public sealed class StyleReferenceFieldTests
{
    private static readonly InlineStyle Entry = InlineStyle.Default with { CharacterStyle = "Main Index Entry", Bold = true };

    private static readonly StyleReferenceField First = new("Main Index Entry");

    private static readonly StyleReferenceField Last = new("Main Index Entry", FromBottom: true);

    /// <summary>
    /// A paragraph of plain text and entry words: each item is text, and an
    /// item starting with '*' is an entry word without the star.
    /// </summary>
    private static RichTextParagraph Paragraph(params string[] items)
    {
        RichTextParagraph paragraph = RichTextParagraph.Empty;
        foreach (string item in items)
        {
            bool entry = item.StartsWith('*');
            string text = entry ? item[1..] : item;
            paragraph = paragraph.InsertText(paragraph.Length, text, entry ? Entry : InlineStyle.Default);
        }

        return paragraph;
    }

    private static readonly RichTextParagraph[] Dictionary =
    [
        Paragraph("*handbooks", ", ", "*hank", ", ", "*infuriating", " Lorem ipsum dolor sit amet."),
        Paragraph("Consectetur adipiscing elit, ", "*initializes", ", ", "*misfitting"),
    ];

    [Fact]
    public void A_Page_Shows_Its_First_Entry_Or_With_L_Its_Last()
    {
        Assert.Equal("handbooks", First.Text(Dictionary, (0, 0), (0, 40)));
        Assert.Equal("infuriating", Last.Text(Dictionary, (0, 0), (0, 40)));
    }

    [Fact]
    public void A_Page_With_No_Entry_Of_Its_Own_Shows_The_Nearest_Before_It_Either_Way()
    {
        // The paragraph's tail and the start of the next: "Lorem ... elit,".
        int lorem = Dictionary[0].Text.IndexOf("Lorem", StringComparison.Ordinal);

        Assert.Equal("infuriating", First.Text(Dictionary, (0, lorem), (1, 10)));
        Assert.Equal("infuriating", Last.Text(Dictionary, (0, lorem), (1, 10)));
    }

    [Fact]
    public void With_Nothing_Before_The_Page_It_Shows_The_First_After_It()
    {
        RichTextParagraph[] paragraphs = [Paragraph("Plain opening text."), .. Dictionary];

        Assert.Equal("handbooks", Last.Text(paragraphs, (0, 0), (0, 5)));
    }

    [Fact]
    public void An_Entry_Running_Onto_The_Page_Counts_As_On_It()
    {
        int hank = Dictionary[0].Text.IndexOf("hank", StringComparison.Ordinal);

        Assert.Equal("hank", First.Text(Dictionary, (0, hank + 2), (1, 0)));
    }

    [Fact]
    public void Adjacent_Runs_In_The_Style_Are_One_Entry()
    {
        RichTextParagraph paragraph = RichTextParagraph.Empty
            .InsertText(0, "Mau", Entry)
            .InsertText(3, "ris", Entry with { Italic = true })
            .InsertText(6, " and more", InlineStyle.Default);

        Assert.Equal("Mauris", First.Text([paragraph], (0, 0), (1, 0)));
    }

    [Fact]
    public void A_Hidden_Entry_Counts()
    {
        RichTextParagraph paragraph = Paragraph("text ")
            .InsertText(5, " ", Entry with { Hidden = true })
            .InsertText(6, " ", InlineStyle.Default with { Hidden = true })
            .InsertText(7, "visible", Entry);

        Assert.Equal(" ", First.Text([paragraph], (0, 0), (0, 6)));
    }

    [Fact]
    public void The_Exact_Name_Wins_Over_One_Matching_Only_Ignoring_Case()
    {
        RichTextParagraph paragraph = RichTextParagraph.Empty
            .InsertText(0, "loose", InlineStyle.Default with { CharacterStyle = "main index entry" })
            .InsertText(5, " ", InlineStyle.Default)
            .InsertText(6, "exact", Entry);

        Assert.Equal("exact", First.Text([paragraph], (0, 0), (1, 0)));
        Assert.Equal("loose", new StyleReferenceField("MAIN INDEX ENTRY").Text([paragraph], (0, 0), (1, 0)));
    }

    [Fact]
    public void A_Document_With_No_Text_In_The_Style_Keeps_The_Saved_Text()
    {
        Assert.Null(new StyleReferenceField("Emphasis").Text(Dictionary, (0, 0), (2, 0)));
    }
}
