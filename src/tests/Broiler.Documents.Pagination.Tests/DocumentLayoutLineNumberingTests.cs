namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Line numbering: a number in the left margin beside each body line, as a
/// page's <see cref="PageGeometry.LineNumbering"/> asks for it.
/// </summary>
/// <remarks>
/// LibreOffice draws <c>tdf121664.docx</c>'s four empty paragraphs, two to a
/// page, as "1, 2" on both pages, right-aligned 0.5 cm left of the text.
/// </remarks>
public sealed class DocumentLayoutLineNumberingTests
{
    private static readonly DocumentPageBox Letter = new(612, 792, 72, 72, 72, 72);

    private static RichTextDocument Lines(int count, LineNumbering? numbering) =>
        RichTextDocument.FromParagraphs(Enumerable.Range(1, count).Select(i =>
                RichTextParagraph.Create("Line " + i, InlineStyle.Default, ParagraphStyle.Default)))
            .WithPageGeometry(new PageGeometry(612, 792, 72, 72, 72, 72) { LineNumbering = numbering });

    private static LayoutResult Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, Letter);
    }

    /// <summary>The number set beside a line, or null for none.</summary>
    private static string? NumberOf(LayoutLine line) =>
        line.Pieces.Where(piece => piece.X < 72).Select(piece => piece.Text).SingleOrDefault();

    [Fact]
    public void Without_Numbering_No_Line_Carries_A_Number()
    {
        LayoutPage page = Layout(Lines(3, numbering: null)).Pages[0];

        Assert.All(page.Lines, line => Assert.Null(NumberOf(line)));
    }

    [Fact]
    public void Each_Body_Line_Is_Numbered_In_The_Left_Margin()
    {
        LayoutPage page = Layout(Lines(3, new LineNumbering(1))).Pages[0];

        Assert.Equal(["1", "2", "3"], page.Lines.Select(NumberOf));

        // Right-aligned the default distance left of the text column.
        LayoutPiece number = page.Lines[0].Pieces.Last();
        Assert.Equal(72 - LineNumbering.DefaultDistance, number.X + number.Width, 3);
    }

    [Fact]
    public void Only_Multiples_Of_The_Count_Show_Their_Number()
    {
        LayoutPage page = Layout(Lines(6, new LineNumbering(countBy: 3))).Pages[0];

        Assert.Equal([null, null, "3", null, null, "6"], page.Lines.Select(NumberOf));
    }

    [Fact]
    public void The_Count_Restarts_On_Each_Page_Unless_It_Is_Continuous()
    {
        int perPage = Layout(Lines(400, numbering: null)).Pages[0].Lines.Count;

        LayoutResult restarting = Layout(Lines(perPage + 1, new LineNumbering(1)));
        Assert.Equal("1", NumberOf(restarting.Pages[1].Lines[0]));

        LayoutResult continuous = Layout(Lines(perPage + 1,
            new LineNumbering(1, start: 5, restart: LineNumberRestart.Continuous)));
        Assert.Equal((perPage + 5).ToString(System.Globalization.CultureInfo.InvariantCulture),
            NumberOf(continuous.Pages[1].Lines[0]));
    }
}
