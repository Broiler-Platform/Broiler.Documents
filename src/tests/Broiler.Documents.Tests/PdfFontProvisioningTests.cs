using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Broiler.Documents.Tests;

/// <summary>
/// Guards PDF roadmap §11.3's chosen font path: a writer takes fonts from the
/// caller's configured set or from nowhere.
/// </summary>
public sealed class PdfFontProvisioningTests
{
    /// <summary>
    /// The types that find fonts on the machine running the conversion. Correct
    /// for drawing on a screen; forbidden for deciding what goes into a file.
    /// </summary>
    private static readonly string[] AmbientDiscovery =
    [
        "BSystemFonts",
        "InstalledFontScan",
        "BSystemFontFiles",
        "FallbackSystemFont",
    ];

    [Fact(Timeout = 600000)]
    public void The_Writer_Never_Reaches_For_A_Font_The_Machine_Happens_To_Have()
    {
        // §11.3 forbids ambient selection for export and forbids substituting an
        // OS font for one that cannot be embedded. Both are true today only
        // because no export path looks for a font at all, which is a fact about
        // the code rather than a rule anything enforced — so this enforces it.
        //
        // The failure it prevents is silent: a document exported on a machine
        // with a font and one without it would differ, and nothing would say so.
        string writing = Path.Combine(
            PdfGuardRoots.Component, "src", "Broiler.Documents.Pdf", "Writing");

        var offenders = Directory
            .EnumerateFiles(writing, "*.cs", SearchOption.AllDirectories)
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .SelectMany(file => AmbientDiscovery
                .Where(type => file.Text.Contains(type, StringComparison.Ordinal))
                .Select(type => Path.GetFileName(file.Path) + " mentions " + type))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact(Timeout = 600000)]
    public void An_Empty_Font_Set_Is_The_Default_And_Not_An_Error()
    {
        Assert.True(DocumentWriteOptions.Default.Fonts.IsEmpty);
        Assert.Empty(DocumentWriteOptions.Default.Fonts.Fonts);
    }

    [Fact(Timeout = 600000)]
    public void A_Font_Reaches_A_Write_Because_The_Caller_Put_It_There()
    {
        var font = new DocumentFontResource(
            new byte[] { 1, 2, 3 },
            "Example Sans",
            Broiler.Graphics.BFontEmbeddingRights.FromFsType(0));
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
