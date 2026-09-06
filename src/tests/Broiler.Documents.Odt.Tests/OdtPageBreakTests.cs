using System.Xml.Linq;

namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Page breaks, in both of the spellings ODF has for them.
/// </summary>
/// <remarks>
/// <para>
/// A document with an explicit page break used to render as one page here and as
/// two in the application that wrote it. Nothing in the codec was obviously
/// wrong: the model had nowhere to hold a break, so there was nothing for the
/// reader to lose it from and nothing for the writer to leave out. It went
/// missing without a diagnostic because as far as the code was concerned it had
/// never been there - which is the failure mode a diagnostic cannot catch, and
/// the reason this is a model change rather than a codec fix.
/// </para>
/// <para>
/// ODF states a break as <c>fo:break-before</c> on
/// <c>style:paragraph-properties</c>, so it arrives through the same inheritance
/// chain as the alignment and the margins and has to be resolved the same way.
/// It also has <c>fo:break-after</c>, which says the same thing from the far
/// side. The model holds the near side only, so what these pin is where the far
/// side is carried to and where the reader admits it could not carry it.
/// </para>
/// </remarks>
public sealed class OdtPageBreakTests
{
    private const string BreakBeforePage = "<style:paragraph-properties fo:break-before=\"page\"/>";
    private const string BreakAfterPage = "<style:paragraph-properties fo:break-after=\"page\"/>";

    private static ParagraphStyle StyleOf(DocumentReadResult result, int index) =>
        result.Document.Paragraphs[index].Style;

    private static bool Reports(DocumentReadResult result, string code) =>
        result.Diagnostics.Any(diagnostic => diagnostic.Code == code);

    [Fact]
    public void Reads_A_Break_Before_Page_As_A_Page_Break()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            OdtTestPackage.Paragraph("first") + OdtTestPackage.StyledParagraph("P1", "second"),
            OdtTestPackage.Style("P1", BreakBeforePage));

        Assert.False(StyleOf(result, 0).PageBreakBefore);
        Assert.True(StyleOf(result, 1).PageBreakBefore);
    }

    [Fact]
    public void A_Document_That_States_No_Break_Has_None()
    {
        // The false positive worth guarding: almost every paragraph in almost
        // every document says nothing about breaks, and reading a break onto one
        // of those would put a page in front of every line.
        DocumentReadResult result = OdtTestPackage.ReadBody(
            OdtTestPackage.Paragraph("first") + OdtTestPackage.StyledParagraph("P1", "second"),
            OdtTestPackage.Style("P1", "<style:paragraph-properties fo:text-align=\"center\"/>"));

        Assert.False(StyleOf(result, 0).PageBreakBefore);
        Assert.False(StyleOf(result, 1).PageBreakBefore);
        Assert.False(Reports(result, "odt.break.column"));
        Assert.False(Reports(result, "odt.break.after"));
    }

    [Fact]
    public void The_Break_Is_Inherited_Through_The_Style_Chain()
    {
        // The shape a word processor writes: an automatic style carrying the
        // direct formatting, inheriting the break from the named style it was
        // derived from. Reading only the style the content names would miss it.
        DocumentReadResult result = OdtTestPackage.ReadStyled(
            OdtTestPackage.StyledParagraph("P1", "body"),
            "<office:styles>" + OdtTestPackage.Style("Chapter", BreakBeforePage) + "</office:styles>",
            OdtTestPackage.Style(
                "P1",
                "<style:paragraph-properties fo:text-align=\"center\"/>",
                parent: "Chapter"));

        Assert.True(StyleOf(result, 0).PageBreakBefore);
        Assert.Equal(TextAlignment.Center, StyleOf(result, 0).Alignment);
    }

    [Fact]
    public void Break_Before_Auto_Cancels_A_Break_The_Chain_Carried()
    {
        // auto is the value a producer writes to turn an inherited break off
        // again. Treating it as "says nothing" would leave the break on and give
        // the paragraph a page the document explicitly took away from it.
        DocumentReadResult result = OdtTestPackage.ReadStyled(
            OdtTestPackage.StyledParagraph("P1", "body"),
            "<office:styles>" + OdtTestPackage.Style("Chapter", BreakBeforePage) + "</office:styles>",
            OdtTestPackage.Style(
                "P1",
                "<style:paragraph-properties fo:break-before=\"auto\"/>",
                parent: "Chapter"));

        Assert.False(StyleOf(result, 0).PageBreakBefore);
    }

    [Fact]
    public void A_Column_Break_Is_Not_A_Page_Break_And_Is_Reported()
    {
        // fo:break-before shares its three values with XSL, and only one of them
        // is a page. A column break moves to the next column of the same page, so
        // reading it as a page break would invent a page; the model has no
        // columns, so it is reported rather than kept.
        DocumentReadResult result = OdtTestPackage.ReadStyled(
            OdtTestPackage.StyledParagraph("P1", "body"),
            "<office:styles>" + OdtTestPackage.Style("Chapter", BreakBeforePage) + "</office:styles>",
            OdtTestPackage.Style(
                "P1",
                "<style:paragraph-properties fo:break-before=\"column\"/>",
                parent: "Chapter"));

        Assert.False(StyleOf(result, 0).PageBreakBefore);
        Assert.True(Reports(result, "odt.break.column"));
    }

    [Fact]
    public void A_Column_Break_After_Is_Reported_Too()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            OdtTestPackage.StyledParagraph("P1", "first") + OdtTestPackage.Paragraph("second"),
            OdtTestPackage.Style("P1", "<style:paragraph-properties fo:break-after=\"column\"/>"));

        Assert.False(StyleOf(result, 1).PageBreakBefore);
        Assert.True(Reports(result, "odt.break.column"));
    }

    [Fact]
    public void A_Break_After_Is_Read_Onto_The_Following_Paragraph()
    {
        // The two spellings describe one break in one gap, which is why the model
        // holds one end of it. The paragraph that states the break is not the one
        // that starts the page.
        DocumentReadResult result = OdtTestPackage.ReadBody(
            OdtTestPackage.StyledParagraph("P1", "first") + OdtTestPackage.Paragraph("second"),
            OdtTestPackage.Style("P1", BreakAfterPage));

        Assert.False(StyleOf(result, 0).PageBreakBefore);
        Assert.True(StyleOf(result, 1).PageBreakBefore);
        Assert.False(Reports(result, "odt.break.after"));
    }

    [Fact]
    public void A_Break_After_Is_Inherited_Through_The_Chain_As_Well()
    {
        DocumentReadResult result = OdtTestPackage.ReadStyled(
            OdtTestPackage.StyledParagraph("P1", "first") + OdtTestPackage.Paragraph("second"),
            "<office:styles>" + OdtTestPackage.Style("Chapter", BreakAfterPage) + "</office:styles>",
            OdtTestPackage.Style("P1", "<style:paragraph-properties/>", parent: "Chapter"));

        Assert.True(StyleOf(result, 1).PageBreakBefore);
    }

    [Fact]
    public void A_Break_After_Above_Beats_A_Break_Before_Of_Auto_Below()
    {
        // The two attributes describe one gap and either of them asking for a
        // page gets one. A paragraph saying no more than "I do not start a page"
        // cannot talk the paragraph above it out of ending one.
        DocumentReadResult result = OdtTestPackage.ReadBody(
            OdtTestPackage.StyledParagraph("P1", "first") +
            OdtTestPackage.StyledParagraph("P2", "second"),
            OdtTestPackage.Style("P1", BreakAfterPage) +
            OdtTestPackage.Style("P2", "<style:paragraph-properties fo:break-before=\"auto\"/>"));

        Assert.True(StyleOf(result, 1).PageBreakBefore);
    }

    [Fact]
    public void A_Break_After_The_Last_Paragraph_Is_Reported_Rather_Than_Lost_Quietly()
    {
        // It asks for a page with nothing on it, and the model is a sequence of
        // paragraphs rather than a sequence of pages, so there is no empty page
        // to add. Saying so is the difference between this and the defect the
        // page break was added to fix.
        DocumentReadResult result = OdtTestPackage.ReadBody(
            OdtTestPackage.Paragraph("first") + OdtTestPackage.StyledParagraph("P1", "last"),
            OdtTestPackage.Style("P1", BreakAfterPage));

        Assert.False(StyleOf(result, 0).PageBreakBefore);
        Assert.False(StyleOf(result, 1).PageBreakBefore);
        Assert.True(Reports(result, "odt.break.after"));
    }

    [Fact]
    public void A_Break_After_Does_Not_Carry_Into_A_Table()
    {
        // The next thing laid out is a grid, and a page break belongs to a
        // paragraph here. Handing it to the first paragraph of the first cell
        // would break the page inside the table rather than before it.
        DocumentReadResult result = OdtTestPackage.ReadBody(
            OdtTestPackage.StyledParagraph("P1", "before") +
            OdtTestPackage.Table([[OdtTestPackage.Paragraph("cell")]]),
            OdtTestPackage.Style("P1", BreakAfterPage));

        Assert.False(StyleOf(result, 1).PageBreakBefore);
        Assert.True(Reports(result, "odt.break.after"));
    }

    [Fact]
    public void A_Break_After_Does_Not_Carry_Across_A_Cell_Boundary()
    {
        // The paragraph after the last one in a cell is the first one in the next
        // cell, which is across the grid rather than down the page.
        DocumentReadResult result = OdtTestPackage.ReadBody(
            OdtTestPackage.Table(
            [
                [
                    OdtTestPackage.StyledParagraph("P1", "left"),
                    OdtTestPackage.Paragraph("right"),
                ],
            ]),
            OdtTestPackage.Style("P1", BreakAfterPage));

        Assert.False(StyleOf(result, 1).PageBreakBefore);
        Assert.True(Reports(result, "odt.break.after"));
    }

    [Fact]
    public void A_Break_At_The_End_Of_A_Header_Does_Not_Reach_The_Body()
    {
        // A header is its own flow. A break carried out of it would land on the
        // document's first paragraph, which is the one place a page break can
        // never be right.
        string styles =
            "<office:styles>" + OdtTestPackage.Style("H1", BreakAfterPage) + "</office:styles>" +
            "<office:automatic-styles>" +
            "<style:page-layout style:name=\"pm1\">" +
            "<style:page-layout-properties fo:page-width=\"21cm\" fo:page-height=\"29.7cm\" " +
            "fo:margin-left=\"2cm\" fo:margin-right=\"2cm\" fo:margin-top=\"2cm\" " +
            "fo:margin-bottom=\"2cm\"/></style:page-layout>" +
            "</office:automatic-styles>" +
            "<office:master-styles>" +
            "<style:master-page style:name=\"Standard\" style:page-layout-name=\"pm1\">" +
            "<style:header>" + OdtTestPackage.StyledParagraph("H1", "running") + "</style:header>" +
            "</style:master-page>" +
            "</office:master-styles>";

        DocumentReadResult result = OdtTestPackage.ReadStyled(OdtTestPackage.Paragraph("body"), styles);

        Assert.False(StyleOf(result, 0).PageBreakBefore);
        Assert.Equal(
            "running",
            Assert.Single(result.Document.RunningContent.Header(PageSelection.Default)).Text);
        Assert.True(Reports(result, "odt.break.after"));
    }

    [Fact]
    public void Writes_The_Break_As_Fo_Break_Before_Page()
    {
        byte[] package = OdtDocumentCodec.WriteToArray(Document(breakOnSecond: true));

        XElement? properties = WrittenParagraphProperties(package, 1);

        Assert.NotNull(properties);
        XNamespace fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
        Assert.Equal("page", (string?)properties.Attribute(fo + "break-before"));
    }

    [Fact]
    public void A_Paragraph_With_No_Break_Writes_No_Break_At_All()
    {
        // Never fo:break-before="auto" to say there is no break. An automatic
        // style inherits from Standard, which states none, so absence already
        // says it - and writing auto would give every ordinary paragraph in the
        // document an automatic style of its own.
        byte[] package = OdtDocumentCodec.WriteToArray(Document(breakOnSecond: true));

        Assert.Null(WrittenParagraphProperties(package, 0));
    }

    [Fact]
    public void A_Page_Break_Round_Trips()
    {
        RichTextDocument document = Document(breakOnSecond: true);

        RichTextDocument read = OdtWriterTests.RoundTrip(document);

        DocumentAssert.Equivalent(document, read);
        Assert.False(read.Paragraphs[0].Style.PageBreakBefore);
        Assert.True(read.Paragraphs[1].Style.PageBreakBefore);
    }

    [Fact]
    public void A_Document_Without_A_Page_Break_Round_Trips_Without_One()
    {
        RichTextDocument document = Document(breakOnSecond: false);

        RichTextDocument read = OdtWriterTests.RoundTrip(document);

        DocumentAssert.Equivalent(document, read);
        Assert.False(read.Paragraphs[1].Style.PageBreakBefore);
    }

    [Fact]
    public void Two_Writes_Of_A_Document_With_A_Page_Break_Produce_The_Same_Bytes()
    {
        // The break is an attribute in an automatic style, and this writer is
        // asserted to be byte-deterministic, so where it is added matters as much
        // as whether it is.
        RichTextDocument document = Document(breakOnSecond: true);

        Assert.Equal(
            OdtDocumentCodec.WriteToArray(document),
            OdtDocumentCodec.WriteToArray(document));
    }

    /// <summary>Two paragraphs, the second optionally starting a page.</summary>
    private static RichTextDocument Document(bool breakOnSecond) =>
        RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Plain("first"),
            RichTextParagraph.Create(
                "second",
                InlineStyle.Default,
                ParagraphStyle.Default with { PageBreakBefore = breakOnSecond }),
        ]);

    /// <summary>
    /// The <c>style:paragraph-properties</c> of the automatic style the written
    /// paragraph at <paramref name="index"/> names, or null when it names none -
    /// which is how this writer says a paragraph carries no formatting at all.
    /// </summary>
    private static XElement? WrittenParagraphProperties(byte[] package, int index)
    {
        XNamespace office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
        XNamespace style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
        XNamespace text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

        XElement paragraph = OdtWriterTests.ReadBodyElement(package)
            .Elements(text + "p")
            .ElementAt(index);

        string? name = (string?)paragraph.Attribute(text + "style-name");
        if (name is null)
            return null;

        return OdtWriterTests.ReadContentRoot(package)
            .Element(office + "automatic-styles")!
            .Elements(style + "style")
            .Single(candidate => (string?)candidate.Attribute(style + "name") == name)
            .Element(style + "paragraph-properties");
    }
}
