using System.Globalization;
using System.Text;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Covers text drawn turned against the page as it is displayed: sideways,
/// upside down, mirrored, at a slant, and a letter at a time along a curve.
/// </summary>
/// <remarks>
/// Turned text used to be set on the page's horizontal lines like any other,
/// and since its pen leaves that line after every glyph it came back a letter
/// at a time, sorted from the top of the page down. A delivery receipt's form
/// number, running up its left margin, read "0. 4 n oi s r e V ... r a l u m r o F".
/// It is read along its own baseline now, and only its turn is lost.
/// </remarks>
public sealed class PdfTurnedTextTests
{
    private static readonly string[] Lines =
    [
        "The first paragraph of a landscape page runs",
        "across three lines of text so that there is",
        "something to join.",
        "A second paragraph follows after a gap here.",
    ];

    private static readonly string[] Paragraphs =
    [
        "The first paragraph of a landscape page runs across three lines of text so that there is something to join.",
        "A second paragraph follows after a gap here.",
    ];

    private const string Body = "The body reads across the page.";

    // ---- straight baselines ---------------------------------------------------

    [Fact]
    public void A_Label_Up_The_Margin_Reads_Along_Its_Baseline()
    {
        PdfReadResult result = Read(PdfFileBuilder.SinglePage(
            PdfFileBuilder.ShowText(Body) + Turned("0 1 -1 0", 40, 300, "Form 12 Version 4.0")));

        Assert.Equal([Body, "Form 12 Version 4.0"], Texts(result));

        DocumentDiagnostic note = Assert.Single(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.TextOrientationNotKept);
        Assert.Equal(DocumentDiagnosticSeverity.Info, note.Severity);
        Assert.StartsWith("16 characters of text were drawn turned against the page as it is displayed", note.Message, StringComparison.Ordinal);
        Assert.EndsWith("On page 1.", note.Message, StringComparison.Ordinal);

        // The words are all there, in order: nothing about the read is partial.
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.TextOrientationUnsupported);
        Assert.Equal(DocumentResultStatus.Success, result.Status);
    }

    [Theory]
    [InlineData("1 0 0 1", false)]
    [InlineData("0.99996 0.00873 -0.00873 0.99996", false)]
    [InlineData("0 1 -1 0", true)]
    [InlineData("0 -1 1 0", true)]
    [InlineData("-1 0 0 -1", true)]
    [InlineData("-1 0 0 1", true)]
    [InlineData("0.9848 0.1736 -0.1736 0.9848", true)]
    public void Text_Reads_Whichever_Way_It_Runs(string matrix, bool turned)
    {
        // Upright, and half a degree off it, read across. Sideways either way,
        // upside down, mirrored, and ten degrees off are each read along their
        // own baseline, and said to have been turned.
        PdfReadResult result = Read(PdfFileBuilder.SinglePage(
            PdfFileBuilder.ShowText(Body) + $"BT /F1 12 Tf {matrix} 300 400 Tm (A turned label) Tj ET\n"));

        Assert.Equal([Body, "A turned label"], Texts(result));
        Assert.Equal(turned, result.Diagnostics.Any(d => d.Code == PdfDiagnosticCodes.TextOrientationNotKept));
        Assert.Equal(DocumentResultStatus.Success, result.Status);
    }

    [Fact]
    public void A_Label_Drawn_A_Letter_At_A_Time_Still_Reads_As_Words()
    {
        // Every letter placed by a matrix of its own, all of them on one
        // sideways baseline, with a letter's room left for the space.
        var content = new StringBuilder(PdfFileBuilder.ShowText(Body));
        const string label = "Side ways";
        for (int i = 0; i < label.Length; i++)
        {
            if (label[i] != ' ')
                content.Append(Turned("0 1 -1 0", 40, 300 + (6 * i), label[i].ToString()));
        }

        Assert.Equal([Body, label], Texts(Read(PdfFileBuilder.SinglePage(content.ToString()))));
    }

    [Theory]
    [InlineData("0 1 -1 0 595 0")]
    [InlineData("-1 0 0 -1 595 842")]
    [InlineData("0 -1 1 0 0 842")]
    public void A_Page_Set_Sideways_In_Its_Box_Reads_In_Paragraphs(string transform)
    {
        // A landscape page drawn up a portrait box with no /Rotate to turn it:
        // the whole body is turned, and it is the page's main direction.
        PdfReadResult result = Read(Page($"q {transform} cm\n{Upright(Lines)}Q\n"));

        Assert.Equal(Paragraphs, Texts(result));
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.TextOrientationNotKept);
        Assert.Equal(DocumentResultStatus.Success, result.Status);
    }

    [Fact]
    public void A_Page_Turned_Only_For_Viewing_Reads_Its_Text_Down_The_Page()
    {
        // Upright text on a box a viewer turns runs down the page as it is
        // displayed. The page is read the way it is shown, and the text along
        // the way it runs there.
        PdfReadResult result = Read(Page(Show("Turned for viewing", 72, 700), " /Rotate 90"));

        Assert.Equal(["Turned for viewing"], Texts(result));
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.TextOrientationNotKept);
    }

    [Fact]
    public void A_Label_Beside_A_Paragraph_Does_Not_Break_It()
    {
        // The label is level with the middle of the first paragraph. Placed by
        // its height it would land between two of its lines, and the paragraph
        // would come back in two pieces with a form number between them.
        PdfReadResult result = Read(Page(Upright(Lines) + Turned("0 1 -1 0", 40, 440, "Form 12")));

        Assert.Equal([.. Paragraphs, "Form 12"], Texts(result));
    }

    [Fact]
    public void A_Label_Up_The_Margin_Leaves_The_Margin_Where_The_Body_States_It()
    {
        // The label sits in the margin by design. Counted as the body's ink, it
        // took the stated left margin out to the page's edge.
        PdfReadResult result = Read(PdfFileBuilder.SinglePage(
            PdfFileBuilder.ShowText(Body) + Turned("0 1 -1 0", 30, 300, "Form 12 Version 4.0")));

        PageGeometry page = Assert.IsType<PageGeometry>(result.Document.PageGeometry);
        Assert.Equal(72, page.MarginLeft, 1);
    }

    [Fact]
    public void A_Tagged_Document_Puts_Its_Turned_Text_Where_It_Declares_It()
    {
        // The label is drawn after the body and declared before it. The tree's
        // order stands, and the label still reads along its baseline.
        PdfReadResult result = Read(TaggedLabelFirst());

        Assert.Equal(["Form 12 Version 4.0", Body], Texts(result));
    }

    [Fact]
    public void A_Sideways_Heading_In_A_Ruled_Table_Is_Its_Cells_Text()
    {
        var content = new StringBuilder();
        foreach (int x in new[] { 72, 222, 372 })
            content.Append(CultureInfo.InvariantCulture, $"{x} 400 1 100 re f\n");
        foreach (int y in new[] { 400, 450, 499 })
            content.Append(CultureInfo.InvariantCulture, $"72 {y} 301 1 re f\n");

        content.Append(Turned("0 1 -1 0", 100, 455, "Net sum"));
        content.Append(Show("B1", 230, 470));
        content.Append(Show("A2", 80, 420)).Append(Show("B2", 230, 420));

        RichTextDocument document = Read(Page(content.ToString())).Document;

        DocumentTable table = Assert.Single(document.Tables);
        Assert.Equal(
            ["Net sum", "B1", "A2", "B2"],
            table.Rows.SelectMany(row => row.Cells).Select(cell => document.Paragraphs[cell.ParagraphIndex].Text));
    }

    [Fact]
    public void A_Link_Over_A_Turned_Label_Still_Covers_It()
    {
        // The annotation's rectangle is on the page; the label is read in its
        // own frame and looked for on the page all the same.
        string link = "<< /Type /Annot /Subtype /Link /Rect [25 290 45 360] /A << /S /URI /URI (https://example.org/form) >> >>";
        PdfReadResult result = Read(Page(PdfFileBuilder.ShowText(Body) + Turned("0 1 -1 0", 40, 300, "Side ways"), annotation: link));

        RichTextParagraph label = Assert.Single(result.Document.Paragraphs, p => p.Text == "Side ways");
        StyleRun linked = Assert.Single(label.Runs);
        Assert.Equal("https://example.org/form", linked.Style.LinkHref);
        Assert.DoesNotContain(result.Document.Paragraphs.Single(p => p.Text == Body).Runs, run => run.Style.IsLink);
    }

    [Fact]
    public void White_Text_Up_A_Coloured_Band_Keeps_The_Band()
    {
        // A sidebar's label in white on its colour. Without the band it is
        // white on white.
        var band = new BColor(68, 114, 196);
        PdfReadResult result = Read(PdfFileBuilder.SinglePage(
            PdfFileBuilder.ShowText(Body) +
            "0.2667 0.447 0.769 rg 20 280 30 120 re f\n" +
            "1 1 1 rg " + Turned("0 1 -1 0", 40, 300, "Sidebar")));

        RichTextParagraph label = Assert.Single(result.Document.Paragraphs, p => p.Text == "Sidebar");
        Assert.Equal(band, label.StyleAt(1).Background);
        Assert.Equal(new BColor(255, 255, 255), label.StyleAt(1).Foreground);
    }

    [Fact]
    public void An_Underline_Rule_Is_Not_Read_Onto_A_Turned_Label()
    {
        // A rule under the body passes beneath the label's box too; it runs
        // along the body's baseline, not the label's.
        PdfReadResult result = Read(PdfFileBuilder.SinglePage(
            PdfFileBuilder.ShowText(Body) + "72 717 160 0.5 re f\n" + Turned("0 1 -1 0", 240, 718, "Up")));

        RichTextParagraph label = Assert.Single(result.Document.Paragraphs, p => p.Text == "Up");
        Assert.False(label.StyleAt(1).Underline);
    }

    // ---- letters each turned their own way ------------------------------------

    [Fact]
    public void Letters_Set_Along_A_Curve_Are_Reported()
    {
        // A seal's rim: one letter at a time, each a further fifteen degrees
        // round. No two read the same way, so there is no line to read them
        // into, and the read does not vouch for what they spell.
        var content = new StringBuilder(PdfFileBuilder.ShowText(Body));
        const string rim = "SEALS";
        for (int i = 0; i < rim.Length; i++)
        {
            double angle = (20 + (15 * i)) * Math.PI / 180;
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            content.Append(CultureInfo.InvariantCulture,
                $"BT /F1 12 Tf {cos:0.####} {sin:0.####} {-sin:0.####} {cos:0.####} {300 + (60 * cos):0.##} {400 + (60 * sin):0.##} Tm ({rim[i]}) Tj ET\n");
        }

        PdfReadResult result = Read(PdfFileBuilder.SinglePage(content.ToString()));

        DocumentDiagnostic note = Assert.Single(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.TextOrientationUnsupported);
        Assert.Equal(DocumentDiagnosticSeverity.Warning, note.Severity);
        Assert.StartsWith("5 characters of text were drawn a letter at a time, each turned its own way", note.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.TextOrientationNotKept);
        Assert.Equal(DocumentResultStatus.Partial, result.Status);
    }

    [Fact]
    public void One_Letter_Turned_On_Its_Own_Is_Read()
    {
        // A single sideways letter reads the same whichever way it faces.
        PdfReadResult result = Read(PdfFileBuilder.SinglePage(
            PdfFileBuilder.ShowText(Body) + Turned("0 1 -1 0", 40, 300, "A")));

        Assert.Equal([Body, "A"], Texts(result));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.TextOrientationUnsupported);
    }

    // ---- fixtures -------------------------------------------------------------

    private static PdfReadResult Read(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec().ReadPdf(stream, null);
    }

    private static string[] Texts(PdfReadResult result) =>
        [.. result.Document.Paragraphs.Select(p => p.Text)];

    /// <summary>A run at whole-point coordinates, which every culture writes the same way.</summary>
    private static string Show(string text, int x, int y) => PdfFileBuilder.ShowText(text, x, y);

    /// <summary>A 12-point run set through <paramref name="matrix"/>'s turn, starting at the point given.</summary>
    private static string Turned(string matrix, int x, int y, string text) =>
        string.Create(CultureInfo.InvariantCulture, $"BT /F1 12 Tf {matrix} {x} {y} Tm ({text}) Tj ET\n");

    /// <summary>The lines down the displayed page, with a gap before the last.</summary>
    private static string Upright(string[] lines)
    {
        var content = new StringBuilder();
        int y = 500;
        for (int i = 0; i < lines.Length; i++)
        {
            if (i == 3)
                y -= 14;
            content.Append(Show(lines[i], 72, y));
            y -= 14;
        }

        return content.ToString();
    }

    /// <summary>An A4 portrait page holding <paramref name="content"/>.</summary>
    private static byte[] Page(string content, string extra = "", string? annotation = null)
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int tree = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int stream = builder.AddStream(string.Empty, content);
        string annots = annotation is null ? string.Empty : $" /Annots [{builder.AddObject(annotation)} 0 R]";

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {tree} 0 R >>");
        builder.SetObject(tree, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(
            page,
            $"<< /Type /Page /Parent {tree} 0 R /MediaBox [0 0 595 842] " +
            $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {stream} 0 R{extra}{annots} >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// A tagged page drawing the body and then a label up its margin, whose
    /// structure tree declares the label first.
    /// </summary>
    private static byte[] TaggedLabelFirst()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int root = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        int content = builder.AddStream(
            string.Empty,
            "/P << /MCID 0 >> BDC\n" + PdfFileBuilder.ShowText(Body) + "EMC\n" +
            "/P << /MCID 1 >> BDC\n" + Turned("0 1 -1 0", 40, 300, "Form 12 Version 4.0") + "EMC\n");

        int label = builder.AddObject($"<< /Type /StructElem /S /P /P {root} 0 R /Pg {page} 0 R /K 1 >>");
        int body = builder.AddObject($"<< /Type /StructElem /S /P /P {root} 0 R /Pg {page} 0 R /K 0 >>");
        builder.SetObject(root, $"<< /Type /StructTreeRoot /K [{label} 0 R {body} 0 R] >>");

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R /StructTreeRoot {root} 0 R /MarkInfo << /Marked true >> >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
            $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");

        return builder.Build(catalog);
    }
}
