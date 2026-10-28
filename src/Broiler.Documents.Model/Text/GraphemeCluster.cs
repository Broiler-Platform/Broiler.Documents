using System;
using System.Globalization;

namespace Broiler.Documents.Model.Text;

/// <summary>
/// Represents an extended grapheme cluster per Unicode UAX #29 that should not be divided across lines.
/// Includes surrogate pairs, base characters with combining marks, and emoji ZWJ sequences.
/// </summary>
public readonly record struct GraphemeCluster : IEquatable<GraphemeCluster>
{
    public int Offset { get; }
    public int Length { get; }
    public string Text { get; }

    public GraphemeCluster(int offset, int length, string text)
    {
        Offset = offset;
        Length = length;
        Text = text;
    }

    /// <summary>True when the cluster is formed from a UTF-16 surrogate pair (code points above U+FFFF).</summary>
    public bool IsSurrogatePair => Length >= 2 && char.IsSurrogatePair(Text, 0);

    /// <summary>True when the cluster contains combining character marks (e.g. accents, diacritics).</summary>
    public bool HasCombiningMarks
    {
        get
        {
            for (int i = 0; i < Text.Length; i++)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(Text, i);
                if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                    return true;
            }
            return false;
        }
    }

    /// <summary>The first Unicode scalar value of the cluster.</summary>
    public int FirstCodePoint => char.ConvertToUtf32(Text, 0);

    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "GraphemeCluster(Offset={0}, Length={1}, Text='{2}')", Offset, Length, Text);
}
