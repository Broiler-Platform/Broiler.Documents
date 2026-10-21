using System.Text;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// Covers <c>\cbpat</c>, a paragraph's shading: the colour behind its pattern,
/// which is all that shows when no <c>\shading</c> states one.
/// </summary>
public sealed class RtfParagraphShadingTests
{
    private static readonly BColor Green = BColor.FromArgb(0x00, 0x79, 0x29);

    private static RichTextDocument Read(string body) =>
        RtfReader.Read(Encoding.ASCII.GetBytes(
            "{\\rtf1\\ansi{\\colortbl;\\red0\\green121\\blue41;}" + body + "}")).Document;

    [Fact]
    public void Reads_A_Paragraphs_Pattern_Background()
    {
        RichTextDocument document = Read("\\pard\\cbpat1 title\\par\\pard plain\\par");

        Assert.Equal(Green, document.Paragraphs[0].Style.Background);
        Assert.True(document.Paragraphs[1].Style.Background.IsEmpty);
        Assert.All(document.Paragraphs[0].Runs, run => Assert.True(run.Style.Background.IsEmpty));
    }

    [Fact]
    public void Survives_A_Round_Trip()
    {
        RichTextDocument source = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Plain("title").WithParagraphStyle(ParagraphStyle.Default with { Background = Green }),
            RichTextParagraph.Plain("plain"),
        ]);

        RichTextDocument actual = RtfReader.Read(RtfWriter.WriteToArray(source)).Document;

        Assert.Equal(Green, actual.Paragraphs[0].Style.Background);
        Assert.True(actual.Paragraphs[1].Style.Background.IsEmpty);
    }
}
