using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Broiler.Documents.Model;

namespace Broiler.Documents.Docx;

/// <summary>
/// The sections of a document body as this reader uses them: the text columns
/// each one is divided into, how many paragraphs it holds, and which one the
/// read has reached.
/// </summary>
/// <remarks>
/// <para>
/// WordprocessingML closes a section rather than opening one. Each paragraph
/// whose <c>w:pPr</c> holds a <c>w:sectPr</c> ends the section it is in, and the
/// body-level <c>w:sectPr</c> describes the last. So what applies to a paragraph
/// is only known from what comes after it, and the sections are found in one pass
/// over the body before the content is read, then followed with
/// <see cref="Advance"/> as the read reaches each paragraph that ends one.
/// </para>
/// <para>
/// Both passes have to agree on which paragraphs end a section, or the cursor
/// drifts. A paragraph inside a text box, in a deleted revision or in the
/// alternate-content branch that is not read is not part of the body's flow:
/// the read does not advance on it, and the scan does not count it either.
/// </para>
/// </remarks>
internal sealed class DocxSections
{
    /// <summary>The column spacing assumed when <c>w:space</c> is absent: half an inch.</summary>
    private const double DefaultGapPoints = 36;

    private readonly List<Section> _sections;
    private readonly List<int> _ends = [];
    private int _current;

    private DocxSections(List<Section> sections) => _sections = sections;

    /// <summary>How many sections the body has; at least one.</summary>
    public int Count => _sections.Count;

    /// <summary>
    /// The column count of the section the read has reached. A column break
    /// in a section of one column goes to the next page, so this is what decides
    /// whether one is a page break.
    /// </summary>
    public int CurrentColumnCount => _sections[Math.Min(_current, _sections.Count - 1)].Columns.Count;

    /// <summary>
    /// True when the section the read has just entered starts on a new page:
    /// ECMA-376's <c>nextPage</c>, <c>oddPage</c> and <c>evenPage</c>, and
    /// an absent <c>w:type</c>, whose default is <c>nextPage</c>. A section with
    /// no paragraphs of its own starts nothing.
    /// </summary>
    public bool CurrentStartsPage =>
        _current < _sections.Count && _sections[_current].StartsPage && _sections[_current].Paragraphs > 0;

    /// <summary>
    /// True when the section the read has just entered starts on a page of a
    /// given parity, which can put a blank page before it.
    /// </summary>
    public bool CurrentWantsParity =>
        _current < _sections.Count && _sections[_current].WantsParity && _sections[_current].Paragraphs > 0;

    /// <summary>
    /// The columns of the section that holds the most paragraphs, which is
    /// what the model's one page records. A tie goes to the later section, the
    /// way the page size and margins go to the last.
    /// </summary>
    /// <remarks>
    /// The page size and margins come from the last section because the running
    /// content belongs to it. Columns are not tied to anything like that, and
    /// the last section is often the wrong place to look: a two-column document
    /// whose producer closed the columns with a continuous break before one
    /// empty paragraph has a one-column last section holding nothing. Taking the
    /// columns that most of the text is written in keeps the layout the reader
    /// sees.
    /// </remarks>
    public DocxColumns Dominant
    {
        get
        {
            Section dominant = _sections[0];
            foreach (Section section in _sections)
            {
                if (section.Paragraphs >= dominant.Paragraphs)
                    dominant = section;
            }

            return dominant.Columns;
        }
    }

    /// <summary>True when the sections do not all have the same columns.</summary>
    public bool ColumnsDiffer => _sections.Select(section => section.Columns.Count).Distinct().Count() > 1;

    /// <summary>True when some section's columns are of unequal width.</summary>
    public bool HasUnequalColumns => _sections.Any(section => section.Columns.Unequal);

    /// <summary>
    /// Finds the body's sections. A body with no section properties at all has
    /// one section of one column, which is what Word lays it out as.
    /// </summary>
    public static DocxSections Read(XElement? body)
    {
        var sections = new List<Section>();
        if (body is null)
            return new DocxSections([new Section(DocxColumns.Single, 0, StartsPage: false, WantsParity: false, Page: null)]);

        int paragraphs = 0;
        foreach (XElement paragraph in BodyParagraphs(body))
        {
            paragraphs++;
            if (EndsSection(paragraph))
            {
                XElement sectPr = paragraph
                    .Element(DocxNamespaces.Wordprocessing + "pPr")!
                    .Element(DocxNamespaces.Wordprocessing + "sectPr")!;
                sections.Add(ReadSection(sectPr, paragraphs));
                paragraphs = 0;
            }
        }

        XElement? final = body.Elements(DocxNamespaces.Wordprocessing + "sectPr").LastOrDefault();
        sections.Add(ReadSection(final, paragraphs));
        return new DocxSections(sections);
    }

    private static Section ReadSection(XElement? sectPr, int paragraphs)
    {
        // w:type says how the section starts against the one before it, and is
        // read only where there is one before it.
        string type = DocxValueReader.WordValue(sectPr?.Element(DocxNamespaces.Wordprocessing + "type")) ?? "nextPage";
        return new Section(
            ReadColumns(sectPr),
            paragraphs,
            StartsPage: type is "nextPage" or "oddPage" or "evenPage",
            WantsParity: type is "oddPage" or "evenPage",
            Page: ReadPage(sectPr));
    }

    /// <summary>
    /// The paper and margins a <c>w:sectPr</c> states in <c>w:pgSz</c> and
    /// <c>w:pgMar</c>, without its columns; null when it states no paper, or a
    /// page with no room to write on.
    /// </summary>
    public static PageGeometry? ReadPage(XElement? sectPr)
    {
        XElement? size = sectPr?.Element(DocxNamespaces.Wordprocessing + "pgSz");
        if (size is null)
            return null;

        XElement? margin = sectPr!.Element(DocxNamespaces.Wordprocessing + "pgMar");
        var geometry = new PageGeometry(
            DocxValueReader.Twips(size, "w"),
            DocxValueReader.Twips(size, "h"),
            DocxValueReader.Twips(margin, "left"),
            DocxValueReader.Twips(margin, "right"),
            DocxValueReader.Twips(margin, "top"),
            DocxValueReader.Twips(margin, "bottom"),
            DocxValueReader.Twips(margin, "header"),
            DocxValueReader.Twips(margin, "footer"));
        return geometry.IsUsable ? geometry : null;
    }

    /// <summary>True when <paramref name="paragraph"/> is the last of its section.</summary>
    public static bool EndsSection(XElement paragraph) =>
        paragraph.Element(DocxNamespaces.Wordprocessing + "pPr")?.Element(DocxNamespaces.Wordprocessing + "sectPr") is not null;

    /// <summary>
    /// Moves the read past a paragraph that ended a section, recording where in
    /// the paragraphs read the section ended.
    /// </summary>
    public void Advance(int paragraphEnd)
    {
        _ends.Add(paragraphEnd);
        _current++;
    }

    /// <summary>
    /// The sections as the model holds them, over the <paramref name="paragraphCount"/>
    /// paragraphs the read produced: one per section, each with its own columns
    /// and, where it states paper or margins other than <paramref name="page"/>,
    /// its own page; or none when every section has the same columns and the
    /// same page, and the document's page says it all.
    /// </summary>
    /// <remarks>
    /// The ranges are where the read was when each section ended, not the
    /// scan's counts, because the read is what numbered the paragraphs. A
    /// section that holds none is left out.
    /// </remarks>
    public IReadOnlyList<DocumentSection> ToModel(int paragraphCount, PageGeometry? page)
    {
        var sections = new List<DocumentSection>(_sections.Count);
        bool differ = ColumnsDiffer;
        int start = 0;
        for (int i = 0; i < _sections.Count; i++)
        {
            int end = i < _ends.Count ? Math.Min(_ends[i], paragraphCount) : paragraphCount;
            if (end > start)
            {
                PageGeometry? own = SamePage(_sections[i].Page, page) ? null : _sections[i].Page;
                differ |= own is not null;
                sections.Add(new DocumentSection(
                    start, end - start, _sections[i].Columns.Count, _sections[i].Columns.Gap, own));
            }

            start = Math.Max(start, end);
        }

        return differ ? sections : [];
    }

    /// <summary>
    /// True when a section's page is the document's, or states none: the same
    /// paper and margins, whatever the columns and the header and footer
    /// distances.
    /// </summary>
    private static bool SamePage(PageGeometry? section, PageGeometry? page) =>
        section is null || page is null ||
        (Near(section.Width, page.Width) && Near(section.Height, page.Height) &&
         Near(section.MarginLeft, page.MarginLeft) && Near(section.MarginRight, page.MarginRight) &&
         Near(section.MarginTop, page.MarginTop) && Near(section.MarginBottom, page.MarginBottom));

    private static bool Near(double a, double b) => Math.Abs(a - b) < 0.01;

    /// <summary>
    /// ECMA-376's <c>w:cols</c>: the columns a <c>w:sectPr</c> states. An absent
    /// <c>w:cols</c>, or one of a single column, is one column.
    /// </summary>
    /// <remarks>
    /// Columns of equal width are a count and a gap. Unequal ones list each
    /// column with its width and the space after it; the model holds equal
    /// columns, so they are read as their count and the mean of their spaces,
    /// which puts the text on the same page in the same column and differs only
    /// in where each column's edge falls.
    /// </remarks>
    internal static DocxColumns ReadColumns(XElement? sectPr)
    {
        XElement? cols = sectPr?.Element(DocxNamespaces.Wordprocessing + "cols");
        if (cols is null)
            return DocxColumns.Single;

        List<XElement> listed = [.. cols.Elements(DocxNamespaces.Wordprocessing + "col")];
        bool equalWidth = !IsOff((string?)cols.Attribute(DocxNamespaces.Wordprocessing + "equalWidth"));
        if (!equalWidth && listed.Count >= 2)
        {
            double[] widths = [.. listed.Select(column => DocxValueReader.Twips(column, "w"))];
            double gap = listed
                .Take(listed.Count - 1)
                .Average(column => DocxValueReader.Twips(column, "space"));
            bool unequal = widths.Max() - widths.Min() > 1;
            return new DocxColumns(listed.Count, gap, unequal);
        }

        if (!DocxValueReader.TryReadInt(cols.Attribute(DocxNamespaces.Wordprocessing + "num"), out int count) || count <= 1)
            return DocxColumns.Single;

        double space = cols.Attribute(DocxNamespaces.Wordprocessing + "space") is null
            ? DefaultGapPoints
            : DocxValueReader.Twips(cols, "space");
        return new DocxColumns(count, space, Unequal: false);
    }

    /// <summary>
    /// The paragraphs of the body's own flow, in document order, walked the way
    /// the read walks block content: into tables, content controls and accepted
    /// revisions, past deleted ones, and into the one branch of an
    /// <c>mc:AlternateContent</c> that is read. A paragraph is not entered, so a
    /// text box's paragraphs - which the read takes as a shape's own story - are
    /// never reached.
    /// </summary>
    private static IEnumerable<XElement> BodyParagraphs(XElement container)
    {
        foreach (XElement child in container.Elements())
        {
            if (child.Name == DocxNamespaces.Wordprocessing + "p")
            {
                yield return child;
                continue;
            }

            if (child.Name == DocxNamespaces.Wordprocessing + "del" ||
                child.Name == DocxNamespaces.Wordprocessing + "moveFrom")
            {
                continue;
            }

            XElement? branch = child.Name == DocxNamespaces.MarkupCompatibility + "AlternateContent"
                ? child.Element(DocxNamespaces.MarkupCompatibility + "Choice") ??
                  child.Element(DocxNamespaces.MarkupCompatibility + "Fallback")
                : child;
            if (branch is null)
                continue;

            foreach (XElement paragraph in BodyParagraphs(branch))
                yield return paragraph;
        }
    }

    private static bool IsOff(string? value) =>
        value is not null &&
        (value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("0", StringComparison.Ordinal) ||
         value.Equals("off", StringComparison.OrdinalIgnoreCase));

    private sealed record Section(DocxColumns Columns, int Paragraphs, bool StartsPage, bool WantsParity, PageGeometry? Page);
}

/// <summary>A section's text columns: how many, and the gap between them in points.</summary>
/// <param name="Unequal">True when the section listed columns of different widths.</param>
internal readonly record struct DocxColumns(int Count, double Gap, bool Unequal)
{
    /// <summary>One column, the whole text area.</summary>
    public static DocxColumns Single { get; } = new(1, 0, Unequal: false);
}
