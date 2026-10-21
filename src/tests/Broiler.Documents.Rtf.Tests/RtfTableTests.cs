using System.Text;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// Tables: <c>\trowd</c> and <c>\cellx</c> define a row, <c>\intbl</c> puts
/// paragraphs in it, <c>\cell</c> and <c>\row</c> close them. The reader knew
/// none of it - <c>\cell</c> was a tab and <c>\row</c> a paragraph - so a table
/// arrived as lines of tab-separated text with no grid and no borders.
/// </summary>
public sealed class RtfTableTests
{
    private static RichTextDocument Read(string rtf) => RtfReader.Read(Encoding.ASCII.GetBytes(rtf)).Document;

    private static IEnumerable<string> Texts(RichTextDocument document) =>
        document.Paragraphs.Select(paragraph => paragraph.Text);

    [Fact]
    public void Reads_A_Table_Whose_Cells_And_Row_Close_Inside_Groups()
    {
        // The shape of fdo66565.rtf: the whole table in a group, each cell's
        // text and its \cell in a group of their own, and \row in another.
        RichTextDocument document = Read(
            "{\\rtf1{\\trowd\\cellx1000\\cellx3000\n\\intbl\n{\\b A1 \\cell}\n{\\b B1 \\cell}\n{\\row}\n\\pard}\n\\par After\\par}");

        DocumentTable table = Assert.Single(document.Tables);
        TableRow row = Assert.Single(table.Rows);
        Assert.Equal(2, row.Cells.Count);
        Assert.Equal(["A1 ", "B1 ", "", "After"], Texts(document));
        Assert.Equal(0, table.ParagraphIndex);
        Assert.Equal(2, table.ParagraphCount);
        Assert.Equal((0, 1), (row.Cells[0].ParagraphIndex, row.Cells[0].ParagraphCount));
        Assert.Equal((1, 1), (row.Cells[1].ParagraphIndex, row.Cells[1].ParagraphCount));
        Assert.Equal([50, 100], table.ColumnWidths);
        Assert.True(document.Paragraphs[0].StyleAt(0).Bold);
    }

    [Fact]
    public void Rows_That_Disagree_Share_One_Grid()
    {
        // fdo66565.rtf has seven cells over eighteen, and LibreOffice's own test
        // of it asks that the second row's first cell is 453 twips wide: each
        // cell keeps the width its own row gives it.
        RichTextDocument document = Read(
            "{\\rtf1\\trowd\\cellx453\\cellx5039\\intbl A1\\cell B1\\cell\\row" +
            "\\trowd\\cellx453\\cellx2000\\cellx5039\\intbl A2\\cell B2\\cell C2\\cell\\row\\pard\\par}");

        DocumentTable table = Assert.Single(document.Tables);
        Assert.Equal([22.65, 77.35, 151.95], table.ColumnWidths.Select(width => Math.Round(width, 2)));

        TableCell b1 = table.Rows[0].Cells[1];
        Assert.Equal((1, 2), (b1.ColumnIndex, b1.ColumnSpan));

        TableCell a2 = table.Rows[1].Cells[0];
        Assert.Equal((0, 1), (a2.ColumnIndex, a2.ColumnSpan));
        Assert.Equal(453 / 20d, table.ColumnWidths[a2.ColumnIndex], 3);
        Assert.Equal((2, 1), (table.Rows[1].Cells[2].ColumnIndex, table.Rows[1].Cells[2].ColumnSpan));
    }

    [Fact]
    public void Reads_The_Borders_Each_Cell_States()
    {
        RichTextDocument document = Read(
            "{\\rtf1{\\colortbl;\\red255\\green0\\blue0;}" +
            "\\trowd\\clbrdrt\\brdrs\\brdrw10\\clbrdrb\\brdrdb\\brdrw20\\brdrcf1\\cellx1000" +
            "\\brdrt\\brdrs\\brdrw30\\clbrdrl\\brdrnone\\cellx2000" +
            "\\intbl A\\cell B\\cell\\row\\pard\\par}");

        TableRow row = Assert.Single(Assert.Single(document.Tables).Rows);
        CellBorders first = row.Cells[0].Borders;
        Assert.Equal(new TableBorder(BColor.Black, 0.5), first.Top);
        Assert.Equal(new TableBorder(new BColor(255, 0, 0), 1), first.Bottom);
        Assert.False(first.Left.IsVisible);

        // A paragraph border before the second cell's definition is not the
        // cell's: \brdrt points the border words away from the cell.
        Assert.False(row.Cells[1].Borders.IsVisible);
    }

    [Fact]
    public void A_Cell_Holds_Every_Paragraph_Before_Its_Cell_Mark()
    {
        RichTextDocument document = Read(
            "{\\rtf1\\trowd\\cellx1000\\cellx2000\\intbl one\\par two\\cell three\\cell\\row\\pard\\par}");

        TableRow row = Assert.Single(Assert.Single(document.Tables).Rows);
        Assert.Equal((0, 2), (row.Cells[0].ParagraphIndex, row.Cells[0].ParagraphCount));
        Assert.Equal((2, 1), (row.Cells[1].ParagraphIndex, row.Cells[1].ParagraphCount));
        Assert.Equal(["one", "two", "three", ""], Texts(document));
    }

    [Fact]
    public void A_Row_Without_A_Definition_Takes_The_One_Before()
    {
        RichTextDocument document = Read(
            "{\\rtf1\\trowd\\trgaph108\\trrh-400\\trhdr\\trkeep\\cellx1000\\cellx2000" +
            "\\intbl A\\cell B\\cell\\row\\intbl C\\cell D\\cell\\row\\pard\\par}");

        DocumentTable table = Assert.Single(document.Tables);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(2, table.Rows[1].Cells.Count);
        Assert.Equal([50, 50], table.ColumnWidths);
        Assert.Equal(5.4, table.CellPadding, 3);

        // An exact height is read as a minimum, as every format's is.
        Assert.Equal(20, table.Rows[0].MinHeight, 3);
        Assert.True(table.Rows[0].IsHeader);
        Assert.False(table.Rows[0].CanSplit);
    }

    [Fact]
    public void A_Table_That_States_No_Gap_Has_No_Padding()
    {
        // LibreOffice sets the text of such a table against the cell's edge.
        DocumentTable table = Assert.Single(
            Read("{\\rtf1\\trowd\\cellx1000\\intbl A\\cell\\row\\pard\\par}").Tables);

        Assert.Equal(0, table.CellPadding);
    }

    [Fact]
    public void The_First_Paragraph_Out_Of_The_Table_Ends_It()
    {
        RichTextDocument document = Read(
            "{\\rtf1 Before\\par\\trowd\\cellx1000\\intbl A\\cell\\row\\pard After\\par" +
            "\\trowd\\cellx1000\\intbl B\\cell\\row\\pard\\par}");

        Assert.Equal(["Before", "A", "After", "B", ""], Texts(document));
        Assert.Equal(2, document.Tables.Count);
        Assert.Equal((1, 1), (document.Tables[0].ParagraphIndex, document.Tables[0].ParagraphCount));
        Assert.Equal((3, 1), (document.Tables[1].ParagraphIndex, document.Tables[1].ParagraphCount));
    }

    [Fact]
    public void A_Table_At_The_End_Of_The_Document_Is_Kept()
    {
        RichTextDocument document = Read("{\\rtf1\\trowd\\cellx1000\\cellx2000\\intbl A\\cell B\\cell\\row}");

        TableRow row = Assert.Single(Assert.Single(document.Tables).Rows);
        Assert.Equal(2, row.Cells.Count);
    }

    [Fact]
    public void Cells_No_Row_Closed_Are_Kept_As_A_Row()
    {
        RichTextDocument document = Read("{\\rtf1\\trowd\\cellx1000\\cellx2000\\intbl A\\cell B\\cell\\pard After\\par}");

        TableRow row = Assert.Single(Assert.Single(document.Tables).Rows);
        Assert.Equal(2, row.Cells.Count);
        Assert.Equal(["A", "B", "After"], Texts(document));
    }

    [Fact]
    public void Cells_Without_A_Boundary_Each_Are_Divided_Evenly()
    {
        DocumentTable table = Assert.Single(
            Read("{\\rtf1\\intbl A\\cell B\\cell\\row\\pard\\par}").Tables);

        Assert.Empty(table.ColumnWidths);
        Assert.Equal([0, 1], table.Rows[0].Cells.Select(cell => cell.ColumnIndex));
    }

    [Fact]
    public void A_Cell_Mark_In_A_Header_Is_Still_A_Tab()
    {
        // Running content is a list of paragraphs with no tables beside it.
        RichTextDocument document = Read("{\\rtf1{\\header A\\cell B\\row}body\\par}");

        RichTextParagraph header = Assert.Single(document.RunningContent.Header(PageSelection.Default));
        Assert.Equal("A\tB", header.Text);
        Assert.Empty(document.Tables);
    }

    [Fact]
    public void A_Document_Without_A_Table_Has_None()
    {
        Assert.Empty(Read("{\\rtf1 one\\par two\\par}").Tables);
    }
}
