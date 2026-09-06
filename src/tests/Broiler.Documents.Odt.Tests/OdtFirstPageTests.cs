namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// The master-page chain: which running content belongs to the first page and
/// which to the rest.
/// </summary>
/// <remarks>
/// The reader resolved the master page the body begins on - correctly - and then
/// stopped, filing its header into the Default selection and never following
/// <c>style:next-style-name</c>. On a letterhead that put the band on every page
/// and the page number on none, which is the arrangement the office suite's
/// <c>letterhead-frames</c> seed was written to hold.
/// </remarks>
public sealed class OdtFirstPageTests
{
    private const string ChainedMasterPages =
        "<style:master-page style:name=\"Letterhead\" style:page-layout-name=\"pm1\" " +
        "style:next-style-name=\"Standard\">" +
        "<style:header><text:p>band</text:p></style:header>" +
        "</style:master-page>" +
        "<style:master-page style:name=\"Standard\" style:page-layout-name=\"pm1\">" +
        "<style:footer><text:p>page number</text:p></style:footer>" +
        "</style:master-page>";

    private const string OneMasterPage =
        "<style:master-page style:name=\"Standard\" style:page-layout-name=\"pm1\">" +
        "<style:header><text:p>band</text:p></style:header>" +
        "<style:footer><text:p>page number</text:p></style:footer>" +
        "</style:master-page>";

    private const string PageLayout =
        "<style:page-layout style:name=\"pm1\"><style:page-layout-properties " +
        "fo:page-width=\"595pt\" fo:page-height=\"842pt\" fo:margin-left=\"72pt\" " +
        "fo:margin-right=\"72pt\" fo:margin-top=\"72pt\" fo:margin-bottom=\"72pt\"/></style:page-layout>";

    /// <summary>An automatic paragraph style whose only job is to name a master page.</summary>
    private static string StyleOn(string master) =>
        "<style:style style:name=\"P1\" style:family=\"paragraph\" " +
        "style:master-page-name=\"" + master + "\"/>";

    /// <summary>The whole styles part: the layout the master pages name, then them.</summary>
    private static string StylesPart(string masterPages) =>
        "<office:automatic-styles>" + PageLayout + "</office:automatic-styles>" +
        "<office:master-styles>" + masterPages + "</office:master-styles>";

    private static RunningContent Read(string masterPages, string? startsOn) =>
        OdtTestPackage.ReadStyled(
            startsOn is null
                ? OdtTestPackage.Paragraph("body")
                : OdtTestPackage.StyledParagraph("P1", "body"),
            StylesPart(masterPages),
            startsOn is null ? string.Empty : StyleOn(startsOn))
            .Document.RunningContent;

    private static string TextOf(IReadOnlyList<RichTextParagraph> paragraphs) =>
        string.Join("|", paragraphs.Select(paragraph => paragraph.Text));

    [Fact]
    public void The_Chain_Puts_The_First_Masters_Header_On_The_First_Page()
    {
        RunningContent running = Read(ChainedMasterPages, startsOn: "Letterhead");

        Assert.Equal("band", TextOf(running.Header(PageSelection.First)));
        Assert.Empty(running.Header(PageSelection.Default));
    }

    [Fact]
    public void The_Next_Masters_Footer_Belongs_To_Every_Page_After()
    {
        RunningContent running = Read(ChainedMasterPages, startsOn: "Letterhead");

        Assert.Equal("page number", TextOf(running.Footer(PageSelection.Default)));
        Assert.Equal("page number", TextOf(running.EffectiveFooter(PageSelection.Even)));
    }

    [Fact]
    public void A_First_Page_That_States_No_Footer_Carries_None()
    {
        // The whole point of the flag. Without it the empty First footer falls
        // back to the default and a page number appears under the letterhead.
        RunningContent running = Read(ChainedMasterPages, startsOn: "Letterhead");

        Assert.True(running.DifferentFirstPage);
        Assert.Empty(running.EffectiveFooter(PageSelection.First));
        Assert.Equal("page number", TextOf(running.EffectiveFooter(PageSelection.Default)));
    }

    [Fact]
    public void One_Master_Page_Still_Applies_To_Every_Page()
    {
        // The document this reader was written for, and the answer it has always
        // given: no chain, so no first page of its own and no empty slot to fall
        // into.
        RunningContent running = Read(OneMasterPage, startsOn: null);

        Assert.False(running.DifferentFirstPage);
        Assert.Equal("band", TextOf(running.EffectiveHeader(PageSelection.First)));
        Assert.Equal("page number", TextOf(running.EffectiveFooter(PageSelection.First)));
    }

    [Fact]
    public void A_Master_Page_Naming_Itself_Is_Not_A_Chain()
    {
        // A redundant style:next-style-name pointing back at the same page means
        // "every page is this one". Treating it as a chain would split it into a
        // first page and a rest that are identical, and then claim they differ.
        string selfNaming =
            "<style:master-page style:name=\"Standard\" style:page-layout-name=\"pm1\" " +
            "style:next-style-name=\"Standard\">" +
            "<style:footer><text:p>page number</text:p></style:footer>" +
            "</style:master-page>";

        RunningContent running = Read(selfNaming, startsOn: null);

        Assert.False(running.DifferentFirstPage);
        Assert.Equal("page number", TextOf(running.EffectiveFooter(PageSelection.First)));
    }

    [Fact]
    public void A_First_Page_That_Carries_Nothing_Round_Trips_As_Carrying_Nothing()
    {
        // Left out of the written master page, ODF's rule is "same as the rest"
        // and the page number comes back under the letterhead. Written as an
        // empty element, LibreOffice drops the footer from every page. One empty
        // paragraph is what says it and reads back the same way.
        RichTextDocument source = RichTextDocument.FromPlainText("body").WithRunningContent(
            RunningContent.Empty
                .WithDifferentFirstPage(true)
                .WithHeader(PageSelection.First, [RichTextParagraph.Plain("band")])
                .WithFooter(PageSelection.Default, [RichTextParagraph.Plain("page number")]));

        byte[] bytes = OdtDocumentCodec.WriteToArray(source);
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        using var reader = new StreamReader(archive.GetEntry("styles.xml")!.Open());
        Assert.Contains("<style:footer-first>", reader.ReadToEnd(), StringComparison.Ordinal);

        using var stream = new MemoryStream(bytes, writable: false);
        RunningContent back = new OdtDocumentCodec().Read(stream).Document.RunningContent;

        Assert.Equal("band", TextOf(back.EffectiveHeader(PageSelection.First)));
        Assert.Equal(string.Empty, TextOf(back.EffectiveFooter(PageSelection.First)));
        Assert.Equal("page number", TextOf(back.EffectiveFooter(PageSelection.Default)));
    }
}
