using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Broiler.Documents.Odt;

/// <summary>
/// Recognises the flat form of an ODF text document (<c>.fodt</c>): the same
/// content as a package, written as one XML file under an
/// <c>office:document</c> root.
/// </summary>
/// <remarks>
/// <para>
/// A package says what it is in a stored <c>mimetype</c> entry. A flat file says
/// it in an <c>office:mimetype</c> attribute on its root, which is the one place
/// a probe can learn it from without parsing the file. LibreOffice writes that
/// attribute after some forty namespace declarations, still well inside the
/// default probe prefix, but a prefix that ends before it is not proof of
/// anything either way, so the probe reports what it saw and lets the hints
/// decide the rest.
/// </para>
/// <para>
/// The prefix is scanned as text rather than parsed: it is a fragment, and an
/// XML reader would refuse it for the element it cuts in half.
/// </para>
/// </remarks>
internal static partial class OdtFlatDocument
{
    /// <summary>The extension LibreOffice and the ODF tools save a flat text document under.</summary>
    public const string Extension = ".fodt";

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// True when <paramref name="bytes"/> opens like an XML document - a byte
    /// order mark, white space, then <c>&lt;</c> - rather than like a ZIP
    /// package. Nothing else a reader is handed as ODT begins that way.
    /// </summary>
    public static bool LooksLikeXml(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 &&
            ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
        {
            // A UTF-16 byte order mark. Nothing writes a ZIP that starts with one.
            return true;
        }

        int index = bytes.StartsWith(Utf8Bom) ? Utf8Bom.Length : 0;
        while (index < bytes.Length && bytes[index] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            index++;

        return index < bytes.Length && bytes[index] == (byte)'<';
    }

    /// <summary>True for the text document and text template media types.</summary>
    public static bool IsTextMediaType(string mediaType) =>
        mediaType.Equals(OdtNamespaces.PackageMediaType, StringComparison.Ordinal) ||
        mediaType.Equals(OdtNamespaces.TemplateMediaType, StringComparison.Ordinal);

    /// <summary>
    /// Reads what a probe prefix says about a flat document: whether its root is
    /// <c>office:document</c>, and the media type its root states, when the
    /// attribute falls inside the prefix.
    /// </summary>
    public static FlatPrefixInfo InspectPrefix(ReadOnlySpan<byte> span)
    {
        if (!LooksLikeXml(span) || (span.Length >= 2 && (span[0] == 0xFF || span[0] == 0xFE)))
        {
            // UTF-16 flat documents read fine, but no producer writes one, so
            // the probe does not decode them and the hints decide.
            return FlatPrefixInfo.None;
        }

        int start = span.StartsWith(Utf8Bom) ? Utf8Bom.Length : 0;
        string text = Encoding.UTF8.GetString(span[start..]);
        int index = SkipProlog(text);
        if (index < 0)
            return FlatPrefixInfo.None;

        Match root = RootElement().Match(text, index);
        if (!root.Success || root.Index != index || root.Groups["local"].Value != "document")
            return FlatPrefixInfo.None;

        // The start tag runs to the first '>'. A prefix that ends inside it
        // still gives up the declarations it does hold.
        int tagEnd = text.IndexOf('>', index);
        string tag = tagEnd < 0 ? text[index..] : text[index..(tagEnd + 1)];

        string rootPrefix = root.Groups["prefix"].Value;
        string? officePrefix = null;
        bool rootInOffice = false;
        foreach (Match declaration in NamespaceDeclaration().Matches(tag))
        {
            if (declaration.Groups["uri"].Value != OdtNamespaces.Office.NamespaceName)
                continue;

            string declared = declaration.Groups["prefix"].Value;
            if (declared == rootPrefix)
                rootInOffice = true;
            if (declared.Length > 0)
                officePrefix ??= declared;
        }

        if (!rootInOffice)
            return FlatPrefixInfo.None;

        string? mediaType = null;
        if (officePrefix is not null)
        {
            foreach (Match attribute in MimeTypeAttribute().Matches(tag))
            {
                if (attribute.Groups["prefix"].Value == officePrefix)
                {
                    mediaType = attribute.Groups["value"].Value.Trim();
                    break;
                }
            }
        }

        int consumed = start + Encoding.UTF8.GetByteCount(text.AsSpan(0, index + tag.Length));
        return new FlatPrefixInfo(IsDocumentRoot: true, mediaType, Math.Min(consumed, span.Length));
    }

    /// <summary>
    /// The index of the root element's <c>&lt;</c>, past the XML declaration,
    /// comments, processing instructions and a doctype; or -1 when the prefix
    /// ends before the root starts.
    /// </summary>
    private static int SkipProlog(string text)
    {
        int index = 0;
        while (true)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
                index++;

            if (index >= text.Length || text[index] != '<')
                return -1;

            string? close =
                text.AsSpan(index).StartsWith("<?") ? "?>" :
                text.AsSpan(index).StartsWith("<!--") ? "-->" :
                text.AsSpan(index).StartsWith("<!") ? ">" :
                null;
            if (close is null)
                return index;

            int end = text.IndexOf(close, index, StringComparison.Ordinal);
            if (end < 0)
                return -1;
            index = end + close.Length;
        }
    }

    [GeneratedRegex(@"<(?:(?<prefix>[A-Za-z_][\w.\-]*):)?(?<local>[A-Za-z_][\w.\-]*)", RegexOptions.CultureInvariant)]
    private static partial Regex RootElement();

    [GeneratedRegex(@"\sxmlns(?::(?<prefix>[A-Za-z_][\w.\-]*))?\s*=\s*(?<q>[""'])(?<uri>.*?)\k<q>", RegexOptions.CultureInvariant)]
    private static partial Regex NamespaceDeclaration();

    [GeneratedRegex(@"\s(?<prefix>[A-Za-z_][\w.\-]*):mimetype\s*=\s*(?<q>[""'])(?<value>.*?)\k<q>", RegexOptions.CultureInvariant)]
    private static partial Regex MimeTypeAttribute();
}

/// <summary>What a probe prefix says about a flat document.</summary>
/// <param name="IsDocumentRoot">True when the root element is <c>office:document</c>.</param>
/// <param name="MediaType">The root's <c>office:mimetype</c>, or null when the prefix does not show one.</param>
/// <param name="BytesConsumed">How far into the prefix the evidence reached.</param>
internal readonly record struct FlatPrefixInfo(bool IsDocumentRoot, string? MediaType, int BytesConsumed)
{
    public static FlatPrefixInfo None => default;
}
