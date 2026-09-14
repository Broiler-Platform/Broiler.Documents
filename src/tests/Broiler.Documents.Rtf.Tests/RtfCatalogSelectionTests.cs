using System.Text;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfCatalogSelectionTests
{
    private static byte[] Bytes(string s) => Encoding.Latin1.GetBytes(s);

    private static DocumentCodecCatalog Catalog() =>
        new(new DocumentCodec[] { new RtfDocumentCodec() });

    [Fact]
    public void Catalog_Selects_Rtf_By_Signature()
    {
        DocumentCodecMatch? match = Catalog().Select(Bytes("{\\rtf1\\ansi\\par}"));

        Assert.NotNull(match);
        Assert.IsType<RtfDocumentCodec>(match!.Codec);
        Assert.Equal(DocumentProbeConfidence.Certain, match.Result.Confidence);
        Assert.Equal("RTF", match.Result.FormatName);
    }

    [Fact]
    public void Catalog_Does_Not_Select_Non_Rtf()
    {
        Assert.Null(Catalog().Select(Bytes("<html></html>")));
    }

    [Fact]
    public void Catalog_Finds_Rtf_By_Extension_And_Mime_Type()
    {
        DocumentCodecCatalog catalog = Catalog();

        Assert.IsType<RtfDocumentCodec>(catalog.FindByExtension(".rtf"));
        Assert.IsType<RtfDocumentCodec>(catalog.FindByMimeType("text/rtf"));
    }
}
