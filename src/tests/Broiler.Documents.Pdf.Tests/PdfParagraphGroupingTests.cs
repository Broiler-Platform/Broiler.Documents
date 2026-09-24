using System.Globalization;
using System.Text;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Covers where paragraphs break: against the column a line is set in rather
/// than the page, and, on a tagged page, where the document's own elements do.
/// </summary>
/// <remarks>
/// Two tagged documents are the reason for most of this. One tagged every line
/// separately and every link apart from its sentence, and came back one
/// paragraph per line and one per link. The other set its paragraphs without a
/// gap between them, which geometry cannot see and its structure tree states.
/// Only which element holds which marked content is read - the tree's shape,
/// never its roles.
/// </remarks>
public sealed class PdfParagraphGroupingTests
{
    // ---- tagged pages ---------------------------------------------------------

    [Fact]
    public void Pieces_Of_One_Line_In_Separate_Marked_Content_Are_One_Line()
    {
        // "(", a link, and ") and more" on one baseline, each its own item; the
        // link is nested in the sentence's element. At five points a letter,
        // each piece starts where the one before stopped.
        string content =
            Marked(0, "(", 72, 700) +
            Marked(1, "example.org", 77, 700) +
            Marked(2, ") and more", 132, 700) +
            Marked(3, "on a second line.", 72, 688);

        RichTextParagraph paragraph = Assert.Single(Read(Tagged(content, new Element(0, new Element(1), 2, 3))).Document.Paragraphs);

        Assert.Equal("(example.org) and more on a second line.", paragraph.Text);
    }

    [Fact]
    public void Two_Elements_Set_Without_A_Gap_Are_Two_Paragraphs()
    {
        // Every line but the last runs the full forty letters, so no line stops
        // short and no spacing says where the first paragraph ends.
        string content =
            Marked(0, "The first paragraph runs its full width,", 72, 700) +
            Marked(1, "and so does its second line, full length", 72, 688) +
            Marked(2, "The second paragraph starts right under.", 72, 676) +
            Marked(3, "Only the tree tells them apart.", 72, 664);

        RichTextDocument document = Read(Tagged(content, new Element(0, 1), new Element(2, 3))).Document;

        Assert.Equal(2, document.ParagraphCount);
        Assert.StartsWith("The second paragraph", document.Paragraphs[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Line_Cut_Short_Inside_One_Element_Is_A_Wrap()
    {
        // The second line stops early because the address after it did not fit.
        // Geometry reads a line that short as the end of a paragraph; the element
        // says it is not, and the address could never have gone on that line.
        string content =
            Marked(0, "The first paragraph runs its full width,", 72, 700) +
            Marked(1, "and stops short", 72, 688) +
            Marked(2, "(https://example.org/long/path)", 72, 676);

        RichTextParagraph paragraph = Assert.Single(Read(Tagged(content, new Element(0, 1, 2))).Document.Paragraphs);

        Assert.Equal("The first paragraph runs its full width, and stops short (https://example.org/long/path)", paragraph.Text);
    }

    [Fact]
    public void A_Line_Ended_By_Hand_Inside_One_Element_Is_A_Break()
    {
        // The other half: a line that stopped with room to spare for the next
        // word was broken there on purpose - a list typed with line breaks, a
        // row of a timetable. The model has no line break inside a paragraph,
        // so it becomes the paragraph break it comes closest to, and a run of
        // numbers counting from one is then the list it was typed as.
        string content =
            Marked(0, "Choose one of the dates below when you", 72, 700) +
            Marked(1, "sign up.", 72, 688) +
            Marked(2, "1. Italian afternoon", 72, 676) +
            Marked(3, "2. Fish night", 72, 664);

        RichTextDocument document = Read(Tagged(content, new Element(0, 1, 2, 3))).Document;

        Assert.Equal(["Choose one of the dates below when you sign up.", "Italian afternoon", "Fish night"], document.Paragraphs.Select(p => p.Text));
        Assert.Equal(ListKind.Numbered, document.Paragraphs[1].Style.ListKind);
    }

    [Fact]
    public void A_Tagged_Page_Keeps_Its_Declared_Order_Around_A_Table()
    {
        // The caption is drawn above the grid and declared after it. A page
        // with a table on it used to be read by geometry whole, and the tree's
        // statement about it was thrown away.
        var content = new StringBuilder();
        foreach (int x in new[] { 72, 222, 372 })
            content.Append(CultureInfo.InvariantCulture, $"{x} 600 0.75 100 re f\n");
        foreach (int y in new[] { 600, 650, 700 })
            content.Append(CultureInfo.InvariantCulture, $"72 {y} 300 0.75 re f\n");

        content.Append(Marked(0, "A1", 80, 670));
        content.Append(Marked(1, "B2", 230, 620));
        content.Append(Marked(2, "Caption drawn above", 72, 730));

        PdfReadResult result = Read(Tagged(content.ToString(), new Element(0), new Element(1), new Element(2)));

        DocumentTable table = Assert.Single(result.Document.Tables);
        int caption = result.Document.Paragraphs.ToList().FindIndex(p => p.Text == "Caption drawn above");
        Assert.True(caption >= table.ParagraphEnd, "The caption is read where the tree declares it: after the table.");
    }

    // ---- geometry -------------------------------------------------------------

    [Fact]
    public void A_Column_Beside_A_Wider_One_Keeps_Its_Paragraphs()
    {
        // A line is short against its own column. Measured against the page,
        // every line of the narrow column was short, and each became a paragraph.
        var content = new StringBuilder();
        for (int i = 0; i < 12; i++)
        {
            double y = 700 - (i * 14);
            content.Append(PdfFileBuilder.ShowText(string.Create(CultureInfo.InvariantCulture, $"Left column text line {i:00}"), x: 72, y: y));
            content.Append(PdfFileBuilder.ShowText(string.Create(CultureInfo.InvariantCulture, $"Right column carries far longer lines {i:00}"), x: 320, y: y));
        }

        RichTextDocument document = Read(PdfFileBuilder.SinglePage(content.ToString())).Document;

        Assert.Equal(2, document.ParagraphCount);
        Assert.StartsWith("Left column text line 00", document.Paragraphs[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Spaces_Before_A_Heading_Do_Not_Move_The_Columns_Edge()
    {
        // Three spaces set in a large size ahead of a heading paint nothing, but
        // they started the heading's line near the page edge - and with it the
        // left edge the lines below were measured against, so none of them
        // could end a paragraph by stopping short.
        string content =
            "BT /F1 48 Tf 1 0 0 1 32 720 Tm (   ) Tj ET\n" +
            "BT /F1 24 Tf 1 0 0 1 72 720 Tm (Heading) Tj ET\n" +
            PdfFileBuilder.ShowText("Body line one is long enough to set the measure", y: 680) +
            PdfFileBuilder.ShowText("and ends short.", y: 666) +
            PdfFileBuilder.ShowText("A new paragraph starts here with more words", y: 652);

        RichTextDocument document = Read(PdfFileBuilder.SinglePage(content)).Document;

        Assert.StartsWith("A new paragraph", document.Paragraphs[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Narrow_Column_Beside_Small_Print_Is_Read_After_It()
    {
        // A voucher: instructions in small print set a letter at a time, and
        // beside them a panel of four short lines at a spacing of their own.
        // Counted in runs the panel is about one percent of the page, and it
        // used to be merged back as a stray element - each of its lines read in
        // between two lines of the instructions, splitting their paragraph.
        var content = new StringBuilder();
        var print = new List<string>();
        for (int i = 0; i < 8; i++)
        {
            string line = string.Create(CultureInfo.InvariantCulture, $"Small print line {i} is set one letter at a time");
            print.Add(line);
            content.Append(LetterByLetter(line, x: 72, y: 600 - (i * 9), size: 7));
        }

        string[] panel = ["Panel value", "Panel code", "Panel expiry", "Panel greeting"];
        for (int i = 0; i < panel.Length; i++)
            content.Append(PdfFileBuilder.ShowText(panel[i], x: 300, y: 595.5 - (i * 9), size: 10));

        RichTextDocument document = Read(PdfFileBuilder.SinglePage(content.ToString())).Document;

        Assert.Equal(string.Join(' ', print), document.Paragraphs[0].Text);
        Assert.DoesNotContain(document.Paragraphs, paragraph => paragraph.Text.Contains("Small print", StringComparison.Ordinal) && paragraph.Text.Contains("Panel", StringComparison.Ordinal));

        string text = document.PlainText;
        int[] order = [.. panel.Select(line => text.IndexOf(line, StringComparison.Ordinal))];
        Assert.All(order, at => Assert.True(at > text.IndexOf("line 7", StringComparison.Ordinal)));
        Assert.Equal(order.Order(), order);
    }

    [Fact]
    public void A_Narrow_Column_Level_With_Each_Row_Is_Read_Across_It()
    {
        // The other half of the rule. An unruled table's narrow column sets
        // each value level with its row, as line numbers stand level with the
        // lines they number; merged, each value is read with its row, which is
        // the reading it has. Only lines between the other column's lines make
        // a slight column a column.
        var content = new StringBuilder();
        for (int i = 0; i < 6; i++)
        {
            double y = 600 - (i * 14);
            content.Append(LetterByLetter(string.Create(CultureInfo.InvariantCulture, $"Item {i} of the delivery, as ordered"), x: 72, y: y, size: 8));
            content.Append(PdfFileBuilder.ShowText(string.Create(CultureInfo.InvariantCulture, $"{i + 1}0.00 EUR"), x: 400, y: y, size: 8));
        }

        RichTextDocument document = Read(PdfFileBuilder.SinglePage(content.ToString())).Document;

        Assert.Equal(6, document.ParagraphCount);
        for (int i = 0; i < 6; i++)
            Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"Item {i} of the delivery, as ordered {i + 1}0.00 EUR"), document.Paragraphs[i].Text);
    }

    [Fact]
    public void A_Running_Head_And_A_Folio_Out_In_The_Margin_Still_Read_By_Height()
    {
        // Furniture across a gutter from the text sits above or below it, not
        // beside it, and a column of it is merged back as it always was: the
        // head first, the folio last.
        var content = new StringBuilder(PdfFileBuilder.ShowText("Chapter One", x: 400, y: 700, size: 10));
        for (int i = 0; i < 6; i++)
            content.Append(LetterByLetter(string.Create(CultureInfo.InvariantCulture, $"Body text line {i} set a letter at a time"), x: 72, y: 600 - (i * 12), size: 8));
        content.Append(PdfFileBuilder.ShowText("Page 3", x: 400, y: 100, size: 10));

        RichTextDocument document = Read(PdfFileBuilder.SinglePage(content.ToString())).Document;

        Assert.Equal("Chapter One", document.Paragraphs[0].Text);
        Assert.Equal("Page 3", document.Paragraphs[^1].Text);
    }

    [Fact]
    public void A_Heading_Set_Close_Above_Smaller_Text_Is_A_Paragraph_Of_Its_Own()
    {
        // Eighteen points under a sixteen-point heading is an ordinary line's
        // gap for the heading and a paragraph's for eight-point text, and
        // weighed against the heading the two ran into one paragraph.
        string[] body =
        [
            "Enter the code while you order, or follow these",
            "steps to add the voucher to your account balance",
            "before the order is placed and the goods are sent",
        ];

        string content =
            PdfFileBuilder.ShowText("Redeeming your voucher", x: 72, y: 700, size: 16) +
            PdfFileBuilder.ShowText(body[0], x: 72, y: 682, size: 8) +
            PdfFileBuilder.ShowText(body[1], x: 72, y: 672, size: 8) +
            PdfFileBuilder.ShowText(body[2], x: 72, y: 662, size: 8);

        RichTextDocument document = Read(PdfFileBuilder.SinglePage(content)).Document;

        Assert.Equal(["Redeeming your voucher", string.Join(' ', body)], document.Paragraphs.Select(p => p.Text));
    }

    [Fact]
    public void A_Larger_Word_Inside_A_Line_Leaves_Its_Paragraph_Whole()
    {
        // The line with the large word stands further from the one above it,
        // and its largest size is almost twice the text's. The size it is set in
        // is the one most of its letters are, which is the text's own.
        string content =
            PdfFileBuilder.ShowText("The small print of this voucher is set at", x: 72, y: 700, size: 8) +
            PdfFileBuilder.ShowText("eight points, with one ", x: 72, y: 686, size: 8) +
            PdfFileBuilder.ShowText("LARGE", x: 164, y: 686, size: 14) +
            PdfFileBuilder.ShowText(" word inside a line", x: 199, y: 686, size: 8) +
            PdfFileBuilder.ShowText("and the paragraph carries on below it.", x: 72, y: 676, size: 8);

        RichTextParagraph paragraph = Assert.Single(Read(PdfFileBuilder.SinglePage(content)).Document.Paragraphs);

        Assert.Equal(
            "The small print of this voucher is set at eight points, with one LARGE word inside a line and the paragraph carries on below it.",
            paragraph.Text);
    }

    // ---- fixtures -------------------------------------------------------------

    /// <summary>
    /// One line shown a letter at a time: each glyph its own show operator, and
    /// the pen moved on by <c>Td</c> - which is how the voucher's producer set
    /// its text, and why its page held a run per letter. The font declares no
    /// <c>/Widths</c>, so every glyph is half an em and lands where the one
    /// before it stopped.
    /// </summary>
    private static string LetterByLetter(string text, double x, double y, double size)
    {
        var content = new StringBuilder();
        content.Append(CultureInfo.InvariantCulture, $"BT /F1 {size} Tf 1 0 0 1 {x} {y} Tm\n");
        foreach (char letter in text)
        {
            string escaped = letter is '(' or ')' or '\\' ? "\\" + letter : letter.ToString();
            content.Append(CultureInfo.InvariantCulture, $"({escaped}) Tj {size / 2} 0 Td\n");
        }

        return content.Append("ET\n").ToString();
    }

    private static PdfReadResult Read(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec().ReadPdf(stream, null);
    }

    /// <summary>
    /// One run shown inside a marked-content item with the given id, at ten
    /// points.
    /// </summary>
    private static string Marked(int mcid, string text, double x, double y) =>
        string.Create(CultureInfo.InvariantCulture, $"/P << /MCID {mcid} >> BDC\n") +
        PdfFileBuilder.ShowText(text, x: x, y: y, size: 10) +
        "EMC\n";

    /// <summary>A structure element: its kids are marked-content ids and nested elements.</summary>
    private sealed class Element(params object[] kids)
    {
        public object[] Kids { get; } = kids;
    }

    /// <summary>
    /// One tagged page whose structure tree holds the given elements, in order.
    /// Its font declares no <c>/Widths</c>, so the reader sets every glyph at
    /// half an em - five points at ten - and the fixtures can say exactly where
    /// every line stops.
    /// </summary>
    private static byte[] Tagged(string content, params Element[] elements)
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int root = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int stream = builder.AddStream(string.Empty, content);

        string Add(Element element, int parent)
        {
            int self = builder.Reserve();
            var kids = new List<string>(element.Kids.Length);
            foreach (object kid in element.Kids)
                kids.Add(kid is Element child ? Add(child, self) : Convert.ToString(kid, CultureInfo.InvariantCulture)!);

            builder.SetObject(self, $"<< /Type /StructElem /S /P /P {parent} 0 R /Pg {page} 0 R /K [{string.Join(' ', kids)}] >>");
            return $"{self} 0 R";
        }

        var top = new List<string>(elements.Length);
        foreach (Element element in elements)
            top.Add(Add(element, root));

        builder.SetObject(root, $"<< /Type /StructTreeRoot /K [{string.Join(' ', top)}] >>");
        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R /StructTreeRoot {root} 0 R /MarkInfo << /Marked true >> >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
            $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {stream} 0 R >>");

        return builder.Build(catalog);
    }
}
