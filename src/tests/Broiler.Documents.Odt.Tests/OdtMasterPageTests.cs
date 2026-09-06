namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Which master page a document begins on.
/// </summary>
/// <remarks>
/// <para>
/// The reader used to take the first <c>style:master-page</c> in
/// <c>styles.xml</c>, which is right for documents a word processor writes from
/// scratch and wrong for ones it converts. LibreOffice turning HTML into ODF
/// emits <c>Standard</c> carrying its own default paper and <c>HTML</c> carrying
/// the page the source asked for, then lays the body out on the second. Every
/// such document read back as A4 while the application that wrote it, and every
/// other reader of the same bytes, saw US Letter.
/// </para>
/// <para>
/// ODF states the link the opposite way from where a reader looks for it: a
/// master page never says it is the first, the content says which master page it
/// is on, through the <c>style:master-page-name</c> of the style on its first
/// block.
/// </para>
/// </remarks>
public sealed class OdtMasterPageTests
{
    private const string A4Width = "21cm";
    private const string A4Height = "29.7cm";
    private const string LetterWidth = "21.59cm";
    private const string LetterHeight = "27.94cm";

    private static string Layout(string name, string width, string height, string margin) =>
        "<style:page-layout style:name=\"" + name + "\">" +
        "<style:page-layout-properties fo:page-width=\"" + width + "\" fo:page-height=\"" + height +
        "\" fo:margin-left=\"" + margin + "\" fo:margin-right=\"" + margin +
        "\" fo:margin-top=\"" + margin + "\" fo:margin-bottom=\"" + margin + "\"/>" +
        "</style:page-layout>";

    private static string MasterPage(string name, string layout) =>
        "<style:master-page style:name=\"" + name + "\" style:page-layout-name=\"" + layout + "\"/>";

    /// <summary>The two-master-page shape LibreOffice writes when it converts HTML.</summary>
    private static string TwoMasterPages(string firstName = "Standard", string secondName = "HTML") =>
        "<office:automatic-styles>" +
        Layout("pmStandard", A4Width, A4Height, "2cm") +
        Layout("pmOther", LetterWidth, LetterHeight, "2.54cm") +
        "</office:automatic-styles>" +
        "<office:master-styles>" +
        MasterPage(firstName, "pmStandard") +
        MasterPage(secondName, "pmOther") +
        "</office:master-styles>";

    private static string ParagraphStyleOn(string name, string? masterPage, string? parent = null) =>
        "<style:style style:name=\"" + name + "\" style:family=\"paragraph\"" +
        (parent is null ? string.Empty : " style:parent-style-name=\"" + parent + "\"") +
        (masterPage is null ? string.Empty : " style:master-page-name=\"" + masterPage + "\"") +
        "/>";

    private static PageGeometry PageOf(DocumentReadResult result) =>
        Assert.IsType<PageGeometry>(result.Document.PageGeometry);

    [Fact]
    public void The_Page_Comes_From_The_Master_Page_The_First_Block_Names()
    {
        // The regression, in the shape LibreOffice actually produces: the body's
        // first paragraph names an automatic style whose only job is to carry
        // style:master-page-name, and the master page it names is the second one
        // defined.
        DocumentReadResult result = OdtTestPackage.ReadStyled(
            OdtTestPackage.StyledParagraph("P1", "first") + OdtTestPackage.Paragraph("second"),
            TwoMasterPages(),
            ParagraphStyleOn("P1", "HTML"));

        PageGeometry page = PageOf(result);

        Assert.Equal(612, page.Width, 1);
        Assert.Equal(792, page.Height, 1);
        Assert.Equal(72, page.MarginLeft, 1);
    }

    [Fact]
    public void A_Document_Whose_First_Block_Names_Nothing_Uses_Standard()
    {
        // ODF's own default name for the master page a document starts on, and
        // the answer for every document that never mentions one.
        DocumentReadResult result = OdtTestPackage.ReadStyled(
            OdtTestPackage.Paragraph("body"),
            TwoMasterPages());

        PageGeometry page = PageOf(result);

        Assert.Equal(595.276, page.Width, 1);
        Assert.Equal(841.89, page.Height, 1);
    }

    [Fact]
    public void With_No_Standard_The_First_Master_Page_Is_The_Floor()
    {
        // The old behaviour, kept as the last resort rather than as the rule. A
        // document with neither a named master page nor a Standard one still has
        // to get a page from somewhere, and the first is the only defensible
        // guess left.
        DocumentReadResult result = OdtTestPackage.ReadStyled(
            OdtTestPackage.Paragraph("body"),
            TwoMasterPages(firstName: "First", secondName: "Second"));

        Assert.Equal(595.276, PageOf(result).Width, 1);
    }

    [Fact]
    public void The_Master_Page_Is_Inherited_Through_The_Style_Chain()
    {
        // A word processor routinely emits an automatic style that carries the
        // formatting and inherits the master page from its parent, so resolving
        // only the style the content names would miss it.
        DocumentReadResult result = OdtTestPackage.ReadStyled(
            OdtTestPackage.StyledParagraph("P1", "body"),
            TwoMasterPages() +
            "<office:styles>" + ParagraphStyleOn("Body", "HTML") + "</office:styles>",
            ParagraphStyleOn("P1", masterPage: null, parent: "Body"));

        Assert.Equal(612, PageOf(result).Width, 1);
    }

    [Fact]
    public void The_Most_Specific_Declaration_Wins()
    {
        DocumentReadResult result = OdtTestPackage.ReadStyled(
            OdtTestPackage.StyledParagraph("P1", "body"),
            TwoMasterPages() +
            "<office:styles>" + ParagraphStyleOn("Body", "HTML") + "</office:styles>",
            ParagraphStyleOn("P1", "Standard", parent: "Body"));

        Assert.Equal(595.276, PageOf(result).Width, 1);
    }

    [Fact]
    public void An_Empty_Master_Page_Name_Cancels_An_Inherited_One()
    {
        // ODF says an empty style:master-page-name means no page break occurs,
        // which is how a style cancels one it would otherwise inherit. Folding
        // empty into absent would turn the cancellation back into the inherited
        // page.
        DocumentReadResult result = OdtTestPackage.ReadStyled(
            OdtTestPackage.StyledParagraph("P1", "body"),
            TwoMasterPages() +
            "<office:styles>" + ParagraphStyleOn("Body", "HTML") + "</office:styles>",
            ParagraphStyleOn("P1", string.Empty, parent: "Body"));

        Assert.Equal(595.276, PageOf(result).Width, 1);
    }

    [Fact]
    public void A_Later_Block_Does_Not_Decide_The_First_Page()
    {
        // A non-empty master page name partway down a document starts a new page
        // there. The model holds one geometry, and it is the first page's.
        DocumentReadResult result = OdtTestPackage.ReadStyled(
            OdtTestPackage.Paragraph("first") + OdtTestPackage.StyledParagraph("P1", "later"),
            TwoMasterPages(),
            ParagraphStyleOn("P1", "HTML"));

        Assert.Equal(595.276, PageOf(result).Width, 1);
    }

    [Fact]
    public void A_Document_Opening_With_A_Table_Reads_Its_Table_Style()
    {
        // A table style carries style:master-page-name in the same way, and a
        // document may open with one.
        DocumentReadResult result = OdtTestPackage.ReadStyled(
            "<table:table table:name=\"T\" table:style-name=\"Ta1\"><table:table-column/>" +
            "<table:table-row><table:table-cell>" + OdtTestPackage.Paragraph("cell") +
            "</table:table-cell></table:table-row></table:table>",
            TwoMasterPages(),
            "<style:style style:name=\"Ta1\" style:family=\"table\" style:master-page-name=\"HTML\"/>");

        Assert.Equal(612, PageOf(result).Width, 1);
    }

    [Fact]
    public void Declarations_Before_The_First_Block_Are_Stepped_Over()
    {
        // Every document a word processor writes opens with text:sequence-decls,
        // which is not a block. A reader that took the first child of
        // office:text would find it and stop.
        DocumentReadResult result = OdtTestPackage.ReadStyled(
            "<text:sequence-decls><text:sequence-decl text:display-outline-level=\"0\" " +
            "text:name=\"Illustration\"/></text:sequence-decls>" +
            OdtTestPackage.StyledParagraph("P1", "body"),
            TwoMasterPages(),
            ParagraphStyleOn("P1", "HTML"));

        Assert.Equal(612, PageOf(result).Width, 1);
    }

    [Fact]
    public void A_Name_No_Master_Page_Answers_Falls_Back_Rather_Than_Losing_The_Page()
    {
        // A dangling reference is a defect in the document, and dropping the page
        // over one would replace a wrong page with no page at all.
        DocumentReadResult result = OdtTestPackage.ReadStyled(
            OdtTestPackage.StyledParagraph("P1", "body"),
            TwoMasterPages(),
            ParagraphStyleOn("P1", "NoSuchMasterPage"));

        Assert.Equal(595.276, PageOf(result).Width, 1);
    }

    [Fact]
    public void The_Header_Comes_From_The_Same_Master_Page_As_The_Paper()
    {
        // The page and the header on it are one thing. Reading them from two
        // different master pages would put a Letter header on an A4 page with
        // nothing saying so.
        string styles =
            "<office:automatic-styles>" +
            Layout("pmStandard", A4Width, A4Height, "2cm") +
            Layout("pmOther", LetterWidth, LetterHeight, "2.54cm") +
            "</office:automatic-styles>" +
            "<office:master-styles>" +
            "<style:master-page style:name=\"Standard\" style:page-layout-name=\"pmStandard\">" +
            "<style:header><text:p>from Standard</text:p></style:header>" +
            "</style:master-page>" +
            "<style:master-page style:name=\"HTML\" style:page-layout-name=\"pmOther\">" +
            "<style:header><text:p>from HTML</text:p></style:header>" +
            "</style:master-page>" +
            "</office:master-styles>";

        DocumentReadResult result = OdtTestPackage.ReadStyled(
            OdtTestPackage.StyledParagraph("P1", "body"),
            styles,
            ParagraphStyleOn("P1", "HTML"));

        Assert.Equal(612, PageOf(result).Width, 1);
        Assert.Equal(
            "from HTML",
            Assert.Single(result.Document.RunningContent.Header(PageSelection.Default)).Text);
    }
}
