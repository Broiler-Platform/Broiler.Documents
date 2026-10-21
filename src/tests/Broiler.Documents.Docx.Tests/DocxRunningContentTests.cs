using System.IO.Compression;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Covers headers and footers. They used to be dropped on the way in with a note
/// saying so, which meant an open-then-save destroyed a letterhead: the reader
/// never saw the parts and the writer had nothing to write back.
/// </summary>
public sealed class DocxRunningContentTests
{
    private const string Rels =
        "<Relationship Id=\"rH\" " +
        "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" " +
        "Target=\"header1.xml\"/>" +
        "<Relationship Id=\"rF\" " +
        "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer\" " +
        "Target=\"footer1.xml\"/>";

    private static string Part(string tag, string text) =>
        Wrap(tag, "<w:p><w:r><w:t>" + text + "</w:t></w:r></w:p>");

    private static string Wrap(string tag, string innerXml) =>
        "<w:" + tag + " " + DocxTestPackage.BodyNamespaceDeclarations + ">" +
        innerXml +
        "</w:" + tag + ">";

    /// <summary>A coloured box in a run, which is what a letterhead's stripe is.</summary>
    private static string ShapeRun(string color) =>
        "<w:r><w:drawing>" +
        "<wp:anchor xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\">" +
        "<wp:positionH relativeFrom=\"column\"><wp:posOffset>-635000</wp:posOffset></wp:positionH>" +
        "<wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>0</wp:posOffset></wp:positionV>" +
        "<wp:extent cx=\"914400\" cy=\"228600\"/>" +
        "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
        "<a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
        "<wps:wsp xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
        "<wps:spPr><a:solidFill><a:srgbClr val=\"" + color + "\"/></a:solidFill></wps:spPr>" +
        "</wps:wsp></a:graphicData></a:graphic></wp:anchor>" +
        "</w:drawing></w:r>";

    private static DocumentReadResult Read(string sectPr, string? headerInnerXml = null, string? body = null)
    {
        // Null rather than a literal default: the body is block XML, and passing
        // bare text here silently produced a document with no paragraphs at all.
        body ??= DocxTestPackage.Paragraph("body");

        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/_rels/document.xml.rels"] =
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                Rels + "</Relationships>",
            ["word/header1.xml"] = headerInnerXml is null
                ? Part("hdr", "the letterhead")
                : Wrap("hdr", headerInnerXml),
            ["word/footer1.xml"] = Part("ftr", "page one"),
        };

        using var stream = new MemoryStream(
            DocxTestPackage.FromBody(body + sectPr, parts),
            writable: false);
        return new DocxDocumentCodec().Read(stream);
    }

    private const string HeaderRef =
        "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"default\"/></w:sectPr>";

    private static string TextOf(IReadOnlyList<RichTextParagraph> paragraphs) =>
        string.Join("\n", paragraphs.Select(p => p.Text));

    [Fact]
    public void Reads_The_Header_And_Footer_A_Section_Names()
    {
        DocumentReadResult result = Read(
            "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"default\"/>" +
            "<w:footerReference r:id=\"rF\" w:type=\"default\"/></w:sectPr>");

        RunningContent running = result.Document.RunningContent;
        Assert.Equal("the letterhead", TextOf(running.Header(PageSelection.Default)));
        Assert.Equal("page one", TextOf(running.Footer(PageSelection.Default)));
    }

    [Fact]
    public void Keeps_The_Header_Out_Of_The_Body_Flow()
    {
        DocumentReadResult result = Read(
            "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"default\"/></w:sectPr>");

        // The letterhead belongs on the page, not in the middle of the letter.
        Assert.Equal("body", result.Document.PlainText);
    }

    [Fact]
    public void Keeps_The_Type_A_Reference_Declares()
    {
        DocumentReadResult result = Read(
            "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"first\"/><w:titlePg/></w:sectPr>");

        RunningContent running = result.Document.RunningContent;
        Assert.Equal("the letterhead", TextOf(running.Header(PageSelection.First)));
        Assert.Empty(running.Header(PageSelection.Default));
    }

    [Fact]
    public void Falls_Back_To_The_Default_For_A_Selection_With_No_Part()
    {
        DocumentReadResult result = Read(
            "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"default\"/></w:sectPr>");

        // Page two has no header of its own, so the default one is what it draws.
        Assert.Equal(
            "the letterhead",
            TextOf(result.Document.RunningContent.EffectiveHeader(PageSelection.Even)));
    }

    [Fact]
    public void A_Document_With_No_Section_References_Has_No_Running_Content()
    {
        Assert.True(Read("<w:sectPr/>").Document.RunningContent.IsEmpty);
    }

    [Fact]
    public void Warns_When_A_Reference_Names_A_Relationship_The_Package_Lacks()
    {
        DocumentReadResult result = Read(
            "<w:sectPr><w:headerReference r:id=\"rMissing\" w:type=\"default\"/></w:sectPr>");

        Assert.Contains(result.Diagnostics, d => d.Code == "docx.part.reference");
    }

    [Fact]
    public void Reads_A_Header_Shape_Onto_The_Running_Content()
    {
        // It used to be handed to the body and anchored to a paragraph there,
        // which is what docx.shape.fromheader reported. A header repeats on every
        // page, so it belongs to the header rather than to a line of the letter.
        DocumentReadResult result = Read(
            HeaderRef,
            headerInnerXml: "<w:p>" + ShapeRun("AECF00") + "</w:p>");

        Assert.Empty(result.Document.Shapes);
        DocumentShape shape = Assert.Single(
            result.Document.RunningContent.HeaderShapes(PageSelection.Default));
        Assert.NotNull(shape.Fill);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "docx.shape.fromheader");
    }

    [Fact]
    public void Reads_A_Header_That_Holds_Only_A_Shape()
    {
        // A part with no words used to report nothing at all, so a letterhead
        // that is only a stripe arrived as no header.
        DocumentReadResult result = Read(
            HeaderRef,
            headerInnerXml: "<w:p>" + ShapeRun("AECF00") + "</w:p>");

        RunningContent running = result.Document.RunningContent;
        Assert.False(running.IsEmpty);
        Assert.Empty(running.Header(PageSelection.Default));
        Assert.Single(running.HeaderShapes(PageSelection.Default));
    }

    [Fact]
    public void Keeps_A_Header_And_Its_Shapes_On_The_Same_Selection()
    {
        // The fallback is resolved once for the whole part. Resolving shapes and
        // paragraphs apart would let a first page with a header of its own borrow
        // the default's stripe.
        DocumentReadResult result = Read(
            "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"first\"/><w:titlePg/></w:sectPr>",
            headerInnerXml: "<w:p>" + ShapeRun("AECF00") + "<w:r><w:t>first page</w:t></w:r></w:p>");

        RunningContent running = result.Document.RunningContent;
        Assert.Single(running.EffectiveHeaderShapes(PageSelection.First));
        Assert.Equal("first page", TextOf(running.EffectiveHeader(PageSelection.First)));
        Assert.Empty(running.EffectiveHeaderShapes(PageSelection.Default));
    }

    [Fact]
    public void Keeps_A_Header_And_A_Footer_Shape_Apart()
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/_rels/document.xml.rels"] =
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                Rels + "</Relationships>",
            ["word/header1.xml"] = Wrap("hdr", "<w:p>" + ShapeRun("AECF00") + "</w:p>"),
            ["word/footer1.xml"] = Wrap("ftr", "<w:p>" + ShapeRun("FF0000") + "</w:p>"),
        };

        using var stream = new MemoryStream(
            DocxTestPackage.FromBody(
                DocxTestPackage.Paragraph("body") +
                "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"default\"/>" +
                "<w:footerReference r:id=\"rF\" w:type=\"default\"/></w:sectPr>",
                parts),
            writable: false);
        RichTextDocument document = new DocxDocumentCodec().Read(stream).Document;

        Assert.Empty(document.Shapes);
        Assert.Single(document.RunningContent.HeaderShapes(PageSelection.Default));
        Assert.Single(document.RunningContent.FooterShapes(PageSelection.Default));
    }

    [Fact]
    public void A_Header_Shape_Survives_A_Round_Trip()
    {
        RichTextDocument source = Read(
            HeaderRef,
            headerInnerXml: "<w:p>" + ShapeRun("AECF00") + "<w:r><w:t>letterhead</w:t></w:r></w:p>").Document;

        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(source), writable: false);
        RichTextDocument actual = new DocxDocumentCodec().Read(stream).Document;

        Assert.Empty(actual.Shapes);
        Assert.Single(actual.RunningContent.HeaderShapes(PageSelection.Default));
        Assert.Equal("letterhead", TextOf(actual.RunningContent.Header(PageSelection.Default)));
    }

    [Fact]
    public void Editing_The_Body_Does_Not_Lose_The_Running_Content()
    {
        RichTextDocument document = Read(
            "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"default\"/></w:sectPr>").Document;

        RichTextDocument edited = document.ApplyParagraphStyle(
            new RichTextRange(RichTextDocument.Start, document.End),
            ParagraphStyleDelta.WithAlignment(TextAlignment.Center));

        Assert.Equal(
            "the letterhead",
            TextOf(edited.RunningContent.Header(PageSelection.Default)));
    }

    [Fact]
    public void A_Header_And_Footer_Survive_A_Round_Trip()
    {
        RichTextDocument source = Read(
            "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"first\"/>" +
            "<w:footerReference r:id=\"rF\" w:type=\"default\"/><w:titlePg/></w:sectPr>").Document;

        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(source), writable: false);
        RunningContent running = new DocxDocumentCodec().Read(stream).Document.RunningContent;

        Assert.Equal("the letterhead", TextOf(running.Header(PageSelection.First)));
        Assert.Equal("page one", TextOf(running.Footer(PageSelection.Default)));
    }

    [Fact]
    public void Writes_The_Parts_And_Declares_Them_In_The_Package()
    {
        RichTextDocument source = Read(
            "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"first\"/><w:titlePg/></w:sectPr>").Document;

        byte[] bytes = DocxDocumentCodec.WriteToArray(source);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        Assert.NotNull(archive.GetEntry("word/header1.xml"));
        using var types = new StreamReader(archive.GetEntry("[Content_Types].xml")!.Open());
        Assert.Contains("wordprocessingml.header+xml", types.ReadToEnd());

        using var document = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
        string xml = document.ReadToEnd();
        Assert.Contains("headerReference", xml);
        // A first-page header only takes effect when the section asks for one.
        Assert.Contains("titlePg", xml);
    }

    [Fact]
    public void TitlePg_Is_What_Makes_The_First_Page_Different()
    {
        // The letterhead's section: a first-page header, a default footer, and
        // the flag. The flag was not read, so the empty first-page footer fell
        // back to the default one and a page number was drawn under a letterhead
        // that carries none - which is what LibreOffice draws too, and did not.
        RunningContent running = Read(
            "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"first\"/>" +
            "<w:footerReference r:id=\"rF\" w:type=\"default\"/>" +
            "<w:titlePg/></w:sectPr>").Document.RunningContent;

        Assert.True(running.DifferentFirstPage);
        Assert.Equal("the letterhead", TextOf(running.EffectiveHeader(PageSelection.First)));
        Assert.Empty(running.EffectiveFooter(PageSelection.First));
        Assert.Equal("page one", TextOf(running.EffectiveFooter(PageSelection.Default)));
    }

    [Fact]
    public void Without_TitlePg_The_First_Page_Takes_The_Default_Footer()
    {
        // The other half of the same rule, and the reason it is a flag rather
        // than "a First part exists". Word ignores a first-page reference in a
        // section that did not ask for one; a default footer applies to page one
        // like every other page.
        RunningContent running = Read(
            "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"first\"/>" +
            "<w:footerReference r:id=\"rF\" w:type=\"default\"/></w:sectPr>").Document.RunningContent;

        Assert.False(running.DifferentFirstPage);
        Assert.Equal("page one", TextOf(running.EffectiveFooter(PageSelection.First)));
    }

    [Fact]
    public void TitlePg_Turned_Off_Is_Off()
    {
        RunningContent running = Read(
            "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"first\"/>" +
            "<w:footerReference r:id=\"rF\" w:type=\"default\"/>" +
            "<w:titlePg w:val=\"false\"/></w:sectPr>").Document.RunningContent;

        Assert.False(running.DifferentFirstPage);
    }

    [Fact]
    public void A_Different_First_Page_Round_Trips_Through_The_Writer()
    {
        RichTextDocument source = RichTextDocument.FromPlainText("body").WithRunningContent(
            RunningContent.Empty
                .WithDifferentFirstPage(true)
                .WithFooter(PageSelection.Default, [RichTextParagraph.Plain("page number")]));

        // No First part at all, so the flag is the only thing asking for the
        // element - and the only thing keeping the page number off page one when
        // the document is read back.
        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(source), writable: false);
        RunningContent back = new DocxDocumentCodec().Read(stream).Document.RunningContent;

        Assert.True(back.DifferentFirstPage);
        Assert.Empty(back.EffectiveFooter(PageSelection.First));
    }

    /// <summary>A body paragraph that ends a section whose properties are <paramref name="sectPrInnerXml"/>.</summary>
    private static string SectionEnd(string text, string sectPrInnerXml) =>
        "<w:p><w:pPr><w:sectPr>" + sectPrInnerXml + "</w:sectPr></w:pPr>" +
        "<w:r><w:t>" + text + "</w:t></w:r></w:p>";

    [Fact]
    public void A_Section_That_Names_No_Header_Continues_The_One_Before_It()
    {
        // ECMA-376 §17.10.5: a section with no reference of a type has the
        // previous section's. tdf168017.docx names its header and footer in the
        // first of nine sections and draws them on every page.
        DocumentReadResult result = Read(
            "<w:sectPr/>",
            body:
                SectionEnd("one", "<w:headerReference r:id=\"rH\" w:type=\"default\"/>" +
                    "<w:footerReference r:id=\"rF\" w:type=\"default\"/>") +
                SectionEnd("two", string.Empty) +
                DocxTestPackage.Paragraph("three"));

        RunningContent running = result.Document.RunningContent;
        Assert.Equal("the letterhead", TextOf(running.Header(PageSelection.Default)));
        Assert.Equal("page one", TextOf(running.Footer(PageSelection.Default)));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.section.multiple");
    }

    [Fact]
    public void Each_Type_Continues_On_Its_Own()
    {
        // The last section states its own footer and no header: the footer is
        // its own, and only the header continues from before.
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/_rels/document.xml.rels"] =
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                Rels +
                "<Relationship Id=\"rF2\" " +
                "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer\" " +
                "Target=\"footer2.xml\"/>" +
                "</Relationships>",
            ["word/header1.xml"] = Part("hdr", "the letterhead"),
            ["word/footer1.xml"] = Part("ftr", "page one"),
            ["word/footer2.xml"] = Part("ftr", "the last section's footer"),
        };

        using var stream = new MemoryStream(
            DocxTestPackage.FromBody(
                SectionEnd("one", "<w:headerReference r:id=\"rH\" w:type=\"default\"/>" +
                    "<w:footerReference r:id=\"rF\" w:type=\"default\"/>") +
                DocxTestPackage.Paragraph("two") +
                "<w:sectPr><w:footerReference r:id=\"rF2\" w:type=\"default\"/></w:sectPr>",
                parts),
            writable: false);
        RunningContent running = new DocxDocumentCodec().Read(stream).Document.RunningContent;

        Assert.Equal("the letterhead", TextOf(running.Header(PageSelection.Default)));
        Assert.Equal("the last section's footer", TextOf(running.Footer(PageSelection.Default)));
    }

    [Fact]
    public void A_Continued_Header_Takes_The_Nearest_Section_Before()
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/_rels/document.xml.rels"] =
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                Rels +
                "<Relationship Id=\"rH2\" " +
                "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" " +
                "Target=\"header2.xml\"/>" +
                "</Relationships>",
            ["word/header1.xml"] = Part("hdr", "the first section's header"),
            ["word/header2.xml"] = Part("hdr", "the second section's header"),
            ["word/footer1.xml"] = Part("ftr", "page one"),
        };

        using var stream = new MemoryStream(
            DocxTestPackage.FromBody(
                SectionEnd("one", "<w:headerReference r:id=\"rH\" w:type=\"default\"/>") +
                SectionEnd("two", "<w:headerReference r:id=\"rH2\" w:type=\"default\"/>") +
                DocxTestPackage.Paragraph("three") +
                "<w:sectPr/>",
                parts),
            writable: false);
        RunningContent running = new DocxDocumentCodec().Read(stream).Document.RunningContent;

        Assert.Equal("the second section's header", TextOf(running.Header(PageSelection.Default)));
    }

    [Fact]
    public void A_First_Page_Part_Is_Not_Read_Without_TitlePg()
    {
        // The model draws a First part on page one wherever there is one; Word
        // draws it only where the section asks for a different first page.
        DocumentReadResult result = Read(
            "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"first\"/></w:sectPr>");

        Assert.Empty(result.Document.RunningContent.Header(PageSelection.First));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.header.first");
    }

    [Fact]
    public void An_Even_Page_Part_Is_Not_Read_Unless_The_Settings_Turn_Even_Pages_On()
    {
        // tdf168017.docx names an even footer holding a lone page number and does
        // not set w:evenAndOddHeaders; Word and LibreOffice draw the default
        // footer on every page.
        DocumentReadResult result = Read(
            "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"even\"/>" +
            "<w:footerReference r:id=\"rF\" w:type=\"default\"/></w:sectPr>");

        RunningContent running = result.Document.RunningContent;
        Assert.Empty(running.Header(PageSelection.Even));
        Assert.Equal("page one", TextOf(running.Footer(PageSelection.Default)));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.header.even");
    }

    [Theory]
    [InlineData("<w:evenAndOddHeaders/>", true)]
    [InlineData("<w:evenAndOddHeaders w:val=\"false\"/>", false)]
    public void W_EvenAndOddHeaders_Decides_Whether_An_Even_Part_Is_Read(string setting, bool read)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/_rels/document.xml.rels"] =
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                Rels + "</Relationships>",
            ["word/header1.xml"] = Part("hdr", "the even header"),
            ["word/footer1.xml"] = Part("ftr", "page one"),
            ["word/settings.xml"] = Wrap("settings", setting),
        };

        using var stream = new MemoryStream(
            DocxTestPackage.FromBody(
                DocxTestPackage.Paragraph("body") +
                "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"even\"/></w:sectPr>",
                parts),
            writable: false);
        RunningContent running = new DocxDocumentCodec().Read(stream).Document.RunningContent;

        Assert.Equal(read ? "the even header" : string.Empty, TextOf(running.Header(PageSelection.Even)));
    }

    [Fact]
    public void An_Even_Page_Part_Is_Written_With_The_Setting_That_Turns_It_On()
    {
        RichTextDocument source = RichTextDocument.FromPlainText("body").WithRunningContent(
            RunningContent.Empty
                .WithHeader(PageSelection.Default, [RichTextParagraph.Plain("odd")])
                .WithHeader(PageSelection.Even, [RichTextParagraph.Plain("even")]));

        byte[] bytes = DocxDocumentCodec.WriteToArray(source);
        using (var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            using var settings = new StreamReader(archive.GetEntry("word/settings.xml")!.Open());
            Assert.Contains("evenAndOddHeaders", settings.ReadToEnd());
            using var types = new StreamReader(archive.GetEntry("[Content_Types].xml")!.Open());
            Assert.Contains("wordprocessingml.settings+xml", types.ReadToEnd());
            using var relationships = new StreamReader(archive.GetEntry("word/_rels/document.xml.rels")!.Open());
            Assert.Contains("relationships/settings", relationships.ReadToEnd());
        }

        using var stream = new MemoryStream(bytes, writable: false);
        RunningContent back = new DocxDocumentCodec().Read(stream).Document.RunningContent;
        Assert.Equal("even", TextOf(back.Header(PageSelection.Even)));
        Assert.Equal("odd", TextOf(back.Header(PageSelection.Default)));
    }

    [Fact]
    public void No_Settings_Part_Is_Written_Without_An_Even_Page_Part()
    {
        RichTextDocument source = RichTextDocument.FromPlainText("body").WithRunningContent(
            RunningContent.Empty.WithHeader(PageSelection.Default, [RichTextParagraph.Plain("header")]));

        using var archive = new ZipArchive(new MemoryStream(DocxDocumentCodec.WriteToArray(source)), ZipArchiveMode.Read);
        Assert.Null(archive.GetEntry("word/settings.xml"));
    }
}
