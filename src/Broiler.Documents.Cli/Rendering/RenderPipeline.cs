using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Broiler.Documents.Cli.Composition;
using Broiler.Documents.Cli.Documents;
using Broiler.Documents.Cli.Infrastructure;
using Broiler.Documents.Model;
using Broiler.Graphics;
using Broiler.Media.Image;

namespace Broiler.Documents.Cli.Rendering;

/// <summary>What one render produced.</summary>
public sealed class RenderOutcome : IDisposable
{
    private bool _disposed;

    internal RenderOutcome(LayoutResult layout, IReadOnlyList<BBitmap> pages, IReadOnlyList<string> notes)
    {
        Layout = layout;
        Pages = pages;
        Notes = notes;
    }

    public LayoutResult Layout { get; }

    /// <summary>The rendered pages, in page order. Owned by this object.</summary>
    public IReadOnlyList<BBitmap> Pages { get; }

    /// <summary>Everything the render approximated, skipped, or fell back on.</summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>Paths written, filled in by <see cref="RenderPipeline.Write"/>.</summary>
    public IList<string> WrittenPaths { get; } = new List<string>();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (BBitmap page in Pages)
            page.Dispose();
    }
}

/// <summary>
/// Assembles a render from the command line: page box, fonts, layout settings,
/// page selection, and the encoder, in one place so <c>render</c>,
/// <c>compare --render</c>, and <c>roundtrip --render</c> cannot drift apart.
/// </summary>
/// <remarks>
/// That last point is the reason this type exists rather than three near-copies.
/// A comparison is only meaningful when both sides were rendered identically,
/// and the cheapest way to guarantee that is for there to be exactly one code
/// path that turns options into a rendered page.
/// </remarks>
public sealed class RenderPipeline
{
    private readonly PageSetup _setup;
    private readonly LayoutSettings _settings;
    private readonly FontResolution _fonts;
    private readonly ImageEncodeFormat _format;
    private readonly int _quality;
    private readonly IReadOnlyList<(int First, int Last)>? _pageRanges;

    private RenderPipeline(
        PageSetup setup,
        LayoutSettings settings,
        FontResolution fonts,
        ImageEncodeFormat format,
        int quality,
        IReadOnlyList<(int First, int Last)>? pageRanges)
    {
        _setup = setup;
        _settings = settings;
        _fonts = fonts;
        _format = format;
        _quality = quality;
        _pageRanges = pageRanges;
    }

    /// <summary>The page box in effect, with the final height for a continuous render.</summary>
    public PageSetup Setup => _setup;

    public FontResolution Fonts => _fonts;

    /// <summary>The options that control how a document is drawn.</summary>
    public static OptionSpec[] Specs { get; } =
    {
        OptionSpec.Value(
            "page-size",
            "size",
            "Paper size: " + string.Join(", ", PageSetup.NamedSizeNames) + ", or WxH with a unit (210x297mm).",
            "a4"),
        OptionSpec.Flag("landscape", "Swap the page width and height."),
        OptionSpec.Value("margin", "length", "Page margin: one, two, or four comma-separated lengths.", "1in"),
        OptionSpec.Value("dpi", "n", "Output resolution. 96 makes a point 1.333 pixels.", "96"),
        OptionSpec.Flag(
            "continuous",
            "Render the whole document as one tall page instead of paginating. Localizes a difference to where it is."),
        OptionSpec.Value("background", "color", "Page colour.", "#FFFFFF"),
        OptionSpec.Value("font", "family", "Family for runs that name none.", "sans-serif"),
        OptionSpec.Value("font-size", "points", "Size for runs that name none.", "11"),
        OptionSpec.Value("text-color", "color", "Colour for runs that name none.", "#000000"),
        OptionSpec.Many(
            "font-file",
            "family=path",
            "Pin a font family to a file. Add :bold, :italic, or :bolditalic to the family for one face."),
        OptionSpec.Many("font-dir", "path", "Scan a directory and map every font file it finds by filename."),
        OptionSpec.Value("indent-step", "points", "Width of one indent level.", "18"),
        OptionSpec.Value("tab-stop", "points", "Distance between the default tab stops.", "36"),
        OptionSpec.Value("max-pages", "n", "Stop after this many pages.", "200"),
        OptionSpec.Value("pages", "ranges", "Render only these pages, for example 1-3,7."),
        OptionSpec.Value("image-format", "name", "png, jpeg, or bmp.", "png"),
        OptionSpec.Value("quality", "n", "Encoder quality for lossy formats, 1-100.", "90"),
        OptionSpec.Flag("show-content-box", "Outline the content area. A layout debugging aid."),
        OptionSpec.Flag("no-link-style", "Draw link runs exactly as the model styles them, with no added underline or colour."),
        OptionSpec.Flag(
            "no-synthetic-italic",
            "Do not shear italic runs that have no real italic face. They then draw upright and are invisible in a diff."),
    };

    /// <summary>
    /// Builds a pipeline and installs the font mapping. The mapping is
    /// process-wide and has to be in place before anything measures, so this is
    /// called once, before the first layout.
    /// </summary>
    public static RenderPipeline Create(CommandLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        CodecComposition.RegisterImageCodecs();

        PageSetup setup = PageSetup.FromCommandLine(line);
        FontResolution fonts = FontResolution.FromCommandLine(line);
        fonts.Install();

        double fontSize = line.GetDouble("font-size", 11);
        if (fontSize <= 0)
            throw new UsageException("--font-size must be greater than zero.");

        double indentStep = line.GetDouble("indent-step", 18);
        if (indentStep < 0)
            throw new UsageException("--indent-step cannot be negative.");

        double tabStop = line.GetDouble("tab-stop", 36);
        if (tabStop <= 0)
            throw new UsageException("--tab-stop must be greater than zero.");

        int maxPages = line.GetInt32("max-pages", 200);
        if (maxPages <= 0)
            throw new UsageException("--max-pages must be greater than zero.");

        var settings = new LayoutSettings
        {
            DefaultFontFamily = line.Get("font", "sans-serif")!,
            DefaultFontSizePoints = fontSize,
            DefaultForeground = ColorText.Parse(line.Get("text-color", "#000000")!, "--text-color"),
            IndentStepPoints = indentStep,
            TabStopPoints = tabStop,
            MaxPages = maxPages,
            ShowContentBox = line.Has("show-content-box"),
            DecorateLinks = !line.Has("no-link-style"),
            SynthesizeItalic = !line.Has("no-synthetic-italic"),
            ItalicFaceAvailable = fonts.HasItalicFace,
        };

        string formatName = line.Get("image-format", "png")!;
        if (!DocumentRasterizer.ImageFormats.TryGetValue(formatName, out ImageEncodeFormat format))
        {
            throw new UsageException(
                "--image-format expects " + string.Join(", ", DocumentRasterizer.ImageFormats.Keys) +
                ", not \"" + formatName + "\".");
        }

        int quality = line.GetInt32("quality", 90);
        if (quality is < 1 or > 100)
            throw new UsageException("--quality must be between 1 and 100.");

        return new RenderPipeline(setup, settings, fonts, format, quality, ParsePages(line.Get("pages")));
    }

    /// <summary>Lays a document out and rasterizes the selected pages.</summary>
    public RenderOutcome Render(RichTextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        using var images = new ImageStore();
        var layout = new DocumentLayout(_settings, images);
        LayoutResult result = layout.Layout(document, _setup);

        var pages = new List<BBitmap>();
        using (var rasterizer = new DocumentRasterizer(_settings, images))
        {
            foreach (LayoutPage page in result.Pages.Where(page => IsSelected(page.Number)))
                pages.Add(rasterizer.Render(page, result.Setup));
        }

        var notes = new List<string>(result.Notes);
        notes.AddRange(images.Notes);

        if (pages.Count == 0)
        {
            throw new UsageException(
                "--pages selected no pages; the document laid out to " + result.Pages.Count + " page(s).");
        }

        return new RenderOutcome(result, pages, notes);
    }

    /// <summary>
    /// Writes the rendered pages. A destination containing <c>{page}</c> has it
    /// replaced with the page number; otherwise a multi-page render appends
    /// <c>-1</c>, <c>-2</c> and so on before the extension, and a single-page
    /// render uses the path exactly as given.
    /// </summary>
    public void Write(RenderOutcome outcome, string destination)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(destination);

        IReadOnlyList<int> numbers = outcome.Layout.Pages
            .Where(page => IsSelected(page.Number))
            .Select(page => page.Number)
            .ToArray();

        // Several images concatenated on one stream is not a file anything can
        // open. Say so rather than producing one.
        if (destination == DocumentIo.StandardStreamToken && outcome.Pages.Count > 1)
        {
            throw new UsageException(
                "--out - writes one image to standard output, but this render produced " +
                outcome.Pages.Count + " pages. Select one with --pages, collapse the document " +
                "with --continuous, or write to a path.");
        }

        for (int i = 0; i < outcome.Pages.Count; i++)
        {
            string path = PathFor(destination, numbers[i], outcome.Pages.Count == 1);
            DocumentIo.WriteAllBytes(path, outcome.Pages[i].Encode(_format, _quality));
            outcome.WrittenPaths.Add(path);
        }
    }

    /// <summary>Everything about this render a later reader would need to reproduce or explain it.</summary>
    public JsonObject Manifest(RenderOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        PageSetup setup = outcome.Layout.Setup;
        var pages = new JsonArray();
        for (int i = 0; i < outcome.Pages.Count; i++)
        {
            pages.Add(new JsonObject
            {
                ["widthPixels"] = outcome.Pages[i].Width,
                ["heightPixels"] = outcome.Pages[i].Height,
                ["path"] = i < outcome.WrittenPaths.Count ? outcome.WrittenPaths[i] : null,
            });
        }

        var notes = new JsonArray();
        foreach (string note in outcome.Notes)
            notes.Add(note);

        var fontNotes = new JsonArray();
        foreach (string note in _fonts.Describe())
            fontNotes.Add(note);

        var unmapped = new JsonArray();
        foreach (string family in _fonts.UnmappedRequests)
            unmapped.Add(family);

        return new JsonObject
        {
            ["page"] = new JsonObject
            {
                ["widthPoints"] = Round(setup.WidthPoints),
                ["heightPoints"] = Round(setup.HeightPoints),
                ["marginTopPoints"] = Round(setup.MarginTopPoints),
                ["marginRightPoints"] = Round(setup.MarginRightPoints),
                ["marginBottomPoints"] = Round(setup.MarginBottomPoints),
                ["marginLeftPoints"] = Round(setup.MarginLeftPoints),
                ["dpi"] = Round(setup.Dpi),
                ["continuous"] = setup.Continuous,
                ["background"] = ColorText.Format(setup.Background),
            },
            ["defaults"] = new JsonObject
            {
                ["fontFamily"] = _settings.DefaultFontFamily,
                ["fontSizePoints"] = Round(_settings.DefaultFontSizePoints),
                ["foreground"] = ColorText.Format(_settings.DefaultForeground),
                ["indentStepPoints"] = Round(_settings.IndentStepPoints),
                ["tabStopPoints"] = Round(_settings.TabStopPoints),
                ["decorateLinks"] = _settings.DecorateLinks,
                ["synthesizeItalic"] = _settings.SynthesizeItalic,
            },
            ["fonts"] = new JsonObject
            {
                ["hostFallback"] = FontResolution.DescribeHostFallback(),
                ["mapped"] = fontNotes,
                ["unmappedFamilies"] = unmapped,
            },
            ["layoutPageCount"] = outcome.Layout.Pages.Count,
            ["renderedPageCount"] = outcome.Pages.Count,
            ["truncated"] = outcome.Layout.Truncated,
            ["imageFormat"] = _format.ToString().ToLowerInvariant(),
            ["pages"] = pages,
            ["notes"] = notes,
        };
    }

    private bool IsSelected(int pageNumber) =>
        _pageRanges is null || _pageRanges.Any(range => pageNumber >= range.First && pageNumber <= range.Last);

    private static string PathFor(string destination, int pageNumber, bool single)
    {
        if (destination.Contains("{page}", StringComparison.Ordinal))
        {
            return destination.Replace(
                "{page}",
                pageNumber.ToString("D3", CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        if (single || destination == DocumentIo.StandardStreamToken)
            return destination;

        string directory = Path.GetDirectoryName(destination) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(destination);
        string extension = Path.GetExtension(destination);
        string name = stem + "-" + pageNumber.ToString(CultureInfo.InvariantCulture) + extension;

        return directory.Length == 0 ? name : Path.Combine(directory, name);
    }

    private static IReadOnlyList<(int First, int Last)>? ParsePages(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var ranges = new List<(int, int)>();
        foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int dash = part.IndexOf('-', 1);
            if (dash < 0)
            {
                int only = PageNumber(part);
                ranges.Add((only, only));
                continue;
            }

            int first = PageNumber(part[..dash]);
            string tail = part[(dash + 1)..].Trim();
            int last = tail.Length == 0 ? int.MaxValue : PageNumber(tail);
            if (last < first)
                throw new UsageException("--pages range \"" + part + "\" ends before it starts.");
            ranges.Add((first, last));
        }

        return ranges;
    }

    private static int PageNumber(string token)
    {
        if (!int.TryParse(token.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int page) || page < 1)
            throw new UsageException("--pages expects page numbers of 1 or more, not \"" + token + "\".");
        return page;
    }

    private static double Round(double value) => Math.Round(value, 4);
}
