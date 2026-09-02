using System;

namespace Broiler.Documents.Model;

/// <summary>
/// An opaque, stable name for one resource inside one conversion.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What it is for.</strong> A document's picture gets resized, gets alt
/// text, gets moved between paragraphs, and is still the same picture — but every
/// one of those edits produces a new immutable model object, so object identity
/// cannot answer "is this the resource the caller approved?". This can: it
/// survives immutable edits, and the conversion context maps it to the decision
/// that was actually made.
/// </para>
/// <para>
/// <strong>Why it carries a namespace.</strong> Equality is the namespace *and*
/// the local id, so an id minted in one conversion never compares equal to one
/// minted in another. That is the whole point rather than an implementation
/// detail: it makes "authorization never transfers automatically" a property of
/// the type instead of a rule someone has to remember. Paste a picture from one
/// document into another and its id does not match anything in the destination's
/// context, so the destination has to ask its own policy — which is exactly the
/// behaviour wanted.
/// </para>
/// <para>
/// <strong>What it is not.</strong> An id is not authorization. It names an entry;
/// the entry carries the decision, and the entry is bound to the payload's digest
/// so that presenting a matching id with different bytes fails rather than
/// succeeds. A forged or stale id finds no entry, and finding no entry denies.
/// </para>
/// <para>
/// The default value is <see cref="None"/>, which belongs to no context and
/// matches no entry.
/// </para>
/// </remarks>
public readonly struct DocumentResourceId : IEquatable<DocumentResourceId>
{
    private readonly string? _namespace;
    private readonly string? _localId;

    public DocumentResourceId(string contextNamespace, string localId)
    {
        if (string.IsNullOrEmpty(contextNamespace))
            throw new ArgumentException("A resource id belongs to a named context.", nameof(contextNamespace));
        if (string.IsNullOrEmpty(localId))
            throw new ArgumentException("A resource id has a local part.", nameof(localId));

        _namespace = contextNamespace;
        _localId = localId;
    }

    /// <summary>An id belonging to no context, which matches no entry anywhere.</summary>
    public static DocumentResourceId None => default;

    /// <summary>The conversion context this id was minted in.</summary>
    public string Namespace => _namespace ?? string.Empty;

    /// <summary>The id's context-local part.</summary>
    public string LocalId => _localId ?? string.Empty;

    /// <summary>True for <see cref="None"/>: an id that names nothing.</summary>
    public bool IsNone => _namespace is null;

    public bool Equals(DocumentResourceId other) =>
        string.Equals(_namespace, other._namespace, StringComparison.Ordinal) &&
        string.Equals(_localId, other._localId, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is DocumentResourceId other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(
            _namespace is null ? 0 : StringComparer.Ordinal.GetHashCode(_namespace),
            _localId is null ? 0 : StringComparer.Ordinal.GetHashCode(_localId));

    public static bool operator ==(DocumentResourceId left, DocumentResourceId right) => left.Equals(right);

    public static bool operator !=(DocumentResourceId left, DocumentResourceId right) => !left.Equals(right);

    public override string ToString() => IsNone ? "(none)" : $"{Namespace}/{LocalId}";
}
