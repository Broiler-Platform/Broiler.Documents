using System;
using System.Collections.Generic;
using System.Threading;
using Broiler.Documents.Model;
using Broiler.Graphics.Paging;
using Broiler.Graphics.Text;

namespace Broiler.Documents.Pagination;

/// <summary>
/// Lays a <see cref="RichTextDocument"/> out on pages for printing, previewing, or writing to
/// a fixed-page format.
/// </summary>
/// <remarks>
/// <para>
/// This is the layout the Documents CLI has rendered with since it existed, moved here so an
/// application prints with it too: paragraphs, lists, tabs, tables broken between rows,
/// floating shapes, running headers and footers, inline images. Its limits are the CLI
/// layout's (see <see cref="DocumentLayout"/>), and it is not yet the paginator the PDF
/// roadmap's §11.1 asks for - that one adds shaped glyph runs and a declared script matrix.
/// It is the shared place that paginator grows from.
/// </para>
/// <para>
/// Images are decoded through <c>BImageCodecs</c>, which the application composes. A
/// picture no composed codec can decode is drawn as a crossed box and reported in
/// <see cref="PaginatedDocument.Notes"/>, never dropped without a word.
/// </para>
/// </remarks>
public sealed class DocumentPaginator
{
    /// <param name="settings">The typographic defaults; <see cref="CreateDefaultSettings"/> when null.</param>
    /// <param name="honourDocumentDefaults">
    /// Fill the default family and size from the document's own style defaults, as a word
    /// processor printing the document would. The CLI turns this off when the caller pins them.
    /// </param>
    public DocumentPaginator(LayoutSettings? settings = null, bool honourDocumentDefaults = true)
    {
        Settings = settings ?? CreateDefaultSettings();
        HonourDocumentDefaults = honourDocumentDefaults;
    }

    /// <summary>The typographic defaults every document is laid out with.</summary>
    public LayoutSettings Settings { get; }

    /// <summary>True when a document's own default family and size replace the settings'.</summary>
    public bool HonourDocumentDefaults { get; }

    /// <summary>
    /// Settings for a printed page: the CLI layout's, with a real italic face assumed wherever
    /// the host's font enumerator knows one, so italic is sheared only where it would
    /// otherwise draw upright.
    /// </summary>
    public static LayoutSettings CreateDefaultSettings() => new()
    {
        ItalicFaceAvailable = family => BSystemFonts.TryGetFontFile(family, bold: false, italic: true, out _),
    };

    /// <summary>The settings <paramref name="document"/> is laid out with.</summary>
    public LayoutSettings SettingsFor(RichTextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!HonourDocumentDefaults)
            return Settings;

        DocumentStyleDefaults defaults = document.StyleDefaults;
        return Settings with
        {
            DefaultFontSizePoints = defaults.FontSizePoints,
            DefaultFontFamily = defaults.FontFamily ?? Settings.DefaultFontFamily,
        };
    }

    /// <summary>Lays <paramref name="document"/> out on pages of <paramref name="setup"/>.</summary>
    public PaginatedDocument Paginate(RichTextDocument document, BPageSetup setup, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setup);
        return Paginate(document, DocumentPageBox.FromSetup(setup), cancellationToken);
    }

    /// <summary>Lays <paramref name="document"/> out in <paramref name="box"/>.</summary>
    public PaginatedDocument Paginate(RichTextDocument document, DocumentPageBox box, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(box);
        cancellationToken.ThrowIfCancellationRequested();

        LayoutSettings settings = SettingsFor(document);
        var images = new ImageStore();
        try
        {
            LayoutResult layout = new DocumentLayout(settings, images).Layout(document, box);
            cancellationToken.ThrowIfCancellationRequested();
            return new PaginatedDocument(layout, new DocumentPagePainter(settings, images), images);
        }
        catch
        {
            images.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The page setup a document asks for: its own <see cref="PageGeometry"/> when it states a
    /// usable one, otherwise <see cref="BPageSetup.Default"/>.
    /// </summary>
    public static BPageSetup PreferredSetup(RichTextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.PageGeometry is not PageGeometry geometry || !geometry.IsUsable)
            return BPageSetup.Default;

        var margins = new BPageMargins(geometry.MarginLeft, geometry.MarginTop, geometry.MarginRight, geometry.MarginBottom);
        return BPageSetup.ForPageSize(new Broiler.Graphics.Geometry.BSize(geometry.Width, geometry.Height), margins);
    }
}

/// <summary>
/// A document laid out on pages. Each page is painted the first time it is asked for and kept,
/// so a preview that returns to a page does not paint it again.
/// </summary>
public sealed class PaginatedDocument : IBPageSource, IDisposable
{
    private readonly DocumentPagePainter _painter;
    private readonly ImageStore _images;
    private readonly BPage?[] _pages;
    private readonly object _gate = new();
    private bool _disposed;

    internal PaginatedDocument(LayoutResult layout, DocumentPagePainter painter, ImageStore images)
    {
        Layout = layout;
        _painter = painter;
        _images = images;
        _pages = new BPage?[layout.Pages.Count];
    }

    /// <summary>The positioned lines, shapes and cells of every page.</summary>
    public LayoutResult Layout { get; }

    public int PageCount => _pages.Length;

    /// <summary>What the layout approximated or could not do, and the images it could not decode.</summary>
    public IReadOnlyList<string> Notes
    {
        get
        {
            var notes = new List<string>(Layout.Notes);
            lock (_gate)
                notes.AddRange(_images.Notes);
            return notes;
        }
    }

    /// <summary>True when the layout stopped at <see cref="LayoutSettings.MaxPages"/>.</summary>
    public bool Truncated => Layout.Truncated;

    public BPage GetPage(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _pages.Length);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _pages[index] ??= _painter.Paint(Layout.Pages[index], Layout.Setup);
        }
    }

    /// <summary>
    /// Releases the decoded images. Pages already painted keep their own copies and stay
    /// usable; pages not yet painted can no longer be.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _images.Dispose();
        }
    }
}

/// <summary>
/// A rich-text document as something a print dialog can paginate: laid out again for each
/// page setup it is asked for, starting from the page the document states.
/// </summary>
public sealed class RichTextPagedDocument : IBPagedDocument
{
    private readonly RichTextDocument _document;
    private readonly DocumentPaginator _paginator;

    public RichTextPagedDocument(RichTextDocument document, string title, DocumentPaginator? paginator = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        Title = string.IsNullOrWhiteSpace(title) ? "Document" : title;
        _paginator = paginator ?? new DocumentPaginator();
        PreferredSetup = DocumentPaginator.PreferredSetup(document);
    }

    public string Title { get; }

    public BPageSetup PreferredSetup { get; }

    public IBPageSource Paginate(BPageSetup setup, CancellationToken cancellationToken = default) =>
        _paginator.Paginate(_document, setup, cancellationToken);
}
