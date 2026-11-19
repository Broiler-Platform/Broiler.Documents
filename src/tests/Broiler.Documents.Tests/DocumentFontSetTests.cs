using Broiler.Documents.Resources;
using Broiler.Graphics.Text;

namespace Broiler.Documents.Tests;

/// <summary>
/// Covers <see cref="DocumentFontSet"/> and default write font configuration.
/// </summary>
public sealed class DocumentFontSetTests
{
    [Fact]
    public void An_Empty_Font_Set_Is_The_Default_And_Not_An_Error()
    {
        Assert.True(DocumentWriteOptions.Default.Fonts.IsEmpty);
        Assert.Empty(DocumentWriteOptions.Default.Fonts.Fonts);
    }

    [Fact]
    public void A_Font_Reaches_A_Write_Because_The_Caller_Put_It_There()
    {
        var font = new DocumentFontResource(
            new byte[] { 1, 2, 3 },
            "Example Sans",
            BFontEmbeddingRights.FromFsType(0));
        var set = new DocumentFontSet([font]);

        Assert.False(set.IsEmpty);
        Assert.True(set.TryFind("example sans", out DocumentFontResource? found));
        Assert.Same(font, found);

        // Exact family matching and nothing cleverer. Fuzzy matching is how a
        // document ends up written in a face nobody chose.
        Assert.False(set.TryFind("Example", out _));
        Assert.False(set.TryFind("Example Sans Condensed", out _));
        Assert.False(set.TryFind("  ", out _));
    }
}
