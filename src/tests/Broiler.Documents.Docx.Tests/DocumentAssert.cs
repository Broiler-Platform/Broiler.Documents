namespace Broiler.Documents.Docx.Tests;

/// <summary>Shared assertion that two documents are structurally identical.</summary>
internal static class DocumentAssert
{
    public static void Equivalent(RichTextDocument expected, RichTextDocument actual)
    {
        Assert.Equal(expected.ParagraphCount, actual.ParagraphCount);
        for (int i = 0; i < expected.ParagraphCount; i++)
        {
            RichTextParagraph pe = expected.Paragraphs[i];
            RichTextParagraph pa = actual.Paragraphs[i];
            Assert.Equal(pe.Text, pa.Text);
            Assert.Equal(pe.Style, pa.Style);
            Assert.Equal(pe.Runs.Count, pa.Runs.Count);
            for (int j = 0; j < pe.Runs.Count; j++)
            {
                Assert.Equal(pe.Runs[j].Length, pa.Runs[j].Length);
                Assert.Equal(pe.Runs[j].Style, pa.Runs[j].Style);
            }
        }
    }
}
