using System.Text;
using Broiler.Documents.Model;
using Xunit;

namespace Broiler.Documents.Html.Tests;

public sealed class HtmlNoteTests
{
    [Fact]
    public void Reads_Footnote_Written_By_HtmlWriter_And_Round_Trips()
    {
        var note = new DocumentNote(NoteKind.Footnote, [RichTextParagraph.Plain("The note body text.")]);
        RichTextDocument document = RichTextDocument.FromParagraphs([
            RichTextParagraph.Plain("Body text before ")
                .InsertText(17, DocumentNote.PlaceholderText, InlineStyle.Default with { Note = note })
                .InsertText(18, " and after.", InlineStyle.Default)
        ]);

        var codec = new HtmlDocumentCodec();
        using var stream = new MemoryStream();
        codec.Write(document, stream);
        string html = Encoding.UTF8.GetString(stream.ToArray());

        Assert.Contains("<sup>1</sup>", html);
        Assert.Contains("<hr>", html);
        Assert.Contains("1 The note body text.", html);

        stream.Position = 0;
        DocumentReadResult read = codec.Read(stream);
        RichTextDocument roundTripped = read.Document;

        Assert.Single(roundTripped.Notes);
        DocumentNote readNote = roundTripped.Notes[0];
        Assert.Equal(NoteKind.Footnote, readNote.Kind);
        Assert.Equal("The note body text.", readNote.Paragraphs[0].Text);

        Assert.Single(roundTripped.Paragraphs);
        RichTextParagraph p = roundTripped.Paragraphs[0];
        Assert.Contains(DocumentNote.PlaceholderText, p.Text);
        Assert.Contains(p.Runs, r => r.Style.Note is not null);
    }

    [Fact]
    public void Reads_Semantic_Footnotes_Section()
    {
        string html = """
            <!DOCTYPE html>
            <html>
            <body>
                <p>Hello world<sup><a href="#fn1" class="footnote-ref">1</a></sup></p>
                <section class="footnotes">
                    <ol>
                        <li id="fn1"><p>Footnote definition text.<a href="#fnref1" class="footnote-back">↩</a></p></li>
                    </ol>
                </section>
            </body>
            </html>
            """;

        var codec = new HtmlDocumentCodec();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        DocumentReadResult read = codec.Read(stream);
        RichTextDocument document = read.Document;

        Assert.Single(document.Notes);
        DocumentNote note = document.Notes[0];
        Assert.Equal("Footnote definition text.", note.Paragraphs[0].Text.Trim());

        Assert.Single(document.Paragraphs);
        Assert.Contains(DocumentNote.PlaceholderText, document.Paragraphs[0].Text);
    }
}
