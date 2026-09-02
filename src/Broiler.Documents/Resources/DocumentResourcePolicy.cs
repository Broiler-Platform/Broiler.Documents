using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Broiler.Documents.Model;
using Broiler.Graphics;

namespace Broiler.Documents;

/// <summary>
/// What a codec tells the policy about a resource it has found, so the policy can
/// decide what may be done with it.
/// </summary>
public sealed class DocumentResourceRequest
{
    public DocumentResourceRequest(
        BImageResource resource,
        DocumentResourceProvenance provenance,
        DocumentResourceDisposition disposition,
        string? name = null,
        string? sourceFormat = null)
    {
        Resource = resource ?? throw new ArgumentNullException(nameof(resource));
        Kind = DocumentResourceKind.Image;
        Provenance = provenance;
        Disposition = disposition;
        Name = name;
        SourceFormat = sourceFormat;
    }

    public DocumentResourceRequest(
        DocumentFontResource font,
        DocumentResourceProvenance provenance,
        DocumentResourceDisposition disposition,
        string? sourceFormat = null)
    {
        Font = font ?? throw new ArgumentNullException(nameof(font));
        Kind = DocumentResourceKind.Font;
        Provenance = provenance;
        Disposition = disposition;
        Name = font.Family;
        SourceFormat = sourceFormat;
    }

    /// <summary>Whether this is about a picture or a font program.</summary>
    public DocumentResourceKind Kind { get; }

    /// <summary>The image payload, or null for a font request.</summary>
    public BImageResource? Resource { get; }

    /// <summary>
    /// The font program, or null for an image request.
    /// </summary>
    /// <remarks>
    /// A policy deciding about a font reads its declared rights from here. They
    /// are one input among several — the font's licence, which this cannot see,
    /// is the one that governs.
    /// </remarks>
    public DocumentFontResource? Font { get; }

    public DocumentResourceProvenance Provenance { get; }

    public DocumentResourceDisposition Disposition { get; }

    /// <summary>A short name from the source, where it had one. Never required.</summary>
    public string? Name { get; }

    /// <summary>The format the resource was found in, for example <c>PDF</c>.</summary>
    public string? SourceFormat { get; }
}

/// <summary>
/// A policy's answer: which operations are permitted, and what obligations
/// travel with the resource if it is used.
/// </summary>
public sealed class DocumentResourceDecision
{
    private static readonly ReadOnlyCollection<string> NoObligations =
        Array.AsReadOnly(Array.Empty<string>());

    public DocumentResourceDecision(
        DocumentResourceOperations permitted,
        IEnumerable<string>? obligations = null,
        string? reason = null)
    {
        Permitted = permitted;
        Obligations = obligations is null
            ? NoObligations
            : Array.AsReadOnly(obligations.Where(o => !string.IsNullOrWhiteSpace(o)).ToArray());
        Reason = reason;
    }

    /// <summary>Denies everything, which is what an unrecognized resource gets.</summary>
    public static DocumentResourceDecision Denied(string? reason = null) =>
        new(DocumentResourceOperations.None, null, reason);

    public DocumentResourceOperations Permitted { get; }

    /// <summary>
    /// Attribution, licence-copy, or naming duties a generated document must
    /// fulfil if this resource reaches it. Carried rather than discharged: the
    /// writer that emits the resource is what owes them.
    /// </summary>
    public IReadOnlyList<string> Obligations { get; }

    /// <summary>Why, for a diagnostic. Never required, and never load-bearing.</summary>
    public string? Reason { get; }
}

/// <summary>
/// The caller's decision procedure for resources a conversion meets.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam every resource decision is made on. A codec never decides
/// what may happen to a resource; it describes what it found and does what it is
/// told. The three policies here differ only in what they say yes to:
/// <see cref="DenyAll"/> refuses everything, <see cref="Default"/> reads into the
/// model and grants no output rights, and <see cref="AllowOwnDocuments"/> adds
/// the rights a round trip needs.
/// </para>
/// <para>
/// A policy is asked once per distinct resource per conversion and its answer is
/// recorded in the context, so it is consulted rather than re-run and cannot give
/// two different answers about one resource inside one document.
/// </para>
/// </remarks>
public abstract class DocumentResourcePolicy
{
    /// <summary>Refuses every operation on every resource.</summary>
    public static DocumentResourcePolicy DenyAll { get; } = new DenyAllPolicy();

    /// <summary>
    /// Permits reading a resource into the result model, and nothing that puts it
    /// into an output. The default for a read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The asymmetry is deliberate and is the roadmap's own rule: acceptance by a
    /// reader grants no later writer authorization. A caller who opened a
    /// document asked to see what is in it, and denying that by default would
    /// make every host that has not yet learned about policies quietly lose its
    /// pictures. A caller who then <em>writes</em> those pictures into a new file
    /// is doing something else, to which reading has never been consent.
    /// </para>
    /// <para>
    /// So this grants projection, transient decoding, and extraction into the
    /// model, and withholds byte transfer, transformation, embedding, and
    /// redistribution. A round trip therefore needs a caller who says so — which
    /// is the decision the roadmap wants made out loud rather than inherited.
    /// </para>
    /// </remarks>
    public static DocumentResourcePolicy Default { get; } = new ReadIntoModelPolicy();

    /// <summary>
    /// Permits reading a resource into the model and writing it back out
    /// unchanged, for a host that has decided its documents are its own.
    /// </summary>
    /// <remarks>
    /// Deliberately not the default. It is offered as a named, obvious choice so
    /// that a host granting it has said so in one readable line, rather than
    /// assembling the same permissions out of flags and hoping they are right.
    /// It still grants neither <see cref="DocumentResourceOperations.EmbedOrSubset"/>
    /// nor <see cref="DocumentResourceOperations.Redistribute"/>, which carry
    /// obligations a general policy cannot discharge on a caller's behalf.
    /// </remarks>
    public static DocumentResourcePolicy AllowOwnDocuments { get; } = new AllowOwnDocumentsPolicy();

    /// <summary>Decides what may be done with the resource in <paramref name="request"/>.</summary>
    public abstract DocumentResourceDecision Decide(DocumentResourceRequest request);

    private sealed class ReadIntoModelPolicy : DocumentResourcePolicy
    {
        public override DocumentResourceDecision Decide(DocumentResourceRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (request.Provenance == DocumentResourceProvenance.Unknown ||
                request.Disposition == DocumentResourceDisposition.Unknown)
            {
                return DocumentResourceDecision.Denied(
                    "the resource's provenance or disposition was not stated, and unknown denies");
            }

            return new DocumentResourceDecision(
                DocumentResourceOperations.SemanticProjection |
                DocumentResourceOperations.MetadataProjection |
                DocumentResourceOperations.TransientDecode |
                DocumentResourceOperations.ExtractToModel);
        }
    }

    private sealed class DenyAllPolicy : DocumentResourcePolicy
    {
        public override DocumentResourceDecision Decide(DocumentResourceRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            return DocumentResourceDecision.Denied("No resource policy is composed, so nothing is permitted.");
        }
    }

    private sealed class AllowOwnDocumentsPolicy : DocumentResourcePolicy
    {
        public override DocumentResourceDecision Decide(DocumentResourceRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            // Unknown provenance or disposition denies, per the roadmap's rule.
            // The point is that a codec which cannot say where something came
            // from does not get the benefit of the doubt from a policy that says
            // yes to everything it recognizes.
            if (request.Provenance == DocumentResourceProvenance.Unknown ||
                request.Disposition == DocumentResourceDisposition.Unknown)
            {
                return DocumentResourceDecision.Denied(
                    "The resource's provenance or disposition was not stated, and unknown denies.");
            }

            return new DocumentResourceDecision(
                DocumentResourceOperations.SemanticProjection |
                DocumentResourceOperations.MetadataProjection |
                DocumentResourceOperations.TransientDecode |
                DocumentResourceOperations.ExtractToModel |
                DocumentResourceOperations.ByteTransfer |
                DocumentResourceOperations.Transform);
        }
    }
}
