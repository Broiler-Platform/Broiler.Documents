using System;
using System.Collections.Generic;
using System.IO;
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

        PdfDocumentMetadata metadata = PdfMetadataReader.Read(store, catalog);
        List<PdfPage> pages = PdfPageTree.Collect(store, catalog);
        if (pages.Count == 0)
        {
            diagnostics.Error(PdfDiagnosticCodes.StructureMalformed, "The document contains no pages.");
            return Rejected(diagnostics);
        }

        NoteDocumentLevelFeatures(store, catalog, diagnostics);

        PdfUriPolicy policy = options.UriPolicy ?? services.UriPolicy;
        var interpreter = new PdfContentInterpreter(store);
        var paragraphs = new List<RichTextParagraph>();
        int emptyPages = 0;

        for (int i = 0; i < pages.Count; i++)
        {
            budget.ThrowIfCancelled();
            PdfPage page = pages[i];

            IReadOnlyList<PdfTextFragment> fragments = interpreter.Run(page);
            if (!options.IncludeInvisibleText)
                fragments = FilterVisible(fragments);

            if (fragments.Count == 0)
            {
                emptyPages++;
                continue;
            }

            List<PdfLinkRegion> links = PdfAnnotationReader.Read(store, page, policy);
            List<PdfTextLine> lines = PdfReadingOrder.BuildLines(fragments, links);

            bool pageBreak = options.MapPageBreaks && i < pages.Count - 1;
            paragraphs.AddRange(PdfModelProjector.Project(lines, pageBreak, options.Limits.MaxParagraphCount));
        }

        if (emptyPages > 0)
        {
            diagnostics.Skipped(
                PdfDiagnosticCodes.TextOcrRequired,
                $"{emptyPages} of {pages.Count} pages carried no extractable text. A scanned page needs OCR, which is outside this release's scope.");
        }

        if (paragraphs.Count > 0)
        {
            diagnostics.Info(
                PdfDiagnosticCodes.ReadingOrderHeuristic,
                "Reading order was inferred from page geometry. PDF states where glyphs are drawn, not what order they are read in, so paragraph and column grouping is a documented heuristic.");
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
            diagnostics.Build());
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
    /// </summary>
    private static void NoteDocumentLevelFeatures(PdfObjectStore store, PdfDictionary catalog, PdfDiagnosticSink diagnostics)
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

        if (store.Resolve(catalog["StructTreeRoot"]) is not null)
        {
            diagnostics.Info(
                PdfDiagnosticCodes.ReadingOrderHeuristic,
                "The document carries a structure tree. This release does not consume tagged-PDF structure, so reading order was still inferred from geometry.");
        }
    }

    private static PdfReadResult Rejected(PdfDiagnosticSink diagnostics) =>
        new(
            RichTextDocument.Empty,
            DocumentResultStatus.Rejected,
            PdfDocumentMetadata.Empty,
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
