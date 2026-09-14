namespace Broiler.Documents.Tests;

public sealed class DocumentFormatDescriptorTests
{
    [Fact]
    public void Extensions_Are_Normalized_To_Leading_Dot_Lower_Case_And_Deduplicated()
    {
        var descriptor = new DocumentFormatDescriptor("RTF", null, new[] { "rtf", ".RTF", "*.rtf" });

        Assert.Equal(new[] { ".rtf" }, descriptor.FileExtensions);
    }

    [Theory]
    [InlineData("rtf", true)]
    [InlineData(".rtf", true)]
    [InlineData(".RTF", true)]
    [InlineData(".txt", false)]
    [InlineData(null, false)]
    public void MatchesExtension_Is_Case_And_Dot_Insensitive(string? extension, bool expected)
    {
        var descriptor = new DocumentFormatDescriptor("RTF", null, new[] { ".rtf" });

        Assert.Equal(expected, descriptor.MatchesExtension(extension));
    }

    [Theory]
    [InlineData("application/rtf", true)]
    [InlineData("APPLICATION/RTF", true)]
    [InlineData("text/plain", false)]
    [InlineData(null, false)]
    public void MatchesMimeType_Is_Case_Insensitive(string? mimeType, bool expected)
    {
        var descriptor = new DocumentFormatDescriptor("RTF", new[] { "application/rtf" }, null);

        Assert.Equal(expected, descriptor.MatchesMimeType(mimeType));
    }

    [Fact]
    public void Empty_Name_Is_Rejected()
    {
        Assert.Throws<ArgumentException>(() => new DocumentFormatDescriptor("  "));
    }

    [Fact]
    public void Null_Lists_Become_Empty()
    {
        var descriptor = new DocumentFormatDescriptor("RTF");

        Assert.Empty(descriptor.MimeTypes);
        Assert.Empty(descriptor.FileExtensions);
    }
}
