using System.Text;

namespace Broiler.Documents.Tests;

public sealed class DocumentCodecCatalogTests
{
    private static DocumentCodecCatalog Catalog(params DocumentCodec[] codecs) => new(codecs);

    [Fact]
    public void Duplicate_Format_Names_Are_Rejected()
    {
        Assert.Throws<ArgumentException>(() => Catalog(
            new FakeDocumentCodec("RTF", DocumentProbeConfidence.High, ".rtf", "application/rtf"),
            new FakeDocumentCodec("rtf", DocumentProbeConfidence.Low, ".rtf2", "application/rtf2")));
    }

    [Fact]
    public void Null_Codec_Collection_Is_Rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new DocumentCodecCatalog(null!));
    }

    [Fact]
    public void Find_By_Name_Extension_And_MimeType()
    {
        var codec = new FakeDocumentCodec("RTF", DocumentProbeConfidence.High, ".rtf", "application/rtf");
        DocumentCodecCatalog catalog = Catalog(codec);

        Assert.Same(codec, catalog.FindByName("rtf"));
        Assert.Same(codec, catalog.FindByExtension("rtf"));
        Assert.Same(codec, catalog.FindByMimeType("application/rtf"));
        Assert.Null(catalog.FindByExtension(".txt"));
    }

    [Fact]
    public void Select_Prefers_The_Highest_Confidence_Codec()
    {
        var low = new FakeDocumentCodec("A", DocumentProbeConfidence.Low, ".a", "application/a");
        var high = new FakeDocumentCodec("B", DocumentProbeConfidence.High, ".b", "application/b");
        DocumentCodecCatalog catalog = Catalog(low, high);

        DocumentCodecMatch? match = catalog.Select(Encoding.ASCII.GetBytes("anything"));

        Assert.NotNull(match);
        Assert.Same(high, match!.Codec);
        Assert.Equal(DocumentProbeConfidence.High, match.Result.Confidence);
    }

    [Fact]
    public void Select_Returns_Null_When_No_Codec_Matches()
    {
        var none = new FakeDocumentCodec("A", DocumentProbeConfidence.None, ".a", "application/a");
        DocumentCodecCatalog catalog = Catalog(none);

        Assert.Null(catalog.Select(Encoding.ASCII.GetBytes("anything")));
    }

    [Fact]
    public void Select_From_Stream_Restores_The_Position()
    {
        var codec = new FakeDocumentCodec("A", DocumentProbeConfidence.High, ".a", "application/a");
        DocumentCodecCatalog catalog = Catalog(codec);
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("some content here"));

        DocumentCodecMatch? match = catalog.Select(stream);

        Assert.NotNull(match);
        Assert.Equal(0, stream.Position);
    }
}
