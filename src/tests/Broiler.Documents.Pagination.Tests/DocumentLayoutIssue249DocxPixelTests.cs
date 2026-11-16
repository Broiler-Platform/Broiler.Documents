using Broiler.Graphics.Color;

namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Issue #249, DOCX pixel cluster: with collapsing borders (every DOCX table,
/// as LibreOffice lays one out) a table's horizontal border lines take room in
/// its rows, each rule between two rows once, as <c>SwRowFrame::Format</c>
/// reserves it. Measured with
/// soffice 24.2 on a two-column table of 12 pt lines: 0.5 pt rules put the
/// rows 14.3 pt apart, 3 pt rules 16.8 pt, and 3 pt outer rules with no
/// inside rule 13.8 pt, with 3 pt above the first row and below the last.
/// </summary>
public sealed class DocumentLayoutIssue249DocxPixelTests
{
    private static readonly DocumentPageBox Letter = new(612, 792, 72, 72, 72, 72);

    private static RichTextParagraph Line(string text) =>
        RichTextParagraph.Create(text, InlineStyle.Default, ParagraphStyle.Default with
        {
            LineHeightRule = LineHeightRule.Exact,
            LineHeight = 12,
        });

    private static TableBorder Rule(double width) => new(BColor.Black, width);

    /// <summary>
    /// "before", a one-column table of three rows whose outer rules are
    /// <paramref name="outer"/> points and whose rule between rows is
    /// <paramref name="inside"/>, then "after".
    /// </summary>
    private static RichTextDocument Table(double outer, double inside, bool collapsing)
    {
        RichTextParagraph[] paragraphs = [Line("before"), Line("one"), Line("two"), Line("three"), Line("after")];
        TableCell Cell(int paragraph, double top, double bottom) =>
            new(paragraph, 1, 0, borders: new CellBorders(Rule(outer), Rule(top), Rule(outer), Rule(bottom)),
                padding: new TableCellPadding(5.4, 0, 5.4, 0));
        return RichTextDocument.FromParagraphs(paragraphs).WithTables([new DocumentTable(1, 3,
        [
            new TableRow([Cell(1, outer, inside)]),
            new TableRow([Cell(2, inside, inside)]),
            new TableRow([Cell(3, inside, outer)]),
        ], [200])]).WithStyleDefaults(DocumentStyleDefaults.Default with { CollapsingTableBorders = collapsing });
    }

    private static IReadOnlyList<double> Tops(RichTextDocument document)
    {
        using var images = new ImageStore();
        LayoutResult result = new DocumentLayout(new LayoutSettings(), images).Layout(document, Letter);
        return [.. result.Pages[0].Lines.Select(line => Math.Round(line.Top - 72, 3))];
    }

    [Fact]
    public void Each_Rule_Between_Rows_Takes_Its_Width_Once()
    {
        Assert.Equal([0, 15, 30, 45, 60], Tops(Table(outer: 3, inside: 3, collapsing: true)));
    }

    [Fact]
    public void Rows_With_No_Rule_Between_Them_Stand_Their_Text_Apart()
    {
        Assert.Equal([0, 15, 27, 39, 54], Tops(Table(outer: 3, inside: 0, collapsing: true)));
    }

    [Fact]
    public void Separated_Borders_Take_No_Room()
    {
        Assert.Equal([0, 12, 24, 36, 48], Tops(Table(outer: 3, inside: 3, collapsing: false)));
    }

    private static RichTextParagraph HiddenMark(RichTextParagraph paragraph) =>
        paragraph.WithMarkStyle(paragraph.MarkStyle with { Hidden = true });

    private static IReadOnlyList<string> BodyLines(RichTextDocument document)
    {
        using var images = new ImageStore();
        LayoutResult result = new DocumentLayout(new LayoutSettings(), images).Layout(document, Letter);
        return [.. result.Pages[0].Lines.Select(line => string.Concat(line.Pieces.Select(piece => piece.Text)))];
    }

    [Fact]
    public void A_Body_Paragraph_With_A_Hidden_Mark_Runs_On_Into_The_Next()
    {
        // hidden-para-separator.docx: Writer 24.2 draws "C", "D" and "E" on
        // one line, their first two marks hidden.
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [HiddenMark(Line("C ")), HiddenMark(Line("D")), Line("E"), Line("after")]);

        Assert.Equal(["C DE", "after"], BodyLines(document));
    }

    [Fact]
    public void A_Hidden_Mark_Does_Not_Join_A_Table_Or_An_Empty_Paragraph()
    {
        var table = new DocumentTable(1, 1, [new TableRow([new TableCell(1, 1, 0)])], [200]);
        RichTextDocument beforeTable = RichTextDocument.FromParagraphs(
            [HiddenMark(Line("text")), Line("cell"), Line("after")]).WithTables([table]);
        Assert.Equal(["text", "cell", "after"], BodyLines(beforeTable));

        RichTextDocument empty = RichTextDocument.FromParagraphs([HiddenMark(Line(string.Empty)), Line("next")]);
        Assert.Equal(["next"], BodyLines(empty));
    }

    private static RichTextParagraph Spaced(string text, float lineSpacing, float before = 0) =>
        RichTextParagraph.Create(text, InlineStyle.Default, ParagraphStyle.Default with
        {
            LineHeightRule = LineHeightRule.Proportional,
            LineSpacing = lineSpacing,
            SpacingBefore = before,
        });

    /// <summary>The tops of the first page's lines on an 18 pt line grid.</summary>
    private static IReadOnlyList<double> GridTops(bool snaps, params RichTextParagraph[] paragraphs)
    {
        var page = new PageGeometry(612, 792, 72, 72, 72, 72)
        {
            TextGrid = new TextGrid(18, 0, 36) { SnapsParagraphsToRows = snaps },
        };
        RichTextDocument document = RichTextDocument.FromParagraphs(paragraphs).WithPageGeometry(page);
        using var images = new ImageStore();
        LayoutResult result = new DocumentLayout(new LayoutSettings(), images).Layout(document, Letter);
        return [.. result.Pages[0].Lines.Select(line => Math.Round(line.Top, 3))];
    }

    /// <summary>A single-spaced line's own height, off any grid.</summary>
    private static double NaturalHeight()
    {
        using var images = new ImageStore();
        LayoutResult result = new DocumentLayout(new LayoutSettings(), images)
            .Layout(RichTextDocument.FromParagraphs([Spaced("x", 1)]), Letter);
        return result.Pages[0].Lines[0].Height;
    }

    [Fact]
    public void In_The_Standard_Page_Mode_The_Space_Above_A_Paragraph_Is_Not_Rounded_To_A_Row()
    {
        // writerfilter turns the squared page mode off for a DOCX, so
        // SwFlowFrame::CalcUpperSpace leaves a paragraph's 5 pt above as it is.
        IReadOnlyList<double> standard = GridTops(false, Spaced("one", 1), Spaced("two", 1, before: 5));
        Assert.Equal(18 + 5, standard[1] - standard[0], 3);

        IReadOnlyList<double> squared = GridTops(true, Spaced("one", 1), Spaced("two", 1, before: 5));
        Assert.Equal(36, squared[1] - squared[0], 3);
    }

    [Fact]
    public void Proportional_Spacing_On_A_Grid_Adds_The_Text_Height_Past_Single_Below_A_Paragraph()
    {
        // chtoutline.docx: one-line headings at triple spacing on an 18 pt
        // grid stand a row and twice their text height apart
        // (SwTextFormatter::CalcRealHeight spaces no paragraph's first line;
        // SwFlowFrame::CalcUpperSpace adds the line spacing of the one before).
        double natural = NaturalHeight();
        Assert.True(natural < 18);
        IReadOnlyList<double> tops = GridTops(false, Spaced("one", 3), Spaced("two", 3), Spaced("three", 1));
        Assert.Equal(18 + (2 * natural), tops[1] - tops[0], 3);
        Assert.Equal(18 + (2 * natural), tops[2] - tops[1], 3);
    }
}
