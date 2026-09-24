using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Broiler.Documents.Model;
using Broiler.Documents.Pdf.Filters;
using Broiler.Documents.Pdf.Structure;
using Broiler.Documents.Pdf.Syntax;
using Broiler.Documents.Pdf.Text;
using Broiler.Documents.Resources;

namespace Broiler.Documents.Pdf;

/// <summary>
/// Drives one read: load the cross-reference data, settle security, walk the page
/// tree, interpret content, and project the result into the rich-text model.
/// </summary>
/// <remarks>
/// The order of the first two steps is a security property, not a convenience.
/// Encryption is decided from the trailers alone, before any object stream is
/// resolved and before the Catalog, metadata, fonts, images, annotations, or
/// content are touched, so an encrypted document is rejected without a single
/// decrypt-dependent object having been interpreted (PDF roadmap §8.1).
/// </remarks>
internal static class PdfReader
{
    public static PdfReadResult Read(
        byte[] data,
        PdfReadOptions options,
        PdfCodecServices services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(services);

        var diagnostics = new PdfDiagnosticSink(options.PdfLimits.MaxDiagnostics);
        var budget = new PdfWorkBudget(options.PdfLimits, cancellationToken);

        try
        {
            return ReadCore(data, options, services, budget, diagnostics, cancellationToken);
        }
        catch (PdfLimitExceededException e)
        {
            diagnostics.Error(PdfDiagnosticCodes.Limit, e.Message);
            return Rejected(diagnostics);
        }
        catch (OperationCanceledException)
        {
            diagnostics.Error(PdfDiagnosticCodes.Cancelled, "The read was cancelled before it produced a usable document.");
            return Rejected(diagnostics);
        }
    }

    private static PdfReadResult ReadCore(
        byte[] data,
        PdfReadOptions options,
        PdfCodecServices services,
        PdfWorkBudget budget,
        PdfDiagnosticSink diagnostics,
        CancellationToken cancellationToken)
    {
        if (data.LongLength > options.PdfLimits.MaxInputBytes)
        {
            diagnostics.Error(
                PdfDiagnosticCodes.Limit,
                $"The input is larger than the {options.PdfLimits.MaxInputBytes}-byte limit for a PDF read.");
            return Rejected(diagnostics);
        }

        var pipeline = new PdfFilterPipeline(services.StreamFilters, cancellationToken);
        PdfObjectStore? store = PdfObjectStore.Load(data, budget, diagnostics, pipeline);
        store?.FontProgramReader = services.FontProgramReader;
        store?.ColorProfileReader = services.ColorProfileReader;
        if (store is null)
        {
            diagnostics.Error(PdfDiagnosticCodes.HeaderMissing, "The input does not begin with a PDF header.");
            return Rejected(diagnostics);
        }

        // Security first, before any content-bearing object is resolved.
        if (store.IsEncrypted)
        {
            diagnostics.Error(
                PdfDiagnosticCodes.EncryptionUnsupported,
                "The document is encrypted. This release rejects encrypted input before interpreting any of its content, and reports nothing about the document or its password.");
            return Rejected(diagnostics);
        }

        if (store.Resolve(store.Trailer["Root"]) is not PdfDictionary catalog)
        {
            diagnostics.Error(PdfDiagnosticCodes.StructureMalformed, "The document has no usable catalog.");
            return Rejected(diagnostics);
        }

        PdfVersion declared = ResolveVersion(store, catalog, diagnostics);
        IReadOnlyList<PdfExtensionDeclaration> extensions = PdfVersionResolver.ReadExtensions(store, catalog);
        if (extensions.Count > 0)
        {
            diagnostics.Skipped(
                PdfDiagnosticCodes.ExtensionUnsupported,
                $"The catalog declares {extensions.Count} developer extensions. They were inventoried; no extension-defined behavior was enabled.");
        }

        DocumentMetadata metadata = PdfMetadataReader.Read(store, catalog);
        List<PdfPage> pages = PdfPageTree.Collect(store, catalog);
        if (pages.Count == 0)
        {
            diagnostics.Error(PdfDiagnosticCodes.StructureMalformed, "The document contains no pages.");
            return Rejected(diagnostics);
        }

        string? structureTree = NoteDocumentLevelFeatures(store, catalog, diagnostics);

        PdfUriPolicy policy = options.UriPolicy ?? services.UriPolicy;
        var resources = new DocumentConversionContextBuilder(options.ResourcePolicy);

        // The document's own default configuration decides which layers are part
        // of its presentation. A caller who wants every layer regardless says so,
        // and then nothing is treated as off.
        PdfOptionalContent optionalContent =
            PdfOptionalContent.Read(store, catalog, enforced: !options.IncludeHiddenOptionalContent);

        store.Features.NoteOptionalContentConfiguration(
            optionalContent.GroupCount, optionalContent.OffGroupCount);

        // Read for its sequence alone. A tagged document has already answered the
        // question the geometric pass can only infer, and taking the answer costs
        // nothing that roles or conformance would.
        PdfStructureTree? structure = PdfStructureTree.Read(store, catalog, pages);

        var interpreter = new PdfContentInterpreter(store, resources, optionalContent);
        var paragraphs = new List<RichTextParagraph>();
        int emptyPages = 0;
        int declaredOrderPages = 0;
        int inferredOrderPages = 0;
        var artifactPages = new SortedSet<int>();
        var tables = new List<DocumentTable>();
        PdfTableGrid? openGrid = null;
        int openTable = -1;
        bool pendingBreak = false;

        // A page's running head and footer are held out of the body until every
        // page is known, because only then can it be said whether they repeat.
        var furniture = new PdfPageFurniture();

        // The page the document is stated on waits for the same thing: which of
        // the bands are running content decides where the body's column ends.
        var pageSizes = new PdfPageGeometryReader();

        for (int i = 0; i < pages.Count; i++)
        {
            budget.ThrowIfCancelled();
            PdfPage page = pages[i];

            // Everything raised from here down - a skipped image, an unreadable
            // font program, a dropped path - belongs to this page, and says so.
            diagnostics.CurrentPage = i + 1;
            pageSizes.AddPage(i, page.DisplayWidth, page.DisplayHeight);

            IReadOnlyList<PdfTextFragment> fragments = interpreter.Run(page);
            if (!options.IncludeInvisibleText)
                fragments = FilterVisible(fragments);

            store.Features.NoteTurnedText(TurnedCharacters(fragments), i + 1);

            // A picture under the whole page is its background rather than part of
            // its flow, which the page's text is what shows; the rest are admitted.
            IReadOnlyList<PdfPlacedImage> images = PlacePictures(
                interpreter.Pictures, fragments, page, resources, store.Features, i + 1);

            // Annotations are page-level objects: whether one carries an
            // unapplied redaction or an executable action has nothing to do
            // with what the content stream drew. Reading them below the
            // emptiness test meant a page that drew nothing was never
            // inspected at all - and a scanned page is empty here whenever
            // this build refuses its image, which is the ordinary shape of a
            // supposedly redacted document. It stays below Run: Pictures
            // is the interpreter's own list, cleared at the top of each page.
            List<PdfLinkRegion> links = PdfAnnotationReader.Read(store, page, policy, optionalContent);

            if (fragments.Count == 0 && images.Count == 0)
            {
                emptyPages++;
                continue;
            }

            furniture.CountPage(i);
            IReadOnlyList<PdfPaintedPath> paths = interpreter.PaintedPaths;
            double pageArea = page.DisplayWidth * page.DisplayHeight;

            // A ruled grid is the one arrangement of dropped artwork the model
            // can carry, and it settles this page's reading order as well: cells
            // are read row-major, which is what the geometric pass cannot infer
            // and what a table defeats it with. Found before anything is turned
            // into spans, because both of the next two steps need it.
            List<PdfTableGrid> grids = PdfTableGrid.Detect(paths, fragments, pageArea);

            // Underline and strikethrough are painted rules rather than text
            // state, so they are read back onto the runs before the runs become
            // styled spans - and after the grids, because a table's own rules
            // are the same shape and must not be mistaken for one.
            bool[]? decorations = PdfTextDecorations.Apply(fragments, paths, grids);

            // A fill beneath a run is the run's background, and one in the
            // paper's colour on bare paper painted nothing at all. Both are read
            // before the runs become spans, and after the grids, whose cell
            // shades are the cells' shading already.
            bool[]? backgrounds = PdfPaintedFills.ReadBackgrounds(fragments, paths, grids, pageArea);
            bool[]? bare = PdfPaintedFills.FindBarePaper(fragments, paths, interpreter.Marks, interpreter.MarksTruncated);

            // A page whose fragments the tree accounts for in full is read in the
            // order it declares. One it accounts for only partly falls back
            // whole: mixing a declared order with an inferred one produces a
            // sequence neither the document nor the heuristic asked for.
            int pageIndex = i;
            Func<PdfTextFragment, int>? declaredOrder = structure is not null && structure.Covers(i, fragments)
                ? fragment => structure.OrderOf(pageIndex, fragment.Mcid)
                : null;
            Func<PdfTextFragment, int>? declaredBlock = declaredOrder is null
                ? null
                : fragment => structure!.BlockOf(pageIndex, fragment.Mcid);

            // Furniture the page draws above or below everything else is held
            // apart. It is not between anything the body holds, so a table cut by
            // the page boundary is judged on what the body drew around it.
            (List<PdfTextFragment> body, List<PdfTextFragment> head, List<PdfTextFragment> foot) = SplitFurniture(fragments);

            pageSizes.AddContent(
                i,
                PdfPageGeometryReader.Ink.Of(body).With(grids).With(images),
                PdfPageGeometryReader.Ink.Of(head),
                PdfPageGeometryReader.Ink.Of(foot));

            // A table broken by a page boundary is one table, and the model
            // holds a table as one contiguous run of paragraphs - so the join
            // has to be decided before anything is emitted between the halves,
            // including the empty paragraph a mapped page break would be. A frame
            // is closed on all four sides by definition, and continues nothing.
            bool joins = openGrid is not null &&
                grids.Count > 0 &&
                !openGrid.IsFrame &&
                !grids[0].IsFrame &&
                Opens(body, images, grids[0]) &&
                openGrid.Continues(grids[0]);

            if (pendingBreak && !joins)
                paragraphs.Add(RichTextParagraph.Empty);

            pendingBreak = false;

            furniture.AddHeader(i, paragraphs.Count, ProjectFurniture(head, links, options.Limits.MaxParagraphCount));

            int before = tables.Count;
            if (grids.Count > 0)
            {
                paragraphs.AddRange(PdfTableProjector.Project(
                    body, links, images, grids,
                    options.Limits.MaxParagraphCount, paragraphs.Count, tables, declaredOrder, declaredBlock));
            }
            else
            {
                List<PdfTextLine> lines = declaredOrder is not null
                    ? PdfReadingOrder.BuildLinesInDeclaredOrder(body, links, declaredOrder, declaredBlock)
                    : PdfReadingOrder.BuildLines(body, links);

                paragraphs.AddRange(PdfModelProjector.Project(lines, images, false, options.Limits.MaxParagraphCount));
            }

            if (declaredOrder is not null)
            {
                declaredOrderPages++;
                if (HasArtifact(body))
                    artifactPages.Add(i);
            }
            else if (fragments.Count > 0)
            {
                inferredOrderPages++;
            }

            if (joins && tables.Count > before)
            {
                tables[openTable] = Join(tables[openTable], tables[before]);
                tables.RemoveAt(before);
                store.Features.NoteTableContinued(i + 1);
            }

            foreach (PdfTableGrid grid in grids)
                store.Features.NoteTable(grid.Rows, grid.Columns, grid.IsInferred, grid.IsFrame, i + 1);

            NoteArtworkFates(store.Features, i + 1, paths, grids, decorations, backgrounds, bare);

            furniture.AddFooter(i, paragraphs.Count, ProjectFurniture(foot, links, options.Limits.MaxParagraphCount));

            // What the next page would have to continue: a grid with nothing
            // drawn below it, which is what a table cut off by the page edge
            // looks like and what a table the page finished with does not.
            bool closes = grids.Count > 0 && !grids[^1].IsFrame && Closes(body, images, grids[^1]);
            openGrid = closes ? grids[^1] : null;
            openTable = closes ? tables.Count - 1 : -1;
            pendingBreak = options.MapPageBreaks && i < pages.Count - 1;
        }

        if (pendingBreak && paragraphs.Count > 0)
            paragraphs.Add(RichTextParagraph.Empty);

        // Every page is known, so what repeats is known: that becomes the
        // document's running content, and the rest goes back where it was drawn.
        PdfPageFurniture.Settlement settled = furniture.Settle(paragraphs, tables);
        foreach (int page in settled.PagesInBody)
            artifactPages.Add(page);

        PdfPageGeometryReader.Result statedPage = pageSizes.Settle(settled.HeaderPages > 0, settled.FooterPages > 0);

        // Back to document scope, and the one point where the constructs the
        // pages recognized but did not implement become diagnostics. Draining
        // here, before the status is decided below, keeps a skipped construct
        // making the read Partial exactly as an immediate report did.
        diagnostics.CurrentPage = null;
        store.Features.Report(diagnostics);

        if (statedPage.MixedSizes is string mixed)
            diagnostics.Info(PdfDiagnosticCodes.PageSizeMixed, mixed);

        if (emptyPages > 0)
        {
            diagnostics.Skipped(
                PdfDiagnosticCodes.TextOcrRequired,
                $"{emptyPages} of {pages.Count} pages carried no extractable text. A scanned page needs OCR, which is outside this release's scope.");
        }

        if (paragraphs.Count > 0 || structureTree is not null)
        {
            var order = new StringBuilder();

            // Which of the two produced the order is the fact a reader needs, and
            // on a partly tagged document the answer is genuinely "both", per
            // page. Saying so beats picking whichever was more common.
            if (declaredOrderPages > 0)
            {
                string plural = declaredOrderPages == 1 ? string.Empty : "s";
                order.Append(CultureInfo.InvariantCulture,
                    $"Reading order on {declaredOrderPages} page{plural} came from the document's own structure tree, which states it. ");
                order.Append(
                    "Only the sequence was taken from it, and which of its elements each run was declared in: paragraphs break where those elements do, the order of glyphs within a block is still geometric, and no role was read. ");
            }

            if (inferredOrderPages > 0)
            {
                if (declaredOrderPages > 0)
                {
                    string plural = inferredOrderPages == 1 ? string.Empty : "s";
                    order.Append(CultureInfo.InvariantCulture,
                        $"On {inferredOrderPages} further page{plural} it was inferred from page geometry, the tree not accounting for every run drawn there. ");
                }
                else
                {
                    order.Append("Reading order was inferred from page geometry. ");
                }

                order.Append(
                    "PDF states where glyphs are drawn, not what order they are read in, so paragraph and column grouping is a documented heuristic. ");
            }

            // Said because it is the part a reader would otherwise have to infer
            // from a silence: the tree covers the page's content, and what it
            // does not cover it is not supposed to cover.
            AppendFurniture(order, settled, artifactPages.Count);

            // One code, one note. The structure tree is the reason the heuristic
            // was still needed on a file that could have said better, so it reads
            // as a clause of that sentence rather than as a second diagnostic the
            // sink would collapse into a count.
            if (structureTree is not null)
                order.Append(structureTree);

            if (order.Length == 0)
                order.Append("Reading order was inferred from page geometry.");

            diagnostics.Info(PdfDiagnosticCodes.ReadingOrderHeuristic, order.ToString().TrimEnd());
        }

        RichTextDocument document = paragraphs.Count == 0
            ? RichTextDocument.Empty
            : RichTextDocument.FromParagraphs(paragraphs);

        if (tables.Count > 0)
            document = document.WithTables(tables);

        if (settled.RunningContent is not null)
            document = document.WithRunningContent(settled.RunningContent);

        // The page every page was drawn on, or most of them: without it every
        // consumer falls back to a page of its own, and a landscape document is
        // reflowed onto portrait paper.
        if (statedPage.Geometry is PageGeometry geometry)
            document = document.WithPageGeometry(geometry);

        DocumentResultStatus status = paragraphs.Count == 0
            ? DocumentResultStatus.Partial
            : diagnostics.HasSkips || diagnostics.HasErrors || store.WasRecovered
                ? DocumentResultStatus.Partial
                : DocumentResultStatus.Success;

        return new PdfReadResult(
            document,
            status,
            metadata,
            declared,
            pages.Count,
            extensions,
            diagnostics.Build(),
            resources.Build());
    }

    /// <summary>
    /// Separates the furniture a page draws above and below everything else on
    /// it - runs marked as artifacts - from the body.
    /// </summary>
    /// <remarks>
    /// Only what is wholly above or below the body counts. A run marked as an
    /// artifact level with the text is not a running head or a footer, and stays
    /// where the order it is read in puts it. A page of nothing but furniture has
    /// no body to be above or below, and is left as it is.
    /// </remarks>
    private static (List<PdfTextFragment> Body, List<PdfTextFragment> Head, List<PdfTextFragment> Foot) SplitFurniture(
        IReadOnlyList<PdfTextFragment> fragments)
    {
        double top = double.NegativeInfinity;
        double bottom = double.PositiveInfinity;
        foreach (PdfTextFragment fragment in fragments)
        {
            if (fragment.IsArtifact)
                continue;

            top = Math.Max(top, fragment.Y);
            bottom = Math.Min(bottom, fragment.Y);
        }

        var body = new List<PdfTextFragment>(fragments.Count);
        var head = new List<PdfTextFragment>();
        var foot = new List<PdfTextFragment>();
        bool hasBody = top >= bottom;

        foreach (PdfTextFragment fragment in fragments)
        {
            if (hasBody && fragment.IsArtifact && fragment.Y > top)
                head.Add(fragment);
            else if (hasBody && fragment.IsArtifact && fragment.Y < bottom)
                foot.Add(fragment);
            else
                body.Add(fragment);
        }

        return (body, head, foot);
    }

    /// <summary>A band of furniture as the paragraphs it reads as.</summary>
    private static List<RichTextParagraph> ProjectFurniture(
        List<PdfTextFragment> fragments,
        IReadOnlyList<PdfLinkRegion> links,
        int maxParagraphs) =>
        fragments.Count == 0
            ? []
            : PdfModelProjector.Project(PdfReadingOrder.BuildLines(fragments, links), [], false, maxParagraphs);

    /// <summary>
    /// Says what became of the page furniture the document marks as artifacts:
    /// carried once as running content where it repeated, and kept in the body
    /// where it did not.
    /// </summary>
    private static void AppendFurniture(StringBuilder text, PdfPageFurniture.Settlement settled, int pagesInBody)
    {
        bool header = settled.HeaderPages > 0;
        bool footer = settled.FooterPages > 0;

        if (header || footer)
        {
            string band = header && footer ? "a running head and a running footer" : header ? "a running head" : "a running footer";
            string part = header && footer ? "header and footer" : header ? "header" : "footer";
            int pages = Math.Max(settled.HeaderPages, settled.FooterPages);
            string where = settled.DifferentFirstPage
                ? string.Create(CultureInfo.InvariantCulture, $"the same on all {pages} pages after the first, which carries its own,")
                : pages == 1
                    ? "on its one page"
                    : string.Create(CultureInfo.InvariantCulture, $"the same on all {pages} pages");

            text.Append(CultureInfo.InvariantCulture,
                $"The page furniture the document marks as artifacts includes {band} {where} and was carried once, as the document's {part}, rather than in the body on every page. ");
        }

        if (pagesInBody > 0)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"On {pagesInBody} page{(pagesInBody == 1 ? string.Empty : "s")}, runs marked as artifacts - running heads, folios, page furniture a structure tree does not place by design - were kept in the body and set around it geometrically. ");
        }
    }

    /// <summary>
    /// The share of the page a picture has to cover, beneath the text, to be
    /// the page's background - the share a fill has to cover to be its colour.
    /// </summary>
    private const double PageBackgroundShare = 0.9;

    /// <summary>
    /// Decides which of the page's pictures are part of its flow, and admits
    /// those through the caller's resource policy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A picture under the whole page, with the text drawn over it, is
    /// the page's background.</strong> Stationery, a scanned form's blank, a
    /// sheet of fold and cut marks: the page is printed on it, and it is no
    /// more a paragraph than the paper is. Placed in the flow it became a
    /// page-sized paragraph ahead of the text - everything after it pushed to
    /// the next page - and, counted as the body's ink, it took the margins to
    /// nothing. The model holds no page background, so it is left out and
    /// reported, the way a fill covering the page is read as the page's colour
    /// rather than as a highlight.
    /// </para>
    /// <para>
    /// Only beneath visible text. A scan whose recognized text is invisible, or
    /// a page that is only a picture, is the picture - there is nothing else on
    /// the page - and a picture painted over text is not beneath it.
    /// </para>
    /// <para>
    /// Admission happens here rather than where the picture was decoded, so a
    /// background is never offered to the policy for a document it is not in.
    /// </para>
    /// </remarks>
    private static List<PdfPlacedImage> PlacePictures(
        IReadOnlyList<PdfPaintedPicture> pictures,
        IReadOnlyList<PdfTextFragment> fragments,
        PdfPage page,
        DocumentConversionContextBuilder resources,
        PdfFeatureTally tally,
        int pageNumber)
    {
        var placed = new List<PdfPlacedImage>(pictures.Count);
        if (pictures.Count == 0)
            return placed;

        int firstText = int.MaxValue;
        foreach (PdfTextFragment fragment in fragments)
        {
            if (!fragment.IsInvisible && !string.IsNullOrWhiteSpace(fragment.Text))
                firstText = Math.Min(firstText, fragment.Order);
        }

        double pageWidth = page.DisplayWidth;
        double pageHeight = page.DisplayHeight;

        foreach (PdfPaintedPicture picture in pictures)
        {
            // The part of the page the picture covers, since a bleed reaches
            // past the edge and the page is all that shows.
            double covered =
                Math.Max(0, Math.Min(pageWidth, picture.Left + picture.Width) - Math.Max(0, picture.Left)) *
                Math.Max(0, Math.Min(pageHeight, picture.Top) - Math.Max(0, picture.Top - picture.Height));

            if (firstText != int.MaxValue && picture.Order < firstText &&
                covered >= pageWidth * pageHeight * PageBackgroundShare)
            {
                tally.NoteImageNotProjected(pageNumber, "a page-sized picture beneath the text, read as the page's background");
                continue;
            }

            if (!resources.TryAdmit(
                    new DocumentResourceRequest(
                        picture.Resource,
                        DocumentResourceProvenance.ReadFromSource,
                        DocumentResourceDisposition.Embedded,
                        name: null,
                        sourceFormat: "PDF"),
                    DocumentResourceOperations.ExtractToModel,
                    out DocumentResourceId id,
                    out string? denial))
            {
                tally.NoteImageDenied(pageNumber, denial);
                continue;
            }

            var image = new InlineImage(picture.Resource, id, picture.Width, picture.Height);
            placed.Add(new PdfPlacedImage(image, picture.Left, picture.Top, picture.Width, picture.Height));
        }

        return placed;
    }

    /// <summary>
    /// Reports what became of each path the page kept: read as a table's rules
    /// and shades, as a run's decoration or background, painted on bare paper
    /// in the paper's colour, or dropped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A repaint shares the reading of the shape it repaints. Producers paint
    /// backgrounds and borders twice, and the lower copy of a band read as a
    /// run's background is that same band. What lay under one pass is a fact
    /// about that pass, though, so a white fill is only bare paper where it
    /// actually was.
    /// </para>
    /// <para>
    /// A dropped path that repeats one dropped before it is counted apart, so
    /// the note can say how many distinct shapes were lost as well as how many
    /// operations painted them.
    /// </para>
    /// </remarks>
    private static void NoteArtworkFates(
        PdfFeatureTally tally,
        int page,
        IReadOnlyList<PdfPaintedPath> paths,
        IReadOnlyList<PdfTableGrid> grids,
        bool[]? decorations,
        bool[]? backgrounds,
        bool[]? bare)
    {
        if (paths.Count == 0)
            return;

        var fates = new ArtworkFate[paths.Count];
        var readings = new Dictionary<ShapeKey, ArtworkFate>();

        for (int p = 0; p < paths.Count; p++)
        {
            PdfPaintedPath path = paths[p];

            // A cell's underline is inside the grid's box as surely as the
            // grid's own rules are, and it has already been read back as
            // something else. Counting it twice would report more paths
            // recovered than the page ever painted.
            fates[p] = decorations?[p] == true ? ArtworkFate.Decoration
                : Inside(grids, path) ? ArtworkFate.Table
                : backgrounds?[p] == true ? ArtworkFate.Background
                : bare?[p] == true ? ArtworkFate.Bare
                : ArtworkFate.Dropped;

            if (fates[p] is ArtworkFate.Decoration or ArtworkFate.Table or ArtworkFate.Background)
                readings.TryAdd(ShapeKey.Of(path), fates[p]);
        }

        int tableRules = 0;
        int tableBlocks = 0;
        int decorationRules = 0;
        int backgroundBlocks = 0;
        int bareBlocks = 0;
        int repeatedRules = 0;
        int repeatedBlocks = 0;
        var dropped = new HashSet<ShapeKey>();

        for (int p = 0; p < paths.Count; p++)
        {
            PdfPaintedPath path = paths[p];
            ShapeKey key = ShapeKey.Of(path);
            ArtworkFate fate = fates[p];

            if (fate is ArtworkFate.Dropped or ArtworkFate.Bare && readings.TryGetValue(key, out ArtworkFate reading))
                fate = reading;

            bool rule = path.Kind == PdfArtworkKind.Rule;
            switch (fate)
            {
                case ArtworkFate.Table when rule:
                    tableRules++;
                    break;
                case ArtworkFate.Table:
                    tableBlocks++;
                    break;
                case ArtworkFate.Decoration:
                    decorationRules++;
                    break;
                case ArtworkFate.Background:
                    backgroundBlocks++;
                    break;
                case ArtworkFate.Bare:
                    bareBlocks++;
                    break;
                default:
                    if (!dropped.Add(key))
                    {
                        if (rule)
                            repeatedRules++;
                        else
                            repeatedBlocks++;
                    }

                    break;
            }
        }

        tally.NoteArtworkReadAsTable(tableRules, tableBlocks, page);
        tally.NoteArtworkReadAsDecoration(decorationRules, page);
        tally.NoteArtworkReadAsBackground(backgroundBlocks, page);
        tally.NoteArtworkOnBarePaper(bareBlocks, page);
        tally.NoteArtworkRepeated(repeatedRules, repeatedBlocks);
    }

    /// <summary>What a kept path turned out to be.</summary>
    private enum ArtworkFate
    {
        Dropped,
        Table,
        Decoration,
        Background,
        Bare,
    }

    /// <summary>A painted path's shape, for telling a repaint from a new shape.</summary>
    private readonly record struct ShapeKey(
        PdfArtworkKind Kind,
        bool Filled,
        bool Stroked,
        Broiler.Graphics.Color.BColor Color,
        double StrokeWidth,
        double MinX,
        double MinY,
        double MaxX,
        double MaxY)
    {
        public static ShapeKey Of(in PdfPaintedPath path) => new(
            path.Kind,
            path.Filled,
            path.Stroked,
            path.Color,
            Math.Round(path.StrokeWidth, 2),
            Math.Round(path.MinX, 2),
            Math.Round(path.MinY, 2),
            Math.Round(path.MaxX, 2),
            Math.Round(path.MaxY, 2));
    }

    /// <summary>
    /// Whether this grid is the first thing on its page: nothing drawn above it.
    /// </summary>
    private static bool Opens(
        IReadOnlyList<PdfTextFragment> fragments,
        IReadOnlyList<PdfPlacedImage> images,
        PdfTableGrid grid)
    {
        foreach (PdfTextFragment fragment in fragments)
        {
            if (fragment.Y > grid.Top)
                return false;
        }

        foreach (PdfPlacedImage image in images)
        {
            if (image.Top > grid.Top)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether this grid is the last thing on its page: nothing drawn below it.
    /// </summary>
    private static bool Closes(
        IReadOnlyList<PdfTextFragment> fragments,
        IReadOnlyList<PdfPlacedImage> images,
        PdfTableGrid grid)
    {
        foreach (PdfTextFragment fragment in fragments)
        {
            if (fragment.Y < grid.Bottom)
                return false;
        }

        foreach (PdfPlacedImage image in images)
        {
            if (image.Top < grid.Bottom)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Joins a table to the one continuing it on the next page. The ranges are
    /// adjacent by construction - nothing was emitted between them - so the
    /// joined table covers both, and the second half's rows follow the first's.
    /// </summary>
    /// <remarks>
    /// The column widths are the first half's. A continued table is drawn to the
    /// same grid on both pages, which is what <c>Continues</c> checked; where
    /// they differ by a fraction the first page's are as right as the second's,
    /// and picking one beats averaging two measurements of the same thing.
    /// </remarks>
    private static DocumentTable Join(DocumentTable first, DocumentTable second)
    {
        var rows = new List<TableRow>(first.Rows.Count + second.Rows.Count);
        rows.AddRange(first.Rows);
        rows.AddRange(second.Rows);

        return new DocumentTable(
            first.ParagraphIndex,
            first.ParagraphCount + second.ParagraphCount,
            rows,
            first.ColumnWidths,
            first.CellPadding);
    }

    /// <summary>
    /// Whether a painted path lies inside any of the page's grids, including a
    /// grid nested in a cell - those rules were read as a table too.
    /// </summary>
    private static bool Inside(IReadOnlyList<PdfTableGrid> grids, in PdfPaintedPath path)
    {
        foreach (PdfTableGrid grid in grids)
        {
            if (grid.Covers(path))
                return true;
        }

        return false;
    }

    /// <summary>Whether any run on the page was drawn as an artifact.</summary>
    private static bool HasArtifact(IReadOnlyList<PdfTextFragment> fragments)
    {
        foreach (PdfTextFragment fragment in fragments)
        {
            if (fragment.IsArtifact)
                return true;
        }

        return false;
    }

    /// <summary>
    /// How many characters, spaces aside, a page drew turned against itself as
    /// displayed, and so set on horizontal lines as if they were not.
    /// </summary>
    private static int TurnedCharacters(IReadOnlyList<PdfTextFragment> fragments)
    {
        int count = 0;
        foreach (PdfTextFragment fragment in fragments)
        {
            if (!fragment.IsTurned)
                continue;

            foreach (char c in fragment.Text)
            {
                if (!char.IsWhiteSpace(c))
                    count++;
            }
        }

        return count;
    }

    private static List<PdfTextFragment> FilterVisible(IReadOnlyList<PdfTextFragment> fragments)
    {
        var visible = new List<PdfTextFragment>(fragments.Count);
        foreach (PdfTextFragment fragment in fragments)
        {
            if (!fragment.IsInvisible)
                visible.Add(fragment);
        }

        return visible;
    }

    private static PdfVersion ResolveVersion(PdfObjectStore store, PdfDictionary catalog, PdfDiagnosticSink diagnostics)
    {
        PdfVersion catalogVersion = PdfVersion.ParseName((store.Resolve(catalog["Version"]) as PdfName)?.Value);
        PdfVersion effective = PdfVersionResolver.Resolve(store.HeaderVersion, catalogVersion);

        if (effective.IsPdf2OrLater)
        {
            diagnostics.Info(
                PdfDiagnosticCodes.VersionToleratedNotSupported,
                $"The file declares PDF {effective}. The declaration was recorded and the file was read as the ISO 32000-1 constructs it actually uses; this is construct tolerance, not ISO 32000-2 conformance.");
        }

        return effective;
    }

    /// <summary>
    /// Inventories the document-level features this release deliberately does not
    /// act on, so their absence from the result is stated rather than silent.
    /// Returns the structure-tree description, if there is one, for the reading-order
    /// note to carry.
    /// </summary>
    private static string? NoteDocumentLevelFeatures(PdfObjectStore store, PdfDictionary catalog, PdfDiagnosticSink diagnostics)
    {
        if (store.Resolve(catalog["Names"]) is PdfDictionary names)
        {
            if (store.Resolve(names["JavaScript"]) is not null || store.Resolve(names["EmbeddedFiles"]) is not null)
            {
                diagnostics.Skipped(
                    PdfDiagnosticCodes.ActiveContentRemoved,
                    "The document carries document-level JavaScript or embedded files. Neither was executed, extracted, or projected.");
            }
        }

        if (store.Resolve(catalog["OpenAction"]) is not null)
        {
            diagnostics.Skipped(
                PdfDiagnosticCodes.ActiveContentRemoved,
                "The document declares an open action. It was detected and never executed.");
        }

        if (store.Resolve(catalog["AcroForm"]) is PdfDictionary form)
        {
            if (store.Resolve(form["SigFlags"]) is not null)
            {
                diagnostics.Skipped(
                    PdfDiagnosticCodes.SignatureNotValidated,
                    "The document declares signature fields. This release neither validates nor preserves signatures, and makes no trust claim about the content.");
            }
        }

        return store.Resolve(catalog["StructTreeRoot"]) is PdfDictionary structureTree
            ? DescribeStructureTree(store, structureTree, catalog)
            : null;
    }

    /// <summary>
    /// Describes a structure tree that was found and not consumed.
    /// </summary>
    /// <remarks>
    /// Whether tagged structure would have helped is not a yes-or-no question. A
    /// file marked <c>/Marked true</c> with a populated <c>/K</c> and a
    /// <c>/ParentTree</c> carries a reading order an implementation could trust;
    /// one with an empty root is a conformance gesture that would have told a
    /// reader nothing it did not already infer. Saying which of the two is in
    /// front of it turns this note from a standing reminder into evidence for the
    /// IP-017 decision.
    /// </remarks>
    private static string DescribeStructureTree(PdfObjectStore store, PdfDictionary root, PdfDictionary catalog)
    {
        var text = new StringBuilder(
            "The document carries a structure tree. This release reads it for reading order only — no role, " +
            "heading level, list, or table semantics is taken from it, and no accessibility or conformance claim " +
            "follows. Its root holds ");

        int topLevel = store.Resolve(root["K"]) switch
        {
            PdfArray kids => kids.Count,
            PdfDictionary => 1,
            _ => 0,
        };

        bool marked = store.Resolve(catalog["MarkInfo"]) is PdfDictionary markInfo &&
            store.Resolve(markInfo["Marked"]) is PdfBoolean flag && flag.Value;

        text.Append(CultureInfo.InvariantCulture, $"{topLevel} top-level element{(topLevel == 1 ? string.Empty : "s")}, the catalog ");
        text.Append(marked ? "marks the document as tagged" : "does not mark the document as tagged");
        text.Append(store.Resolve(root["ParentTree"]) is not null
            ? ", and a ParentTree maps marked content back to it."
            : ", and there is no ParentTree, so marked content could not be mapped back to the tree even by a reader that consumed it.");

        if (store.Resolve(root["RoleMap"]) is PdfDictionary roleMap && roleMap.Count > 0)
            text.Append(CultureInfo.InvariantCulture, $" A role map remaps {roleMap.Count} custom type{(roleMap.Count == 1 ? string.Empty : "s")} onto the standard set.");

        return text.ToString();
    }

    private static PdfReadResult Rejected(PdfDiagnosticSink diagnostics) =>
        new(
            RichTextDocument.Empty,
            DocumentResultStatus.Rejected,
            DocumentMetadata.Empty,
            PdfVersion.Unknown,
            0,
            [],
            diagnostics.Build());

    /// <summary>
    /// Materializes a stream under the input budget. The ceiling is checked while
    /// reading rather than after, so an oversized source is refused before it is
    /// in memory.
    /// </summary>
    public static byte[] ReadAllBytes(Stream source, long maxBytes)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.CanSeek)
        {
            long remaining = source.Length - source.Position;
            if (remaining > maxBytes)
                throw PdfWorkBudget.Exceeded(nameof(PdfLimits.MaxInputBytes), maxBytes);
        }

        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        long total = 0;

        while (true)
        {
            int read = source.Read(chunk, 0, chunk.Length);
            if (read == 0)
                break;

            total += read;
            if (total > maxBytes)
                throw PdfWorkBudget.Exceeded(nameof(PdfLimits.MaxInputBytes), maxBytes);

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
