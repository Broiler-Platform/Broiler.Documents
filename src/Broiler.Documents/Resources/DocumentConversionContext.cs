using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Broiler.Documents.Model;
using Broiler.Graphics;

namespace Broiler.Documents;

/// <summary>
/// One resource, the decision made about it, and the payload that decision was
/// made about.
/// </summary>
public sealed class DocumentResourceEntry
{
    public DocumentResourceEntry(
        DocumentResourceId id,
        DocumentResourceBinding binding,
        DocumentResourceProvenance provenance,
        DocumentResourceDisposition disposition,
        DocumentResourceOperations permitted,
        IReadOnlyList<string> obligations)
    {
        if (id.IsNone)
            throw new ArgumentException("A resource entry needs a real id.", nameof(id));

        Id = id;
        Binding = binding ?? throw new ArgumentNullException(nameof(binding));
        Provenance = provenance;
        Disposition = disposition;
        Permitted = permitted;
        Obligations = obligations ?? throw new ArgumentNullException(nameof(obligations));
    }

    public DocumentResourceId Id { get; }

    /// <summary>What the decision was made about; checked before it is honoured.</summary>
    public DocumentResourceBinding Binding { get; }

    public DocumentResourceProvenance Provenance { get; }

    public DocumentResourceDisposition Disposition { get; }

    public DocumentResourceOperations Permitted { get; }

    /// <summary>Duties a document emitting this resource takes on.</summary>
    public IReadOnlyList<string> Obligations { get; }

    /// <summary>True when every operation in <paramref name="operations"/> is permitted.</summary>
    public bool Allows(DocumentResourceOperations operations) =>
        operations != DocumentResourceOperations.None && (Permitted & operations) == operations;
}

/// <summary>
/// The immutable record of which resources a conversion met and what the caller's
/// policy allowed for each.
/// </summary>
/// <remarks>
/// <para>
/// A read produces one of these beside the document, and a write consumes one. It
/// is the thing that stops a resource laundering its permissions by changing
/// format: a picture read out of a PDF under a policy that allowed extraction but
/// not redistribution arrives at a DOCX writer with that same entry, rather than
/// with whatever the DOCX writer would have assumed.
/// </para>
/// <para>
/// <strong>Deciding is separate from asking.</strong> The context holds decisions;
/// it does not make them. <see cref="DocumentConversionContextBuilder"/> is what
/// runs a policy while a codec reads, and the context it builds is closed. There
/// is no method here that adds an entry, because a mutable context handed to a
/// codec would let the codec grant itself permissions.
/// </para>
/// <para>
/// <strong>Ids do not travel.</strong> Every id is namespaced to this context, so
/// a resource moving to another conversion finds no entry there and its
/// destination must ask its own policy. <see cref="Empty"/> is a context that
/// contains nothing and therefore permits nothing, which is what an unspecified
/// write gets.
/// </para>
/// </remarks>
public sealed class DocumentConversionContext
{
    private readonly IReadOnlyDictionary<DocumentResourceId, DocumentResourceEntry> _entries;

    internal DocumentConversionContext(
        string contextNamespace,
        IReadOnlyDictionary<DocumentResourceId, DocumentResourceEntry> entries)
    {
        Namespace = contextNamespace;
        _entries = entries;
    }

    /// <summary>
    /// A context holding nothing. It permits nothing, which is the documented
    /// behaviour of a write that was given no context: such a write cannot
    /// redistribute resources whose origin nobody recorded.
    /// </summary>
    public static DocumentConversionContext Empty { get; } = new(
        "empty",
        new ReadOnlyDictionary<DocumentResourceId, DocumentResourceEntry>(
            new Dictionary<DocumentResourceId, DocumentResourceEntry>()));

    /// <summary>The namespace every id in this context carries.</summary>
    public string Namespace { get; }

    /// <summary>The entries, in the order the conversion admitted them.</summary>
    public IReadOnlyCollection<DocumentResourceEntry> Entries => (IReadOnlyCollection<DocumentResourceEntry>)_entries.Values;

    /// <summary>Finds the entry for <paramref name="id"/>, if this context has one.</summary>
    public bool TryGetEntry(DocumentResourceId id, out DocumentResourceEntry? entry)
    {
        if (!id.IsNone && _entries.TryGetValue(id, out DocumentResourceEntry? found))
        {
            entry = found;
            return true;
        }

        entry = null;
        return false;
    }

    /// <summary>
    /// Whether <paramref name="operations"/> may be performed on the resource
    /// <paramref name="id"/> names, carrying <paramref name="payload"/>.
    /// </summary>
    /// <remarks>
    /// Both halves are checked. The id must name an entry in this context — a
    /// foreign, forged, or stale id names nothing and is denied — and the payload
    /// must match what that entry was bound to, so a valid id presented with
    /// different bytes cannot borrow the decision made about the original.
    /// </remarks>
    public bool IsAllowed(DocumentResourceId id, DocumentResourceOperations operations, BImageResource payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!TryGetEntry(id, out DocumentResourceEntry? entry) || !entry!.Allows(operations))
            return false;

        return entry.Binding.Equals(DocumentResourceBinding.ForImage(payload));
    }

    /// <summary>
    /// Explains a denial in terms a diagnostic can carry, without naming the
    /// resource's content.
    /// </summary>
    public string ExplainDenial(DocumentResourceId id, DocumentResourceOperations operations, BImageResource payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (id.IsNone)
            return "the resource carries no context id";
        if (!TryGetEntry(id, out DocumentResourceEntry? entry))
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"no entry for {id} in conversion context '{Namespace}'");
        }

        if (!entry!.Binding.Equals(DocumentResourceBinding.ForImage(payload)))
            return string.Create(CultureInfo.InvariantCulture, $"the payload for {id} is not the one that was approved");

        DocumentResourceOperations missing = operations & ~entry.Permitted;
        return string.Create(CultureInfo.InvariantCulture, $"{missing} is not permitted for {id}");
    }
}
