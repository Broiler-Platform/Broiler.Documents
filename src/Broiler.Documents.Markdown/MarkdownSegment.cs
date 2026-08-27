using Broiler.Documents.Model;

namespace Broiler.Documents.Markdown;

internal readonly record struct MarkdownSegment(string Text, InlineStyle Style);
