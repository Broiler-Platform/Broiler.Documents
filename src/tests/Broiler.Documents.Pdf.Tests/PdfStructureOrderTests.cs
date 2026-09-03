namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Covers reading order taken from a tagged document's structure tree.
/// </summary>
/// <remarks>
/// Every fixture here is built so that geometry and the structure tree disagree.
/// A page whose declared order matches its geometric one would pass these tests
/// whether or not the tree was consulted at all, which would leave the feature
/// free to be a no-op; drawing the text bottom-up and declaring it top-down means
/// only one of the two answers can be the one that came out.
/// </remarks>
public sealed class PdfStructureOrderTests
{
    [Fact]
    public void A_Tagged_Page_Is_Read_In_The_Declared_Order_Not_The_Geometric_One()
    {
        // "Second" is drawn above "First", so geometry reads it first. The tree
        // says otherwise, and the tree is the document's own statement.
        string text = Text(Read(Tagged()));

        Assert.True(
            text.IndexOf("First", StringComparison.Ordinal) < text.IndexOf("Second", StringComparison.Ordinal),
            $"Expected the declared order, got: {text}");
    }

    [Fact]
    public void The_Same_Page_Without_A_Structure_Tree_Falls_Back_To_Geometry()
    {
        // The control. Identical content, no tree, so the top-down inference
        // stands and the two tests together prove which answer came from where.
        string text = Text(Read(Tagged(withTree: false)));

        Assert.True(
            text.IndexOf("Second", StringComparison.Ordinal) < text.IndexOf("First", StringComparison.Ordinal),
            $"Expected the geometric order, got: {text}");
    }

    [Fact]
    public void A_Page_The_Tree_Only_Partly_Covers_Falls_Back_Whole()
    {
        // One run drawn outside any marked content. Ordering the tagged half by
        // the tree and appending the rest would produce a sequence neither the
        // document nor the heuristic asked for, so the page reverts entirely.
        string text = Text(Read(Tagged(untaggedExtra: true)));

        Assert.True(
            text.IndexOf("Second", StringComparison.Ordinal) < text.IndexOf("First", StringComparison.Ordinal),
            $"Expected the geometric order, got: {text}");
        Assert.Contains("Untagged", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Nested_Sequence_Inherits_The_Marked_Content_It_Sits_In()
    {
        // A /Span inside a tagged paragraph states no MCID of its own. Treating
        // it as untagged would make the page only partly covered and silently
        // drop it back to geometry, so this asserts the declared order survives.
        string text = Text(Read(Tagged(nestSpan: true)));

        Assert.True(
            text.IndexOf("First", StringComparison.Ordinal) < text.IndexOf("Second", StringComparison.Ordinal),
            $"Expected the declared order, got: {text}");
        Assert.Contains("Nested", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Marked_Content_Reference_Places_Its_Own_Page()
    {
        // /K may hold an MCR dictionary rather than a bare integer, and the MCR
        // carries the page. A reader that only understood integers would leave
        // this page uncovered and fall back.
        string text = Text(Read(Tagged(useMcr: true)));

        Assert.True(
            text.IndexOf("First", StringComparison.Ordinal) < text.IndexOf("Second", StringComparison.Ordinal),
            $"Expected the declared order, got: {text}");
    }

    [Fact]
    public void A_Tree_That_Points_Back_At_Itself_Does_Not_Hang()
    {
        // A malformed tree is a document defect, not a reason to spin. The walk
        // cuts the cycle, marks itself truncated, and the page reads geometrically.
        string text = Text(Read(Tagged(cycle: true)));

        Assert.Contains("First", text, StringComparison.Ordinal);
        Assert.Contains("Second", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Diagnostic_Says_Where_The_Order_Came_From_And_Claims_Nothing_More()
    {
        PdfReadResult result = Read(Tagged());
        DocumentDiagnostic note = Only(result, PdfDiagnosticCodes.ReadingOrderHeuristic);

        Assert.Contains("came from the document's own structure tree", note.Message, StringComparison.Ordinal);
        Assert.Contains("Only the sequence was taken", note.Message, StringComparison.Ordinal);

        // The line that keeps this from becoming an accessibility claim.
        Assert.Contains("no role", note.Message, StringComparison.Ordinal);
        Assert.Contains("no accessibility or conformance claim", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_Untagged_Document_Still_Reports_The_Heuristic()
    {
        DocumentDiagnostic note = Only(Read(Tagged(withTree: false)), PdfDiagnosticCodes.ReadingOrderHeuristic);

        Assert.Contains("inferred from page geometry", note.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("structure tree", note.Message, StringComparison.Ordinal);
    }

    // ---- fixtures -------------------------------------------------------------

    private static PdfReadResult Read(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec().ReadPdf(stream, null);
    }

    private static DocumentDiagnostic Only(PdfReadResult result, string code) =>
        Assert.Single(result.Diagnostics.Where(d => d.Code == code));

    private static string Text(PdfReadResult result) =>
        string.Join("\n", result.Document.Paragraphs.Select(p => p.Text));

    /// <summary>
    /// One page drawing "Second" above "First", with a structure tree declaring
    /// the opposite order. Geometry and the declaration disagree by construction.
    /// </summary>
    private static byte[] Tagged(
        bool withTree = true,
        bool untaggedExtra = false,
        bool nestSpan = false,
        bool useMcr = false,
        bool cycle = false)
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int root = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        // MCID 0 is drawn high on the page, MCID 1 low. The tree below declares
        // 1 before 0.
        string nested = nestSpan
            ? "/Span BMC\n" + PdfFileBuilder.ShowText("Nested", y: 580) + "\nEMC\n"
            : string.Empty;

        string body =
            "/P << /MCID 0 >> BDC\n" + PdfFileBuilder.ShowText("Second", y: 700) + "\nEMC\n" +
            "/P << /MCID 1 >> BDC\n" + PdfFileBuilder.ShowText("First", y: 600) + "\n" + nested + "EMC\n" +
            (untaggedExtra ? PdfFileBuilder.ShowText("Untagged", y: 500) + "\n" : string.Empty);

        int content = builder.AddStream(string.Empty, body);

        // The low run first, then the high one: the declared order.
        string lowKid = useMcr
            ? $"<< /Type /MCR /Pg {page} 0 R /MCID 1 >>"
            : "1";

        int low = builder.AddObject(
            $"<< /Type /StructElem /S /P /P {root} 0 R /Pg {page} 0 R /K {lowKid} >>");

        // A cycle points the second element's kids back at the first element,
        // which the walk has already entered.
        string highKid = cycle ? $"{low} 0 R" : "0";
        int high = builder.AddObject(
            $"<< /Type /StructElem /S /P /P {root} 0 R /Pg {page} 0 R /K {highKid} >>");

        builder.SetObject(root, $"<< /Type /StructTreeRoot /K [{low} 0 R {high} 0 R] >>");

        string tree = withTree
            ? $" /StructTreeRoot {root} 0 R /MarkInfo << /Marked true >>"
            : string.Empty;

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R{tree} >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
            $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");

        return builder.Build(catalog);
    }
}
