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

    [Fact(Timeout = 600000)]
    public void Reads_The_Header_And_Footer_A_Section_Names()
    {
        DocumentReadResult result = Read(
            "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"default\"/>" +
            "<w:footerReference r:id=\"rF\" w:type=\"default\"/></w:sectPr>");

        RunningContent running = result.Document.RunningContent;
        Assert.Equal("the letterhead", TextOf(running.Header(PageSelection.Default)));
        Assert.Equal("page one", TextOf(running.Footer(PageSelection.Default)));
    }

    [Fact(Timeout = 600000)]
    public void Keeps_The_Header_Out_Of_The_Body_Flow()
    {
        DocumentReadResult result = Read(
            "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"default\"/></w:sectPr>");

        // The letterhead belongs on the page, not in the middle of the letter.
        Assert.Equal("body", result.Document.PlainText);
    }

    [Fact(Timeout = 600000)]
    public void Keeps_The_Type_A_Reference_Declares()
    {
        DocumentReadResult result = Read(
            "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"first\"/></w:sectPr>");

        RunningContent running = result.Document.RunningContent;
        Assert.Equal("the letterhead", TextOf(running.Header(PageSelection.First)));
        Assert.Empty(running.Header(PageSelection.Default));
    }

    [Fact(Timeout = 600000)]
    public void Falls_Back_To_The_Default_For_A_Selection_With_No_Part()
    {
        DocumentReadResult result = Read(
            "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"default\"/></w:sectPr>");

        // Page two has no header of its own, so the default one is what it draws.
        Assert.Equal(
            "the letterhead",
            TextOf(result.Document.RunningContent.EffectiveHeader(PageSelection.Even)));
    }

    [Fact(Timeout = 600000)]
    public void A_Document_With_No_Section_References_Has_No_Running_Content()
    {
        Assert.True(Read("<w:sectPr/>").Document.RunningContent.IsEmpty);
    }

    [Fact(Timeout = 600000)]
    public void Warns_When_A_Reference_Names_A_Relationship_The_Package_Lacks()
    {
        DocumentReadResult result = Read(
            "<w:sectPr><w:headerReference r:id=\"rMissing\" w:type=\"default\"/></w:sectPr>");

        Assert.Contains(result.Diagnostics, d => d.Code == "docx.part.reference");
    }

    [Fact(Timeout = 600000)]
    public void Anchors_A_Header_Shape_To_The_Start_Of_The_Body()
    {
        // The shape sits on the header's second paragraph. It used to keep that
        // index and land on the body's second paragraph, which the note it is
        // reported with never claimed and which counts a different flow.
        DocumentReadResult result = Read(
            HeaderRef,
            headerInnerXml:
                "<w:p><w:r><w:t>letterhead</w:t></w:r></w:p>" +
                "<w:p>" + ShapeRun("AECF00") + "</w:p>",
            body: DocxTestPackage.Paragraph("first") + DocxTestPackage.Paragraph("second"));

        Assert.Equal(0, Assert.Single(result.Document.Shapes).ParagraphIndex);
    }

    [Fact(Timeout = 600000)]
    public void Keeps_A_Header_Shape_A_Short_Body_Cannot_Reach()
    {
        // Three header paragraphs, one body paragraph. The shape used to anchor
        // at index 2, past the end of the body, where a renderer looking for that
        // paragraph finds none and draws nothing at all.
        DocumentReadResult result = Read(
            HeaderRef,
            headerInnerXml:
                "<w:p><w:r><w:t>one</w:t></w:r></w:p>" +
                "<w:p><w:r><w:t>two</w:t></w:r></w:p>" +
                "<w:p>" + ShapeRun("AECF00") + "</w:p>",
            body: DocxTestPackage.Paragraph("only"));

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.Equal(0, shape.ParagraphIndex);
        Assert.InRange(shape.ParagraphIndex, 0, result.Document.Paragraphs.Count - 1);
    }

    [Fact(Timeout = 600000)]
    public void Reports_Two_Shapes_In_One_Header_Once()
    {
        // Two shapes raised two identical notes, unlike every other repeated
        // diagnostic this reader emits.
        DocumentReadResult result = Read(
            HeaderRef,
            headerInnerXml: "<w:p>" + ShapeRun("AECF00") + ShapeRun("FF0000") + "</w:p>");

        Assert.Equal(2, result.Document.Shapes.Count);
        Assert.Single(result.Diagnostics.Where(d => d.Code == "docx.shape.fromheader"));
    }

    [Fact(Timeout = 600000)]
    public void Reports_Shapes_From_Two_Different_Parts_Once()
    {
        // Each part is read with a builder of its own, so counting per part is
        // not enough: the note is held against the whole read, the way every
        // other once-only diagnostic here is.
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
        DocumentReadResult result = new DocxDocumentCodec().Read(stream);

        Assert.Equal(2, result.Document.Shapes.Count);
        Assert.All(result.Document.Shapes, shape => Assert.Equal(0, shape.ParagraphIndex));
        Assert.Single(result.Diagnostics.Where(d => d.Code == "docx.shape.fromheader"));
    }

    [Fact(Timeout = 600000)]
    public void Editing_The_Body_Does_Not_Lose_The_Running_Content()
    {
        RichTextDocument document = Read(
            "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"default\"/></w:sectPr>").Document;

        RichTextDocument edited = document.ApplyParagraphStyle(
            new RichTextRange(document.Start, document.End),
            ParagraphStyleDelta.WithAlignment(TextAlignment.Center));

        Assert.Equal(
            "the letterhead",
            TextOf(edited.RunningContent.Header(PageSelection.Default)));
    }

    [Fact(Timeout = 600000)]
    public void A_Header_And_Footer_Survive_A_Round_Trip()
    {
        RichTextDocument source = Read(
            "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"first\"/>" +
            "<w:footerReference r:id=\"rF\" w:type=\"default\"/></w:sectPr>").Document;

        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(source), writable: false);
        RunningContent running = new DocxDocumentCodec().Read(stream).Document.RunningContent;

        Assert.Equal("the letterhead", TextOf(running.Header(PageSelection.First)));
        Assert.Equal("page one", TextOf(running.Footer(PageSelection.Default)));
    }

    [Fact(Timeout = 600000)]
    public void Writes_The_Parts_And_Declares_Them_In_The_Package()
    {
        RichTextDocument source = Read(
            "<w:sectPr><w:headerReference r:id=\"rH\" w:type=\"first\"/></w:sectPr>").Document;

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
}
