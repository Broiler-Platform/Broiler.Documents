namespace Broiler.Documents.Markdown.Tests;

internal static class DocumentAssert
{
    public static void Equivalent(RichTextDocument expected, RichTextDocument actual)
    {
        Assert.Equal(expected.ParagraphCount, actual.ParagraphCount);
        for (int i = 0; i < expected.ParagraphCount; i++)
        {
            RichTextParagraph expectedParagraph = expected.Paragraphs[i];
            RichTextParagraph actualParagraph = actual.Paragraphs[i];
            Assert.Equal(expectedParagraph.Text, actualParagraph.Text);
            Assert.Equal(expectedParagraph.Style, actualParagraph.Style);
            Assert.Equal(expectedParagraph.Runs.Count, actualParagraph.Runs.Count);
            for (int j = 0; j < expectedParagraph.Runs.Count; j++)
            {
                Assert.Equal(expectedParagraph.Runs[j].Length, actualParagraph.Runs[j].Length);
                Assert.Equal(expectedParagraph.Runs[j].Style, actualParagraph.Runs[j].Style);
            }
        }
    }
}
