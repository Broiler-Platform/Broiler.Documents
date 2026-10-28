using System.Collections.Generic;
using System.Linq;
using Broiler.Documents.Model.Text;
using Xunit;

namespace Broiler.Documents.Model.Tests;

public sealed class UnicodeBoundaryClassifierTests
{
    #region 1. Grapheme Clusters (UAX #29)

    [Fact]
    public void GraphemeClusters_AsciiAndLatin_SegmentedPerCharacter()
    {
        string text = "Hello";
        var clusters = UnicodeBoundaryClassifier.GetGraphemeClusters(text);

        Assert.Equal(5, clusters.Count);
        Assert.Equal(["H", "e", "l", "l", "o"], clusters.Select(c => c.Text));
    }

    [Fact]
    public void GraphemeClusters_SurrogatePairs_NeverSplit()
    {
        // 🍎 (U+1F34E) and 😀 (U+1F600)
        string text = "A🍎B😀C";
        var clusters = UnicodeBoundaryClassifier.GetGraphemeClusters(text);

        Assert.Equal(5, clusters.Count);
        Assert.Equal("A", clusters[0].Text);
        Assert.Equal("🍎", clusters[1].Text);
        Assert.True(clusters[1].IsSurrogatePair);
        Assert.Equal(2, clusters[1].Length);
        Assert.Equal("B", clusters[2].Text);
        Assert.Equal("😀", clusters[3].Text);
        Assert.True(clusters[3].IsSurrogatePair);
        Assert.Equal("C", clusters[4].Text);
    }

    [Fact]
    public void GraphemeClusters_CombiningMarks_AttachedToBase()
    {
        // e + combining acute (\u0301) = é, C + combining cedilla (\u0327) = Ç
        string text = "e\u0301C\u0327";
        var clusters = UnicodeBoundaryClassifier.GetGraphemeClusters(text);

        Assert.Equal(2, clusters.Count);
        Assert.Equal("e\u0301", clusters[0].Text);
        Assert.True(clusters[0].HasCombiningMarks);
        Assert.Equal(2, clusters[0].Length);

        Assert.Equal("C\u0327", clusters[1].Text);
        Assert.True(clusters[1].HasCombiningMarks);
        Assert.Equal(2, clusters[1].Length);
    }

    [Fact]
    public void GraphemeClusters_CrLfPair_KeptAsSingleCluster()
    {
        string text = "Line1\r\nLine2";
        var clusters = UnicodeBoundaryClassifier.GetGraphemeClusters(text);

        Assert.Contains(clusters, c => c.Text == "\r\n");
    }

    #endregion

    #region 2. Line Breaking & Space Classification (UAX #14)

    [Theory]
    [InlineData(' ', true)]
    [InlineData('\t', true)]
    [InlineData('\u3000', true)] // Ideographic space
    [InlineData('\u2002', true)] // EN space
    [InlineData('\u2003', true)] // EM space
    [InlineData('\u200B', true)] // Zero-width space
    [InlineData('\u00AD', true)] // Soft hyphen
    public void BreakableSpaces_ClassifiedAsBreakOpportunities(char c, bool expected)
    {
        Assert.Equal(expected, UnicodeBoundaryClassifier.IsBreakableWhitespace(c));
    }

    [Theory]
    [InlineData('\u00A0')] // NO-BREAK SPACE
    [InlineData('\u202F')] // NARROW NO-BREAK SPACE
    [InlineData('\u2007')] // FIGURE SPACE
    [InlineData('\u2060')] // WORD JOINER
    [InlineData('\uFEFF')] // ZERO WIDTH NO-BREAK SPACE
    public void NonBreakingSpaces_ClassifiedAsProhibitedBreak(char c)
    {
        Assert.False(UnicodeBoundaryClassifier.IsBreakableWhitespace(c));
        Assert.True(UnicodeBoundaryClassifier.IsNonBreakingSpace(c));
    }

    [Theory]
    [InlineData('\u2028')] // U+2028 LINE SEPARATOR
    [InlineData('\n')]
    [InlineData('\r')]
    [InlineData('\u2029')] // PARAGRAPH SEPARATOR
    public void MandatoryBreaks_ClassifiedCorrectly(char c)
    {
        Assert.True(UnicodeBoundaryClassifier.IsMandatoryBreak(c));
    }

    [Fact]
    public void ClassifyBoundaries_IdentifiesCorrectBoundaryKinds()
    {
        string text = "A B\u00A0C\u2028D";
        var boundaries = UnicodeBoundaryClassifier.ClassifyBoundaries(text);

        Assert.Equal(UnicodeBoundaryKind.GraphemeBoundary, boundaries[0].Kind);     // 'A'
        Assert.Equal(UnicodeBoundaryKind.WordBreakOpportunity, boundaries[1].Kind); // ' '
        Assert.Equal(UnicodeBoundaryKind.GraphemeBoundary, boundaries[2].Kind);     // 'B'
        Assert.Equal(UnicodeBoundaryKind.None, boundaries[3].Kind);                 // '\u00A0' (NBSP prohibited)
        Assert.Equal(UnicodeBoundaryKind.GraphemeBoundary, boundaries[4].Kind);     // 'C'
        Assert.Equal(UnicodeBoundaryKind.MandatoryBreak, boundaries[5].Kind);       // '\u2028'
        Assert.Equal(UnicodeBoundaryKind.GraphemeBoundary, boundaries[6].Kind);     // 'D'
    }

    [Fact]
    public void CjkIdeographs_ProvideBreakOpportunities_ExceptAroundPunctuation()
    {
        // 漢字、日本語
        string text = "漢字、日本語";
        var boundaries = UnicodeBoundaryClassifier.ClassifyBoundaries(text);

        // 漢 (ideograph) -> WordBreakOpportunity before next unless prohibited
        Assert.Equal(UnicodeBoundaryKind.WordBreakOpportunity, boundaries[0].Kind);

        // 字 followed by '、' (prohibited before punctuation) -> GraphemeBoundary, not break opportunity
        Assert.Equal(UnicodeBoundaryKind.GraphemeBoundary, boundaries[1].Kind);
    }

    #endregion

    #region 3. Script Support Catalog & Declarations

    [Theory]
    [InlineData("Hello World", false)]
    [InlineData("Привет мир", false)]
    [InlineData("Γειά σου κόσμε", false)]
    [InlineData("مرحبا بالعالم", false)] // Arabic supported via ComplexTextShaper
    [InlineData("שלום עולם", false)]     // Hebrew supported
    [InlineData("こんにちは", false)]       // Japanese supported
    [InlineData("नमस्ते", true)]        // Hindi (Devanagari - unsupported complex Indic)
    [InlineData("বাংলা", true)]          // Bengali - unsupported
    [InlineData("தமிழ்", true)]          // Tamil - unsupported
    [InlineData("สวัสดี", true)]         // Thai - unsupported dictionary-based breaking
    public void ScriptSupportStatus_DistinguishesSupportedFromUnsupported(string text, bool isUnsupported)
    {
        Assert.Equal(isUnsupported, ScriptSupportStatus.ContainsUnsupportedScript(text));
    }

    #endregion
}
