namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Covers optional content: which layers the default configuration puts outside
/// the presentation, and what the extraction does about it.
/// </summary>
/// <remarks>
/// The rule under test is narrow on purpose. Reading <c>/OCProperties/D</c> is
/// reading a declaration the catalog makes about itself, which is why it may be
/// acted on; deciding whether a reader would see some glyphs is a rendering
/// question this release still refuses. Every assertion here is about the first,
/// and the fixtures keep the two apart.
/// </remarks>
public sealed class PdfOptionalContentTests
{
    // ---- the default configuration --------------------------------------------

    [Fact]
    public void Content_In_A_Group_The_Default_Configuration_Turns_Off_Is_Omitted()
    {
        PdfReadResult result = Read(Layered(hiddenText: "Draft watermark", visibleText: "Quarterly report"));

        Assert.Contains("Quarterly report", Text(result), StringComparison.Ordinal);
        Assert.DoesNotContain("Draft watermark", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public void A_Group_The_Configuration_Leaves_On_Keeps_Its_Content()
    {
        PdfReadResult result = Read(Layered("Alternate language", "Quarterly report", hide: false));

        Assert.Contains("Alternate language", Text(result), StringComparison.Ordinal);
        Assert.Contains("Quarterly report", Text(result), StringComparison.Ordinal);

        // A layered document that hid nothing is not news, and the extraction is
        // exactly what it would have been without any of this.
        Assert.DoesNotContain(
            result.Diagnostics,
            d => d.Code == PdfDiagnosticCodes.OptionalContentOmitted);
    }

    [Fact]
    public void A_Base_State_Of_Off_Turns_Every_Declared_Group_Off()
    {
        PdfReadResult result = Read(Layered("Draft watermark", "Quarterly report", baseState: "/OFF"));

        Assert.DoesNotContain("Draft watermark", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public void An_On_Array_Overrides_A_Base_State_Of_Off()
    {
        // BaseState, then /ON, then /OFF, in that order. A group named in /ON
        // after a base state of OFF is on.
        PdfReadResult result = Read(Layered(
            "Draft watermark",
            "Quarterly report",
            baseState: "/OFF",
            onGroup: true));

        Assert.Contains("Draft watermark", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public void A_Document_With_No_Default_Configuration_Shows_Everything()
    {
        // No /D at all. The base state is ON by definition, so nothing is off and
        // this must not be read as "everything is off".
        PdfReadResult result = Read(Layered("Alternate language", "Quarterly report", configuration: false));

        Assert.Contains("Alternate language", Text(result), StringComparison.Ordinal);
    }

    // ---- what the diagnostic says ---------------------------------------------

    [Fact]
    public void The_Omission_Is_Reported_As_A_Declaration_Rather_Than_A_Visibility_Claim()
    {
        PdfReadResult result = Read(Layered("Draft watermark", "Quarterly report"));

        DocumentDiagnostic omitted = Only(result, PdfDiagnosticCodes.OptionalContentOmitted);
        Assert.Contains("turns 1 of 1 group off", omitted.Message, StringComparison.Ordinal);
        Assert.Contains("not a judgement about what a reader displays", omitted.Message, StringComparison.Ordinal);

        // The rendering-mode code is the one that would be a visibility claim, and
        // nothing here is drawn in an invisible mode.
        Assert.DoesNotContain(
            result.Diagnostics,
            d => d.Code == PdfDiagnosticCodes.TextVisibilityUncertain);
    }

    [Fact]
    public void Asking_For_Every_Layer_Keeps_The_Content_And_Still_Reports_The_Configuration()
    {
        PdfReadResult result = Read(
            Layered("Draft watermark", "Quarterly report"),
            new PdfReadOptions(includeHiddenOptionalContent: true));

        Assert.Contains("Draft watermark", Text(result), StringComparison.Ordinal);

        // Taking everything is a choice the caller gets told about, not one that
        // silently discards the document's own statement about itself.
        DocumentDiagnostic kept = Only(result, PdfDiagnosticCodes.OptionalContentOmitted);
        Assert.Contains("extracted anyway", kept.Message, StringComparison.Ordinal);
        Assert.Contains("not a claim the content is displayed", kept.Message, StringComparison.Ordinal);
    }

    // ---- nesting ---------------------------------------------------------------

    [Fact]
    public void A_Plain_Marked_Content_Sequence_Inside_A_Layer_Does_Not_End_It()
    {
        // BMC pairs with EMC exactly as BDC does. Counting only BDC would let the
        // inner sequence's EMC close the layer and leak the rest of it into the
        // text, which is the failure this asserts against.
        PdfReadResult result = Read(LayeredWithNesting(
            "/OC /L0 BDC\n" +
            Show("Hidden before") +
            "/Span BMC\n" +
            Show("Hidden inside") +
            "EMC\n" +
            Show("Hidden after") +
            "EMC\n" +
            Show("Visible")));

        string text = Text(result);
        Assert.DoesNotContain("Hidden before", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden inside", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden after", text, StringComparison.Ordinal);
        Assert.Contains("Visible", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_Before_And_After_A_Layer_Survives_It()
    {
        PdfReadResult result = Read(LayeredWithNesting(
            Show("Before the layer") +
            "/OC /L0 BDC\n" +
            Show("Inside") +
            "EMC\n" +
            Show("After the layer")));

        string text = Text(result);
        Assert.Contains("Before the layer", text, StringComparison.Ordinal);
        Assert.Contains("After the layer", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Inside", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_Unbalanced_EMC_Does_Not_Reopen_A_Layer()
    {
        // A stray EMC in a malformed stream must not drive the depth negative and
        // let a later one cancel a layer that was legitimately entered.
        PdfReadResult result = Read(LayeredWithNesting(
            "EMC\n" +
            "EMC\n" +
            "/OC /L0 BDC\n" +
            Show("Inside") +
            "EMC\n" +
            Show("Visible")));

        Assert.DoesNotContain("Inside", Text(result), StringComparison.Ordinal);
        Assert.Contains("Visible", Text(result), StringComparison.Ordinal);
    }

    // ---- membership dictionaries ------------------------------------------------

    [Theory]
    [InlineData("/AnyOn", true)]
    [InlineData("/AllOn", true)]
    [InlineData("/AnyOff", false)]
    [InlineData("/AllOff", false)]
    public void A_Membership_Policy_Over_One_Off_Group_Decides_Each_Way(string policy, bool hidden)
    {
        // One group, and it is off. AnyOn and AllOn both fail, so the content is
        // hidden; AnyOff and AllOff both hold, so it is shown.
        PdfReadResult result = Read(WithMembership($"/Type /OCMD /OCGs [4 0 R] /P {policy}"));

        if (hidden)
            Assert.DoesNotContain("Inside", Text(result), StringComparison.Ordinal);
        else
            Assert.Contains("Inside", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public void A_Membership_Dictionary_With_No_Groups_Shows_Its_Content()
    {
        PdfReadResult result = Read(WithMembership("/Type /OCMD"));

        Assert.Contains("Inside", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public void A_Visibility_Expression_Is_Not_Evaluated_And_Its_Content_Is_Kept()
    {
        // /VE outranks /OCGs and /P where present. Honouring those instead would
        // answer a question the document did not ask, so the content is kept and
        // the fact is said out loud.
        PdfReadResult result = Read(WithMembership("/Type /OCMD /OCGs [4 0 R] /VE [/Not 4 0 R]"));

        Assert.Contains("Inside", Text(result), StringComparison.Ordinal);

        DocumentDiagnostic note = Only(result, PdfDiagnosticCodes.OptionalContentOmitted);
        Assert.Contains("named a visibility expression", note.Message, StringComparison.Ordinal);
        Assert.Contains("kept rather than guessed at", note.Message, StringComparison.Ordinal);
    }

    // ---- fixtures -------------------------------------------------------------

    private static PdfReadResult Read(byte[] pdf, PdfReadOptions? options = null)
    {
        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec().ReadPdf(stream, options);
    }

    private static DocumentDiagnostic Only(PdfReadResult result, string code) =>
        Assert.Single(result.Diagnostics.Where(d => d.Code == code));

    private static string Text(PdfReadResult result) =>
        string.Join("\n", result.Document.Paragraphs.Select(p => p.Text));

    private static string Show(string text, double y = 700) =>
        PdfFileBuilder.ShowText(text, y: y) + "\n";

    /// <summary>
    /// One page with a layer holding <paramref name="hiddenText"/> and ordinary
    /// content holding <paramref name="visibleText"/>.
    /// </summary>
    private static byte[] Layered(
        string hiddenText,
        string visibleText,
        bool hide = true,
        string? baseState = null,
        bool onGroup = false,
        bool configuration = true)
    {
        string content =
            "/OC /L0 BDC\n" + Show(hiddenText, 700) + "EMC\n" + Show(visibleText, 600);

        string state = baseState is null ? string.Empty : $" /BaseState {baseState}";
        string on = onGroup ? " /ON [4 0 R]" : string.Empty;
        string off = hide && !onGroup && baseState is null ? " /OFF [4 0 R]" : string.Empty;
        string config = configuration
            ? $" /D << /Order [4 0 R]{state}{on}{off} >>"
            : string.Empty;

        return Document(content, $"<< /OCGs [4 0 R]{config} >>", "/L0 4 0 R");
    }

    /// <summary>One page whose content stream is given verbatim, with `/L0` off.</summary>
    private static byte[] LayeredWithNesting(string content) =>
        Document(content, "<< /OCGs [4 0 R] /D << /OFF [4 0 R] >> >>", "/L0 4 0 R");

    /// <summary>One page whose layer is entered through a membership dictionary.</summary>
    private static byte[] WithMembership(string membership) =>
        Document(
            "/OC /L0 BDC\n" + Show("Inside") + "EMC\n" + Show("Visible", 600),
            "<< /OCGs [4 0 R] /D << /OFF [4 0 R] >> >>",
            $"/L0 << {membership} >>");

    /// <summary>
    /// A one-page document whose catalog carries <paramref name="ocProperties"/>
    /// and whose page resources map the marked-content property names. Object 4
    /// is always the optional-content group, so a fixture can refer to it.
    /// </summary>
    private static byte[] Document(string content, string ocProperties, string properties)
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int group = builder.AddObject("<< /Type /OCG /Name (Layer) >>");
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int stream = builder.AddStream(string.Empty, content);

        // The fixtures name the group as "4 0 R", which holds because the catalog,
        // page tree and page are reserved first.
        Assert.Equal(4, group);

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R /OCProperties {ocProperties} >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
            $"/Resources << /Font << /F1 {font} 0 R >> /Properties << {properties} >> >> " +
            $"/Contents {stream} 0 R >>");

        return builder.Build(catalog);
    }
}
