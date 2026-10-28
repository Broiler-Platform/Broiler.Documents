using System;
using System.Collections.Generic;
using System.Text;
using Broiler.Documents.Model;
using Broiler.Documents.Resources;

namespace Broiler.Documents.Markdown;

internal static class MarkdownReader
{
    public static DocumentReadResult Read(byte[] bytes, DocumentReadOptions options, bool truncated)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(options);

        var diagnostics = new List<DocumentDiagnostic>();
        if (truncated)
        {
            diagnostics.Add(DocumentDiagnostic.Error(
                "markdown.limit.bytes",
                "Markdown input exceeded MaxDocumentBytes and was truncated before parsing."));
        }

        string markdown = DecodeUtf8(bytes);
        string[] lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var resources = new DocumentConversionContextBuilder(options.ResourcePolicy);
        var builder = new MarkdownDocumentBuilder(options.Limits, diagnostics, resources);
        var footnotes = ExtractFootnotes(lines, diagnostics, options.Limits, resources, out HashSet<int> footnoteLineIndices);

        var paragraph = new List<string>();
        ParagraphStyle paragraphStyle = ParagraphStyle.Default;
        bool inFence = false;
        string fence = string.Empty;
        var codeLines = new List<string>();

        for (int index = 0; index < lines.Length; index++)
        {
            if (footnoteLineIndices.Contains(index))
            {
                FlushParagraph(builder, paragraph, paragraphStyle, options.Limits, resources, footnotes);
                paragraphStyle = ParagraphStyle.Default;
                continue;
            }

            string line = lines[index];
            string trimmed = line.TrimStart();

            if (inFence)
            {
                if (trimmed.StartsWith(fence, StringComparison.Ordinal))
                {
                    builder.AddParagraph(
                        string.Join(((char)0x2028).ToString(), codeLines),
                        InlineStyle.Default with { FontFamily = "monospace" },
                        ParagraphStyle.Default);
                    codeLines.Clear();
                    inFence = false;
                    fence = string.Empty;
                }
                else
                {
                    codeLines.Add(line);
                }

                continue;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                FlushParagraph(builder, paragraph, paragraphStyle, options.Limits, resources, footnotes);
                inFence = true;
                fence = trimmed.StartsWith("```", StringComparison.Ordinal) ? "```" : "~~~";
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph(builder, paragraph, paragraphStyle, options.Limits, resources, footnotes);
                paragraphStyle = ParagraphStyle.Default;
                continue;
            }

            if (TryParseHeading(trimmed, out int level, out string headingText))
            {
                FlushParagraph(builder, paragraph, paragraphStyle, options.Limits, resources, footnotes);
                builder.AddInlineParagraph(
                    MarkdownInlineParser.Parse(headingText, HeadingStyle(level), diagnostics, options.Limits, resources, footnotes),
                    ParagraphStyle.Default);
                paragraphStyle = ParagraphStyle.Default;
                continue;
            }

            string blockquoteLine = UnwrapBlockquote(line, out int quoteDepth);
            if (quoteDepth > 0)
            {
                FlushParagraph(builder, paragraph, paragraphStyle, options.Limits, resources, footnotes);
                paragraphStyle = ParagraphStyle.Default with { IndentLevel = quoteDepth };
                paragraph.Add(blockquoteLine.Trim());
                continue;
            }

            if (TryParseListItem(line, out ListKind kind, out int indent, out string itemText))
            {
                FlushParagraph(builder, paragraph, paragraphStyle, options.Limits, resources, footnotes);
                builder.AddInlineParagraph(
                    MarkdownInlineParser.Parse(itemText, InlineStyle.Default, diagnostics, options.Limits, resources, footnotes),
                    ParagraphStyle.Default with { ListKind = kind, IndentLevel = Math.Max(1, indent + 1) });
                paragraphStyle = ParagraphStyle.Default;
                continue;
            }

            paragraph.Add(line.TrimStart());
        }

        if (inFence)
        {
            diagnostics.Add(DocumentDiagnostic.Warning(
                "markdown.fence",
                "A fenced code block was not closed; remaining lines were imported as code."));
            builder.AddParagraph(
                string.Join(((char)0x2028).ToString(), codeLines),
                InlineStyle.Default with { FontFamily = "monospace" },
                ParagraphStyle.Default);
        }

        FlushParagraph(builder, paragraph, paragraphStyle, options.Limits, resources, footnotes);
        return new DocumentReadResult(builder.Build(), diagnostics, DocumentReadResult.StatusFrom(diagnostics), resources.Build());
    }

    private static void FlushParagraph(
        MarkdownDocumentBuilder builder,
        List<string> paragraph,
        ParagraphStyle style,
        DocumentLimits limits,
        DocumentConversionContextBuilder resources,
        IReadOnlyDictionary<string, DocumentNote>? footnotes = null)
    {
        if (paragraph.Count == 0)
            return;

        var text = new StringBuilder();
        for (int i = 0; i < paragraph.Count; i++)
        {
            if (i > 0)
                text.Append(IsHardBreakLine(paragraph[i - 1]) ? (char)0x2028 : ' ');

            string line = TrimHardBreakMarker(paragraph[i]);
            text.Append(line);
        }

        builder.AddInlineParagraph(
            MarkdownInlineParser.Parse(text.ToString(), InlineStyle.Default, builder.Diagnostics, limits, resources, footnotes),
            style);
        paragraph.Clear();
    }

    private static Dictionary<string, DocumentNote> ExtractFootnotes(
        string[] lines,
        List<DocumentDiagnostic> diagnostics,
        DocumentLimits limits,
        DocumentConversionContextBuilder resources,
        out HashSet<int> lineIndices)
    {
        var footnotes = new Dictionary<string, DocumentNote>(StringComparer.Ordinal);
        lineIndices = [];
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("[^", StringComparison.Ordinal))
            {
                int close = trimmed.IndexOf("]:", StringComparison.Ordinal);
                if (close > 2)
                {
                    string label = trimmed.Substring(2, close - 2);
                    string firstLine = trimmed.Substring(close + 2).TrimStart();
                    lineIndices.Add(i);
                    var noteParagraphs = new List<RichTextParagraph>();
                    var currentLines = new List<string>();
                    if (!string.IsNullOrEmpty(firstLine))
                        currentLines.Add(firstLine);
                    int j = i + 1;
                    while (j < lines.Length)
                    {
                        string nextLine = lines[j];
                        if (string.IsNullOrWhiteSpace(nextLine))
                        {
                            if (j + 1 < lines.Length && (lines[j + 1].StartsWith("    ", StringComparison.Ordinal) || lines[j + 1].StartsWith("\t", StringComparison.Ordinal)))
                            {
                                if (currentLines.Count > 0)
                                {
                                    noteParagraphs.Add(BuildNoteParagraph(currentLines, diagnostics, limits, resources));
                                    currentLines.Clear();
                                }
                                lineIndices.Add(j);
                                j++;
                                continue;
                            }
                            break;
                        }
                        if (nextLine.StartsWith("    ", StringComparison.Ordinal) || nextLine.StartsWith("\t", StringComparison.Ordinal))
                        {
                            lineIndices.Add(j);
                            string unindented = nextLine.StartsWith("\t", StringComparison.Ordinal) ? nextLine[1..] : nextLine[4..];
                            currentLines.Add(unindented.TrimStart());
                            j++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    if (currentLines.Count > 0)
                        noteParagraphs.Add(BuildNoteParagraph(currentLines, diagnostics, limits, resources));
                    if (noteParagraphs.Count == 0)
                        noteParagraphs.Add(RichTextParagraph.Empty);
                    footnotes[label] = new DocumentNote(NoteKind.Footnote, noteParagraphs, label);
                    i = j - 1;
                }
            }
        }
        return footnotes;
    }

    private static RichTextParagraph BuildNoteParagraph(
        List<string> lines,
        List<DocumentDiagnostic> diagnostics,
        DocumentLimits limits,
        DocumentConversionContextBuilder resources)
    {
        var text = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0)
                text.Append(IsHardBreakLine(lines[i - 1]) ? (char)0x2028 : ' ');
            text.Append(TrimHardBreakMarker(lines[i]));
        }
        var segments = MarkdownInlineParser.Parse(text.ToString(), InlineStyle.Default, diagnostics, limits, resources);
        RichTextParagraph paragraph = RichTextParagraph.Empty;
        int offset = 0;
        foreach (MarkdownSegment segment in segments)
        {
            paragraph = paragraph.InsertText(offset, segment.Text, segment.Style);
            offset += segment.Text.Length;
        }
        return paragraph;
    }

    private static bool IsHardBreakLine(string line) =>
        line.EndsWith("  ", StringComparison.Ordinal) || line.EndsWith('\\');

    private static string TrimHardBreakMarker(string line)
    {
        line = line.TrimEnd();
        if (line.EndsWith('\\'))
            line = line[..^1].TrimEnd();
        return line;
    }

    private static bool TryParseHeading(string trimmed, out int level, out string text)
    {
        level = 0;
        text = string.Empty;
        while (level < trimmed.Length && level < 6 && trimmed[level] == '#')
            level++;

        if (level == 0 || level >= trimmed.Length || trimmed[level] != ' ')
            return false;

        text = trimmed[(level + 1)..].Trim();
        if (text.EndsWith('#'))
            text = text.TrimEnd('#').TrimEnd();
        return true;
    }

    private static InlineStyle HeadingStyle(int level)
    {
        float size = level switch
        {
            1 => 24f,
            2 => 20f,
            3 => 17f,
            4 => 15f,
            5 => 13f,
            _ => 12f,
        };

        return InlineStyle.Default with { Bold = true, FontSize = size };
    }

    private static string UnwrapBlockquote(string line, out int depth)
    {
        depth = 0;
        string current = line.TrimStart();
        while (current.StartsWith('>'))
        {
            depth++;
            current = current[1..].TrimStart();
        }

        return current;
    }

    private static bool TryParseListItem(
        string line,
        out ListKind kind,
        out int indent,
        out string text)
    {
        kind = ListKind.None;
        indent = 0;
        text = string.Empty;

        int leading = line.Length - line.TrimStart(' ', '\t').Length;
        string trimmed = line.TrimStart();
        indent = leading / 2;

        if (trimmed.Length >= 2 && (trimmed[0] == '-' || trimmed[0] == '*' || trimmed[0] == '+') && trimmed[1] == ' ')
        {
            kind = ListKind.Bullet;
            text = trimmed[2..].Trim();
            return true;
        }

        int i = 0;
        while (i < trimmed.Length && char.IsDigit(trimmed[i]))
            i++;

        if (i > 0 && i + 1 < trimmed.Length && (trimmed[i] == '.' || trimmed[i] == ')') && trimmed[i + 1] == ' ')
        {
            kind = ListKind.Numbered;
            text = trimmed[(i + 2)..].Trim();
            return true;
        }

        return false;
    }

    private static string DecodeUtf8(byte[] bytes)
    {
        string text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
            .GetString(bytes);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }
}
