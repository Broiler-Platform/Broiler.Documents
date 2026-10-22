using System.Text;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// RTF's tracked changes: <c>\revised</c> and <c>\deleted</c>, their authors by
/// index into <c>\revtbl</c> and their times as packed DTTMs. Deleted text used
/// to be read as ordinary text and <c>\revtbl</c> skipped.
/// </summary>
public sealed class RtfRevisionTests
{
    private static RichTextDocument Read(string text) =>
        RtfReader.Read(Encoding.Latin1.GetBytes(text)).Document;

    /// <summary>1 March 2024, 10:15, a Friday, packed as a DTTM.</summary>
    private static readonly int March2024 = unchecked((int)(15u | (10u << 6) | (1u << 11) | (3u << 16) | (124u << 20) | (5u << 29)));

    [Fact]
    public void Deleted_And_Revised_Text_Is_Marked_With_Its_Author_And_Time()
    {
        RichTextParagraph paragraph = Assert.Single(Read(
            @"{\rtf1{\*\revtbl{Unknown;}{Anna;}{Ben;}}kept " +
            @"{\deleted\revauthdel2\revdttmdel" + March2024 + @" gone}" +
            @"{\revised\revauth1 added}\par}").Paragraphs);

        Assert.Equal("kept goneadded", paragraph.Text);
        Assert.Null(paragraph.StyleAt(0).Revision);
        Assert.Equal(
            new TextRevision(RevisionKind.Deletion, "Ben", new DateTimeOffset(2024, 3, 1, 10, 15, 0, TimeSpan.Zero)),
            paragraph.StyleAt(5).Revision);
        Assert.Equal(new TextRevision(RevisionKind.Insertion, "Anna"), paragraph.StyleAt(9).Revision);
    }

    [Fact]
    public void A_Paragraph_Break_Inside_A_Deletion_Is_Deleted()
    {
        RichTextDocument document = Read(@"{\rtf1{\deleted one\par}two\par}");

        Assert.True(document.Paragraphs[0].MarkStyle.IsDeleted);
        Assert.Null(document.Paragraphs[1].MarkStyle.Revision);
    }

    [Fact]
    public void The_Writer_Writes_Changes_Accepted_And_Says_So()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Plain("kept ")
                .InsertText(5, "gone", InlineStyle.Default with { Revision = new TextRevision(RevisionKind.Deletion) })
                .InsertText(9, "added", InlineStyle.Default with { Revision = new TextRevision(RevisionKind.Insertion) }),
        ]);

        using var stream = new MemoryStream();
        DocumentWriteResult result = RtfWriter.Write(document, stream);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "rtf.revision");
        Assert.Equal("kept added", Assert.Single(RtfReader.Read(stream.ToArray()).Document.Paragraphs).Text);
    }
}
