namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// <c>text:page-number</c> and <c>text:page-count</c>, read as runs that keep the
/// value LibreOffice saved and say which field they are, so a header draws each
/// page's own number; and <c>style:page-number</c>, the restart of the count on
/// the paragraph that starts a page style.
/// </summary>
public sealed class OdtPageFieldTests
{
    private const string Masters =
        "<office:automatic-styles>" +
        "<style:page-layout style:name=\"a\"><style:page-layout-properties fo:page-width=\"200pt\" fo:page-height=\"300pt\"/></style:page-layout>" +
        "</office:automatic-styles>" +
        "<office:master-styles>" +
        "<style:master-page style:name=\"Standard\" style:page-layout-name=\"a\"><style:header><text:p>- <text:page-number text:select-page=\"current\">3</text:page-number> -</text:p></style:header></style:master-page>" +
        "<style:master-page style:name=\"Sheet\" style:page-layout-name=\"a\"/>" +
        "</office:master-styles>";

    private static StyleRun FieldRun(RichTextParagraph paragraph) =>
        Assert.Single(paragraph.Runs, run => run.Style.PageField is not null);

    private static string FieldText(RichTextParagraph paragraph)
    {
        int start = 0;
        foreach (StyleRun run in paragraph.Runs)
        {
            if (run.Style.PageField is not null)
                return paragraph.Text.Substring(start, run.Length);
            start += run.Length;
        }

        throw new InvalidOperationException("no field");
    }

    private static RichTextDocument RoundTrip(RichTextDocument document)
    {
        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(document), writable: false);
        return new OdtDocumentCodec().Read(stream).Document;
    }

    [Fact]
    public void A_Header_Page_Number_Keeps_Its_Saved_Value_And_Says_It_Is_A_Field()
    {
        RichTextDocument document = OdtTestPackage.ReadStyled("<text:p>body</text:p>", Masters).Document;

        RichTextParagraph header = Assert.Single(document.RunningContent.Header(PageSelection.Default));
        Assert.Equal("- 3 -", header.Text);
        Assert.Equal(new PageField(PageFieldKind.PageNumber), FieldRun(header).Style.PageField);
        Assert.Equal("3", FieldText(header));
    }

    [Theory]
    [InlineData("<text:page-number text:select-page=\"previous\">1</text:page-number>", PageFieldKind.PageNumber, ListNumberFormat.Decimal, -1)]
    [InlineData("<text:page-number text:select-page=\"next\" text:page-adjust=\"2\">4</text:page-number>", PageFieldKind.PageNumber, ListNumberFormat.Decimal, 3)]
    [InlineData("<text:page-number style:num-format=\"i\">ii</text:page-number>", PageFieldKind.PageNumber, ListNumberFormat.LowerRoman, 0)]
    [InlineData("<text:page-number style:num-format=\"A\">B</text:page-number>", PageFieldKind.PageNumber, ListNumberFormat.UpperLetter, 0)]
    [InlineData("<text:page-count>7</text:page-count>", PageFieldKind.PageCount, ListNumberFormat.Decimal, 0)]
    public void A_Field_Reads_What_It_Counts_How_It_Writes_It_And_Its_Adjustment(
        string field, PageFieldKind kind, ListNumberFormat format, int adjust)
    {
        RichTextDocument document = OdtTestPackage.ReadBody("<text:p>page " + field + "</text:p>").Document;

        Assert.Equal(new PageField(kind, format, adjust), FieldRun(document.Paragraphs[0]).Style.PageField);
    }

    [Fact]
    public void A_Field_Saved_Without_A_Value_Stands_As_The_First_Page()
    {
        RichTextDocument document = OdtTestPackage.ReadBody("<text:p>page <text:page-number/></text:p>").Document;

        Assert.Equal("page 1", document.Paragraphs[0].Text);
        Assert.NotNull(FieldRun(document.Paragraphs[0]).Style.PageField);
    }

    [Fact]
    public void Page_Fields_Round_Trip()
    {
        RichTextParagraph paragraph = RichTextParagraph.Plain("page  of ")
            .InsertText(5, "ii", InlineStyle.Default with { PageField = new PageField(PageFieldKind.PageNumber, ListNumberFormat.LowerRoman, 1) })
            .InsertText(11, "9", InlineStyle.Default with { PageField = new PageField(PageFieldKind.PageCount) });
        RichTextDocument document = RichTextDocument.FromParagraphs([paragraph]);

        RichTextParagraph read = RoundTrip(document).Paragraphs[0];

        Assert.Equal("page ii of 9", read.Text);
        Assert.Equal(
            [new PageField(PageFieldKind.PageNumber, ListNumberFormat.LowerRoman, 1), new PageField(PageFieldKind.PageCount)],
            read.Runs.Select(run => run.Style.PageField).Where(field => field is not null));
    }

    [Fact]
    public void A_Paragraph_That_Starts_A_Page_Style_Restarts_The_Count_Where_It_Says()
    {
        RichTextDocument document = OdtTestPackage.ReadStyled(
            "<text:p>first</text:p><text:p text:style-name=\"sheet\">approval sheet</text:p><text:p>more</text:p>",
            Masters,
            "<style:style style:name=\"sheet\" style:family=\"paragraph\" style:master-page-name=\"Sheet\">" +
            "<style:paragraph-properties style:page-number=\"1\"/></style:style>").Document;

        DocumentSection sheet = DocumentSection.Containing(document.Sections, 1)!;
        Assert.Equal(1, sheet.ParagraphIndex);
        Assert.Equal(1, sheet.PageNumberStart);
        Assert.Null(DocumentSection.Containing(document.Sections, 0)!.PageNumberStart);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("0")]
    public void A_Page_Number_Of_Auto_Or_Nought_Carries_The_Count_On(string value)
    {
        RichTextDocument document = OdtTestPackage.ReadStyled(
            "<text:p>first</text:p><text:p text:style-name=\"sheet\">approval sheet</text:p>",
            Masters,
            "<style:style style:name=\"sheet\" style:family=\"paragraph\" style:master-page-name=\"Sheet\">" +
            "<style:paragraph-properties style:page-number=\"" + value + "\"/></style:style>").Document;

        Assert.All(document.Sections, section => Assert.Null(section.PageNumberStart));
    }

    [Fact]
    public void Writing_A_Restart_It_Cannot_Keep_Says_So()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("a"), RichTextParagraph.Plain("b")])
            .WithSections([new DocumentSection(1, 1, 1, 0, breakType: SectionBreakType.NextPage, pageNumberStart: 1)]);
        using var stream = new MemoryStream();

        DocumentWriteResult result = new OdtDocumentCodec().Write(document, stream);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "odt.section.page-number");
    }
}
