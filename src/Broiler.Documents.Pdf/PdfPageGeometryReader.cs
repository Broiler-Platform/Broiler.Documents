using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Broiler.Documents.Model;
using Broiler.Documents.Pdf.Text;

namespace Broiler.Documents.Pdf;

/// <summary>
/// The page a PDF was made for, as the model states it: the size its pages are
/// displayed at, and margins where its content sits.
/// </summary>
/// <remarks>
/// <para>
/// PDF states each page's size and nothing else about it. The size is the
/// visible box as a viewer displays it - turned by <c>/Rotate</c> and in points -
/// so a landscape page is stated landscape whichever of the two ways the file
/// made it one. Nothing at all used to be stated, and every consumer fell back
/// to a page of its own: a landscape document rendered on A4 portrait, into a
/// column not two-thirds the width of the tables it had to hold, broke words
/// the page had never broken.
/// </para>
/// <para>
/// <strong>The margins are where the content sits.</strong> A PDF has no
/// margins, only marks at coordinates, and the model needs a column to reflow
/// the text into. The column the pages already used is the one everything is
/// known to fit: the box around every run, table and picture in the body, on
/// every page of the stated size. Where that box stops more than a quarter of
/// the page short of the right edge or the foot, the gap is a short line or a
/// page left half empty rather than a margin, and the margin opposite stands in.
/// Running content is not the body - it sits in the margins, and how far it
/// sits from the edge is the header or footer distance. A fixed inch would have
/// been tidier and wrong: a table drawn a little wider than that column wraps
/// words the page never wrapped.
/// </para>
/// <para>
/// <strong>One page for the document.</strong> The model states one page per
/// document, so pages of different sizes state the size most of them share, and
/// the read says so, as a DOCX of several sections does.
/// </para>
/// </remarks>
internal sealed class PdfPageGeometryReader
{
    /// <summary>
    /// How close two sizes are before they are one size, in points. A4 is
    /// 595.28 wide, or 595.3, or 595, depending on who rounded.
    /// </summary>
    private const double SameSize = 1.0;

    /// <summary>
    /// How far above its baseline a run's glyphs reach, as a share of its size.
    /// The top margin is measured to there, so the first line set on the page
    /// starts about where it was drawn.
    /// </summary>
    private const double Ascent = 0.8;

    /// <summary>How far below its baseline a run's descenders reach, as a share of its size.</summary>
    private const double Descent = 0.2;

    /// <summary>
    /// The deepest a right or bottom margin read off the ink is taken to be, as
    /// a share of the page's width or height. Past it, the gap is a line or a
    /// page that ended early.
    /// </summary>
    private const double MaxEndGap = 0.25;

    /// <summary>
    /// The margin a page gets where nothing on it says where the column is -
    /// the inch every other codec's default page has - and never more than a
    /// quarter of the page's side, so a small page keeps a column.
    /// </summary>
    private const double FallbackMargin = 72;

    /// <summary>Other sizes the note names before it counts the rest.</summary>
    private const int MaxNamedSizes = 3;

    /// <summary>Pages one size names before it counts the rest.</summary>
    private const int MaxNamedPages = 6;

    private readonly List<Page> _pages = [];
    private readonly Dictionary<int, Page> _byIndex = [];

    /// <summary>Records a page's size as displayed, whether or not anything was drawn on it.</summary>
    public void AddPage(int index, double width, double height)
    {
        var page = new Page(index, width, height);
        _pages.Add(page);
        _byIndex[index] = page;
    }

    /// <summary>
    /// Records where a page's content sits: its body, and the running head and
    /// footer held apart from it.
    /// </summary>
    public void AddContent(int index, Ink body, Ink head, Ink foot)
    {
        if (!_byIndex.TryGetValue(index, out Page? page))
            return;

        page.Body = body.Within(page.Width, page.Height);
        page.Head = head.Within(page.Width, page.Height);
        page.Foot = foot.Within(page.Width, page.Height);
    }

    /// <summary>
    /// Decides the document's page once every page is known, and whether the
    /// running head and footer were carried as running content or went back
    /// into the body.
    /// </summary>
    public Result Settle(bool headerLifted, bool footerLifted)
    {
        if (_pages.Count == 0)
            return new Result(null, null);

        // Grouped in page order, so a tie goes to the size the document starts on.
        var sizes = new List<Size>();
        foreach (Page page in _pages)
        {
            Size? size = sizes.Find(size => size.Matches(page));
            if (size is null)
            {
                size = new Size(page.Width, page.Height);
                sizes.Add(size);
            }

            size.Pages.Add(page.Index);
        }

        Size stated = sizes[0];
        foreach (Size size in sizes)
        {
            if (size.Pages.Count > stated.Pages.Count)
                stated = size;
        }

        PageGeometry? geometry = Measure(stated, headerLifted, footerLifted);
        return new Result(geometry, sizes.Count > 1 ? DescribeMixed(sizes, stated) : null);
    }

    /// <summary>
    /// The margins the pages of one size keep - the least each side leaves on
    /// any of them - and the header and footer distances their running content
    /// was drawn at.
    /// </summary>
    private PageGeometry? Measure(Size size, bool headerLifted, bool footerLifted)
    {
        double left = double.PositiveInfinity;
        double right = double.PositiveInfinity;
        double top = double.PositiveInfinity;
        double bottom = double.PositiveInfinity;
        double header = double.PositiveInfinity;
        double footer = double.PositiveInfinity;

        foreach (Page page in _pages)
        {
            if (!size.Matches(page))
                continue;

            // A band that stayed in the body is body, and the column holds it.
            Ink column = page.Body;
            if (!headerLifted)
                column = column.Union(page.Head);
            if (!footerLifted)
                column = column.Union(page.Foot);

            if (!column.IsEmpty)
            {
                left = Math.Min(left, column.MinX);
                right = Math.Min(right, page.Width - column.MaxX);
                top = Math.Min(top, page.Height - column.MaxY);
                bottom = Math.Min(bottom, column.MinY);
            }

            if (headerLifted && !page.Head.IsEmpty)
                header = Math.Min(header, page.Height - page.Head.MaxY);
            if (footerLifted && !page.Foot.IsEmpty)
                footer = Math.Min(footer, page.Foot.MinY);
        }

        double width = size.Width;
        double height = size.Height;
        if (double.IsPositiveInfinity(left))
            return Fallback(width, height);

        // Text starts where the column does, and stops wherever its lines and
        // its pages happen to. The longest line comes within a word of the
        // column's edge and the fullest page within a line of its foot - unless
        // every line is short and every page ends early, as a list's, a slide's
        // or a three-line letter's do. A gap that deep is not a margin, and the
        // margin opposite stands in for it, so such a document is not set in a
        // column three short lines wide and one line tall.
        if (right > width * MaxEndGap)
            right = Math.Min(right, left);
        if (bottom > height * MaxEndGap)
            bottom = Math.Min(bottom, top);

        // Where nothing ran in a margin, the band a document would put there
        // sits halfway into it, as a word processor's default does.
        var geometry = new PageGeometry(
            Round(width),
            Round(height),
            Round(left),
            Round(right),
            Round(top),
            Round(bottom),
            Round(double.IsPositiveInfinity(header) ? top / 2 : header),
            Round(double.IsPositiveInfinity(footer) ? bottom / 2 : footer));

        return geometry.IsUsable ? geometry : Fallback(width, height);
    }

    private static PageGeometry? Fallback(double width, double height)
    {
        double side = Math.Min(FallbackMargin, width / 4);
        double end = Math.Min(FallbackMargin, height / 4);
        var geometry = new PageGeometry(Round(width), Round(height), Round(side), Round(side), Round(end), Round(end), Round(end / 2), Round(end / 2));
        return geometry.IsUsable ? geometry : null;
    }

    private static double Round(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The note for a document whose pages are not all one size: which size was
    /// stated, and which others were not.
    /// </summary>
    private static string DescribeMixed(List<Size> sizes, Size stated)
    {
        int total = 0;
        foreach (Size size in sizes)
            total += size.Pages.Count;

        var parts = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"{stated.Pages.Count} of {total} are {stated.Describe()}"),
        };

        int unnamed = 0;
        foreach (Size size in sizes)
        {
            if (ReferenceEquals(size, stated))
                continue;

            if (parts.Count > MaxNamedSizes)
            {
                unnamed += size.Pages.Count;
                continue;
            }

            parts.Add(string.Create(CultureInfo.InvariantCulture,
                $"{size.Pages.Count} {(size.Pages.Count == 1 ? "is" : "are")} {size.Describe()}, on {DescribePages(size.Pages)}"));
        }

        if (unnamed > 0)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{unnamed} more {(unnamed == 1 ? "is another size" : "are other sizes")}"));

        // Each part carries commas of its own, so three or more are set apart
        // more firmly than two.
        string separator = parts.Count == 2 ? " and " : "; ";
        string last = parts.Count == 2 ? " and " : "; and ";
        var text = new StringBuilder("The pages are not all one size: ");
        text.Append(string.Join(separator, parts.GetRange(0, parts.Count - 1))).Append(last).Append(parts[^1]);
        text.Append(
            ". The model holds one page for a whole document, so the size most pages share is the one stated. " +
            "What the other pages drew was carried all the same, and is set on that page like the rest.");
        return text.ToString();
    }

    private static string DescribePages(List<int> pages)
    {
        var text = new StringBuilder(pages.Count == 1 ? "page " : "pages ");
        int named = pages.Count <= MaxNamedPages + 1 ? pages.Count : MaxNamedPages;
        for (int i = 0; i < named; i++)
        {
            if (i > 0)
                text.Append(i == pages.Count - 1 ? " and " : ", ");
            text.Append((pages[i] + 1).ToString(CultureInfo.InvariantCulture));
        }

        if (named < pages.Count)
            text.Append(CultureInfo.InvariantCulture, $" and {pages.Count - named} more");

        return text.ToString();
    }

    /// <summary>What settling decided.</summary>
    /// <param name="Geometry">The page to state, or null where no usable one could be read.</param>
    /// <param name="MixedSizes">The note for pages of more than one size, or null where they agree.</param>
    public sealed record Result(PageGeometry? Geometry, string? MixedSizes);

    /// <summary>
    /// The box a page's marks cover, in points on the page as displayed, or
    /// nothing at all.
    /// </summary>
    public readonly record struct Ink(double MinX, double MinY, double MaxX, double MaxY)
    {
        public static Ink None { get; } = new(double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity);

        public bool IsEmpty => MinX > MaxX || MinY > MaxY;

        /// <summary>
        /// The runs' glyphs, from each one's first ink to its last, reaching as
        /// high and as low as a line of that size does. A run of spaces paints
        /// nothing and moves no edge.
        /// </summary>
        public static Ink Of(IEnumerable<PdfTextFragment> fragments)
        {
            Ink ink = None;
            foreach (PdfTextFragment fragment in fragments)
            {
                if (string.IsNullOrWhiteSpace(fragment.Text))
                    continue;

                ink = ink.Union(new Ink(
                    Math.Min(fragment.X, fragment.EndX),
                    fragment.Y - (fragment.FontSize * Descent),
                    Math.Max(fragment.X, fragment.EndX),
                    fragment.Y + (fragment.FontSize * Ascent)));
            }

            return ink;
        }

        /// <summary>This box and the tables' outer rules.</summary>
        public Ink With(IEnumerable<PdfTableGrid> grids)
        {
            Ink ink = this;
            foreach (PdfTableGrid grid in grids)
                ink = ink.Union(new Ink(grid.Left, grid.Bottom, grid.Right, grid.Top));

            return ink;
        }

        /// <summary>This box and the pictures' boxes.</summary>
        public Ink With(IEnumerable<PdfPlacedImage> images)
        {
            Ink ink = this;
            foreach (PdfPlacedImage image in images)
                ink = ink.Union(new Ink(image.Left, image.Top - image.Height, image.Left + image.Width, image.Top));

            return ink;
        }

        public Ink Union(Ink other) =>
            other.IsEmpty ? this
            : IsEmpty ? other
            : new Ink(Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY), Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));

        /// <summary>
        /// The part of this box on the page. Whatever was drawn off the visible
        /// page is out of every viewer's sight, and says nothing about where the
        /// column is.
        /// </summary>
        public Ink Within(double width, double height)
        {
            if (IsEmpty || MaxX <= 0 || MaxY <= 0 || MinX >= width || MinY >= height)
                return None;

            return new Ink(Math.Max(0, MinX), Math.Max(0, MinY), Math.Min(width, MaxX), Math.Min(height, MaxY));
        }
    }

    private sealed class Page(int index, double width, double height)
    {
        public int Index { get; } = index;

        public double Width { get; } = width;

        public double Height { get; } = height;

        public Ink Body { get; set; } = Ink.None;

        public Ink Head { get; set; } = Ink.None;

        public Ink Foot { get; set; } = Ink.None;
    }

    /// <summary>One size the pages come in, and which pages, zero-based, come in it.</summary>
    private sealed class Size(double width, double height)
    {
        public double Width { get; } = width;

        public double Height { get; } = height;

        public List<int> Pages { get; } = [];

        public bool Matches(Page page) =>
            Math.Abs(page.Width - Width) <= SameSize && Math.Abs(page.Height - Height) <= SameSize;

        public string Describe()
        {
            string shape = Width > Height + SameSize ? "landscape" : Height > Width + SameSize ? "portrait" : "square";
            return string.Create(CultureInfo.InvariantCulture, $"{Width:0.#} x {Height:0.#} pt ({shape})");
        }
    }
}
