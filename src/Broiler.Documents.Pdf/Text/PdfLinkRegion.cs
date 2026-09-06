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
/// shared policy admits it, and only from a <c>/Link</c> annotation; JavaScript,
/// Launch, GoToR, GoToE, SubmitForm, ImportData, Named, embedded-file, unknown,
/// unnamed and wrong-typed actions are counted and reported, never projected as
/// links and never executed or fetched. A plain GoTo and a bare <c>/Dest</c> are
/// the odd pair out: both name a place inside this same file, so neither is
/// active content, and both are reported as a destination this build cannot
/// carry. An unapplied Redact annotation gets a high-severity diagnostic of its
/// own, because an overlay is not a deletion and callers must not read a
/// conversion as a redaction. Optional content decides one thing here and one
/// only: whether the link is projected. Classification is not a visibility
/// question - a layer the default configuration turns off still leaves the
/// Redact unapplied and the JavaScript in the file - so the inventory is taken
/// on every layer, and only the link, the destination note and the URI refusal
/// are withheld with it.
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
        int droppedDestinations = 0;

        foreach (PdfObject entry in annotations)
        {
            store.Budget.ThrowIfCancelled();
            if (store.Resolve(entry) is not PdfDictionary annotation)
                continue;

            // An annotation names its layer the same way an XObject does. The
            // layer decides what is presented, not what the file carries, so the
            // membership is recorded here and applied further down - to the link
            // this annotation would project, and to nothing else. Leaving from
            // here dropped it before it was ever classified: one /OC key on a
            // /Redact silenced the only error this codec raises about an
            // unapplied redaction, and one on a /Widget silenced its JavaScript.
            bool hiddenLayer = false;
            if (layers.IsHidden(store, annotation["OC"], out _))
            {
                if (layers.Enforced)
                {
                    hiddenLayer = true;
                    store.Features.NoteOptionalContentHidden(store.CurrentPage);
                }
                else
                {
                    store.Features.NoteOptionalContentKept(store.CurrentPage);
                }
            }

            string subtype = (store.Resolve(annotation["Subtype"]) as PdfName)?.Value ?? string.Empty;

            switch (subtype)
            {
                case "Redact":
                    // One wording for every shape, because the sink keeps the
                    // first message a code records and a later raise only bumps
                    // the count - a conditional sentence would report whichever
                    // page happened to be read first. "Was extracted" was never
                    // checked here, and it is plainly false on a page that
                    // yielded no text, which is where a redaction matters most.
                    store.Diagnostics.Error(
                        PdfDiagnosticCodes.RedactionNotApplied,
                        "The document carries an unapplied Redact annotation. A redaction overlay is not a deletion: the content underneath it is still in the file. What this read extracted is a separate question - a scanned page, or a layer outside the default presentation, yields less text rather than less content.");
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
            {
                // An /A that is present and is not an action dictionary states an
                // action of a kind that cannot be named, so it is inventoried
                // rather than trusted. It used to leave here in silence: an /A
                // resolving to a number, or absent with a /Dest beside it, dropped
                // the annotation with no counter touched and the read still
                // reported Success. /A and /Dest are alternatives, so the action is
                // decided first and a damaged one is never read as a destination.
                if (annotation["A"] is not null and not PdfNull)
                    activeContent++;
                else if (!hiddenLayer && subtype == "Link" && annotation["Dest"] is not null and not PdfNull)
                    droppedDestinations++;

                continue;
            }

            string actionType = (store.Resolve(action["S"]) as PdfName)?.Value ?? string.Empty;
            if (actionType == "GoTo")
            {
                // A GoTo names a place in this same file: it executes nothing,
                // fetches nothing, and reaches nothing outside the document. It was
                // counted as active content, which made a table of contents read
                // like a document carrying JavaScript and gave a caller one merged
                // number the two could not be told apart in. The loss is the same
                // one a bare /Dest is, and is reported the same way. The equality is
                // exact on purpose: GoToR and GoToE name another file and stay
                // active content.
                // Same reason as the bare /Dest above: this reports a link that
                // was not projected, and on a layer the configuration turns off
                // there is no run left for the jump to have been kept as.
                if (!hiddenLayer)
                    droppedDestinations++;

                continue;
            }

            if (actionType != "URI")
            {
                // Every other non-URI action is active content by definition: it
                // does something other than name a place in this document. None is
                // projected. An action dictionary with no /S, or an /S that is not a
                // name, is counted here too - it was falling past the old length
                // guard uncounted, and that is the shape a broken producer emits.
                activeContent++;
                continue;
            }

            if (subtype != "Link")
            {
                // A URI action on a Widget or a Stamp is still a click target that
                // would leave the document if anything executed it. It is not
                // projected, because only a Link is a link - but leaving here
                // without a counter meant the same javascript: target was reported
                // on a /Link and silent on a /Widget.
                activeContent++;
                continue;
            }

            // Everything above is an inventory of what the file carries, which no
            // configuration alters. From here down the annotation would become a
            // link in the extracted text, and a link on a layer the default
            // configuration turns off is no more part of the presentation than
            // the text under it - so this is the one place the layer decides
            // anything. Nothing is read from the target above this line.
            if (hiddenLayer)
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

        if (droppedDestinations > 0)
        {
            // Skipped, not Info: the same visible loss - a run that stays plain
            // text - is Skipped when the policy refuses a URI, and a table of
            // contents that came back with every entry as text used to report
            // Success. The destination value is never read, so nothing the file
            // named reaches the message.
            store.Diagnostics.Skipped(
                PdfDiagnosticCodes.LinkDestinationDropped,
                $"{droppedDestinations} annotations name a place inside the document rather than a URI. The text was kept and no link was projected: this build carries no bookmark or anchor for an internal jump to land on.");
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
