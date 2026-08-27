using System;
using System.Collections.Generic;
using System.Text;
using Broiler.Documents.Model;

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
        var builder = new MarkdownDocumentBuilder(options.Limits, diagnostics);

        var paragraph = new List<string>();
        ParagraphStyle paragraphStyle = ParagraphStyle.Default;
        bool inFence = false;
        string fence = string.Empty;
        var codeLines = new List<string>();

        for (int index = 0; index < lines.Length; index++)
        {
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
                FlushParagraph(builder, paragraph, paragraphStyle);
                inFence = true;
                fence = trimmed.StartsWith("```", StringComparison.Ordinal) ? "```" : "~~~";
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph(builder, paragraph, paragraphStyle);
                paragraphStyle = ParagraphStyle.Default;
                continue;
            }

            if (TryParseHeading(trimmed, out int level, out string headingText))
            {
                FlushParagraph(builder, paragraph, paragraphStyle);
                builder.AddInlineParagraph(
                    MarkdownInlineParser.Parse(headingText, HeadingStyle(level), diagnostics),
                    ParagraphStyle.Default);
                paragraphStyle = ParagraphStyle.Default;
                continue;
            }

            string blockquoteLine = UnwrapBlockquote(line, out int quoteDepth);
            if (quoteDepth > 0)
            {
                FlushParagraph(builder, paragraph, paragraphStyle);
                paragraphStyle = ParagraphStyle.Default with { IndentLevel = quoteDepth };
                paragraph.Add(blockquoteLine.Trim());
                continue;
            }

            if (TryParseListItem(line, out ListKind kind, out int indent, out string itemText))
            {
                FlushParagraph(builder, paragraph, paragraphStyle);
                builder.AddInlineParagraph(
                    MarkdownInlineParser.Parse(itemText, InlineStyle.Default, diagnostics),
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

        FlushParagraph(builder, paragraph, paragraphStyle);
        return new DocumentReadResult(builder.Build(), diagnostics, DocumentReadResult.StatusFrom(diagnostics));
    }

    private static void FlushParagraph(
        MarkdownDocumentBuilder builder,
        List<string> paragraph,
        ParagraphStyle style)
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
            MarkdownInlineParser.Parse(text.ToString(), InlineStyle.Default, builder.Diagnostics),
            style);
        paragraph.Clear();
    }

    private static bool IsHardBreakLine(string line) =>
        line.EndsWith("  ", StringComparison.Ordinal) || line.EndsWith("\\", StringComparison.Ordinal);

    private static string TrimHardBreakMarker(string line)
    {
        line = line.TrimEnd();
        if (line.EndsWith("\\", StringComparison.Ordinal))
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
        while (current.StartsWith(">", StringComparison.Ordinal))
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
