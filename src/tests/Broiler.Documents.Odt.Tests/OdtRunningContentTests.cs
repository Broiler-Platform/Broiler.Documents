using Broiler.Graphics.Color;

namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Headers and footers in ODT. ODF hangs them off a master page in styles.xml
/// rather than putting them in the content, so they were never read: the reader
/// only ever walked office:text.
/// </summary>
public sealed class OdtRunningContentTests
{
    private static RichTextDocument WithRunning(string? header, string? footer)
    {
        RunningContent running = RunningContent.Empty;
        if (header is not null)
            running = running.WithHeader(PageSelection.Default, [RichTextParagraph.Plain(header)]);
        if (footer is not null)
            running = running.WithFooter(PageSelection.First, [RichTextParagraph.Plain(footer)]);
        return RichTextDocument.FromPlainText("body").WithRunningContent(running);
    }

    private static RichTextDocument RoundTrip(RichTextDocument document)
    {
        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(document), writable: false);
        return new OdtDocumentCodec().Read(stream).Document;
    }

    private static string TextOf(IReadOnlyList<RichTextParagraph> paragraphs) =>
        string.Join("|", paragraphs.Select(p => p.Text));

    [Fact]
    public void A_Header_And_Footer_Round_Trip_Through_The_Master_Page()
    {
        RunningContent running = RoundTrip(WithRunning("letterhead", "pagefooter")).RunningContent;

        Assert.Equal("letterhead", TextOf(running.Header(PageSelection.Default)));
        Assert.Equal("pagefooter", TextOf(running.Footer(PageSelection.First)));
    }

    [Fact]
    public void A_Header_Shape_Round_Trips_Through_The_Master_Page()
    {
        // The reader built the part and kept only its paragraphs, so a master
        // page whose header is a coloured stripe lost the stripe outright.
        RichTextDocument source = RichTextDocument.FromPlainText("body").WithRunningContent(
            RunningContent.Empty.WithHeader(
                PageSelection.Default,
                [RichTextParagraph.Plain("letterhead")],
                [new DocumentShape(0, 0, 20, 120, 8, ShapeFill.Solid(BColor.Black))]));

        RunningContent running = RoundTrip(source).RunningContent;

        DocumentShape shape = Assert.Single(running.HeaderShapes(PageSelection.Default));
        Assert.Equal(20, shape.OffsetY, 1);
        Assert.Equal(120, shape.Width, 1);
        Assert.Equal("letterhead", TextOf(running.Header(PageSelection.Default)));
    }

    [Fact]
    public void A_Paragraph_Anchored_Footer_Shape_Is_Offset_From_The_Footer()
    {
        string styles = "<office:automatic-styles><style:page-layout style:name='page'>" +
            "<style:page-layout-properties fo:page-width='600pt' fo:page-height='800pt' fo:margin-bottom='60pt'/>" +
            "<style:footer-style><style:header-footer-properties svg:height='80pt' fo:margin-top='10pt'/>" +
            "</style:footer-style></style:page-layout><style:style style:name='shape' style:family='graphic'>" +
            "<style:graphic-properties draw:fill='solid' draw:fill-color='#000000' style:vertical-rel='paragraph'/>" +
            "</style:style></office:automatic-styles><office:master-styles><style:master-page style:name='Standard' " +
            "style:page-layout-name='page'><style:footer><text:p><draw:rect text:anchor-type='paragraph' " +
            "draw:style-name='shape' svg:y='5pt' svg:width='30pt' svg:height='20pt'/></text:p></style:footer>" +
            "</style:master-page></office:master-styles>";
        var document = OdtTestPackage.ReadStyled("<text:p>body</text:p>", styles).Document;

        Assert.True(document.StyleDefaults.PreserveOpeningParagraphSpacing);
        DocumentShape shape = Assert.Single(document.RunningContent.FooterShapes(PageSelection.Default));
        Assert.Equal(675, shape.OffsetY, 4);
        Assert.Equal(shape.OffsetY,
            Assert.Single(RoundTrip(document).RunningContent.FooterShapes(PageSelection.Default)).OffsetY, 4);
    }

    [Fact]
    public void A_Header_That_Holds_Only_A_Shape_Is_Still_A_Header()
    {
        RichTextDocument source = RichTextDocument.FromPlainText("body").WithRunningContent(
            RunningContent.Empty.WithHeader(
                PageSelection.Default,
                paragraphs: null,
                [new DocumentShape(0, 0, 20, 120, 8, ShapeFill.Solid(BColor.Black))]));

        RunningContent running = RoundTrip(source).RunningContent;

        Assert.False(running.IsEmpty);
        Assert.Single(running.HeaderShapes(PageSelection.Default));
    }

    [Fact]
    public void The_Body_Keeps_Its_Own_Text()
    {
        Assert.Equal("body", RoundTrip(WithRunning("letterhead", null)).PlainText);
    }

    [Fact]
    public void A_Document_Without_Running_Content_Gets_None()
    {
        Assert.True(RoundTrip(RichTextDocument.FromPlainText("body")).RunningContent.IsEmpty);
    }
}
