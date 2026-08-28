using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Broiler.Documents.Cli.Infrastructure;
using Broiler.Documents.Model;

namespace Broiler.Documents.Cli.Documents;

/// <summary>
/// The document manipulation language: one line per edit, applied in order.
/// </summary>
/// <remarks>
/// <para>
/// The grammar is <c>verb:field:field:...</c>, split on colons that are not
/// escaped with a backslash. Verbs whose last field is free text take the whole
/// remainder of the line, colons included, so <c>append:12:30 - start</c> appends
/// the text you would expect rather than demanding escapes for a common case.
/// </para>
/// <para>
/// Edits work on the paragraph list and rebuild the document, rather than
/// driving the model's position-based editor. That is deliberate: positions are
/// opaque by contract (ADR 0014) and cannot be constructed from the integer
/// offsets a script has to speak in, while <c>RichTextParagraph</c> exposes the
/// offset-addressed operations publicly. The paragraph and character offsets a
/// script uses are therefore exactly the ones <c>dump --as json</c> reports.
/// </para>
/// <para>
/// Every edit is checked against the document it is about to change, and an
/// out-of-range index is an error rather than a clamp. A script that silently
/// styled paragraph 9 because paragraph 12 did not exist would report a success
/// for work it did not do.
/// </para>
/// </remarks>
public static class EditOperations
{
    /// <summary>Human-readable grammar, printed by <c>edit --help</c>.</summary>
    public static IReadOnlyList<string> GrammarHelp { get; } = new[]
    {
        "  append:TEXT                        Add a paragraph at the end.",
        "  insert:P:TEXT                      Insert a paragraph before paragraph P.",
        "  text:P:TEXT                        Replace paragraph P's text, keeping its paragraph style.",
        "  delete:PARAGRAPHS                  Delete paragraphs.",
        "  merge:P                            Join paragraph P with the one after it.",
        "  split:P:OFFSET                     Split paragraph P at a character offset.",
        "  replace:SEARCH:REPLACEMENT         Replace literal text everywhere, keeping the style at each hit.",
        "  inline:PARAGRAPHS:CHARS:PROPS      Apply inline formatting to a character range.",
        "  clear:PARAGRAPHS:CHARS             Reset inline formatting on a character range.",
        "  para:PARAGRAPHS:PROPS              Apply paragraph formatting.",
        "  image:P:OFFSET:PROPS               Insert an image file at a character offset.",
        "",
        "  PARAGRAPHS  3, 2-5, 2-$, * (all), $ (last).",
        "  CHARS       0-5, 3-$, * (whole paragraph). Offsets are UTF-16 indices into the",
        "              paragraph text, the same ones dump --as json reports.",
        "  PROPS       comma-separated key=value. Quote a value containing a comma.",
        "",
        "  inline keys  bold, italic, underline, strike (on|off); caps (none|all|small);",
        "               color, highlight (#RRGGBB, #RRGGBBAA, CSS name, or default);",
        "               font (family name or default); size (points or default);",
        "               link (URL or off).",
        "  para keys    align (left|center|right); list (none|bullet|numbered);",
        "               indent (level); linespacing (multiplier); before, after (points).",
        "  image keys   file (path, required); width, height (points, default the encoded",
        "               pixel size read as CSS pixels); alt; name. This field runs to the",
        "               end of the line and is taken literally, so a path keeps its drive",
        "               colon and its backslashes.",
        "",
        "  Escapes      \\n newline, \\t tab, \\r return, \\: literal colon, \\\\ literal backslash.",
        "               A backslash before anything else keeps both characters. The image",
        "               props field is exempt entirely: it is taken exactly as written.",
    };

    /// <summary>Reads operations from <c>--op</c> values and <c>--script</c> files, in that order.</summary>
    public static IReadOnlyList<string> Collect(CommandLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var operations = new List<string>(line.GetAll("op"));

        foreach (string path in line.GetAll("script"))
        {
            if (!File.Exists(path))
                throw new DocumentIoException(ExitCode.Input, "Script not found: " + path);

            foreach (string scriptLine in File.ReadAllLines(path))
            {
                string trimmed = scriptLine.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                    continue;
                operations.Add(trimmed);
            }
        }

        return operations;
    }

    /// <summary>Applies every operation in order and returns the resulting document.</summary>
    public static RichTextDocument Apply(RichTextDocument document, IEnumerable<string> operations)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(operations);

        var paragraphs = document.Paragraphs.ToList();
        int index = 0;
        foreach (string operation in operations)
        {
            try
            {
                ApplyOne(paragraphs, operation);
            }
            catch (UsageException exception)
            {
                throw new UsageException(
                    "Operation " + (index + 1) + " (\"" + operation + "\"): " + exception.Message);
            }

            index++;
        }

        return RichTextDocument.FromParagraphs(paragraphs);
    }

    private static void ApplyOne(List<RichTextParagraph> paragraphs, string operation)
    {
        string[] fields = SplitFields(operation);
        string verb = fields[0].Trim().ToLowerInvariant();

        switch (verb)
        {
            case "append":
                AppendParagraphs(paragraphs, TailFrom(operation, 1));
                return;

            case "insert":
                InsertParagraphs(
                    paragraphs,
                    ParagraphIndex(paragraphs, Field(fields, 1, "P"), allowEnd: true),
                    TailFrom(operation, 2));
                return;

            case "text":
                SetText(paragraphs, ParagraphIndex(paragraphs, Field(fields, 1, "P")), TailFrom(operation, 2));
                return;

            case "delete":
                DeleteParagraphs(paragraphs, ParagraphRange(paragraphs, Field(fields, 1, "PARAGRAPHS")));
                return;

            case "merge":
                Merge(paragraphs, ParagraphIndex(paragraphs, Field(fields, 1, "P")));
                return;

            case "split":
                Split(paragraphs, ParagraphIndex(paragraphs, Field(fields, 1, "P")), Field(fields, 2, "OFFSET"));
                return;

            case "replace":
                Replace(paragraphs, Unescape(Field(fields, 1, "SEARCH")), Unescape(TailFrom(operation, 2)));
                return;

            case "inline":
                ApplyInline(
                    paragraphs,
                    ParagraphRange(paragraphs, Field(fields, 1, "PARAGRAPHS")),
                    Field(fields, 2, "CHARS"),
                    ParseInlineDelta(Field(fields, 3, "PROPS")));
                return;

            case "clear":
                ApplyInline(
                    paragraphs,
                    ParagraphRange(paragraphs, Field(fields, 1, "PARAGRAPHS")),
                    Field(fields, 2, "CHARS"),
                    InlineStyleDelta.Clear);
                return;

            case "image":
                InsertImage(
                    paragraphs,
                    ParagraphIndex(paragraphs, Field(fields, 1, "P")),
                    Field(fields, 2, "OFFSET"),
                    TailFrom(operation, 3, unescape: false));
                return;

            case "para":
                ApplyParagraph(
                    paragraphs,
                    ParagraphRange(paragraphs, Field(fields, 1, "PARAGRAPHS")),
                    ParseParagraphDelta(Field(fields, 2, "PROPS")));
                return;

            default:
                throw new UsageException(
                    "Unknown operation \"" + fields[0] + "\". Known operations: append, insert, text, " +
                    "delete, merge, split, replace, inline, clear, para, image.");
        }
    }

    private static void AppendParagraphs(List<RichTextParagraph> paragraphs, string text)
    {
        foreach (string line in SplitLines(text))
            paragraphs.Add(RichTextParagraph.Plain(line));
    }

    private static void InsertParagraphs(List<RichTextParagraph> paragraphs, int index, string text)
    {
        string[] lines = SplitLines(text);
        for (int i = 0; i < lines.Length; i++)
            paragraphs.Insert(index + i, RichTextParagraph.Plain(lines[i]));
    }

    private static void SetText(List<RichTextParagraph> paragraphs, int index, string text)
    {
        // The paragraph's own style survives; its inline runs do not, because the
        // new text has no relationship to the old offsets they described.
        ParagraphStyle style = paragraphs[index].Style;
        string[] lines = SplitLines(text);

        paragraphs[index] = RichTextParagraph.Create(lines[0], InlineStyle.Default, style);
        for (int i = 1; i < lines.Length; i++)
            paragraphs.Insert(index + i, RichTextParagraph.Create(lines[i], InlineStyle.Default, style));
    }

    private static void DeleteParagraphs(List<RichTextParagraph> paragraphs, (int Start, int End) range)
    {
        paragraphs.RemoveRange(range.Start, range.End - range.Start + 1);

        // The model guarantees at least one paragraph; deleting the last one
        // leaves an empty document rather than an invalid one.
        if (paragraphs.Count == 0)
            paragraphs.Add(RichTextParagraph.Empty);
    }

    private static void Merge(List<RichTextParagraph> paragraphs, int index)
    {
        if (index + 1 >= paragraphs.Count)
            throw new UsageException("Paragraph " + index + " is the last one and has nothing to merge with.");

        paragraphs[index] = paragraphs[index].Append(paragraphs[index + 1]);
        paragraphs.RemoveAt(index + 1);
    }

    private static void Split(List<RichTextParagraph> paragraphs, int index, string offsetToken)
    {
        int offset = CharacterOffset(paragraphs[index], offsetToken);
        (RichTextParagraph head, RichTextParagraph tail) = paragraphs[index].SplitAt(offset);
        paragraphs[index] = head;
        paragraphs.Insert(index + 1, tail);
    }

    private static void Replace(List<RichTextParagraph> paragraphs, string search, string replacement)
    {
        if (search.Length == 0)
            throw new UsageException("replace needs a non-empty search string.");

        for (int i = 0; i < paragraphs.Count; i++)
        {
            RichTextParagraph paragraph = paragraphs[i];
            int from = 0;

            while (true)
            {
                int hit = paragraph.Text.IndexOf(search, from, StringComparison.Ordinal);
                if (hit < 0)
                    break;

                // Take the style from the first character being replaced, so
                // replacing a word inside a bold run stays bold.
                InlineStyle style = paragraph.StyleAt(hit);
                paragraph = paragraph.RemoveRange(hit, search.Length);
                if (replacement.Length > 0)
                    paragraph = paragraph.InsertText(hit, replacement, style);

                // Resume after the replacement, so replacing "a" with "aa"
                // terminates instead of matching what it just wrote.
                from = hit + replacement.Length;
                if (from > paragraph.Length)
                    break;
            }

            paragraphs[i] = paragraph;
        }
    }

    private static void ApplyInline(
        List<RichTextParagraph> paragraphs,
        (int Start, int End) range,
        string charRange,
        InlineStyleDelta delta)
    {
        for (int i = range.Start; i <= range.End; i++)
        {
            (int start, int length) = CharacterRange(paragraphs[i], charRange);
            if (length <= 0)
                continue;
            paragraphs[i] = paragraphs[i].ApplyInlineStyle(start, length, delta);
        }
    }

    /// <summary>
    /// Inserts an image as the one placeholder character the model represents a
    /// picture with, carrying the encoded bytes on the run's style.
    /// </summary>
    /// <remarks>
    /// The bytes are read here and never re-read, so a later edit to the file on
    /// disk cannot change the document that was built from it. Leaving the size
    /// unstated is meaningful rather than lazy: <c>HasExplicitSize</c> is false,
    /// and a writer that has to state a size falls back to the encoded pixel
    /// size the same way a renderer does.
    /// </remarks>
    private static void InsertImage(
        List<RichTextParagraph> paragraphs,
        int index,
        string offsetToken,
        string properties)
    {
        string? file = null;
        double width = 0;
        double height = 0;
        string? alt = null;
        string? name = null;

        foreach ((string key, string value) in ParseProperties(properties))
        {
            switch (key)
            {
                case "file":
                case "path": file = value; break;
                case "width": width = Points(key, value); break;
                case "height": height = Points(key, value); break;
                case "alt":
                case "alttext": alt = value; break;
                case "name": name = value; break;
                default:
                    throw new UsageException(
                        "Unknown image property \"" + key + "\". Known: file, width, height, alt, name.");
            }
        }

        if (string.IsNullOrEmpty(file))
            throw new UsageException("image needs file=<path>.");
        if (!File.Exists(file))
            throw new DocumentIoException(ExitCode.Input, "Image not found: " + file);
        if (width < 0 || height < 0)
            throw new UsageException("image width and height cannot be negative.");

        // Both or neither: the model treats a zero in either as "no stated size",
        // so accepting one alone would silently discard it.
        if ((width > 0) != (height > 0))
            throw new UsageException("image width and height must be given together, or neither.");

        byte[] bytes = File.ReadAllBytes(file);
        var image = new InlineImage(
            bytes,
            ContentTypeFor(file, bytes),
            width,
            height,
            alt,
            name ?? Path.GetFileNameWithoutExtension(file));

        RichTextParagraph paragraph = paragraphs[index];
        int offset = CharacterOffset(paragraph, offsetToken);
        InlineStyle style = paragraph.StyleBefore(offset) with { Image = image };

        paragraphs[index] = paragraph.InsertText(offset, InlineImage.PlaceholderText, style);
    }

    /// <summary>
    /// The image's media type, read from its bytes and falling back to its file
    /// extension.
    /// </summary>
    /// <remarks>
    /// Signature first, because the extension is a claim and the bytes are the
    /// fact: a PNG named <c>.jpg</c> would otherwise be written into a DOCX part
    /// declaring a content type nothing can decode.
    /// </remarks>
    private static string ContentTypeFor(string path, byte[] bytes)
    {
        ReadOnlySpan<byte> span = bytes;

        if (span.Length >= 8 && span[0] == 0x89 && span[1] == 0x50 && span[2] == 0x4E && span[3] == 0x47)
            return "image/png";
        if (span.Length >= 3 && span[0] == 0xFF && span[1] == 0xD8 && span[2] == 0xFF)
            return "image/jpeg";
        if (span.Length >= 6 && span[0] == 0x47 && span[1] == 0x49 && span[2] == 0x46)
            return "image/gif";
        if (span.Length >= 2 && span[0] == 0x42 && span[1] == 0x4D)
            return "image/bmp";
        if (span.Length >= 12 &&
            span[0] == 0x52 && span[1] == 0x49 && span[2] == 0x46 && span[3] == 0x46 &&
            span[8] == 0x57 && span[9] == 0x45 && span[10] == 0x42 && span[11] == 0x50)
        {
            return "image/webp";
        }

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".tif" or ".tiff" => "image/tiff",
            ".svg" => "image/svg+xml",
            _ => throw new UsageException(
                "Cannot tell what kind of image \"" + path + "\" is: its bytes match no known " +
                "signature and its extension is not one this tool recognizes."),
        };
    }

    private static void ApplyParagraph(
        List<RichTextParagraph> paragraphs,
        (int Start, int End) range,
        ParagraphStyleDelta delta)
    {
        for (int i = range.Start; i <= range.End; i++)
            paragraphs[i] = paragraphs[i].WithParagraphStyle(delta.Apply(paragraphs[i].Style));
    }

    private static InlineStyleDelta ParseInlineDelta(string properties)
    {
        var delta = new InlineStyleDelta();

        foreach ((string key, string value) in ParseProperties(properties))
        {
            switch (key)
            {
                case "bold": delta = delta with { Bold = Switch(key, value) }; break;
                case "italic": delta = delta with { Italic = Switch(key, value) }; break;
                case "underline": delta = delta with { Underline = Switch(key, value) }; break;
                case "strike":
                case "strikethrough": delta = delta with { Strikethrough = Switch(key, value) }; break;
                case "caps":
                case "capitalization": delta = delta with { Capitalization = Capitalization(value) }; break;
                case "color":
                case "foreground": delta = delta with { Foreground = ColorText.Parse(value, "color") }; break;
                case "highlight":
                case "background": delta = delta with { Background = ColorText.Parse(value, "highlight") }; break;
                case "font":
                case "fontfamily":
                    delta = delta with
                    {
                        SetFontFamily = true,
                        FontFamily = IsDefault(value) ? null : value,
                    };
                    break;
                case "size":
                case "fontsize":
                    delta = delta with
                    {
                        SetFontSize = true,
                        FontSize = IsDefault(value) ? null : Points(key, value),
                    };
                    break;
                case "link":
                    delta = delta with
                    {
                        SetLink = true,
                        LinkHref = IsOff(value) || IsDefault(value) ? null : value,
                    };
                    break;
                default:
                    throw new UsageException(
                        "Unknown inline property \"" + key + "\". Known: bold, italic, underline, strike, " +
                        "caps, color, highlight, font, size, link.");
            }
        }

        return delta;
    }

    private static ParagraphStyleDelta ParseParagraphDelta(string properties)
    {
        var delta = new ParagraphStyleDelta();

        foreach ((string key, string value) in ParseProperties(properties))
        {
            switch (key)
            {
                case "align":
                case "alignment": delta = delta with { Alignment = Alignment(value) }; break;
                case "list":
                case "listkind": delta = delta with { ListKind = List(value) }; break;
                case "indent":
                case "indentlevel": delta = delta with { IndentLevel = NonNegativeInt(key, value) }; break;
                case "linespacing": delta = delta with { LineSpacing = PositivePoints(key, value) }; break;
                case "before":
                case "spacingbefore": delta = delta with { SpacingBefore = Points(key, value) }; break;
                case "after":
                case "spacingafter": delta = delta with { SpacingAfter = Points(key, value) }; break;
                default:
                    throw new UsageException(
                        "Unknown paragraph property \"" + key + "\". Known: align, list, indent, " +
                        "linespacing, before, after.");
            }
        }

        return delta;
    }

    private static IEnumerable<(string Key, string Value)> ParseProperties(string properties)
    {
        foreach (string entry in SplitProperties(properties))
        {
            int separator = entry.IndexOf('=');
            if (separator <= 0)
                throw new UsageException("Property \"" + entry + "\" is not key=value.");

            string key = entry[..separator].Trim().ToLowerInvariant();
            string value = Unquote(entry[(separator + 1)..].Trim());
            yield return (key, value);
        }
    }

    /// <summary>Splits on commas that are not inside double quotes.</summary>
    private static IEnumerable<string> SplitProperties(string properties)
    {
        var current = new StringBuilder();
        bool quoted = false;

        foreach (char character in properties)
        {
            if (character == '"')
            {
                quoted = !quoted;
                current.Append(character);
            }
            else if (character == ',' && !quoted)
            {
                if (current.Length > 0)
                    yield return current.ToString();
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        if (current.Length > 0)
            yield return current.ToString();
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    private static bool IsDefault(string value) =>
        string.Equals(value, "default", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "inherit", StringComparison.OrdinalIgnoreCase);

    private static bool IsOff(string value) =>
        string.Equals(value, "off", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "none", StringComparison.OrdinalIgnoreCase);

    private static bool Switch(string key, string value) => value.ToLowerInvariant() switch
    {
        "on" or "true" or "yes" or "1" => true,
        "off" or "false" or "no" or "0" => false,
        _ => throw new UsageException(key + " expects on or off, not \"" + value + "\"."),
    };

    private static TextCapitalization Capitalization(string value) => value.ToLowerInvariant() switch
    {
        "none" or "off" => TextCapitalization.None,
        "all" or "allcaps" or "upper" => TextCapitalization.AllCaps,
        "small" or "smallcaps" => TextCapitalization.SmallCaps,
        _ => throw new UsageException("caps expects none, all, or small, not \"" + value + "\"."),
    };

    private static TextAlignment Alignment(string value) => value.ToLowerInvariant() switch
    {
        "left" or "start" => TextAlignment.Left,
        "center" or "centre" => TextAlignment.Center,
        "right" or "end" => TextAlignment.Right,
        _ => throw new UsageException("align expects left, center, or right, not \"" + value + "\"."),
    };

    private static ListKind List(string value) => value.ToLowerInvariant() switch
    {
        "none" or "off" => ListKind.None,
        "bullet" or "unordered" => ListKind.Bullet,
        "numbered" or "ordered" or "number" => ListKind.Numbered,
        _ => throw new UsageException("list expects none, bullet, or numbered, not \"" + value + "\"."),
    };

    private static float Points(string key, string value)
    {
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ||
            float.IsNaN(parsed) || float.IsInfinity(parsed))
        {
            throw new UsageException(key + " expects a finite number of points, not \"" + value + "\".");
        }

        return parsed;
    }

    private static float PositivePoints(string key, string value)
    {
        float parsed = Points(key, value);
        if (parsed <= 0)
            throw new UsageException(key + " must be greater than zero.");
        return parsed;
    }

    private static int NonNegativeInt(string key, string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < 0)
            throw new UsageException(key + " expects a whole number of zero or more, not \"" + value + "\".");
        return parsed;
    }

    private static int ParagraphIndex(List<RichTextParagraph> paragraphs, string token, bool allowEnd = false)
    {
        int limit = allowEnd ? paragraphs.Count : paragraphs.Count - 1;

        if (token == "$")
            return Math.Max(0, paragraphs.Count - 1);
        if (allowEnd && (token == "end" || token == "+"))
            return paragraphs.Count;

        if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            throw new UsageException("Expected a paragraph index, not \"" + token + "\".");

        if (index < 0 || index > limit)
        {
            throw new UsageException(
                "Paragraph " + index + " is out of range; the document has " + paragraphs.Count + " paragraph(s).");
        }

        return index;
    }

    private static (int Start, int End) ParagraphRange(List<RichTextParagraph> paragraphs, string token)
    {
        token = token.Trim();
        if (token is "*" or "all")
            return (0, paragraphs.Count - 1);

        int dash = token.IndexOf('-', 1);
        if (dash < 0)
        {
            int single = ParagraphIndex(paragraphs, token);
            return (single, single);
        }

        int start = ParagraphIndex(paragraphs, token[..dash]);
        int end = ParagraphIndex(paragraphs, token[(dash + 1)..]);
        if (end < start)
            throw new UsageException("Paragraph range \"" + token + "\" ends before it starts.");

        return (start, end);
    }

    private static (int Start, int Length) CharacterRange(RichTextParagraph paragraph, string token)
    {
        token = token.Trim();
        if (token is "*" or "all")
            return (0, paragraph.Length);

        int dash = token.IndexOf('-', 1);
        if (dash < 0)
        {
            int only = CharacterOffset(paragraph, token);
            return (only, Math.Min(1, paragraph.Length - only));
        }

        int start = CharacterOffset(paragraph, token[..dash]);
        int end = CharacterOffset(paragraph, token[(dash + 1)..]);
        if (end < start)
            throw new UsageException("Character range \"" + token + "\" ends before it starts.");

        return (start, end - start);
    }

    private static int CharacterOffset(RichTextParagraph paragraph, string token)
    {
        token = token.Trim();
        if (token == "$" || token.Length == 0)
            return paragraph.Length;

        if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int offset))
            throw new UsageException("Expected a character offset, not \"" + token + "\".");

        if (offset < 0 || offset > paragraph.Length)
        {
            throw new UsageException(
                "Character offset " + offset + " is out of range; the paragraph has " +
                paragraph.Length + " character(s).");
        }

        return offset;
    }

    private static string Field(string[] fields, int index, string name)
    {
        if (index >= fields.Length)
            throw new UsageException("Missing " + name + " field.");
        return fields[index];
    }

    /// <summary>
    /// The rest of the operation from field <paramref name="index"/> onwards,
    /// colons and all. This is what makes <c>append:see 3:14 below</c> mean what
    /// it looks like.
    /// </summary>
    /// <remarks>
    /// <paramref name="unescape"/> is off for the <c>image</c> verb, whose tail
    /// carries a file path. Escapes and paths do not mix: on Windows a directory
    /// called <c>temp</c> or <c>new</c> would turn into a tab or a newline, and
    /// the failure would be a file-not-found error naming a path the caller never
    /// typed. That field needs no escapes - its parser splits on commas and equals
    /// signs, and a value containing a comma can be quoted.
    /// </remarks>
    private static string TailFrom(string operation, int index, bool unescape = true)
    {
        int position = 0;
        for (int field = 0; field < index; field++)
        {
            position = IndexOfUnescapedColon(operation, position);
            if (position < 0)
                throw new UsageException("Missing text after field " + field + ".");
            position++;
        }

        string tail = operation[position..];
        return unescape ? Unescape(tail) : tail;
    }

    /// <summary>Splits an operation into its colon-separated fields, honouring <c>\:</c>.</summary>
    private static string[] SplitFields(string operation)
    {
        var fields = new List<string>();
        int start = 0;

        while (true)
        {
            int colon = IndexOfUnescapedColon(operation, start);
            if (colon < 0)
            {
                fields.Add(operation[start..]);
                break;
            }

            fields.Add(operation[start..colon]);
            start = colon + 1;
        }

        return fields.ToArray();
    }

    private static int IndexOfUnescapedColon(string value, int start)
    {
        for (int i = start; i < value.Length; i++)
        {
            if (value[i] == '\\')
            {
                i++;
                continue;
            }

            if (value[i] == ':')
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Resolves the escapes an operation may contain.
    /// </summary>
    /// <remarks>
    /// A backslash before anything that is not an escape keeps <em>both</em>
    /// characters. That is not pedantry: a Windows path is an ordinary thing to
    /// write in an <c>image</c> operation, and a rule that swallowed every
    /// backslash would silently turn it into a path that does not exist. The
    /// escapes that do mean something are the short list below.
    /// </remarks>
    private static string Unescape(string value)
    {
        if (value.IndexOf('\\') < 0)
            return value;

        var builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                builder.Append(value[i]);
                continue;
            }

            char next = value[++i];
            switch (next)
            {
                case 'n': builder.Append('\n'); break;
                case 't': builder.Append('\t'); break;
                case 'r': builder.Append('\r'); break;
                case ':': builder.Append(':'); break;
                case '\\': builder.Append('\\'); break;
                default:
                    builder.Append('\\');
                    builder.Append(next);
                    break;
            }
        }

        return builder.ToString();
    }

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
}
