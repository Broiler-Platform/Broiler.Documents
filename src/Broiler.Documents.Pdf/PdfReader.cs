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
        if (store is not null)
            store.FontProgramReader = services.FontProgramReader;
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

        PdfDictionary? catalog = store.Resolve(store.Trailer["Root"]) as PdfDictionary;
        if (catalog is null)
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

        for (int i = 0; i < pages.Count; i++)
        {
            budget.ThrowIfCancelled();
            PdfPage page = pages[i];

            // Everything raised from here down - a skipped image, an unreadable
            // font program, a dropped path - belongs to this page, and says so.
            diagnostics.CurrentPage = i + 1;

            IReadOnlyList<PdfTextFragment> fragments = interpreter.Run(page);
            if (!options.IncludeInvisibleText)
                fragments = FilterVisible(fragments);

            IReadOnlyList<PdfPlacedImage> images = interpreter.PlacedImages;
            if (fragments.Count == 0 && images.Count == 0)
            {
                emptyPages++;
                continue;
            }

            List<PdfLinkRegion> links = PdfAnnotationReader.Read(store, page, policy, optionalContent);

            // A page whose fragments the tree accounts for in full is read in the
            // order it declares. One it accounts for only partly falls back
            // whole: mixing a declared order with an inferred one produces a
            // sequence neither the document nor the heuristic asked for.
            List<PdfTextLine> lines;
            if (structure is not null && structure.Covers(i, fragments))
            {
                lines = PdfReadingOrder.BuildLinesInDeclaredOrder(
                    fragments, links, fragment => structure.OrderOf(i, fragment.Mcid));
                declaredOrderPages++;
            }
            else
            {
                lines = PdfReadingOrder.BuildLines(fragments, links);
                if (fragments.Count > 0)
                    inferredOrderPages++;
            }

            bool pageBreak = options.MapPageBreaks && i < pages.Count - 1;
            paragraphs.AddRange(
                PdfModelProjector.Project(lines, images, pageBreak, options.Limits.MaxParagraphCount));
        }

        // Back to document scope, and the one point where the constructs the
        // pages recognized but did not implement become diagnostics. Draining
        // here, before the status is decided below, keeps a skipped construct
        // making the read Partial exactly as an immediate report did.
        diagnostics.CurrentPage = null;
        store.Features.Report(diagnostics);

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
                    "Only the sequence was taken from it: the order of glyphs within a block is still geometric, and no role was read. ");
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
                    "PDF states where glyphs are drawn, not what order they are read in, so paragraph and column grouping is a documented heuristic.");
            }

            // One code, one note. The structure tree is the reason the heuristic
            // was still needed on a file that could have said better, so it reads
            // as a clause of that sentence rather than as a second diagnostic the
            // sink would collapse into a count.
            if (structureTree is not null)
            {
                if (order.Length > 0)
                    order.Append(' ');
                order.Append(structureTree);
            }

            if (order.Length == 0)
                order.Append("Reading order was inferred from page geometry.");

            diagnostics.Info(PdfDiagnosticCodes.ReadingOrderHeuristic, order.ToString());
        }

        RichTextDocument document = paragraphs.Count == 0
            ? RichTextDocument.Empty
            : RichTextDocument.FromParagraphs(paragraphs);

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

    private static IReadOnlyList<PdfTextFragment> FilterVisible(IReadOnlyList<PdfTextFragment> fragments)
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
            Array.Empty<PdfExtensionDeclaration>(),
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
