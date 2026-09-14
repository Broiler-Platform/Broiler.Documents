using System.Text;
using Broiler.Documents.Rtf;

namespace Broiler.Documents.Html.Tests;

public sealed class HtmlDocumentCodecProbeTests
{
    [Fact]
    public void Probe_Matches_Html_Document_Markers()
    {
        var codec = new HtmlDocumentCodec();
        DocumentProbeResult result = codec.Probe(new DocumentProbeRequest(
            Encoding.UTF8.GetBytes("\uFEFF <!doctype html><html><body>Hello</body></html>")));

        Assert.True(result.IsMatch);
        Assert.Equal(DocumentProbeConfidence.High, result.Confidence);
        Assert.Equal("HTML", result.FormatName);
        Assert.Equal("text/html", result.MimeType);
    }

    [Fact]
    public void Probe_Matches_Common_Html_Fragment_Markers()
    {
        var codec = new HtmlDocumentCodec();
        DocumentProbeResult result = codec.Probe(new DocumentProbeRequest(
            Encoding.UTF8.GetBytes("<p>Hello</p>")));

        Assert.True(result.IsMatch);
        Assert.Equal(DocumentProbeConfidence.Medium, result.Confidence);
    }

    [Fact]
    public void Probe_Falls_Back_To_File_And_Mime_Hints()
    {
        var codec = new HtmlDocumentCodec();
        DocumentProbeResult result = codec.Probe(new DocumentProbeRequest(
            Encoding.UTF8.GetBytes("plain-ish"),
            new DocumentSourceHints(fileName: "note.html", mimeType: "text/html")));

        Assert.True(result.IsMatch);
        Assert.Equal(DocumentProbeConfidence.Low, result.Confidence);
    }

    [Fact]
    public void Catalog_Selects_Html_Without_Changing_The_Catalog()
    {
        var catalog = new DocumentCodecCatalog(new DocumentCodec[]
        {
            new RtfDocumentCodec(),
            new HtmlDocumentCodec(),
        });

        DocumentCodecMatch? match = catalog.Select(Encoding.UTF8.GetBytes("<html><body>x</body></html>"));

        Assert.NotNull(match);
        Assert.IsType<HtmlDocumentCodec>(match!.Codec);
    }
}
