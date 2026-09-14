namespace Broiler.Documents.Tests;

public sealed class DocumentLimitsTests
{
    [Fact]
    public void Default_Exposes_Positive_Bounds()
    {
        DocumentLimits limits = DocumentLimits.Default;

        Assert.Equal(DocumentLimits.DefaultMaxProbeBytes, limits.MaxProbeBytes);
        Assert.True(limits.MaxDocumentBytes > 0);
        Assert.True(limits.MaxGroupDepth > 0);
        Assert.True(limits.MaxRunLength > 0);
        Assert.True(limits.MaxParagraphCount > 0);
        Assert.True(limits.MaxBinBytes >= 0);
    }

    [Fact]
    public void Non_Positive_Bounds_Are_Rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentLimits(maxProbeBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentLimits(maxDocumentBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentLimits(maxGroupDepth: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentLimits(maxRunLength: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentLimits(maxParagraphCount: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentLimits(maxBinBytes: -1));
    }

    [Fact]
    public void Zero_Bin_Bytes_Is_Allowed()
    {
        var limits = new DocumentLimits(maxBinBytes: 0);

        Assert.Equal(0, limits.MaxBinBytes);
    }
}
