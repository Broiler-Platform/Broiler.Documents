using System.Globalization;
using System.Text;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Covers reading underline and strikethrough back off the bars a page painted
/// under and through its text.
/// </summary>
/// <remarks>
/// PDF has no text-decoration operator, so this is an inference from geometry
/// exactly as the table reconstruction is. The fixtures are built so that a pass
/// which marked every thin bar, or marked none of them, would fail them: the
/// same shape is a table rule, a separator, and an underline, and only where it
/// sits says which.
/// </remarks>
public sealed class PdfTextDecorationTests
{
    [Fact]
    public void A_Bar_On_A_Run_Baseline_Is_Its_Underline()
    {
        PdfReadResult result = Read(Decorated(barY: 698));

        Assert.Contains(Styles(result), style => style.Underline);
        Assert.DoesNotContain(Styles(result), style => style.Strikethrough);
    }

    [Fact]
    public void A_Bar_Across_The_Glyphs_Is_A_Strikethrough()
    {
        // The only thing that differs from the underline fixture is five points
        // of height. Below the baseline the bar runs under the word; a quarter
        // of the em above it, it runs through the word.
        PdfReadResult result = Read(Decorated(barY: 703));

        Assert.Contains(Styles(result), style => style.Strikethrough);
        Assert.DoesNotContain(Styles(result), style => style.Underline);
    }

    [Fact]
    public void A_Bar_The_Text_Cannot_Account_For_Is_Not_A_Decoration()
    {
        // A rule three times the width of the words above it is a rule that
        // happens to pass beneath some text. Marking the text would claim
        // formatting from a coincidence of layout.
        PdfReadResult result = Read(Decorated(barY: 698, barWidth: 500));

        Assert.DoesNotContain(Styles(result), style => style.Underline || style.Strikethrough);
    }

    [Fact]
    public void A_Bar_Far_Below_The_Baseline_Is_A_Separator()
    {
        // Two thirds of an em below the baseline is past the descender. A line
        // there separates the paragraph from what follows it.
        PdfReadResult result = Read(Decorated(barY: 692));

        Assert.DoesNotContain(Styles(result), style => style.Underline || style.Strikethrough);
    }

    [Fact]
    public void A_Grid_Rule_Is_Not_An_Underline()
    {
        // The case the whole inference has to survive. This rule passes every
        // geometric test - it is thin, it is two points under a baseline, and
        // the run above it accounts for most of its length - and it is the
        // bottom edge of a table row. Lines a grid was read from are excluded
        // before the geometry is looked at.
        PdfReadResult result = Read(NarrowGrid());

        Assert.Single(result.Document.Tables);
        Assert.DoesNotContain(Styles(result), style => style.Underline || style.Strikethrough);
    }

    [Fact]
    public void A_Decoration_Is_Reported_As_Read_Back_Rather_Than_Dropped()
    {
        // The bar is the page's only path and it was not lost, so the artwork
        // note has to say so. Reporting it as a dropped bar while the underline
        // it became sits in the document is the note contradicting the model.
        DocumentDiagnostic note = Assert.Single(
            Read(Decorated(barY: 698)).Diagnostics,
            d => d.Code == PdfDiagnosticCodes.VectorArtworkDropped);

        Assert.Contains("read as a run's underline or strikethrough", note.Message, StringComparison.Ordinal);
        Assert.Contains("none was dropped", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_Invisible_Run_Is_Not_Decorated()
    {
        // Rendering mode 3 paints nothing, so a bar beneath it is not that run's
        // underline; it is a bar over whatever the page really shows there.
        PdfReadResult result = Read(Decorated(barY: 698, renderMode: 3));

        Assert.DoesNotContain(Styles(result), style => style.Underline);
    }

    [Fact]
    public void A_Rule_That_Runs_On_Past_Its_Text_Is_Not_An_Underline()
    {
        // A paragraph's bottom border under a line of text: the words cover most
        // of it, which is all the coverage test asks, and it runs on for a
        // column's width after they stop. An underline stops with its words.
        PdfReadResult result = Read(Stroked(barFrom: 72, barTo: 250, lineWidth: 0.5));

        Assert.DoesNotContain(Styles(result), style => style.Underline || style.Strikethrough);
    }

    [Fact]
    public void A_Thin_Stroked_Line_Under_A_Run_Is_Its_Underline()
    {
        // The stroked half of the pair below, and the reason the other one is
        // not vacuous: the same line at a decoration's weight is read.
        PdfReadResult result = Read(Stroked(barFrom: 72, barTo: 150, lineWidth: 0.5));

        Assert.Contains(Styles(result), style => style.Underline);
    }

    [Fact]
    public void A_Heavy_Stroked_Line_Is_Not_An_Underline()
    {
        // A stroked line's box has no height, so its weight is the pen's. Two
        // points under twelve-point text is a separator, and reading the box
        // instead let a one-and-a-half point rule through as an underline.
        PdfReadResult result = Read(Stroked(barFrom: 72, barTo: 150, lineWidth: 2));

        Assert.DoesNotContain(Styles(result), style => style.Underline || style.Strikethrough);
    }

    // ---- fixtures -------------------------------------------------------------

    private static PdfReadResult Read(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec().ReadPdf(stream, null);
    }

    private static IEnumerable<InlineStyle> Styles(PdfReadResult result) =>
        result.Document.Paragraphs.SelectMany(p => p.Runs).Select(run => run.Style);

    /// <summary>
    /// One line of 12-point text at x 72, baseline 700, with a thin bar under or
    /// through it. The bar is narrower than the run, so the run accounts for all
    /// of it whatever the exact advance width of the string turns out to be.
    /// </summary>
    private static byte[] Decorated(double barY, double barWidth = 100, int renderMode = 0)
    {
        var content = new StringBuilder();
        content.Append(CultureInfo.InvariantCulture, $"72 {barY} {barWidth} 0.5 re f\n");
        content.Append(CultureInfo.InvariantCulture,
            $"BT /F1 12 Tf {renderMode} Tr 1 0 0 1 72 700 Tm (Decorated running text) Tj ET\n");

        return PdfFileBuilder.SinglePage(content.ToString());
    }

    /// <summary>
    /// The same line of text with a stroked line two points under its baseline,
    /// drawn with the given pen. The run is about 121 points long, from x 72.
    /// </summary>
    private static byte[] Stroked(double barFrom, double barTo, double lineWidth)
    {
        var content = new StringBuilder();
        content.Append(CultureInfo.InvariantCulture, $"{lineWidth} w 0 0 0 RG {barFrom} 698 m {barTo} 698 l S\n");
        content.Append("BT /F1 12 Tf 1 0 0 1 72 700 Tm (Decorated running text) Tj ET\n");

        return PdfFileBuilder.SinglePage(content.ToString());
    }

    /// <summary>
    /// A 2x2 grid narrow enough that one run spans most of a row rule, with that
    /// run's baseline two points above the rule.
    /// </summary>
    private static byte[] NarrowGrid()
    {
        var content = new StringBuilder();

        foreach (int x in new[] { 72, 160, 240 })
            content.Append(CultureInfo.InvariantCulture, $"{x} 600 0.75 100 re f\n");

        foreach (int y in new[] { 600, 650, 700 })
            content.Append(CultureInfo.InvariantCulture, $"72 {y} 168 0.75 re f\n");

        content.Append(PdfFileBuilder.ShowText("Decorated running text", x: 72, y: 652));
        content.Append(PdfFileBuilder.ShowText("Below", x: 80, y: 620));

        return PdfFileBuilder.SinglePage(content.ToString());
    }
}
