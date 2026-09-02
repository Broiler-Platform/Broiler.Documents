using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using Broiler.Documents.Model;
using Broiler.Graphics;

namespace Broiler.Documents;

/// <summary>
/// Runs a caller's resource policy while a codec reads, and closes into an
/// immutable <see cref="DocumentConversionContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// The builder exists so that the thing a codec holds while reading and the thing
/// a caller receives afterwards are different types. A codec can ask this to
/// admit a resource; it cannot write an entry, cannot alter a decision, and
/// cannot hand itself a permission. What it gets back is an id and a yes or no.
/// </para>
/// <para>
/// <strong>One question per payload.</strong> Two occurrences of the same picture
/// — the same bytes, the same form, the same size — are one resource, admitted
/// once and given one id. That is not only an optimization: asking a policy twice
/// about identical bytes invites two different answers inside one document, and
/// a document where the same picture is permitted in one place and denied in
/// another is not a document anyone can reason about.
/// </para>
/// <para>
/// Namespaces are unique per builder, so ids minted here match nothing minted
/// anywhere else. That is what makes a resource crossing into another conversion
/// require a fresh decision.
/// </para>
/// </remarks>
public sealed class DocumentConversionContextBuilder
{
    private static long _sequence;

    private readonly DocumentResourcePolicy _policy;
    private readonly Dictionary<DocumentResourceId, DocumentResourceEntry> _entries = [];
    private readonly Dictionary<DocumentResourceBinding, DocumentResourceId> _byPayload = [];
    private int _nextLocalId = 1;

    public DocumentConversionContextBuilder(DocumentResourcePolicy policy, string? contextNamespace = null)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        Namespace = contextNamespace ?? string.Create(
            CultureInfo.InvariantCulture,
            $"conv-{Interlocked.Increment(ref _sequence)}");
    }

    /// <summary>
    /// Continues <paramref name="context"/>: the same namespace, the same
    /// entries, and room to admit more under <paramref name="policy"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what an edit is. A caller who reads a document, inserts a picture,
    /// and saves has performed one conversion with two sources of resources, and
    /// the images that came out of the file must keep the ids they were given —
    /// the model still holds them, and minting new ones would invalidate every
    /// image in the document to admit one.
    /// </para>
    /// <para>
    /// Distinct from <see cref="Rekey"/>, which is for a resource crossing
    /// <em>into</em> a different conversion and must be decided on afresh. Here
    /// the conversion is the same one; there it is not.
    /// </para>
    /// </remarks>
    public static DocumentConversionContextBuilder Continuing(
        DocumentConversionContext context,
        DocumentResourcePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(context);

        var builder = new DocumentConversionContextBuilder(policy, context.Namespace);
        foreach (DocumentResourceEntry entry in context.Entries)
        {
            builder._entries[entry.Id] = entry;
            builder._byPayload[entry.Binding] = entry.Id;

            // Keep minting past every id already in use, so a continued context
            // never hands a new resource the name of an existing one.
            if (int.TryParse(entry.Id.LocalId, CultureInfo.InvariantCulture, out int local) &&
                local >= builder._nextLocalId)
            {
                builder._nextLocalId = local + 1;
            }
        }

        return builder;
    }

    /// <summary>The namespace every id this builder mints will carry.</summary>
    public string Namespace { get; }

    /// <summary>
    /// Asks the policy about <paramref name="request"/> and records its answer.
    /// </summary>
    /// <remarks>
    /// Always produces an entry, including a denying one. A denial is a decision
    /// worth keeping: it is what lets a write report that a picture was dropped
    /// because the read policy refused it, rather than because it went missing.
    /// </remarks>
    public DocumentResourceEntry Admit(DocumentResourceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        DocumentResourceBinding binding = DocumentResourceBinding.ForImage(request.Resource);
        if (_byPayload.TryGetValue(binding, out DocumentResourceId existing))
            return _entries[existing];

        DocumentResourceDecision decision = _policy.Decide(request)
            ?? DocumentResourceDecision.Denied("The resource policy returned no decision.");

        var id = new DocumentResourceId(
            Namespace,
            _nextLocalId.ToString(CultureInfo.InvariantCulture));
        _nextLocalId++;

        var entry = new DocumentResourceEntry(
            id,
            binding,
            request.Provenance,
            request.Disposition,
            decision.Permitted,
            decision.Obligations);

        _entries[id] = entry;
        _byPayload[binding] = id;
        return entry;
    }

    /// <summary>
    /// Admits <paramref name="request"/> and reports whether the operations a
    /// caller intends are permitted, which is the shape a reader wants: it is
    /// about to construct something, and needs one answer.
    /// </summary>
    public bool TryAdmit(
        DocumentResourceRequest request,
        DocumentResourceOperations intended,
        out DocumentResourceId id,
        out string? denial)
    {
        DocumentResourceEntry entry = Admit(request);
        id = entry.Id;

        if (entry.Allows(intended))
        {
            denial = null;
            return true;
        }

        DocumentResourceOperations missing = intended & ~entry.Permitted;
        denial = string.Create(CultureInfo.InvariantCulture, $"{missing} is not permitted for this resource");
        return false;
    }

    /// <summary>
    /// Admits <paramref name="image"/>'s resource and returns the same image
    /// bound to the entry, which is what a caller holding a picture needs.
    /// </summary>
    /// <remarks>
    /// The returned image is a new object — the model is immutable — so callers
    /// use the one they get back rather than the one they passed in. An image
    /// still carrying the old id is exactly the case the gate refuses, so the
    /// mistake shows up as a reported omission rather than as silent
    /// misbehaviour.
    /// </remarks>
    public InlineImage AdmitImage(
        InlineImage image,
        DocumentResourceProvenance provenance,
        DocumentResourceDisposition disposition,
        string? sourceFormat = null)
    {
        ArgumentNullException.ThrowIfNull(image);

        DocumentResourceEntry entry = Admit(new DocumentResourceRequest(
            image.Resource,
            provenance,
            disposition,
            image.Name,
            sourceFormat));

        return image.WithResourceId(entry.Id);
    }

    /// <summary>
    /// Re-asks the policy about a resource that arrived from somewhere else, and
    /// mints a fresh id for it in this context.
    /// </summary>
    /// <remarks>
    /// This is what a paste, a merge, or a deserialization goes through. The
    /// incoming id is deliberately not consulted: authorization does not travel,
    /// and the only thing an id from another context could contribute here is a
    /// false sense that a decision was already made.
    /// </remarks>
    public DocumentResourceEntry Rekey(DocumentResourceRequest request) => Admit(request);

    /// <summary>Closes the builder into the context a caller receives.</summary>
    public DocumentConversionContext Build() =>
        new(Namespace, new ReadOnlyDictionary<DocumentResourceId, DocumentResourceEntry>(
            new Dictionary<DocumentResourceId, DocumentResourceEntry>(_entries)));
}
