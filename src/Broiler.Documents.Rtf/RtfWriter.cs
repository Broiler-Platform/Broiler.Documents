using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Broiler.Documents.Model;
using Broiler.Graphics;

namespace Broiler.Documents.Rtf;

/// <summary>
/// Serializes a <see cref="RichTextDocument"/> to portable, ASCII-safe RTF. Font
/// and color tables are built from the styles actually used; each styled run is
/// group-wrapped so formatting never leaks; non-ASCII characters are escaped as
/// <c>\uN?</c> with <c>\uc1</c> (surrogate-safe); hyperlinks are written as
/// <c>\field</c>. A <c>\par</c> is emitted after every paragraph, which round-trips
/// exactly through <see cref="RtfReader"/>'s terminator semantics.
/// </summary>
public static class RtfWriter
{
    public static DocumentWriteResult Write(
        RichTextDocument document,
        Stream destination,
        DocumentWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);

        var fonts = new ResourceTable<string>(StringComparer.Ordinal);
        var colors = new ResourceTable<BColor>(EqualityComparer<BColor>.Default);
        var diagnostics = new List<DocumentDiagnostic>();

        // The writer always escapes non-ASCII as \uN?; there is no raw-byte mode.
        // A caller asking for one is told, rather than silently given the escaped
        // form it did not ask for.
        if (options is { AsciiOnly: false })
        {
            diagnostics.Add(DocumentDiagnostic.Warning(
                DocumentDiagnosticCodes.CapabilityNotComposed,
                "This writer emits non-ASCII characters as \\uN? escapes only; raw high bytes are not implemented, so AsciiOnly=false was not honoured."));
        }

        var reported = new HashSet<string>(StringComparer.Ordinal);
        CollectResources(document, fonts, colors);

        var sb = new StringBuilder();
        sb.Append("{\\rtf1\\ansi\\ansicpg1252\\deff0\\uc1");
        WriteFontTable(sb, fonts);
        WriteColorTable(sb, colors);
        WriteRunningContent(sb, document.RunningContent, fonts, colors, diagnostics, reported);

        foreach (RichTextParagraph paragraph in document.Paragraphs)
        {
            sb.Append("\\pard\\plain");
            WriteParagraphProperties(sb, paragraph.Style);
            sb.Append(' ');
            WriteRuns(sb, paragraph, fonts, colors, diagnostics, reported);
            sb.Append("\\par\n");
        }

        sb.Append('}');

        byte[] bytes = Encoding.ASCII.GetBytes(sb.ToString());
        destination.Write(bytes, 0, bytes.Length);
        return new DocumentWriteResult(bytes.Length, diagnostics, DocumentWriteResult.StatusFrom(diagnostics));
    }

    /// <summary>Serialize to a byte array (convenience over the stream overload).</summary>
    public static byte[] WriteToArray(RichTextDocument document, DocumentWriteOptions? options = null)
    {
        using var stream = new MemoryStream();
        Write(document, stream, options);
        return stream.ToArray();
    }

    private static void CollectResources(
        RichTextDocument document,
        ResourceTable<string> fonts,
        ResourceTable<BColor> colors)
    {
        foreach (RichTextParagraph paragraph in document.Paragraphs)
        {
            foreach (StyleRun run in paragraph.Runs)
            {
                InlineStyle style = run.Style;
                if (style.FontFamily is not null)
                    fonts.Intern(style.FontFamily);
                if (!style.Foreground.IsEmpty)
                    colors.Intern(style.Foreground);
                if (!style.Background.IsEmpty)
                    colors.Intern(style.Background);
            }
        }
    }

    /// <summary>
    /// Writes the header and footer destinations, before the body.
    /// </summary>
    /// <remarks>
    /// RTF puts them in the section they belong to, and a reader takes them as
    /// section properties, so they precede the first paragraph. \\headerl is the
    /// left - even - page and \\headerf the first; a document that wants one header
    /// everywhere writes \\header alone.
    /// </remarks>
    private static void WriteRunningContent(
        StringBuilder sb,
        RunningContent running,
        ResourceTable<string> fonts,
        ResourceTable<BColor> colors,
        List<DocumentDiagnostic> diagnostics,
        HashSet<string> reported)
    {
        if (running is null || running.IsEmpty)
            return;

        foreach ((string word, bool isHeader, PageSelection selection) in RunningDestinations)
        {
            IReadOnlyList<RichTextParagraph> paragraphs =
                isHeader ? running.Header(selection) : running.Footer(selection);
            if (paragraphs.Count == 0)
                continue;

            sb.Append('{').Append('\\').Append(word);
            foreach (RichTextParagraph paragraph in paragraphs)
            {
                sb.Append("\\pard\\plain");
                WriteParagraphProperties(sb, paragraph.Style);
                sb.Append(' ');
                WriteRuns(sb, paragraph, fonts, colors, diagnostics, reported);
                sb.Append("\\par");
            }

            sb.Append('}');
        }
    }

    private static readonly (string Word, bool IsHeader, PageSelection Selection)[] RunningDestinations =
    [
        ("header", true, PageSelection.Default),
        ("headerf", true, PageSelection.First),
        ("headerl", true, PageSelection.Even),
        ("footer", false, PageSelection.Default),
        ("footerf", false, PageSelection.First),
        ("footerl", false, PageSelection.Even),
    ];

    private static void WriteFontTable(StringBuilder sb, ResourceTable<string> fonts)
    {
        sb.Append("{\\fonttbl{\\f0\\fnil ;}");
        IReadOnlyList<string> families = fonts.Ordered;
        for (int i = 0; i < families.Count; i++)
        {
            sb.Append("{\\f").Append(i + 1).Append("\\fnil ");
            AppendEscaped(sb, families[i]);
            sb.Append(";}");
        }

        sb.Append('}');
    }

    private static void WriteColorTable(StringBuilder sb, ResourceTable<BColor> colors)
    {
        sb.Append("{\\colortbl;");
        foreach (BColor color in colors.Ordered)
        {
            sb.Append("\\red").Append(color.R)
              .Append("\\green").Append(color.G)
              .Append("\\blue").Append(color.B)
              .Append(';');
        }

        sb.Append('}');
    }

    private static void WriteParagraphProperties(StringBuilder sb, ParagraphStyle style)
    {
        switch (style.Alignment)
        {
            case TextAlignment.Center: sb.Append("\\qc"); break;
            case TextAlignment.Right: sb.Append("\\qr"); break;
            case TextAlignment.Justify: sb.Append("\\qj"); break;
            default: break; // Left is the \pard default.
        }

        if (style.IndentLevel > 0)
            sb.Append("\\li").Append(style.IndentLevel * 360);
        if (style.SpacingBefore != 0f)
            sb.Append("\\sb").Append(Twips(style.SpacingBefore));
        if (style.SpacingAfter != 0f)
            sb.Append("\\sa").Append(Twips(style.SpacingAfter));
    }

    private static void WriteRuns(
        StringBuilder sb,
        RichTextParagraph paragraph,
        ResourceTable<string> fonts,
        ResourceTable<BColor> colors,
        List<DocumentDiagnostic> diagnostics,
        HashSet<string> reported)
    {
        int offset = 0;
        foreach (StyleRun run in paragraph.Runs)
        {
            string text = paragraph.Text.Substring(offset, run.Length);
            offset += run.Length;
            WriteRun(sb, text, run.Style, fonts, colors, diagnostics, reported);
        }
    }

    private static void WriteRun(
        StringBuilder sb,
        string text,
        InlineStyle style,
        ResourceTable<string> fonts,
        ResourceTable<BColor> colors,
        List<DocumentDiagnostic> diagnostics,
        HashSet<string> reported)
    {
        if (style.Image is InlineImage image)
        {
            WriteImageRun(sb, text, image, style, fonts, colors, diagnostics, reported);
            return;
        }

        if (!string.IsNullOrEmpty(style.LinkHref))
        {
            sb.Append("{\\field{\\*\\fldinst{HYPERLINK \"");
            AppendEscaped(sb, style.LinkHref);
            sb.Append("\"}}{\\fldrslt ");
            WriteStyledText(sb, text, style, fonts, colors);
            sb.Append("}}");
            return;
        }

        WriteStyledText(sb, text, style, fonts, colors);
    }

    /// <summary>
    /// Writes an image run as an RTF <c>\pict</c> destination. RTF names only a
    /// few picture encodings; bytes in any other format are dropped with a
    /// diagnostic rather than written under a label that would misdescribe them.
    /// </summary>
    private static void WriteImageRun(
        StringBuilder sb,
        string text,
        InlineImage image,
        InlineStyle style,
        ResourceTable<string> fonts,
        ResourceTable<BColor> colors,
        List<DocumentDiagnostic> diagnostics,
        HashSet<string> reported)
    {
        string? blip = PictureControlWord(image.ContentType);
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != InlineImage.Placeholder)
                continue;

            if (i > start)
                WriteStyledText(sb, text[start..i], style, fonts, colors);

            if (blip is null)
            {
                AddOnce(
                    diagnostics,
                    reported,
                    "rtf.image.format",
                    "RTF carries only PNG and JPEG pictures; an image in another format was dropped.");
            }
            else
            {
                WritePicture(sb, image, blip);
            }

            start = i + 1;
        }

        if (start < text.Length)
            WriteStyledText(sb, text[start..], style, fonts, colors);
    }

    private static void WritePicture(StringBuilder sb, InlineImage image, string blip)
    {
        sb.Append("{\\pict").Append(blip);
        if (image.HasExplicitSize)
        {
            sb.Append("\\picwgoal").Append(Twips((float)image.Width));
            sb.Append("\\pichgoal").Append(Twips((float)image.Height));
        }

        sb.Append(' ');
        ReadOnlySpan<byte> data = image.Data.Span;
        foreach (byte value in data)
            sb.Append(HexDigits[value >> 4]).Append(HexDigits[value & 0x0F]);

        sb.Append('}');
    }

    private static string? PictureControlWord(string contentType) =>
        contentType.ToLowerInvariant() switch
        {
            "image/png" => "\\pngblip",
            "image/jpeg" or "image/jpg" => "\\jpegblip",
            _ => null,
        };

    private static void AddOnce(
        List<DocumentDiagnostic> diagnostics,
        HashSet<string> reported,
        string code,
        string message)
    {
        if (reported.Add(code))
            diagnostics.Add(DocumentDiagnostic.Warning(code, message));
    }

    private const string HexDigits = "0123456789abcdef";

    private static void WriteStyledText(
        StringBuilder sb,
        string text,
        InlineStyle style,
        ResourceTable<string> fonts,
        ResourceTable<BColor> colors)
    {
        string format = FormatControlWords(style, fonts, colors);
        if (format.Length == 0)
        {
            AppendEscaped(sb, text);
            return;
        }

        sb.Append('{').Append(format).Append(' ');
        AppendEscaped(sb, text);
        sb.Append('}');
    }

    private static string FormatControlWords(
        InlineStyle style,
        ResourceTable<string> fonts,
        ResourceTable<BColor> colors)
    {
        var b = new StringBuilder();
        if (style.Bold) b.Append("\\b");
        if (style.Italic) b.Append("\\i");
        if (style.Underline) b.Append("\\ul");
        if (style.Strikethrough) b.Append("\\strike");
        if (style.Capitalization == TextCapitalization.AllCaps) b.Append("\\caps");
        else if (style.Capitalization == TextCapitalization.SmallCaps) b.Append("\\scaps");
        if (style.FontFamily is not null)
            b.Append("\\f").Append(fonts.IndexOf(style.FontFamily));
        if (style.FontSize.HasValue)
            b.Append("\\fs").Append((int)Math.Round(style.FontSize.Value * 2f));
        if (!style.Foreground.IsEmpty)
            b.Append("\\cf").Append(colors.IndexOf(style.Foreground));
        if (!style.Background.IsEmpty)
            b.Append("\\highlight").Append(colors.IndexOf(style.Background));
        return b.ToString();
    }

    private static void AppendEscaped(StringBuilder sb, string text)
    {
        foreach (char c in text)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '{': sb.Append("\\{"); break;
                case '}': sb.Append("\\}"); break;
                case '\t': sb.Append("\\tab "); break;
                case (char)0x2028: sb.Append("\\line "); break;
                default:
                    if (c is >= (char)0x20 and <= (char)0x7E)
                    {
                        sb.Append(c);
                    }
                    else
                    {
                        int n = c > 32767 ? c - 65536 : c;
                        sb.Append("\\u").Append(n.ToString(CultureInfo.InvariantCulture)).Append('?');
                    }

                    break;
            }
        }
    }

    private static int Twips(float points) => (int)Math.Round(points * 20f);

    private sealed class ResourceTable<T>
        where T : notnull
    {
        // Index 0 is reserved (default font / auto color); interned entries start at 1.
        private readonly Dictionary<T, int> _index;
        private readonly List<T> _ordered = [];

        public ResourceTable(IEqualityComparer<T> comparer) => _index = new Dictionary<T, int>(comparer);

        public IReadOnlyList<T> Ordered => _ordered;

        public void Intern(T value)
        {
            if (_index.ContainsKey(value))
                return;
            _ordered.Add(value);
            _index[value] = _ordered.Count; // 1-based (0 reserved)
        }

        public int IndexOf(T value) => _index.TryGetValue(value, out int i) ? i : 0;
    }
}
