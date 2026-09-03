using System;
using System.Collections.Generic;
using Broiler.Documents.Pdf.Structure;
using Broiler.Documents.Pdf.Syntax;

namespace Broiler.Documents.Pdf.Text;

/// <summary>A rectangle on a page whose text carries an admitted link target.</summary>
internal sealed class PdfLinkRegion
{
    public PdfLinkRegion(PdfRectangle bounds, string href)
    {
        Bounds = bounds;
        Href = href;
    }

    public PdfRectangle Bounds { get; }

    /// <summary>The canonical URI, already admitted by the active policy.</summary>
    public string Href { get; }

    /// <summary>True when the point sits inside the region, with a small tolerance.</summary>
    public bool Contains(double x, double y) =>
        x >= Bounds.Left - 1 && x <= Bounds.Right + 1 &&
        y >= Bounds.Bottom - 1 && y <= Bounds.Top + 1;
}

/// <summary>
/// Reads a page's annotations: link targets that pass the URI policy, and an
/// inventory of everything active that is deliberately not acted on.
/// </summary>
/// <remarks>
/// Actions are inert source data here. A URI action becomes a link only after the
/// shared policy admits it; JavaScript, Launch, GoToR, SubmitForm, ImportData,
/// embedded-file, and unknown actions are counted and reported, never projected
/// as links and never executed or fetched. An unapplied Redact annotation gets a
/// high-severity diagnostic of its own, because an overlay is not a deletion and
/// callers must not read a conversion as a redaction.
/// </remarks>
internal static class PdfAnnotationReader
{
    public static List<PdfLinkRegion> Read(
        PdfObjectStore store,
        PdfPage page,
        PdfUriPolicy policy,
        PdfOptionalContent? optionalContent = null)
    {
        var regions = new List<PdfLinkRegion>();
        if (store.Resolve(page.Dictionary["Annots"]) is not PdfArray annotations)
            return regions;

        store.Budget.ChargeAnnotations(annotations.Count);

        PdfOptionalContent layers = optionalContent ?? PdfOptionalContent.None;
        int activeContent = 0;
        int rejectedUris = 0;

        foreach (PdfObject entry in annotations)
        {
            store.Budget.ThrowIfCancelled();
            if (store.Resolve(entry) is not PdfDictionary annotation)
                continue;

            // An annotation names its layer the same way an XObject does, and a
            // link on a layer the default configuration turns off is no more part
            // of the presentation than the text under it.
            if (layers.IsHidden(store, annotation["OC"], out _))
            {
                if (layers.Enforced)
                {
                    store.Features.NoteOptionalContentHidden(store.CurrentPage);
                    continue;
                }

                store.Features.NoteOptionalContentKept(store.CurrentPage);
            }

            string subtype = (store.Resolve(annotation["Subtype"]) as PdfName)?.Value ?? string.Empty;

            switch (subtype)
            {
                case "Redact":
                    store.Diagnostics.Error(
                        PdfDiagnosticCodes.RedactionNotApplied,
                        "The document carries an unapplied Redact annotation. The content underneath it is still present and was extracted; a redaction overlay is not a deletion.");
                    continue;
                case "FileAttachment":
                case "Screen":
                case "Movie":
                case "RichMedia":
                case "3D":
                    activeContent++;
                    continue;
            }

            if (store.Resolve(annotation["A"]) is not PdfDictionary action)
                continue;

            string actionType = (store.Resolve(action["S"]) as PdfName)?.Value ?? string.Empty;
            if (actionType != "URI")
            {
                // Every non-URI action is active content by definition: it does
                // something other than name a document. None is projected.
                if (actionType.Length > 0)
                    activeContent++;
                continue;
            }

            if (subtype != "Link")
                continue;

            string? raw = ReadUriValue(store, action);
            if (!policy.TryAdmit(raw, out string canonical, out _))
            {
                rejectedUris++;
                continue;
            }

            if (ReadRectangle(store, annotation["Rect"]) is { } bounds)
                regions.Add(new PdfLinkRegion(bounds, canonical));
        }

        if (activeContent > 0)
        {
            store.Diagnostics.Skipped(
                PdfDiagnosticCodes.ActiveContentRemoved,
                $"{activeContent} active annotations or actions were detected. None was executed, fetched, or projected into the document.");
        }

        if (rejectedUris > 0)
        {
            store.Diagnostics.Skipped(
                PdfDiagnosticCodes.UriRejected,
                $"{rejectedUris} link targets did not pass the active URI policy and remain inert source data.");
        }

        return regions;
    }

    private static string? ReadUriValue(PdfObjectStore store, PdfDictionary action)
    {
        if (store.Resolve(action["URI"]) is not PdfString uri)
            return null;

        // A URI action's value is a byte string in a PDF text encoding, not a
        // Unicode string; it is decoded here and never fetched.
        var builder = new System.Text.StringBuilder(uri.Bytes.Length);
        foreach (byte b in uri.Bytes)
            builder.Append(PdfDocEncoding.ToChar(b));
        return builder.ToString();
    }

    private static PdfRectangle? ReadRectangle(PdfObjectStore store, PdfObject? value)
    {
        if (store.Resolve(value) is not PdfArray array || array.Count < 4)
            return null;

        Span<double> coordinates = stackalloc double[4];
        for (int i = 0; i < 4; i++)
        {
            if (store.Resolve(array[i]) is not PdfNumber number || !double.IsFinite(number.Value))
                return null;
            coordinates[i] = number.Value;
        }

        var rectangle = new PdfRectangle(coordinates[0], coordinates[1], coordinates[2], coordinates[3]);
        return rectangle.IsUsable ? rectangle : null;
    }
}
