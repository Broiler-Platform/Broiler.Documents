using System.Collections.Generic;
using Broiler.Documents.Model;

namespace Broiler.Documents.Markdown;

internal sealed class MarkdownDocumentBuilder
{
    private readonly DocumentLimits _limits;
    private readonly List<RichTextParagraph> _paragraphs = [];
    private readonly HashSet<string> _diagnosticOnce = new(System.StringComparer.Ordinal);

    public MarkdownDocumentBuilder(DocumentLimits limits, List<DocumentDiagnostic> diagnostics)
    {
        _limits = limits;
        Diagnostics = diagnostics;
    }

    public List<DocumentDiagnostic> Diagnostics { get; }

    public void AddParagraph(string text, InlineStyle style, ParagraphStyle paragraphStyle) =>
        AddInlineParagraph([new MarkdownSegment(text, style)], paragraphStyle);

    public void AddInlineParagraph(IReadOnlyList<MarkdownSegment> segments, ParagraphStyle paragraphStyle)
    {
        if (_paragraphs.Count >= _limits.MaxParagraphCount)
        {
            AddDiagnosticOnce("markdown.limit.paragraphs", "Markdown input exceeded MaxParagraphCount; remaining paragraphs were dropped.");
            return;
        }

        RichTextParagraph paragraph = RichTextParagraph.Empty.WithParagraphStyle(paragraphStyle);
        int offset = 0;
        foreach (MarkdownSegment segment in segments)
        {
            string text = segment.Text;
            if (text.Length > _limits.MaxRunLength)
            {
                text = text[.._limits.MaxRunLength];
                AddDiagnosticOnce("markdown.limit.run", "A Markdown text run exceeded MaxRunLength and was truncated.");
            }

            paragraph = paragraph.InsertText(offset, text, segment.Style);
            offset += text.Length;
        }

        _paragraphs.Add(paragraph);
    }

    public RichTextDocument Build() =>
        _paragraphs.Count == 0 ? RichTextDocument.Empty : RichTextDocument.FromParagraphs(_paragraphs);

    private void AddDiagnosticOnce(string code, string message)
    {
        if (_diagnosticOnce.Add(code))
            Diagnostics.Add(DocumentDiagnostic.Warning(code, message));
    }
}
