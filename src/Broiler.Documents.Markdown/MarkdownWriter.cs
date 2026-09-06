using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Broiler.Documents.Model;

namespace Broiler.Documents.Markdown;

/// <summary>Serializes the rich-text model to deterministic UTF-8 Markdown.</summary>
public static class MarkdownWriter
{
    public static DocumentWriteResult Write(
        RichTextDocument document,
        Stream destination,
        DocumentWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);
        DocumentConversionContext resources = (options ?? DocumentWriteOptions.Default).Resources;

        var diagnostics = new List<DocumentDiagnostic>();
        if (document.Tables.Count > 0)
        {
            // Markdown has a table syntax, but not one that carries spans,
            // borders, or shading - and a grid written without them would say
            // the document had less in it than it does.
            diagnostics.Add(DocumentDiagnostic.Warning(
                "markdown.table.flattened",
                "A table was written as its cell paragraphs, in row order; " +
                "Markdown output carries no grid."));
        }

        var builder = new StringBuilder();
        for (int i = 0; i < document.ParagraphCount; i++)
        {
            if (i > 0)
                builder.Append("\n\n");

            WriteParagraph(builder, document.Paragraphs[i], resources, diagnostics);
        }

        builder.Append('\n');
        byte[] bytes = Encoding.UTF8.GetBytes(builder.ToString());
        destination.Write(bytes, 0, bytes.Length);
        return new DocumentWriteResult(bytes.Length, diagnostics, DocumentWriteResult.StatusFrom(diagnostics));
    }

    public static byte[] WriteToArray(RichTextDocument document, DocumentWriteOptions? options = null)
    {
        using var stream = new MemoryStream();
        Write(document, stream, options);
        return stream.ToArray();
    }

    private static void WriteParagraph(
        StringBuilder builder,
        RichTextParagraph paragraph,
        DocumentConversionContext resources,
        List<DocumentDiagnostic> diagnostics)
    {
        ParagraphStyle style = paragraph.Style;
        if (style.IndentLevel > 0)
            builder.Append(new string(' ', Math.Max(0, style.IndentLevel - 1) * 2));

        if (style.ListKind == ListKind.Bullet)
            builder.Append("- ");
        else if (style.ListKind == ListKind.Numbered)
            builder.Append("1. ");

        // Markdown has no page, so it has no page break: there is no syntax to
        // write this one in and no fallback that would be honest, since a
        // horizontal rule is a rule and a form feed is a character in the prose.
        // What is left is to say so. Its own code rather than
        // markdown.paragraph-style, because that diagnostic names the three
        // fields it drops and a reader deciding whether to re-export somewhere
        // paginated is asking a different question than one chasing lost
        // spacing.
        if (style.PageBreakBefore)
        {
            diagnostics.Add(DocumentDiagnostic.Warning(
                "markdown.page-break",
                "Markdown has no page break; a paragraph that starts a new page was written " +
                "as an ordinary paragraph."));
        }

        if (style.Alignment != TextAlignment.Left ||
            Math.Abs(style.LineSpacing - 1f) > 0.001f ||
            Math.Abs(style.SpacingBefore) > 0.001f ||
            Math.Abs(style.SpacingAfter) > 0.001f)
        {
            diagnostics.Add(DocumentDiagnostic.Warning(
                "markdown.paragraph-style",
                "Markdown writer dropped paragraph alignment, line spacing, or spacing values."));
        }

        int offset = 0;
        foreach (StyleRun run in paragraph.Runs)
        {
            string text = paragraph.Text.Substring(offset, run.Length);
            offset += run.Length;
            builder.Append(FormatRun(text, run.Style, resources, diagnostics));
        }
    }

    private static string FormatRun(
        string text,
        InlineStyle style,
        DocumentConversionContext resources,
        List<DocumentDiagnostic> diagnostics)
    {
        if (style.Image is InlineImage image)
            return FormatImageRun(text, image, style, resources, diagnostics);

        string formatted = EscapeText(text);

        if (style.FontFamily is not null && style.FontFamily.Equals("monospace", StringComparison.OrdinalIgnoreCase))
            formatted = "`" + formatted.Replace("`", "\\`", StringComparison.Ordinal) + "`";
        else if (style.FontFamily is not null)
            diagnostics.Add(DocumentDiagnostic.Warning("markdown.inline-style", "Markdown writer dropped a non-monospace font family."));

        if (style.Strikethrough)
            formatted = "~~" + formatted + "~~";
        if (style.Italic)
            formatted = "*" + formatted + "*";
        if (style.Bold)
            formatted = "**" + formatted + "**";
        // The reader's rule, applied on the way out as well. Without it a
        // javascript: or data: target was written into the destination and
        // nothing said so.
        if (!string.IsNullOrEmpty(style.LinkHref))
        {
            if (DocumentLinkTarget.IsAllowed(style.LinkHref))
            {
                formatted = "[" + formatted + "](" + EscapeLinkDestination(style.LinkHref) + ")";
            }
            else
            {
                diagnostics.Add(DocumentDiagnostic.Warning(
                    "markdown.link",
                    "A hyperlink with a disallowed or relative target was written as plain text."));
            }
        }

        if (style.Underline ||
            style.FontSize.HasValue ||
            !style.Foreground.IsEmpty ||
            !style.Background.IsEmpty)
        {
            diagnostics.Add(DocumentDiagnostic.Warning(
                "markdown.inline-style",
                "Markdown writer dropped underline, size, foreground, or background styling."));
        }

        return formatted;
    }

    /// <summary>
    /// Writes an image run as CommonMark image syntax with a data URI. Markdown
    /// has no container of its own, so the bytes travel in the link destination
    /// or they do not travel at all.
    /// </summary>
    private static string FormatImageRun(
        string text,
        InlineImage image,
        InlineStyle style,
        DocumentConversionContext resources,
        List<DocumentDiagnostic> diagnostics)
    {
        if (!DocumentResourceGate.TryTakeEncodedBytes(
                image,
                resources,
                DocumentResourceOperations.ByteTransfer,
                out ReadOnlyMemory<byte> data,
                out string? contentType,
                out string? denial))
        {
            diagnostics.Add(DocumentDiagnostic.Warning(
                "markdown.image.omitted",
                "An image was left out of the Markdown output because " + denial + "."));
            return EscapeText(text.Replace(InlineImage.PlaceholderText, string.Empty, StringComparison.Ordinal));
        }

        string destination = "data:" + contentType + ";base64," + Convert.ToBase64String(data.Span);
        string alt = image.AltText.Replace("]", "\\]", StringComparison.Ordinal);
        var builder = new StringBuilder();
        foreach (char character in text)
        {
            if (character == InlineImage.Placeholder)
                builder.Append("![").Append(alt).Append("](").Append(destination).Append(')');
            else
                builder.Append(EscapeText(character.ToString()));
        }

        diagnostics.Add(DocumentDiagnostic.Info(
            "markdown.image.datauri",
            "An embedded image was written as a base64 data URI."));
        if (style.Bold || style.Italic || style.Strikethrough || style.Underline)
        {
            diagnostics.Add(DocumentDiagnostic.Warning(
                "markdown.inline-style",
                "Markdown writer dropped character formatting carried by an image run."));
        }

        return builder.ToString();
    }

    private static string EscapeText(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            switch (c)
            {
                case '\\':
                case '`':
                case '*':
                case '_':
                case '[':
                case ']':
                case '(':
                case ')':
                case '#':
                case '+':
                case '-':
                case '.':
                case '!':
                // Doubled, this is GitHub-flavored strikethrough, which this
                // codec's own reader honours - so literal tildes written out bare
                // came back as a struck run with the tildes gone. Escaped
                // singly, like every other delimiter here: a lone tilde means
                // nothing on its own, and `\~` is a literal tilde either way.
                case '~':
                    builder.Append('\\').Append(c);
                    break;
                case (char)0x2028:
                    builder.Append("  \n");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    private static string EscapeLinkDestination(string href) =>
        href.Replace(")", "%29", StringComparison.Ordinal).Replace(" ", "%20", StringComparison.Ordinal);
}
