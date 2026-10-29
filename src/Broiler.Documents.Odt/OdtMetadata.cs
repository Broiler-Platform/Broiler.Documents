using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace Broiler.Documents.Odt;

/// <summary>
/// What an ODF package states about itself in <c>meta.xml</c>, to and from the
/// shared <see cref="DocumentMetadata"/> envelope.
/// </summary>
/// <remarks>
/// <para>
/// One part states all of it, so there is nothing to reconcile here and no
/// conflict to diagnose — unlike a PDF, where an Info dictionary and an XMP
/// packet can each name the title and disagree.
/// </para>
/// <para>
/// Two mappings are worth stating because the obvious reading of the element
/// names is wrong. ODF's <c>dc:creator</c> is <em>the last person to modify the
/// document</em>, not its author; the author is
/// <c>meta:initial-creator</c>, and that is what
/// <see cref="DocumentMetadata.Authors"/> is read from. And
/// <c>dc:date</c> is the modification time, with the creation time in
/// <c>meta:creation-date</c>. Reading the pair the other way round is a silent
/// error: both fields are populated, both look plausible, and every timestamp is
/// wrong.
/// </para>
/// <para>
/// ODF states one producing application in <c>meta:generator</c> and no separate
/// authoring one, so <see cref="DocumentMetadata.CreatorApplication"/> stays
/// absent rather than repeating <see cref="DocumentMetadata.Producer"/>.
/// </para>
/// </remarks>
internal static class OdtMetadata
{
    /// <summary>What this writer names itself when the caller states no producer.</summary>
    public const string DefaultGenerator = "Broiler.Documents.Odt";

    /// <summary>
    /// True when <paramref name="generator"/>, a <c>meta:generator</c>, names
    /// OpenOffice.org 2.x - or another product of its code line - by the
    /// build id LibreOffice reads from it: the <c>680</c> of
    /// <c>OpenOffice.org/2.2$Win32 OpenOffice.org_project/680m14$Build-9134</c>
    /// (xmlimp.cxx, <c>DocumentInfo</c>, <c>OOo_2x</c>).
    /// </summary>
    public static bool WrittenByOpenOffice2(string? generator)
    {
        if (string.IsNullOrEmpty(generator))
            return false;

        System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(
            generator, @"_project/(\d+)m\d+\$Build-\d+", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success && match.Groups[1].Value == "680";
    }

    /// <summary>
    /// How much of the page a page layout's own fill covers in a document
    /// <paramref name="generator"/> wrote, as LibreOffice decides it when the
    /// master page has no drawing-page style to say (PageMasterImportContext.cxx,
    /// <c>FillPropertySet_PageStyle</c>).
    /// </summary>
    /// <remarks>
    /// Writer painted a page background inside the margins until 6.3, and ODF
    /// had no way to state the bounds until 7.0 wrote
    /// <c>draw:background-size</c>. So LibreOffice keeps the fill of an
    /// OpenOffice.org or AOO document, or one written before 6.3, inside the
    /// margins, and guesses from the fill for 6.3 and 6.4. Every other document,
    /// including one that names no producer or one LibreOffice does not
    /// recognise, gets today's default: the whole page. The version is read the
    /// way xmlimp.cxx's <c>DocumentInfo</c> reads it from the build id
    /// xmlmetai.cxx makes of <c>meta:generator</c>.
    /// </remarks>
    public static OdtPageFillBounds PageFillBounds(string? generator)
    {
        if (string.IsNullOrEmpty(generator))
            return OdtPageFillBounds.WholePage;

        // "LibreOffice_project" has been in every LibreOffice generator since
        // 3.3. Its version is the digits and dots after the first slash, and
        // once it has one LibreOffice ignores the build id.
        int slash = generator.IndexOf('/');
        if (slash >= 0 && generator.Contains("LibreOffice_project/", StringComparison.Ordinal))
        {
            int end = slash + 1;
            while (end < generator.Length && (char.IsAsciiDigit(generator[end]) || generator[end] == '.'))
                end++;
            if (end > slash + 1)
                return LibreOfficePageFillBounds(generator[(slash + 1)..end]);
        }

        (int upd, int build) = BuildId(generator);
        bool openOffice = upd is >= 640 and <= 645 or 680 or 310 or 320 or 330 or 340 or 400 or 401 or >= 410 ||
            upd == 300 && build <= 9379;
        return openOffice ? OdtPageFillBounds.InsideMargins : OdtPageFillBounds.WholePage;
    }

    private static OdtPageFillBounds LibreOfficePageFillBounds(string version)
    {
        // 24.2 and later have a two-digit major and are current.
        if (version.IndexOf('.') != 1)
            return OdtPageFillBounds.WholePage;
        char minor = version.Length > 2 ? version[2] : '\0';
        return version[0] switch
        {
            '3' or '5' => OdtPageFillBounds.InsideMargins,
            '4' => minor is >= '0' and <= '4' ? OdtPageFillBounds.InsideMargins : OdtPageFillBounds.WholePage,
            '6' => minor is '0' or '1' or '2' ? OdtPageFillBounds.InsideMargins : OdtPageFillBounds.ByFill,
            _ => OdtPageFillBounds.WholePage,
        };
    }

    /// <summary>
    /// The product and build numbers of an OpenOffice.org-line generator: the
    /// <c>330</c> and <c>9567</c> of
    /// <c>OpenOffice.org/3.3$Unix OpenOffice.org_project/330m20$Build-9567</c>,
    /// or zeros.
    /// </summary>
    private static (int Upd, int Build) BuildId(string generator)
    {
        int space = generator.IndexOf(' ');
        int slash = space < 0 ? -1 : generator.IndexOf('/', space);
        int milestone = slash < 0 ? -1 : generator.IndexOf('m', slash);
        int build = milestone < 0 ? -1 : generator.IndexOf("$Build-", milestone, StringComparison.Ordinal);
        if (build >= 0)
            return (LeadingNumber(generator.AsSpan(slash + 1, milestone - slash - 1)), LeadingNumber(generator.AsSpan(build + 7)));

        string[] firstLine = ["StarOffice 7", "StarSuite 7", "StarOffice 6", "StarSuite 6", "OpenOffice.org 1"];
        if (firstLine.Any(prefix => generator.StartsWith(prefix, StringComparison.Ordinal)))
            return (645, 8687);
        return generator.StartsWith("NeoOffice/2", StringComparison.Ordinal) ? (680, 9134) : (0, 0);
    }

    private static int LeadingNumber(ReadOnlySpan<char> text)
    {
        int value = 0;
        foreach (char c in text)
        {
            if (!char.IsAsciiDigit(c) || value > 100_000_000)
                break;
            value = value * 10 + (c - '0');
        }
        return value;
    }

    /// <summary>Reads the package's <c>meta.xml</c>, if it has one.</summary>
    public static DocumentMetadata Read(
        ZipArchive archive,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics)
    {
        ZipArchiveEntry? entry = OdtPackage.FindEntry(archive, OdtNamespaces.MetaPart);
        if (entry is null)
            return DocumentMetadata.Empty;

        XElement? meta = OdtPackage
            .LoadEntryXml(entry, limits, diagnostics, "odt.meta.xml")?
            .Root?
            .Element(OdtNamespaces.Office + "meta");

        return Read(meta, diagnostics);
    }

    /// <summary>
    /// Reads an <c>office:meta</c> element: the root's child in <c>meta.xml</c>,
    /// or in a flat document the child of <c>office:document</c> itself.
    /// </summary>
    public static DocumentMetadata Read(XElement? meta, List<DocumentDiagnostic> diagnostics)
    {
        if (meta is null)
            return DocumentMetadata.Empty;

        return new DocumentMetadata(
            title: Text(meta, OdtNamespaces.DublinCore + "title"),
            authors: Author(meta),
            subject: Text(meta, OdtNamespaces.DublinCore + "subject"),
            keywords: meta.Elements(OdtNamespaces.Meta + "keyword")
                .Select(static k => k.Value)
                .Where(static k => k.Length > 0),
            language: Text(meta, OdtNamespaces.DublinCore + "language"),
            creatorApplication: null,
            producer: Text(meta, OdtNamespaces.Meta + "generator"),
            creationDate: Date(meta, OdtNamespaces.Meta + "creation-date", diagnostics),
            modificationDate: Date(meta, OdtNamespaces.DublinCore + "date", diagnostics));
    }

    /// <summary>
    /// The <c>office:meta</c> body for a write. Always returns at least the
    /// generator, because a package that names no producer at all is less useful
    /// than one that names this writer.
    /// </summary>
    public static XElement Build(DocumentMetadata metadata)
    {
        var meta = new XElement(OdtNamespaces.Office + "meta");

        Add(meta, OdtNamespaces.DublinCore + "title", metadata.Title);
        if (metadata.Authors.Count > 0)
            meta.Add(new XElement(OdtNamespaces.Meta + "initial-creator", metadata.Authors[0]));
        Add(meta, OdtNamespaces.DublinCore + "subject", metadata.Subject);
        foreach (string keyword in metadata.Keywords)
            meta.Add(new XElement(OdtNamespaces.Meta + "keyword", keyword));
        Add(meta, OdtNamespaces.DublinCore + "language", metadata.Language);
        meta.Add(new XElement(
            OdtNamespaces.Meta + "generator",
            metadata.Producer ?? DefaultGenerator));
        AddDate(meta, OdtNamespaces.Meta + "creation-date", metadata.CreationDate);
        AddDate(meta, OdtNamespaces.DublinCore + "date", metadata.ModificationDate);

        return meta;
    }

    /// <summary>The fields ODF states no equivalent for at all.</summary>
    public static IEnumerable<string> UnsupportedFields(DocumentMetadata metadata)
    {
        yield return nameof(metadata.CreatorApplication);
    }

    /// <summary>
    /// The fields ODF states in a narrower form than the envelope carries.
    /// <c>meta:initial-creator</c> holds one name: ODF records who started a
    /// document, not everyone who wrote it, and folding the rest into that
    /// element would assert that one person is called "A; B". The first author
    /// does reach the file, so this is a narrowing rather than a loss — and a
    /// narrowing is the one a caller cannot see for themselves, because the
    /// output looks like a document with a single author.
    /// </summary>
    public static IEnumerable<string> NarrowedFields(DocumentMetadata metadata)
    {
        if (metadata.Authors.Count > 1)
            yield return nameof(metadata.Authors);
    }

    private static IEnumerable<string>? Author(XElement meta)
    {
        string? author = Text(meta, OdtNamespaces.Meta + "initial-creator");
        return string.IsNullOrEmpty(author) ? null : [author];
    }

    private static string? Text(XElement parent, XName name) => parent.Element(name)?.Value;

    private static DocumentDate? Date(XElement meta, XName name, List<DocumentDiagnostic> diagnostics)
    {
        string? value = Text(meta, name);
        if (string.IsNullOrEmpty(value))
            return null;

        if (DocumentTimestamp.TryParse(value, out DocumentDate date))
            return date;

        diagnostics.Add(DocumentDiagnostic.Warning(
            "odt.meta.date",
            "A document property stated a timestamp this build could not read, so it is " +
            "dropped rather than guessed at: " + name.LocalName + "."));
        return null;
    }

    private static void Add(XElement meta, XName name, string? value)
    {
        if (value is not null)
            meta.Add(new XElement(name, value));
    }

    private static void AddDate(XElement meta, XName name, DocumentDate? date)
    {
        if (date is not null)
            meta.Add(new XElement(name, DocumentTimestamp.ToW3cdtf(date.Value)));
    }
}

/// <summary>How much of the page a page layout's fill covers when the document does not say.</summary>
internal enum OdtPageFillBounds
{
    /// <summary>The whole sheet, margins included.</summary>
    WholePage,

    /// <summary>The content rectangle inside the margins.</summary>
    InsideMargins,

    /// <summary>
    /// LibreOffice 6.3 and 6.4: a single or stretched picture inside the
    /// margins, a colour, gradient, hatch or tiled picture over the whole page.
    /// </summary>
    ByFill,
}
