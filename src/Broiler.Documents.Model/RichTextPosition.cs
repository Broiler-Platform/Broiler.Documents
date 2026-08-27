using System;

namespace Broiler.Documents.Model;

/// <summary>
/// An opaque, stable location within a <see cref="RichTextDocument"/>. Positions
/// are produced by the document (for example <see cref="RichTextDocument.Start"/>)
/// and by edit results; the underlying indexing is intentionally not part of the
/// public contract, leaving room for grapheme- and bidi-aware movement later
/// (ADR 0014). Positions are only meaningful against the document that produced
/// them.
/// </summary>
public readonly struct RichTextPosition : IEquatable<RichTextPosition>, IComparable<RichTextPosition>
{
    internal RichTextPosition(int paragraphIndex, int offset)
    {
        ParagraphIndex = paragraphIndex;
        Offset = offset;
    }

    internal int ParagraphIndex { get; }

    internal int Offset { get; }

    public bool Equals(RichTextPosition other) =>
        ParagraphIndex == other.ParagraphIndex && Offset == other.Offset;

    public override bool Equals(object? obj) => obj is RichTextPosition other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ParagraphIndex, Offset);

    public int CompareTo(RichTextPosition other)
    {
        int byParagraph = ParagraphIndex.CompareTo(other.ParagraphIndex);
        return byParagraph != 0 ? byParagraph : Offset.CompareTo(other.Offset);
    }

    public static bool operator ==(RichTextPosition left, RichTextPosition right) => left.Equals(right);

    public static bool operator !=(RichTextPosition left, RichTextPosition right) => !left.Equals(right);

    public static bool operator <(RichTextPosition left, RichTextPosition right) => left.CompareTo(right) < 0;

    public static bool operator >(RichTextPosition left, RichTextPosition right) => left.CompareTo(right) > 0;

    public static bool operator <=(RichTextPosition left, RichTextPosition right) => left.CompareTo(right) <= 0;

    public static bool operator >=(RichTextPosition left, RichTextPosition right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"({ParagraphIndex}:{Offset})";
}
