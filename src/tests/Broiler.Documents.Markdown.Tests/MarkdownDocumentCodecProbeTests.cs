using System.Text;
using Broiler.Documents.Html;
using Broiler.Documents.Rtf;

namespace Broiler.Documents.Markdown.Tests;

public sealed class MarkdownDocumentCodecProbeTests
{
    [Fact]
    public void Probe_Matches_Block_Syntax_Conservatively()
    {
        var codec = new MarkdownDocumentCodec();
        DocumentProbeResult result = codec.Probe(new DocumentProbeRequest(Encoding.UTF8.GetBytes("# Title\n\nBody")));

        Assert.True(result.IsMatch);
        Assert.Equal(DocumentProbeConfidence.Medium, result.Confidence);
        Assert.Equal("Markdown", result.FormatName);
        Assert.Equal("text/markdown", result.MimeType);
    }

    [Fact]
    public void Probe_Matches_File_And_Mime_Hints_For_Plain_Markdown()
    {
        var codec = new MarkdownDocumentCodec();
        DocumentProbeResult result = codec.Probe(new DocumentProbeRequest(
            Encoding.UTF8.GetBytes("plain text"),
            new DocumentSourceHints(fileName: "notes.md", mimeType: "text/markdown")));

        Assert.True(result.IsMatch);
        Assert.Equal(DocumentProbeConfidence.Low, result.Confidence);
    }

    [Fact]
    public void Probe_Does_Not_Claim_Unhinted_Plain_Text()
    {
        var codec = new MarkdownDocumentCodec();
        DocumentProbeResult result = codec.Probe(new DocumentProbeRequest(Encoding.UTF8.GetBytes("plain text")));

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Catalog_Selects_Markdown_Without_Catalog_Changes()
    {
        var catalog = new DocumentCodecCatalog(new DocumentCodec[]
        {
            new RtfDocumentCodec(),
            new HtmlDocumentCodec(),
            new MarkdownDocumentCodec(),
        });

        DocumentCodecMatch? match = catalog.Select(Encoding.UTF8.GetBytes("- item"));

        Assert.NotNull(match);
        Assert.IsType<MarkdownDocumentCodec>(match!.Codec);
    }
}
