using System.Text;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Covers what a page's filled areas are read as: the background of the words
/// painted over them, paint in the paper's own colour that shows nothing, or
/// artwork that was dropped.
/// </summary>
/// <remarks>
/// Every reading here depends on paint order as much as on position. A band
/// painted before a word is behind it; the same band painted after it covers
/// it; and white on bare paper is only bare where nothing was painted first.
/// The fixtures change one of those at a time.
/// </remarks>
public sealed class PdfFillReadingTests
{
    private static readonly BColor Band = new(68, 114, 196);
    private static readonly BColor White = new(255, 255, 255);

    // ---- backgrounds ----------------------------------------------------------

    [Fact]
    public void A_Fill_Beneath_A_Run_Is_Its_Background()
    {
        // White words on a blue band. Without the band they are white on white,
        // which is what a banner title read as.
        InlineStyle title = StyleOf(Read(Banner()), "Title on a band");

        Assert.Equal(White, title.Foreground);
        Assert.Equal(Band, title.Background);
    }

    [Fact]
    public void A_Run_Beside_The_Fill_Has_No_Background()
    {
        Assert.True(StyleOf(Read(Banner()), "Body text").Background.IsEmpty);
    }

    [Fact]
    public void A_Fill_Painted_Over_A_Run_Is_Not_Its_Background()
    {
        // The same band painted after the words lies over them, not under them.
        Assert.True(StyleOf(Read(Banner(bandAfterText: true)), "Title on a band").Background.IsEmpty);
    }

    [Fact]
    public void A_White_Box_Over_A_Band_Takes_The_Band_From_The_Words_On_It()
    {
        // Only the topmost fill is what a reader sees behind the words.
        PdfReadResult result = Read(Banner(whiteBoxOverBand: true));

        Assert.Equal(Band, StyleOf(result, "On the blue").Background);
        Assert.True(StyleOf(result, "On the white").Background.IsEmpty);
    }

    [Fact]
    public void A_Fill_Covering_The_Page_Is_Not_A_Background()
    {
        // The page's colour, not a highlight behind every run on it.
        string content =
            "0.95 0.95 0.8 rg 0 0 612 792 re f 0 0 0 rg\n" +
            PdfFileBuilder.ShowText("Text on a tinted page");

        Assert.True(StyleOf(Read(PdfFileBuilder.SinglePage(content)), "Text on a tinted page").Background.IsEmpty);
    }

    [Fact]
    public void A_Cell_Shade_Is_Not_Read_Again_As_The_Background_Of_Its_Text()
    {
        string content =
            "0.8 0.8 0.8 rg 72 650 150 50 re f 0 0 0 rg\n" +
            "72 600 300 0.75 re f 72 650 300 0.75 re f 72 700 300 0.75 re f\n" +
            "72 600 0.75 100 re f 222 600 0.75 100 re f 372 600 0.75 100 re f\n" +
            PdfFileBuilder.ShowText("Shaded", x: 80, y: 670) +
            PdfFileBuilder.ShowText("Plain", x: 80, y: 620);

        PdfReadResult result = Read(PdfFileBuilder.SinglePage(content));

        Assert.False(Assert.Single(result.Document.Tables).Rows[0].Cells[0].Shading.IsEmpty);
        Assert.True(StyleOf(result, "Shaded").Background.IsEmpty);
    }

    [Fact]
    public void A_Background_Is_Reported_As_Read_Back()
    {
        DocumentDiagnostic note = Artwork(Read(Banner()));

        Assert.Contains("as a run's background", note.Message, StringComparison.Ordinal);
        Assert.Contains("none was dropped", note.Message, StringComparison.Ordinal);
    }

    // ---- paint in the paper's colour ------------------------------------------

    [Fact]
    public void A_White_Fill_On_Bare_Paper_Is_Not_Reported_As_Lost()
    {
        // A white background behind a paragraph on a white page: nothing a
        // reader could ever see, so nothing lost.
        string content =
            "1 1 1 rg 72 500 300 100 re f 0 0 0 rg\n" +
            PdfFileBuilder.ShowText("Body");

        DocumentDiagnostic note = Artwork(Read(PdfFileBuilder.SinglePage(content)));

        Assert.Contains("paper's colour on bare paper", note.Message, StringComparison.Ordinal);
        Assert.Contains("none was dropped", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_White_Fill_Over_Text_Is_Not_Bare_Paper()
    {
        // The same fill painted over a line hides it. That is something the
        // page did, and dropping the fill undoes it.
        string content =
            PdfFileBuilder.ShowText("Hidden under white", x: 80, y: 550) +
            "1 1 1 rg 72 500 300 100 re f 0 0 0 rg\n" +
            PdfFileBuilder.ShowText("Body");

        DocumentDiagnostic note = Artwork(Read(PdfFileBuilder.SinglePage(content)));

        Assert.Contains("1 axis-aligned area", note.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("bare paper", note.Message, StringComparison.Ordinal);
    }

    // ---- repaints and the note itself -----------------------------------------

    [Fact]
    public void A_Shape_Painted_Twice_Is_Counted_Once_Among_The_Losses()
    {
        // Producers repaint - LibreOffice paints every background and border
        // twice - and a count of operations alone doubles what was lost.
        string content =
            "0.8 g 72 300 200 100 re f 72 300 200 100 re f 0 g\n" +
            PdfFileBuilder.ShowText("Body");

        DocumentDiagnostic note = Artwork(Read(PdfFileBuilder.SinglePage(content)));

        Assert.Contains("2 path-painting operations were dropped", note.Message, StringComparison.Ordinal);
        Assert.Contains("1 of them repaints a shape the page had already painted", note.Message, StringComparison.Ordinal);
        Assert.Contains("1 distinct shape was lost", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Artwork_Note_Speaks_For_The_Document()
    {
        // Its counts are every page's, so it cannot open by talking about one.
        string content = "0.8 g 72 300 200 100 re f 0 g\n" + PdfFileBuilder.ShowText("Body");

        DocumentDiagnostic note = Artwork(Read(PdfFileBuilder.SinglePage(content)));

        Assert.StartsWith("The document draws vector artwork", note.Message, StringComparison.Ordinal);
    }

    // ---- fixtures -------------------------------------------------------------

    private static PdfReadResult Read(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec().ReadPdf(stream, null);
    }

    private static DocumentDiagnostic Artwork(PdfReadResult result) =>
        Assert.Single(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.VectorArtworkDropped);

    /// <summary>The style of the run holding <paramref name="text"/>.</summary>
    private static InlineStyle StyleOf(PdfReadResult result, string text)
    {
        RichTextParagraph paragraph = Assert.Single(
            result.Document.Paragraphs,
            p => p.Text.Contains(text, StringComparison.Ordinal));

        return paragraph.StyleAt(paragraph.Text.IndexOf(text, StringComparison.Ordinal) + 1);
    }

    /// <summary>
    /// A blue band from x 72 to 472 and y 690 to 720 with white 20-point words
    /// on it, and a line of body text well below.
    /// </summary>
    private static byte[] Banner(bool bandAfterText = false, bool whiteBoxOverBand = false)
    {
        const string band = "0.2667 0.447 0.769 rg 72 690 400 30 re f\n";

        var content = new StringBuilder();
        if (!bandAfterText)
            content.Append(band);

        if (whiteBoxOverBand)
        {
            content.Append("1 1 1 rg 300 690 172 30 re f\n");
            content.Append("0 0 0 rg BT /F1 12 Tf 1 0 0 1 80 700 Tm (On the blue) Tj ET\n");
            content.Append("BT /F1 12 Tf 1 0 0 1 310 700 Tm (On the white) Tj ET\n");
        }
        else
        {
            content.Append("1 1 1 rg BT /F1 20 Tf 1 0 0 1 80 697 Tm (Title on a band) Tj ET\n");
        }

        if (bandAfterText)
            content.Append(band);

        content.Append("0 0 0 rg\n");
        content.Append(PdfFileBuilder.ShowText("Body text", y: 600));

        return PdfFileBuilder.SinglePage(content.ToString());
    }
}
