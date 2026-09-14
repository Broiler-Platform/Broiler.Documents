namespace Broiler.Documents.Tests;

public sealed class DocumentProbeResultTests
{
    [Fact]
    public void NoMatch_Is_Not_A_Match()
    {
        DocumentProbeResult result = DocumentProbeResult.NoMatch();

        Assert.False(result.IsMatch);
        Assert.Equal(DocumentProbeConfidence.None, result.Confidence);
    }

    [Fact]
    public void Match_Carries_Positive_Confidence_And_Format_Name()
    {
        DocumentProbeResult result = DocumentProbeResult.Match(DocumentProbeConfidence.High, "RTF", "application/rtf");

        Assert.True(result.IsMatch);
        Assert.Equal(DocumentProbeConfidence.High, result.Confidence);
        Assert.Equal("RTF", result.FormatName);
        Assert.Equal("application/rtf", result.MimeType);
    }

    [Fact]
    public void Match_Rejects_None_Confidence_And_Empty_Name()
    {
        Assert.Throws<ArgumentException>(() => DocumentProbeResult.Match(DocumentProbeConfidence.None, "RTF"));
        Assert.Throws<ArgumentException>(() => DocumentProbeResult.Match(DocumentProbeConfidence.High, "  "));
    }
}
