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

    // ---- fixtures -------------------------------------------------------------

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
