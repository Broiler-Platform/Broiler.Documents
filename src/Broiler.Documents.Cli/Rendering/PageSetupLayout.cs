using Broiler.Documents.Model;
using Broiler.Documents.Pagination;

namespace Broiler.Documents.Cli.Rendering;

/// <summary>Lays a document out on the page box a command line describes.</summary>
public static class PageSetupLayout
{
    /// <summary>
    /// <see cref="DocumentLayout.Layout"/> on <paramref name="setup"/>'s page box; its
    /// resolution and background are for rasterizing and change nothing here.
    /// </summary>
    public static LayoutResult Layout(this DocumentLayout layout, RichTextDocument document, PageSetup setup)
    {
        System.ArgumentNullException.ThrowIfNull(layout);
        System.ArgumentNullException.ThrowIfNull(setup);
        return layout.Layout(document, setup.ToPageBox());
    }
}
