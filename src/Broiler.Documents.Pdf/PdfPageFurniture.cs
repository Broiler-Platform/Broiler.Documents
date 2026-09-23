using System;
using System.Collections.Generic;
using System.Text;
using Broiler.Documents.Model;

namespace Broiler.Documents.Pdf;

/// <summary>
/// The running heads and footers a document marks as page furniture, held out of
/// the body while its pages are read and settled once every page is known.
/// </summary>
/// <remarks>
/// <para>
/// PDF 32000-1 §14.8.2.2 lets a document say which of its text is furniture: an
/// artifact is content that is not part of the author's logical content. Text
/// marked that way above everything else on the page is a running head, and
/// below everything else a running footer. Kept in the body, a footer the same
/// on every page came back once per page, between the paragraphs, and a table
/// the page boundary cut in half could not be joined across it, because the
/// footer stood between the halves.
/// </para>
/// <para>
/// <strong>Only what repeats becomes running content.</strong> A band that
/// reads the same on every page is the document's header or footer, and the
/// model carries exactly that. A band the same on every page but the first is
/// a letterhead's first page, which the model states with
/// <see cref="RunningContent.DifferentFirstPage"/>. Repetition is the evidence,
/// so a document of one page lifts nothing. Anything else - a folio that counts
/// the pages, a running head that follows the chapter - is text that differs
/// page by page, and the model has no field to hold what varies. It goes back
/// into the body where it was drawn, as it did before, except that it is never
/// set down inside a table joined across the page it sat on.
/// </para>
/// </remarks>
internal sealed class PdfPageFurniture
{
    private readonly List<Band> _headers = [];
    private readonly List<Band> _footers = [];
    private readonly List<int> _pages = [];

    /// <summary>Records that a page produced content, so its bands, or its lack of them, count.</summary>
    public void CountPage(int page) => _pages.Add(page);

    /// <summary>A page's running head, and where in the body it was drawn.</summary>
    public void AddHeader(int page, int index, List<RichTextParagraph> paragraphs) =>
        Add(_headers, page, index, paragraphs);

    /// <summary>A page's running footer, and where in the body it was drawn.</summary>
    public void AddFooter(int page, int index, List<RichTextParagraph> paragraphs) =>
        Add(_footers, page, index, paragraphs);

    /// <summary>
    /// Decides which bands are the document's running content and puts the
    /// rest back into the body, moving the tables that follow each insertion.
    /// </summary>
    public Settlement Settle(List<RichTextParagraph> paragraphs, List<DocumentTable> tables)
    {
        Decision header = Decide(_headers);
        Decision footer = Decide(_footers);

        bool differentFirstPage = header.FirstPageOwn || footer.FirstPageOwn;
        RunningContent running = RunningContent.Empty;

        if (header.Lifted)
        {
            running = running.WithHeader(PageSelection.Default, header.Default);
            if (differentFirstPage)
                running = running.WithHeader(PageSelection.First, header.First);
        }

        if (footer.Lifted)
        {
            running = running.WithFooter(PageSelection.Default, footer.Default);
            if (differentFirstPage)
                running = running.WithFooter(PageSelection.First, footer.First);
        }

        running = running.WithDifferentFirstPage(differentFirstPage && (header.Lifted || footer.Lifted));

        var reinserted = new List<Band>();
        if (!header.Lifted)
            reinserted.AddRange(_headers);
        if (!footer.Lifted)
            reinserted.AddRange(_footers);

        // Last first, so every earlier index still means what it meant. A
        // header and a footer can share an index where a page drew nothing
        // else; the footer belongs after the header, so it goes in first.
        reinserted.Sort(static (left, right) =>
        {
            int byIndex = right.Index.CompareTo(left.Index);
            return byIndex != 0 ? byIndex : right.IsFooter.CompareTo(left.IsFooter);
        });

        var pagesInBody = new SortedSet<int>();
        foreach (Band band in reinserted)
        {
            if (band.Paragraphs.Count == 0)
                continue;

            Insert(paragraphs, tables, AfterJoinedTable(tables, band.Index), band.Paragraphs);
            pagesInBody.Add(band.Page);
        }

        return new Settlement(
            running.IsEmpty ? null : running,
            header.Lifted ? header.Pages : 0,
            footer.Lifted ? footer.Pages : 0,
            differentFirstPage,
            pagesInBody);
    }

    private void Add(List<Band> bands, int page, int index, List<RichTextParagraph> paragraphs)
    {
        if (paragraphs.Count == 0)
            return;

        var text = new StringBuilder();
        foreach (RichTextParagraph paragraph in paragraphs)
            text.Append(paragraph.Text).Append('\n');

        bands.Add(new Band(page, index, paragraphs, text.ToString(), ReferenceEquals(bands, _footers)));
    }

    /// <summary>
    /// Whether one kind of band is running content: the same text on every page
    /// that drew anything, or on every one of them but the first.
    /// </summary>
    private Decision Decide(List<Band> bands)
    {
        // Repetition is the evidence, and one page repeats nothing: its
        // furniture stays in the body, where it was drawn.
        if (bands.Count == 0 || _pages.Count < 2)
            return default;

        var byPage = new Dictionary<int, Band>();
        foreach (Band band in bands)
            byPage[band.Page] = band;

        string? Text(int page) => byPage.TryGetValue(page, out Band? band) ? band.Text : null;

        string? common = Text(_pages[0]);
        bool everywhere = common is not null;
        for (int i = 1; i < _pages.Count && everywhere; i++)
            everywhere = Text(_pages[i]) == common;

        // The same band stands in the first page's slot as well, so a first page
        // of its own for the other band does not leave this one off page one.
        if (everywhere)
        {
            List<RichTextParagraph> band = byPage[_pages[0]].Paragraphs;
            return new Decision(true, false, band, band, _pages.Count);
        }

        // A first page of its own: the document's first page, and every page
        // after it agreeing among themselves - at least two of them, for the
        // same reason one page is not enough above.
        if (_pages.Count < 3 || _pages[0] != 0)
            return default;

        string? rest = Text(_pages[1]);
        if (rest is null)
            return default;

        for (int i = 2; i < _pages.Count; i++)
        {
            if (Text(_pages[i]) != rest)
                return default;
        }

        List<RichTextParagraph> first = byPage.TryGetValue(_pages[0], out Band? own) ? own.Paragraphs : [];
        return new Decision(true, true, byPage[_pages[1]].Paragraphs, first, _pages.Count - 1);
    }

    /// <summary>
    /// The index a band goes back in at: where it was drawn, unless a table the
    /// page boundary cut in half was joined across that point, in which case
    /// straight after the table.
    /// </summary>
    private static int AfterJoinedTable(List<DocumentTable> tables, int index)
    {
        foreach (DocumentTable table in tables)
        {
            if (table.ParagraphIndex < index && index < table.ParagraphEnd)
                return table.ParagraphEnd;
        }

        return index;
    }

    /// <summary>
    /// Inserts paragraphs into the body and moves every table at or after the
    /// insertion point with them. A table that ends where the insertion begins
    /// stays where it is: the paragraphs go after it, not into it.
    /// </summary>
    private static void Insert(
        List<RichTextParagraph> paragraphs,
        List<DocumentTable> tables,
        int index,
        List<RichTextParagraph> inserted)
    {
        paragraphs.InsertRange(index, inserted);

        for (int t = 0; t < tables.Count; t++)
        {
            DocumentTable table = tables[t];
            if (table.ParagraphIndex >= index)
                tables[t] = table.Shifted(table.ParagraphIndex - 1, 0, inserted.Count) ?? table;
        }
    }

    private sealed record Band(int Page, int Index, List<RichTextParagraph> Paragraphs, string Text, bool IsFooter);

    /// <param name="Lifted">True when the band is carried as running content.</param>
    /// <param name="FirstPageOwn">True when the first page's band differs from the rest.</param>
    /// <param name="Default">The band every page carries, or every page but the first.</param>
    /// <param name="First">The first page's own band, where it has one.</param>
    /// <param name="Pages">How many pages the carried band stands for.</param>
    private readonly record struct Decision(
        bool Lifted,
        bool FirstPageOwn,
        List<RichTextParagraph> Default,
        List<RichTextParagraph> First,
        int Pages);

    /// <summary>What settling decided.</summary>
    /// <param name="RunningContent">The running content read, or null where nothing repeated.</param>
    /// <param name="HeaderPages">How many pages the carried header stands for; zero where none was carried.</param>
    /// <param name="FooterPages">How many pages the carried footer stands for; zero where none was carried.</param>
    /// <param name="DifferentFirstPage">True when the first page's own band was carried apart from the rest.</param>
    /// <param name="PagesInBody">The pages, zero-based, whose furniture stayed in the body.</param>
    public sealed record Settlement(
        RunningContent? RunningContent,
        int HeaderPages,
        int FooterPages,
        bool DifferentFirstPage,
        IReadOnlyCollection<int> PagesInBody);
}
