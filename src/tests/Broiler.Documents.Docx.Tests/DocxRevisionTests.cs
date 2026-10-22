using System.IO.Compression;
using System.Text;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Tracked changes (ECMA-376 §17.13.5): <c>w:ins</c> and <c>w:del</c> around
/// runs, a deleted run's <c>w:delText</c>, moves, a change of formatting, and a
/// paragraph mark's own change. Deleted text used to be skipped; LibreOffice
/// keeps it and draws it struck through, and about a hundred
/// <c>sw/qa/extras</c> DOCX files carry changes.
/// </summary>
public sealed class DocxRevisionTests
{
    private const string Inserted = "w:id=\"1\" w:author=\"Anna\" w:date=\"2024-03-01T10:15:00Z\"";
    private const string Deleted = "w:id=\"2\" w:author=\"Ben\"";

    private static string Run(string text) => "<w:r><w:t xml:space=\"preserve\">" + text + "</w:t></w:r>";

    private static string DeletedRun(string text) => "<w:r><w:delText xml:space=\"preserve\">" + text + "</w:delText></w:r>";

    private static RichTextDocument RoundTrip(RichTextDocument document)
    {
        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(document), writable: false);
        return new DocxDocumentCodec().Read(stream).Document;
    }

    private static string Part(byte[] package, string name)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Inserted_And_Deleted_Runs_Are_Kept_And_Marked()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p>" + Run("kept ") +
            "<w:ins " + Inserted + ">" + Run("added ") + "</w:ins>" +
            "<w:del " + Deleted + ">" + DeletedRun("removed") + "</w:del>" +
            "</w:p>");

        RichTextParagraph paragraph = Assert.Single(result.Document.Paragraphs);
        Assert.Equal("kept added removed", paragraph.Text);
        Assert.Null(paragraph.StyleAt(0).Revision);
        Assert.Equal(
            new TextRevision(RevisionKind.Insertion, "Anna", new DateTimeOffset(2024, 3, 1, 10, 15, 0, TimeSpan.Zero)),
            paragraph.StyleAt(5).Revision);
        Assert.Equal(new TextRevision(RevisionKind.Deletion, "Ben"), paragraph.StyleAt(11).Revision);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("docx.revision", StringComparison.Ordinal));
    }

    [Fact]
    public void Moves_Are_Marked_As_Each_Side_Of_A_Move()
    {
        RichTextParagraph paragraph = Assert.Single(DocxTestPackage.ReadBody(
            "<w:p><w:moveFrom " + Deleted + ">" + DeletedRun("here") + "</w:moveFrom>" +
            "<w:moveTo " + Inserted + ">" + Run("there") + "</w:moveTo></w:p>").Document.Paragraphs);

        Assert.Equal(RevisionKind.Deletion, paragraph.StyleAt(0).Revision?.Kind);
        Assert.True(paragraph.StyleAt(0).Revision?.Moved);
        Assert.Equal(RevisionKind.Insertion, paragraph.StyleAt(4).Revision?.Kind);
        Assert.True(paragraph.StyleAt(4).Revision?.Moved);
    }

    [Fact]
    public void A_Change_Of_Formatting_Marks_Its_Run()
    {
        RichTextParagraph paragraph = Assert.Single(DocxTestPackage.ReadBody(
            "<w:p><w:r><w:rPr><w:b/><w:rPrChange " + Inserted + "><w:rPr/></w:rPrChange></w:rPr>" +
            "<w:t>bold now</w:t></w:r></w:p>").Document.Paragraphs);

        Assert.Equal(RevisionKind.Format, paragraph.StyleAt(0).Revision?.Kind);
        Assert.True(paragraph.StyleAt(0).Bold);
    }

    [Fact]
    public void A_Change_Of_Paragraph_Properties_Marks_Its_Text_As_A_Change_Of_Formatting()
    {
        // tdf119019.docx: LibreOffice draws such a paragraph bold in the author's colour.
        RichTextParagraph paragraph = Assert.Single(DocxTestPackage.ReadBody(
            "<w:p><w:pPr><w:jc w:val=\"center\"/><w:pPrChange " + Inserted + "><w:pPr/></w:pPrChange></w:pPr>" +
            Run("centred ") + "<w:del " + Deleted + ">" + DeletedRun("gone") + "</w:del></w:p>").Document.Paragraphs);

        Assert.Equal(RevisionKind.Format, paragraph.StyleAt(0).Revision?.Kind);
        Assert.Equal("Anna", paragraph.StyleAt(0).Revision?.Author);
        Assert.True(paragraph.StyleAt(8).IsDeleted);
    }

    [Fact]
    public void A_Paragraph_Marks_Own_Change_Marks_The_Mark()
    {
        RichTextDocument document = DocxTestPackage.ReadBody(
            "<w:p><w:pPr><w:rPr><w:del " + Deleted + "/></w:rPr></w:pPr>" + Run("joined") + "</w:p>" +
            DocxTestPackage.Paragraph("next")).Document;

        Assert.True(document.Paragraphs[0].MarkStyle.IsDeleted);
        Assert.Null(document.Paragraphs[1].MarkStyle.Revision);
    }

    [Fact]
    public void Paragraphs_Inside_A_Block_Level_Change_Are_Marked_Whole()
    {
        RichTextDocument document = DocxTestPackage.ReadBody(
            "<w:ins " + Inserted + ">" + DocxTestPackage.Paragraph("kept") + "</w:ins>" +
            "<w:del " + Deleted + ">" + DocxTestPackage.Paragraph("gone") + "</w:del>").Document;

        Assert.Equal(["kept", "gone"], document.Paragraphs.Select(paragraph => paragraph.Text));
        Assert.Equal(RevisionKind.Insertion, document.Paragraphs[0].StyleAt(0).Revision?.Kind);
        Assert.True(document.Paragraphs[1].StyleAt(0).IsDeleted);
        Assert.True(document.Paragraphs[1].MarkStyle.IsDeleted);
    }

    [Theory]
    [InlineData("<w:revisionView w:insDel=\"0\"/>", false)]
    [InlineData("<w:revisionView w:markup=\"false\"/>", false)]
    [InlineData("<w:revisionView w:insDel=\"0\" w:formatting=\"1\"/>", true)]
    [InlineData("<w:revisionView w:formatting=\"0\"/>", true)]
    [InlineData("", true)]
    public void The_Revision_View_Says_Whether_Changes_Are_Shown(string view, bool shown)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/settings.xml"] = "<w:settings " + DocxTestPackage.BodyNamespaceDeclarations + ">" + view + "</w:settings>",
        };
        using var stream = new MemoryStream(DocxTestPackage.FromBody(DocxTestPackage.Paragraph("x"), parts), writable: false);

        Assert.Equal(shown, new DocxDocumentCodec().Read(stream).Document.ShowsRevisions);
    }

    [Fact]
    public void Changes_Are_Written_Back_And_Read_Again()
    {
        var anna = new TextRevision(RevisionKind.Insertion, "Anna", new DateTimeOffset(2024, 3, 1, 10, 15, 0, TimeSpan.Zero));
        var ben = new TextRevision(RevisionKind.Deletion, "Ben", Moved: true);
        var format = new TextRevision(RevisionKind.Format, null);
        RichTextParagraph first = RichTextParagraph.Plain("plain ")
            .InsertText(6, "added", InlineStyle.Default with { Revision = anna })
            .InsertText(11, " moved", InlineStyle.Default with { Revision = ben })
            .InsertText(17, " bold", InlineStyle.Default with { Bold = true, Revision = format });
        first = first.WithMarkStyle(first.MarkStyle with { Revision = new TextRevision(RevisionKind.Deletion, "Ben") });
        RichTextDocument document = RichTextDocument.FromParagraphs([first, RichTextParagraph.Plain("next")])
            .WithShowsRevisions(false);

        byte[] package = DocxDocumentCodec.WriteToArray(document);
        RichTextDocument read = RoundTrip(document);

        Assert.Contains("<w:delText", Part(package, "word/document.xml"), StringComparison.Ordinal);
        Assert.Contains("w:insDel=\"0\"", Part(package, "word/settings.xml"), StringComparison.Ordinal);
        RichTextParagraph paragraph = read.Paragraphs[0];
        Assert.Equal(first.Text, paragraph.Text);
        Assert.Equal(anna, paragraph.StyleAt(6).Revision);
        Assert.Equal(ben, paragraph.StyleAt(11).Revision);
        Assert.Equal(format, paragraph.StyleAt(17).Revision);
        Assert.True(paragraph.MarkStyle.IsDeleted);
        Assert.False(read.ShowsRevisions);
    }
}
