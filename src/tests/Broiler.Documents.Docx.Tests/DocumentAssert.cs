namespace Broiler.Documents.Docx.Tests;

/// <summary>Shared assertion that two documents are structurally identical.</summary>
internal static class DocumentAssert
{
    private static ParagraphStyle Normalized(ParagraphStyle style) => style with
    {
        List = null, LeftIndent = style.LeftIndent ?? (style.ListKind != ListKind.None ? (style.IndentLevel + 1) * 18 : (float)style.LeftIndentIn()),
        RightIndent = (float)style.RightIndentIn,
        FirstLineIndent = style.FirstLineIndent ?? (style.ListKind != ListKind.None ? -18 : 0),
    };
    /// <remarks>
    /// A list is compared by the labels its paragraphs draw rather than by the
    /// definition instance, which a round trip cannot keep: a list read back is
    /// a new list, and a paragraph that named no list reads back in the one the
    /// writer named for it.
    /// </remarks>
    public static void Equivalent(RichTextDocument expected, RichTextDocument actual)
    {
        Assert.Equal(expected.ParagraphCount, actual.ParagraphCount);
        var expectedLabels = new ListNumberer();
        var actualLabels = new ListNumberer();
        for (int i = 0; i < expected.ParagraphCount; i++)
        {
            RichTextParagraph pe = expected.Paragraphs[i];
            RichTextParagraph pa = actual.Paragraphs[i];
            Assert.Equal(pe.Text, pa.Text);
            Assert.Equal(Normalized(pe.Style), Normalized(pa.Style));
            Assert.Equal(expectedLabels.Advance(pe.Style), actualLabels.Advance(pa.Style));
            Assert.Equal(pe.Runs.Count, pa.Runs.Count);
            for (int j = 0; j < pe.Runs.Count; j++)
            {
                Assert.Equal(pe.Runs[j].Length, pa.Runs[j].Length);
                Assert.Equal(pe.Runs[j].Style, pa.Runs[j].Style);
            }
        }
    }
}
