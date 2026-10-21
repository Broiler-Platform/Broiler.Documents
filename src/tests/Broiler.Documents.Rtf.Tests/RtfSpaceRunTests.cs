using System.Text;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// Word lays out an RTF document's runs of spaces wider than their font, and
/// LibreOffice reads every RTF document that way unless it says it came from
/// somewhere else (tdf#123703, tdf#128428). The reader takes the same answer
/// into <see cref="DocumentStyleDefaults.WidensSpaceRuns"/>, and the writer says
/// which a document it writes is.
/// </summary>
public sealed class RtfSpaceRunTests
{
    private static RichTextDocument Read(string rtf) => RtfReader.Read(Encoding.ASCII.GetBytes(rtf)).Document;

    [Theory]
    [InlineData(@"{\rtf1 word  word2\par}")]
    [InlineData(@"{\rtf1{\*\generator Microsoft Word 11.0.0000;}word  word2\par}")]
    [InlineData(@"{\rtf1{\*\generator microsoft word;}word  word2\par}")]
    [InlineData(@"{\rtf1\stshfdbch31505 word  word2\par}")]
    public void A_Document_That_Does_Not_Say_Otherwise_Widens_Its_Runs(string rtf)
    {
        Assert.True(Read(rtf).StyleDefaults.WidensSpaceRuns);
    }

    [Theory]
    [InlineData(@"{\rtf1{\*\generator LibreOffice/24.2.7.2$Linux_X86_64 LibreOffice_project/420$Build-2}word  word2\par}")]
    [InlineData(@"{\rtf1{\*\generator Broiler.Documents;}word  word2\par}")]
    [InlineData(@"{\rtf1\stshfdbch0 word  word2\par}")]
    [InlineData(@"{\rtf1\dntblnsbdb word  word2\par}")]
    public void A_Document_That_Says_It_Is_Not_Words_Keeps_Its_Spaces(string rtf)
    {
        Assert.False(Read(rtf).StyleDefaults.WidensSpaceRuns);
    }

    [Fact]
    public void The_Generator_Is_Not_Body_Text()
    {
        RichTextDocument document = Read(@"{\rtf1{\*\generator Something \b Else;}word\par}");

        Assert.Equal("word", document.Paragraphs[0].Text);
        Assert.False(document.Paragraphs[0].Runs[0].Style.Bold);
    }

    [Fact]
    public void The_Spaces_Themselves_Are_Kept_As_They_Were_Typed()
    {
        // LibreOffice puts a U+2006 in front of each space; the model keeps the
        // text and lets layout widen it.
        Assert.Equal("word   word2", Read(@"{\rtf1 word   word2\par}").Paragraphs[0].Text);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Writing_Keeps_Which_It_Is(bool widens)
    {
        RichTextDocument document = RichTextDocument.FromPlainText("word  word2")
            .WithStyleDefaults(DocumentStyleDefaults.Default with { WidensSpaceRuns = widens });

        byte[] written = RtfWriter.WriteToArray(document);

        Assert.Equal(!widens, Encoding.ASCII.GetString(written).Contains(@"{\*\generator ", StringComparison.Ordinal));
        Assert.Equal(widens, RtfReader.Read(written).Document.StyleDefaults.WidensSpaceRuns);
    }
}
