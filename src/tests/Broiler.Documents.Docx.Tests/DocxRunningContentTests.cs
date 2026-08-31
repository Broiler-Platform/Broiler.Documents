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
        "<w:" + tag + " " + DocxTestPackage.BodyNamespaceDeclarations + ">" +
        "<w:p><w:r><w:t>" + text + "</w:t></w:r></w:p>" +
        "</w:" + tag + ">";

    private static DocumentReadResult Read(string sectPr)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/_rels/document.xml.rels"] =
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                Rels + "</Relationships>",
            ["word/header1.xml"] = Part("hdr", "the letterhead"),
            ["word/footer1.xml"] = Part("ftr", "page one"),
        };

        using var stream = new MemoryStream(
            DocxTestPackage.FromBody(DocxTestPackage.Paragraph("body") + sectPr, parts),
            writable: false);
        return new DocxDocumentCodec().Read(stream);
    }

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
