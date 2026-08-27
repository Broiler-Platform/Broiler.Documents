using System;

namespace Broiler.Documents.Model;

/// <summary>
/// A directional span between an <see cref="Anchor"/> (the fixed end) and a
/// <see cref="Focus"/> (the moving end), with normalized <see cref="Start"/> and
/// <see cref="End"/>. An empty range is a caret. This is also the document's
/// selection representation for Phase 1.
/// </summary>
public readonly struct RichTextRange : IEquatable<RichTextRange>
{
    public RichTextRange(RichTextPosition anchor, RichTextPosition focus)
    {
        Anchor = anchor;
        Focus = focus;
    }

    public RichTextPosition Anchor { get; }

    public RichTextPosition Focus { get; }

    /// <summary>The earlier of anchor and focus.</summary>
    public RichTextPosition Start => Anchor <= Focus ? Anchor : Focus;

    /// <summary>The later of anchor and focus.</summary>
    public RichTextPosition End => Anchor <= Focus ? Focus : Anchor;

    /// <summary>True when the range is a caret (anchor equals focus).</summary>
    public bool IsEmpty => Anchor == Focus;

    /// <summary>A caret range at <paramref name="position"/>.</summary>
    public static RichTextRange Caret(RichTextPosition position) => new(position, position);

    public bool Equals(RichTextRange other) => Anchor == other.Anchor && Focus == other.Focus;

    public override bool Equals(object? obj) => obj is RichTextRange other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Anchor, Focus);

    public static bool operator ==(RichTextRange left, RichTextRange right) => left.Equals(right);

    public static bool operator !=(RichTextRange left, RichTextRange right) => !left.Equals(right);

    public override string ToString() => IsEmpty ? $"[caret {Start}]" : $"[{Start}..{End}]";
}
