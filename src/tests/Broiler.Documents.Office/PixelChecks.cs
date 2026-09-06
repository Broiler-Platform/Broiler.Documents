using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Broiler.Documents.Corpus;

namespace Broiler.Documents.Office;

/// <summary>Everything one document's pixel pass produced.</summary>
internal sealed record PixelOutcome(IReadOnlyList<CheckResult> Results, RenderRow? Row);

/// <summary>
/// Whether the same document puts ink in the same places.
/// </summary>
/// <remarks>
/// <para>
/// Be honest about this axis before reading a line of it. Two backends inside
/// poppler itself - splash and cairo - disagreed on 8.39% of the pixels of one
/// PDF at one resolution, and LibreOffice's own PNG export disagreed with
/// poppler on 14.44%. A WPT-style "at least 99% of pixels match" gate would not
/// be strict here, it would be measuring anti-aliasing. Broiler's layout engine
/// and LibreOffice Writer are independent implementations with their own line
/// breaking, justification, font fallback and page-break policy, and raw pixel
/// equality is not an available assertion at any threshold.
/// </para>
/// <para>
/// What survived that measurement is the reason this axis exists at all: the
/// inked first and last rows were <em>identical</em> across all three
/// renderings, and after a blur of about two and a half pixels the differences
/// fell to 0.000% and 0.054%. Text baselines land on the same rows; only glyph
/// rasterisation differs. So the metrics here are ink geometry and a blurred
/// difference, and they were chosen because they were measured to survive a
/// change of rasteriser.
/// </para>
/// <para>
/// The verdict is computed here, over raw PPM and BMP bytes, rather than by
/// <c>broilerdoc compare --mode image</c>. Partly because that comparator is
/// exact-pixel with a per-channel tolerance and cannot express "blur, then
/// threshold", and partly because using the tool under test as the instrument
/// that judges it is a circularity worth avoiding where avoiding it is cheap.
/// The tool is still asked for a heat diff on a failure, but only for a human to
/// look at.
/// </para>
/// </remarks>
internal static class PixelChecks
{
    /// <summary>
    /// The height of one band of the ink profile, in pixels.
    /// </summary>
    /// <remarks>
    /// Fine on purpose. The profile is smoothed afterwards, so the band decides
    /// resolution and <see cref="ProfileSmoothingBands"/> decides tolerance -
    /// two knobs rather than one, which is what lets the metric be both
    /// localised and insensitive to a shift of a few pixels.
    /// </remarks>
    private const int BandHeight = 4;

    /// <summary>
    /// Smoothing radius for the ink profile, in bands - about a line of text at
    /// the resolutions this suite runs at.
    /// </summary>
    /// <remarks>
    /// Six four-pixel bands either side is twenty-four pixels, which at 144 dots
    /// per inch is roughly the height of a twelve-point line. That is the scale
    /// a difference has to reach before it is about the document rather than
    /// about where the typesetter put the baseline.
    /// </remarks>
    private const int ProfileSmoothingBands = 6;

    private const int BlurRadius = 3;
    private const int BlurDelta = 32;

    public static async Task<PixelOutcome> RunAsync(
        BroilerDocTool tool,
        OfficeWorkspace space,
        Rasterizer rasterizer,
        OfficeDocument document,
        OfficeBaseline? baseline,
        bool comparing,
        string notComparing,
        int dpi,
        int maxPages,
        IReadOnlyList<string> pinnedFonts,
        IReadOnlyList<string> toolFonts,
        IReadOnlyList<string> fontDirectories,
        IReadOnlyList<string> fontArguments)
    {
        var results = new List<CheckResult>();
        string id = document.Seed.Id;
        string via = document.Via;
        string prefix = "pixel/" + id + "/" + via;

        // The pinned set plus the faces the office suite brings with it. Both
        // are permitted in an exported PDF; only the second arrives without a
        // seed having asked.
        string[] permittedFonts = pinnedFonts.Concat(toolFonts).ToArray();

        LibreOfficeRun pdf = space.ExportPdf(document);
        CheckResult exported = OfficeWorkspace.Validate(
            "pixel", prefix + "/pdf", pdf, "writer_pdf_Export", 256);
        results.Add(exported);

        if (exported.Outcome != CheckOutcome.Passed)
        {
            results.Add(CheckResult.Skip("pixel", prefix,
                "LibreOffice produced no PDF, so there was nothing to rasterise."));
            return new PixelOutcome(results, null);
        }

        // The font guard, and it is the most valuable check in this file.
        // LibreOffice substitutes a missing family in silence - a made-up name
        // came back as DejaVuSans with exit 0 and an empty stderr - and every
        // metric downstream would then be measuring the machine's font set
        // rather than this component.
        IReadOnlyList<string>? embedded = rasterizer.EmbeddedFonts(pdf.OutputPath!, TimeSpan.FromSeconds(30));
        if (embedded is null)
        {
            results.Add(CheckResult.Skip("pixel", prefix + "/fonts",
                "pdffonts is not installed beside pdftoppm, so what LibreOffice actually embedded " +
                "could not be checked."));
        }
        else if (pinnedFonts.Count == 0)
        {
            results.Add(CheckResult.Skip("pixel", prefix + "/fonts",
                "no font set was pinned with --font-dir, so what LibreOffice embedded cannot be " +
                "held against anything."));
        }
        else if (document.Seed.Expect.SubstitutesFonts)
        {
            // The seed says outright that it asks for glyphs no pinned family
            // has, and the guard steps aside rather than reporting a
            // substitution nobody could have avoided. A skip and not a pass:
            // this document's later pixel numbers were measured through a face
            // this suite did not choose, and a reader should know that.
            results.Add(CheckResult.Skip("pixel", prefix + "/fonts",
                "the seed declares that it asks for glyphs no pinned family covers, so LibreOffice " +
                "had to substitute. " + document.Seed.Why));
        }
        else
        {
            string[] strangers = embedded
                .Where(font => !permittedFonts.Any(pinned =>
                    font.Replace(" ", string.Empty, StringComparison.Ordinal)
                        .Contains(pinned.Replace(" ", string.Empty, StringComparison.Ordinal),
                            StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            results.Add(strangers.Length == 0
                ? CheckResult.Pass("pixel", prefix + "/fonts")
                : CheckResult.Fail("pixel", prefix + "/fonts",
                    "LibreOffice embedded " + string.Join(", ", strangers) + ", which is outside the " +
                    "pinned set. It substitutes a missing family without a word, so this is the " +
                    "guard firing rather than the codec."));
        }

        RasterRun raster = rasterizer.Render(
            pdf.OutputPath!, Path.Combine(space.Area("lo-px", id, via), id), dpi, maxPages,
            TimeSpan.FromSeconds(180));

        if (raster.TimedOut || raster.ExitCode != 0 || raster.Pages.Count == 0)
        {
            results.Add(CheckResult.Fail("pixel", prefix,
                "pdftoppm produced no pages (exit " +
                raster.ExitCode.ToString(CultureInfo.InvariantCulture) + ").",
                raster.CommandLine()));
            return new PixelOutcome(results, null);
        }

        string ourDirectory = space.Area("bd-px", id, via);

        // Cleared before the render, for the reason the rasteriser clears its
        // own output: a document that lost a page would otherwise still show the
        // previous run's last page, and the page counts would agree over a set
        // of bitmaps that came from two different renders.
        foreach (string stale in Directory.GetFiles(ourDirectory, id + "-*.bmp"))
        {
            try
            {
                File.Delete(stale);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // Left behind rather than failing the run. The page-count check
                // below is what notices, and it says something a reader can act
                // on.
            }
        }

        var arguments = new List<string>
        {
            "render", document.Path,
            "--out", Path.Combine(ourDirectory, id + "-{page}.bmp"),
            "--image-format", "bmp",
            "--dpi", dpi.ToString(CultureInfo.InvariantCulture),
            "--max-pages", maxPages.ToString(CultureInfo.InvariantCulture),
            "--background", "#FFFFFF",
            "--json", "--quiet",
        };

        // The same faces LibreOffice was given. Without this the two sides draw
        // the same document with different faces, and every number below is then
        // a fact about the machine rather than about either implementation.
        //
        // --font-dir first for breadth, then the explicit --font-file mappings,
        // which win. The directory scan maps by file name and so misses a family
        // whose name carries a space; the explicit list is what closes that and
        // what pins the CSS generic a document with no stated family falls back
        // to.
        foreach (string directory in fontDirectories)
        {
            arguments.Add("--font-dir");
            arguments.Add(directory);
        }

        arguments.AddRange(fontArguments);

        ToolRun rendered = await tool.RunAsync(arguments.ToArray()).ConfigureAwait(false);
        if (rendered.ExitCode != 0)
        {
            results.Add(CheckResult.Fail("pixel", prefix,
                "render exited " + rendered.ExitCode.ToString(CultureInfo.InvariantCulture) + ".",
                rendered.CommandLine()));
            return new PixelOutcome(results, null);
        }

        JsonElement? json = rendered.Json();
        string[] unmapped = Unmapped(json);
        if (fontDirectories.Count == 0)
        {
            // Not a pass. With no pinned set there is nothing for a mapping to
            // be right or wrong about, and reporting that as a pass would put a
            // green tick against the one property this axis most depends on.
            results.Add(CheckResult.Skip("pixel", prefix + "/mapping",
                "no font set was pinned with --font-dir, so the render had nothing to map to."));
        }
        else if (unmapped.Except(toolFonts, StringComparer.OrdinalIgnoreCase).Any())
        {
            // In the corpus suite an unmapped family is a skip, because the font
            // set there is whatever the machine has. Here the font set is ours,
            // so it is a failure.
            results.Add(CheckResult.Fail("pixel", prefix + "/mapping",
                "the render left " +
                string.Join(", ", unmapped.Except(toolFonts, StringComparer.OrdinalIgnoreCase)) +
                " unmapped, so it drew with the host fallback face while LibreOffice drew with the " +
                "pinned one.",
                rendered.CommandLine()));
        }
        else if (unmapped.Length > 0)
        {
            // Only tool fonts left unmapped, and that is an asymmetry rather
            // than a defect: LibreOffice draws a list bullet from a symbol font
            // it ships with itself, and a pinned directory assembled from font
            // packages cannot contain it. Neither side chose it and the document
            // did not ask for it.
            //
            // A skip and not a pass, because the bullets on this page really
            // were drawn by two different faces, and the numbers below are worth
            // that much less. Saying so is the point.
            results.Add(CheckResult.Skip("pixel", prefix + "/mapping",
                "the render left " + string.Join(", ", unmapped) + " unmapped, which the office " +
                "suite supplies to LibreOffice and not to this component. The bullets on this page " +
                "are drawn by two different faces."));
        }
        else
        {
            results.Add(CheckResult.Pass("pixel", prefix + "/mapping"));
        }

        string[] ourPages = Directory
            .GetFiles(ourDirectory, id + "-*.bmp")
            .OrderBy(path => Ordinal(path), Comparer<int>.Default)
            .ToArray();

        // Page count first. It is a small integer that does not drift, and a
        // document that came out with the wrong number of pages makes every
        // per-page comparison below meaningless rather than merely worse.
        //
        // Held against the baseline like everything else, rather than being an
        // unconditional failure. A pagination difference somebody has looked at
        // and classified is exactly what a baseline row is for, and a check that
        // failed regardless would mean the suite could never reach green while
        // one existed - which is another way of saying nobody would run it.
        int theirPages = raster.Pages.Count;
        int layoutPages = Number(json, "layoutPageCount") ?? ourPages.Length;
        RenderRow? recorded = comparing ? baseline?.FindRender(id, via) : null;

        string counted =
            "LibreOffice paginated to " + theirPages.ToString(CultureInfo.InvariantCulture) +
            " page(s) and this component to " + ourPages.Length.ToString(CultureInfo.InvariantCulture) +
            " (layout said " + layoutPages.ToString(CultureInfo.InvariantCulture) + ").";

        if (!comparing)
        {
            results.Add(theirPages == ourPages.Length
                ? CheckResult.Pass("pixel", prefix + "/pages")
                : CheckResult.Skip("pixel", prefix + "/pages", notComparing));
        }
        else if (recorded is null)
        {
            results.Add(theirPages == ourPages.Length
                ? CheckResult.Pass("pixel", prefix + "/pages")
                : CheckResult.Fail("pixel", prefix + "/pages",
                    "no baseline row, so the two were expected to paginate alike. " + counted,
                    rendered.CommandLine()));
        }
        else if (recorded.BroilerdocPages == ourPages.Length && recorded.LibreOfficePages == theirPages)
        {
            results.Add(CheckResult.Pass("pixel", prefix + "/pages"));
        }
        else
        {
            results.Add(CheckResult.Fail("pixel", prefix + "/pages",
                "the pagination is not the one the baseline records. " + counted +
                " The baseline records " +
                recorded.BroilerdocPages.ToString(CultureInfo.InvariantCulture) + " here and " +
                recorded.LibreOfficePages.ToString(CultureInfo.InvariantCulture) + " there.",
                rendered.CommandLine()));
        }

        // The other half of the seed's stated expectation. A seed that claims to
        // reach a third page and does not has either lost content or stopped
        // paginating, and both are worth a name; a seed that claims one page and
        // sprawls is the same statement the other way round.
        // More than one page, which is what the word says. An earlier reading of
        // "reaches a third page" failed the seed whose whole point is a single
        // explicit break, and a threshold that disagrees with the name of the
        // field it reads is a trap for whoever writes the next seed.
        bool paginated = ourPages.Length > 1 || theirPages > 1;
        results.Add(document.Seed.Expect.Multipage == paginated
            ? CheckResult.Pass("pixel", prefix + "/multipage")
            : CheckResult.Fail("pixel", prefix + "/multipage",
                document.Seed.Expect.Multipage
                    ? "the seed states it paginates and neither side reached a second page (" +
                      ourPages.Length.ToString(CultureInfo.InvariantCulture) + " here, " +
                      theirPages.ToString(CultureInfo.InvariantCulture) + " there)."
                    : "the seed states it fits on one page and one side needed more (" +
                      ourPages.Length.ToString(CultureInfo.InvariantCulture) + " here, " +
                      theirPages.ToString(CultureInfo.InvariantCulture) + " there).",
                rendered.CommandLine()));

        int pages = Math.Min(theirPages, ourPages.Length);
        if (pages == 0)
        {
            results.Add(CheckResult.Skip("pixel", prefix, "neither side produced a page to compare."));
            return new PixelOutcome(results, null);
        }

        var measurements = new List<PageMeasurement>();
        int blankPairs = 0;
        for (int page = 0; page < pages; page++)
        {
            RawImage theirs = RawImage.Read(raster.Pages[page]);
            RawImage ours = RawImage.Read(ourPages[page]);
            if (InkProfile.Box(theirs).IsEmpty && InkProfile.Box(ours).IsEmpty)
                blankPairs++;

            measurements.Add(Measure(page + 1, theirs, ours));
        }

        // Two blank pages agree on every metric this file has, which is true and
        // useless: a document that rendered to nothing on both sides would score
        // the best band five times over and pass. Every seed in this corpus puts
        // ink on every page it claims, so a blank pair is a broken render rather
        // than a document, and it is reported as one instead of as agreement.
        results.Add(blankPairs == 0
            ? CheckResult.Pass("pixel", prefix + "/ink")
            : CheckResult.Fail("pixel", prefix + "/ink",
                blankPairs.ToString(CultureInfo.InvariantCulture) + " of " +
                pages.ToString(CultureInfo.InvariantCulture) + " compared page(s) are blank on both " +
                "sides. Two blank pages agree on every metric here, so this is reported rather than " +
                "scored - no seed in this corpus renders to an empty page.",
                rendered.CommandLine()));

        PageMeasurement worst = measurements
            .OrderByDescending(measurement => measurement.Severity)
            .First();

        var bands = new RenderBands(
            worst.Geometry, worst.InkBoxBand, worst.InkProfileBand, worst.CoverageBand, worst.BlurBand);

        var observed = new RenderObserved(
            worst.InkBoxDelta, worst.InkProfileSimilarity, worst.CoverageDelta, worst.BlurRatio);

        var row = new RenderRow(
            id, via, ourPages.Length, theirPages, worst.Page, bands, observed,
            BaselineState.SuspectedDefect, null, string.Empty, null, null);

        results.Add(Hold(prefix, row, baseline, comparing, notComparing));

        if (results.Any(result => result.Outcome == CheckOutcome.Failed) && worst.Page <= pages)
        {
            await TriageAsync(tool, space, document, rasterizer, pdf.OutputPath!, ourPages[worst.Page - 1],
                worst.Page, dpi).ConfigureAwait(false);
        }

        return new PixelOutcome(results, row);
    }

    /// <summary>
    /// Holds one render against the committed baseline, by band rather than by
    /// number.
    /// </summary>
    /// <remarks>
    /// A recorded number would fail on every poppler point release, every font
    /// package update and every LibreOffice patch, and a suite that cries wolf
    /// on its own toolchain teaches everybody to ignore it. A band is a policy,
    /// and the raw numbers are kept beside it so a reviewer can see whether a
    /// row is comfortably classified or sitting on a line.
    /// </remarks>
    private static CheckResult Hold(
        string name, RenderRow observed, OfficeBaseline? baseline, bool comparing,
        string notComparing)
    {
        if (!comparing)
            return CheckResult.Skip("pixel", name, notComparing);

        RenderRow? row = baseline?.FindRender(observed.Seed, observed.Via);
        bool clean = Bands.IsBest(observed.Bands) &&
                     observed.BroilerdocPages == observed.LibreOfficePages;

        if (row is null)
        {
            return clean
                ? CheckResult.Pass("pixel", name)
                : CheckResult.Fail("pixel", name,
                    "no baseline row, so the two renderings were expected to agree and they did not." +
                    Describe(observed));
        }

        int recorded = Bands.Rank(row.Bands);
        int now = Bands.Rank(observed.Bands);

        if (Bands.Same(row.Bands, observed.Bands) &&
            row.BroilerdocPages == observed.BroilerdocPages &&
            row.LibreOfficePages == observed.LibreOfficePages)
        {
            return CheckResult.Pass("pixel", name);
        }

        if (now < recorded)
        {
            return CheckResult.Fail("pixel", name,
                "the two renderings agree better than the baseline records. An improvement nobody " +
                "records is an improvement nobody can show: update the baseline." + Describe(observed));
        }

        return CheckResult.Fail("pixel", name,
            "the two renderings agree less well than the baseline records." + Describe(observed) +
            "\n      recorded: " + Bands.Describe(row.Bands));
    }

    private static string Describe(RenderRow row) =>
        (row.Bands.Geometry == "off"
            // Said first, and said plainly, because the other four numbers were
            // measured over pages of different sizes and are consequences of
            // this rather than findings beside it. A reader who takes the ink
            // box at face value here is reading a fact about two page boxes.
            ? "\n      The two sides used different page boxes, so every number " +
              "below is downstream of that and not a separate finding."
            : string.Empty) +
        "\n      " + Bands.Describe(row.Bands) +
        "\n      worst page " + row.WorstPage.ToString(CultureInfo.InvariantCulture) +
        ": ink box off by " + row.Observed.InkBoxDeltaPx.ToString(CultureInfo.InvariantCulture) +
        " px, profile similarity " +
        row.Observed.InkProfileSimilarity.ToString("0.000", CultureInfo.InvariantCulture) +
        ", coverage delta " +
        row.Observed.CoverageDelta.ToString("0.0000", CultureInfo.InvariantCulture) +
        ", blurred difference " +
        row.Observed.BlurDiffRatio.ToString("0.0000", CultureInfo.InvariantCulture) +
        "\n      pages: " + row.BroilerdocPages.ToString(CultureInfo.InvariantCulture) +
        " here, " + row.LibreOfficePages.ToString(CultureInfo.InvariantCulture) + " there";

    private static PageMeasurement Measure(int page, RawImage theirs, RawImage ours)
    {
        InkBox theirBox = InkProfile.Box(theirs);
        InkBox ourBox = InkProfile.Box(ours);
        int boxDelta = InkProfile.BoxDelta(theirBox, ourBox);

        double similarity = InkProfile.CosineSimilarity(
            InkProfile.Smooth(InkProfile.RowProfile(theirs, BandHeight), ProfileSmoothingBands),
            InkProfile.Smooth(InkProfile.RowProfile(ours, BandHeight), ProfileSmoothingBands));

        double coverage = Math.Abs(InkProfile.Coverage(theirs) - InkProfile.Coverage(ours));
        double blur = InkProfile.BlurredDifferenceRatio(theirs, ours, BlurRadius, BlurDelta);

        return new PageMeasurement(
            page,
            Bands.Geometry(Math.Abs(theirs.Width - ours.Width), Math.Abs(theirs.Height - ours.Height)),
            Bands.InkBox(boxDelta),
            Bands.InkProfile(similarity),
            Bands.Coverage(coverage),
            Bands.BlurDiff(blur),
            boxDelta,
            similarity,
            coverage,
            blur);
    }

    /// <summary>
    /// Draws the difference a human will actually look at.
    /// </summary>
    /// <remarks>
    /// This is the one place the tool under test is used on the pixel axis, and
    /// it is deliberately kept away from the verdict. The heat style pays for
    /// itself the first time somebody looks at one: the difference between "the
    /// glyphs are drawn slightly differently" and "the lines drift apart down
    /// the page" is obvious in the picture and invisible in any single number.
    /// </remarks>
    private static async Task TriageAsync(
        BroilerDocTool tool, OfficeWorkspace space, OfficeDocument document, Rasterizer rasterizer,
        string pdfPath, string ourPage, int page, int dpi)
    {
        string directory = space.Area("failures", document.Seed.Id, document.Via);
        RasterRun single = rasterizer.RenderPagePng(
            pdfPath, Path.Combine(directory, "libreoffice"), page, dpi, TimeSpan.FromSeconds(60));

        if (single.Pages.Count == 0)
            return;

        await tool.RunAsync(
            "compare", single.Pages[0], ourPage,
            "--mode", "image",
            "--diff", Path.Combine(directory, "diff-p" + page.ToString(CultureInfo.InvariantCulture) + ".png"),
            "--diff-style", "heat",
            "--allow-size-difference",
            "--quiet").ConfigureAwait(false);
    }

    private static string[] Unmapped(JsonElement? json)
    {
        if (json is not { } element || !element.TryGetProperty("render", out JsonElement render) ||
            !render.TryGetProperty("fonts", out JsonElement fonts) ||
            !fonts.TryGetProperty("unmappedFamilies", out JsonElement families) ||
            families.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return families.EnumerateArray()
            .Where(family => family.ValueKind == JsonValueKind.String)
            .Select(family => family.GetString()!)
            .ToArray();
    }

    private static int? Number(JsonElement? json, string property)
    {
        if (json is not { } element || !element.TryGetProperty("render", out JsonElement render) ||
            !render.TryGetProperty(property, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return value.GetInt32();
    }

    /// <summary>
    /// The page number in a rendered file name.
    /// </summary>
    /// <remarks>
    /// broilerdoc pads its <c>{page}</c> token to three digits and pdftoppm's
    /// padding varies with the page count, so both sides are ordered by the
    /// integer rather than by the string. Ordering ten bitmaps as text puts page
    /// 10 before page 2 and every later comparison is then off by a page, which
    /// is a failure that looks exactly like a layout bug.
    /// </remarks>
    private static int Ordinal(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        int dash = name.LastIndexOf('-');
        return dash >= 0 && int.TryParse(
            name[(dash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : 0;
    }

    private sealed record PageMeasurement(
        int Page,
        string Geometry,
        string InkBoxBand,
        string InkProfileBand,
        string CoverageBand,
        string BlurBand,
        int InkBoxDelta,
        double InkProfileSimilarity,
        double CoverageDelta,
        double BlurRatio)
    {
        /// <summary>How bad this page is, for picking the one worth recording.</summary>
        public int Severity => Bands.Rank(
            new RenderBands(Geometry, InkBoxBand, InkProfileBand, CoverageBand, BlurBand));
    }
}
