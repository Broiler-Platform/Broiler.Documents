using System.IO.Compression;
using System.Text;

namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Tracked changes (ODF 1.3 §5.5): the regions <c>text:tracked-changes</c>
/// lists, and the marks in the body that say where each one is. They used to
/// be skipped with <c>odt.revision.tracked</c>, which lost every deleted word;
/// LibreOffice puts a deletion's text back where its mark is, and about ninety
/// <c>sw/qa/extras</c> ODT files carry changes.
/// </summary>
public sealed class OdtRevisionTests
{
    private const string Dc = "xmlns:dc=\"http://purl.org/dc/elements/1.1/\"";

    private static string Info(string author, string date = "2024-03-01T10:15:00") =>
        "<office:change-info><dc:creator>" + author + "</dc:creator><dc:date>" + date + "</dc:date></office:change-info>";

    private static string Regions(params string[] regions) =>
        "<text:tracked-changes " + Dc + ">" + string.Concat(regions) + "</text:tracked-changes>";

    private static string Region(string id, string kind, string author, string content = "") =>
        "<text:changed-region text:id=\"" + id + "\"><text:" + kind + ">" + Info(author) + content + "</text:" + kind + "></text:changed-region>";

    private static string Settings(bool show) =>
        "<office:document-settings xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
        "xmlns:config=\"urn:oasis:names:tc:opendocument:xmlns:config:1.0\"><office:settings>" +
        "<config:config-item-set config:name=\"ooo:view-settings\">" +
        "<config:config-item config:name=\"ShowRedlineChanges\" config:type=\"boolean\">" + (show ? "true" : "false") +
        "</config:config-item></config:config-item-set></office:settings></office:document-settings>";

    private static RichTextDocument RoundTrip(RichTextDocument document)
    {
        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(document), writable: false);
        return new OdtDocumentCodec().Read(stream).Document;
    }

    [Fact]
    public void An_Insertion_Is_The_Text_Between_Its_Marks()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            Regions(Region("c1", "insertion", "Anna")) +
            "<text:p>kept <text:change-start text:change-id=\"c1\"/>added<text:change-end text:change-id=\"c1\"/> kept</text:p>");

        RichTextParagraph paragraph = Assert.Single(result.Document.Paragraphs);
        Assert.Equal("kept added kept", paragraph.Text);
        Assert.Null(paragraph.StyleAt(0).Revision);
        Assert.Equal(
            new TextRevision(RevisionKind.Insertion, "Anna", new DateTimeOffset(2024, 3, 1, 10, 15, 0, TimeSpan.Zero)),
            paragraph.StyleAt(5).Revision);
        Assert.Null(paragraph.StyleAt(11).Revision);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "odt.revision.tracked");
    }

    [Fact]
    public void A_Deletion_Inside_A_Paragraph_Is_Put_Back_At_Its_Mark()
    {
        RichTextParagraph paragraph = Assert.Single(OdtTestPackage.ReadBody(
            Regions(Region("c1", "deletion", "Ben", "<text:p>removed </text:p>")) +
            "<text:p>Before <text:change text:change-id=\"c1\"/>after</text:p>").Document.Paragraphs);

        Assert.Equal("Before removed after", paragraph.Text);
        Assert.Equal(RevisionKind.Deletion, paragraph.StyleAt(7).Revision?.Kind);
        Assert.Equal("Ben", paragraph.StyleAt(7).Revision?.Author);
        Assert.Null(paragraph.StyleAt(15).Revision);
        Assert.Null(paragraph.MarkStyle.Revision);
    }

    [Fact]
    public void A_Deleted_Whole_Paragraph_Comes_Back_As_A_Paragraph_With_Its_Break_Deleted()
    {
        // LibreOffice writes a deleted paragraph as the paragraph and an empty
        // one, which joins the text after the mark.
        RichTextDocument document = OdtTestPackage.ReadBody(
            Regions(Region("c1", "deletion", "Ben", "<text:p>gone</text:p><text:p/>")) +
            "<text:p><text:change text:change-id=\"c1\"/>stays</text:p>").Document;

        Assert.Equal(["gone", "stays"], document.Paragraphs.Select(paragraph => paragraph.Text));
        Assert.True(document.Paragraphs[0].StyleAt(0).IsDeleted);
        Assert.True(document.Paragraphs[0].MarkStyle.IsDeleted);
        Assert.Null(document.Paragraphs[1].StyleAt(0).Revision);
        Assert.Null(document.Paragraphs[1].MarkStyle.Revision);
    }

    [Fact]
    public void A_Table_In_A_Deletion_Is_Read_As_A_Deleted_Table()
    {
        RichTextDocument document = OdtTestPackage.ReadBody(
            Regions(Region("c1", "deletion", "Ben", OdtTestPackage.Table([OdtTestPackage.Paragraph("a"), OdtTestPackage.Paragraph("b")]) + "<text:p/>")) +
            "<text:p>first</text:p><text:p><text:change text:change-id=\"c1\"/>last</text:p>").Document;

        Assert.Equal(["first", "a", "b", "last"], document.Paragraphs.Select(paragraph => paragraph.Text));
        DocumentTable table = Assert.Single(document.Tables);
        Assert.Equal(1, table.ParagraphIndex);
        Assert.True(document.Paragraphs[1].StyleAt(0).IsDeleted);
        Assert.True(RevisionDisplay.WithoutDeletions(document).Tables.Count == 0);
    }

    [Fact]
    public void Settings_That_Turn_Changes_Off_Hide_Them()
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal) { ["settings.xml"] = Settings(show: false) };
        using var stream = new MemoryStream(
            OdtTestPackage.FromBody(OdtTestPackage.Paragraph("x"), extraParts: parts),
            writable: false);

        Assert.False(new OdtDocumentCodec().Read(stream).Document.ShowsRevisions);
        Assert.True(OdtTestPackage.ReadBody(OdtTestPackage.Paragraph("x")).Document.ShowsRevisions);
    }

    [Fact]
    public void Changes_Are_Written_Back_And_Read_Again()
    {
        var anna = new TextRevision(RevisionKind.Insertion, "Anna", new DateTimeOffset(2024, 3, 1, 10, 15, 0, TimeSpan.Zero));
        var ben = new TextRevision(RevisionKind.Deletion, "Ben");
        RichTextParagraph first = RichTextParagraph.Plain("Head ")
            .InsertText(5, "added", InlineStyle.Default with { Revision = anna })
            .InsertText(10, " tail", InlineStyle.Default with { Revision = ben });
        first = first.WithMarkStyle(first.MarkStyle with { Revision = ben });
        RichTextParagraph second = RichTextParagraph.Plain(" rest").InsertText(0, "lead", InlineStyle.Default with { Revision = ben });
        RichTextDocument document = RichTextDocument.FromParagraphs([first, second, RichTextParagraph.Plain("next")])
            .WithShowsRevisions(false);

        byte[] package = OdtDocumentCodec.WriteToArray(document);
        RichTextDocument read = RoundTrip(document);

        using (var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read))
        {
            using var reader = new StreamReader(archive.GetEntry("content.xml")!.Open(), Encoding.UTF8);
            string content = reader.ReadToEnd();
            Assert.Contains("<text:tracked-changes>", content, StringComparison.Ordinal);
            Assert.NotNull(archive.GetEntry("settings.xml"));
        }

        Assert.Equal(["Head added tail", "lead rest", "next"], read.Paragraphs.Select(paragraph => paragraph.Text));
        Assert.Equal(anna, read.Paragraphs[0].StyleAt(5).Revision);
        Assert.Equal(ben, read.Paragraphs[0].StyleAt(11).Revision);
        Assert.Equal(ben, read.Paragraphs[0].MarkStyle.Revision);
        Assert.Equal(ben, read.Paragraphs[1].StyleAt(0).Revision);
        Assert.Null(read.Paragraphs[1].StyleAt(5).Revision);
        Assert.False(read.ShowsRevisions);
    }
}
