using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Broiler.Documents.Model;

/// <summary>How one level of a list writes its number.</summary>
public enum ListNumberFormat
{
    /// <summary>No number: the level counts, and draws nothing of its own.</summary>
    None = 0,

    /// <summary>1, 2, 3.</summary>
    Decimal,

    /// <summary>01, 02 ... 10: decimal padded to two digits.</summary>
    DecimalZero,

    /// <summary>a, b ... z, aa, bb: the letter repeated past the alphabet, as Word and LibreOffice write it.</summary>
    LowerLetter,

    /// <summary>A, B ... Z, AA, BB.</summary>
    UpperLetter,

    /// <summary>i, ii, iii.</summary>
    LowerRoman,

    /// <summary>I, II, III.</summary>
    UpperRoman,

    /// <summary>A bullet: <see cref="ListLevel.Template"/> is the bullet's text.</summary>
    Bullet,
}

/// <summary>One level of a <see cref="ListDefinition"/>.</summary>
/// <remarks>
/// <para>
/// <see cref="Template"/> is the label with each level's number left as
/// <c>%1</c> to <c>%9</c>, which is how DOCX writes it (<c>w:lvlText</c>,
/// ECMA-376 §17.9.11) and what ODF's prefix, suffix and
/// <c>text:display-levels</c> come to: <c>"%1.%2."</c> draws "2.3." at the third
/// item under the second. A bullet level's template is the bullet itself.
/// </para>
/// <para>
/// <see cref="RestartAfter"/> is DOCX's <c>w:lvlRestart</c> counted from nought:
/// the level whose appearance - or any shallower one's - starts this level
/// again. Null is the default, the level just above; -1 never restarts.
/// </para>
/// </remarks>
public sealed record ListLevel
{
    public ListNumberFormat Format { get; init; } = ListNumberFormat.Decimal;

    public string Template { get; init; } = "%1.";

    /// <summary>The number the level's first item gets.</summary>
    public int Start { get; init; } = 1;

    /// <summary>
    /// True when every number in the label is written in decimal, whatever its
    /// own level's format: DOCX's <c>w:isLgl</c>, so a legal heading reads
    /// "1.1" under a chapter numbered "I".
    /// </summary>
    public bool Legal { get; init; }

    /// <summary>The level, counted from nought, that restarts this one; null for the one above, -1 for never.</summary>
    public int? RestartAfter { get; init; }

    /// <summary>A decimal level with the given number in its label, as a list with no definition draws.</summary>
    public static ListLevel DecimalAt(int level) => new() { Template = "%" + (level + 1) + "." };
}

/// <summary>
/// A list's levels: what each one counts in, from where, and how its label
/// reads.
/// </summary>
/// <remarks>
/// <para>
/// The definition is the list's identity as well as its look. A paragraph names
/// the definition it is numbered in (<see cref="ListReference"/>), and every
/// paragraph naming the same instance counts on, however much body text lies
/// between them - a contract whose clauses run 4, 5, 6 around unnumbered
/// paragraphs. Two lists that look alike but count separately are two
/// instances: each <c>text:list</c> ODF starts afresh, or a DOCX <c>w:num</c>
/// that overrides its start.
/// </para>
/// <para>
/// So equality is reference equality, on purpose: it is the question "is this
/// the same list", not "do these lists look the same".
/// </para>
/// </remarks>
public sealed class ListDefinition
{
    /// <summary>The most levels a list has here, which is what DOCX and ODF both allow.</summary>
    public const int MaxLevels = 9;

    private readonly ListLevel[] _levels;

    public ListDefinition(IReadOnlyList<ListLevel>? levels, string? name = null)
    {
        _levels = new ListLevel[MaxLevels];
        for (int i = 0; i < MaxLevels; i++)
            _levels[i] = levels is not null && i < levels.Count && levels[i] is ListLevel level ? level : ListLevel.DecimalAt(i);

        Name = string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>All <see cref="MaxLevels"/> levels; a level the source left out is decimal.</summary>
    public IReadOnlyList<ListLevel> Levels => _levels;

    /// <summary>The name the source gave the list, if any: a list style's name, or a DOCX abstract numbering's.</summary>
    public string? Name { get; }

    /// <summary>The level at <paramref name="level"/>, clamped into the levels there are.</summary>
    public ListLevel LevelAt(int level) => _levels[Math.Clamp(level, 0, MaxLevels - 1)];

    /// <summary>
    /// The levels a bulleted paragraph that names no list draws: a solid, a
    /// hollow and a square bullet in turn. A writer that has to name a list for
    /// such a paragraph names this one, so it reads back drawn as it was.
    /// </summary>
    public static ListDefinition PlainBullets { get; } = new(
        [.. Enumerable.Range(0, MaxLevels).Select(level => new ListLevel
        {
            Format = ListNumberFormat.Bullet,
            Template = ((char)((level % 3) switch { 0 => 0x2022, 1 => 0x25E6, _ => 0x25AA })).ToString(),
        })]);

    /// <summary>
    /// The levels a numbered paragraph that names no list draws: decimal,
    /// letters and roman numerals in turn, each followed by a full stop.
    /// </summary>
    public static ListDefinition PlainNumbers { get; } = new(
        [.. Enumerable.Range(0, MaxLevels).Select(level => new ListLevel
        {
            Format = (level % 3) switch
            {
                0 => ListNumberFormat.Decimal,
                1 => ListNumberFormat.LowerLetter,
                _ => ListNumberFormat.LowerRoman,
            },
            Template = "%" + (level + 1).ToString(CultureInfo.InvariantCulture) + ".",
        })]);

    /// <summary>The same levels, counted as a list of their own.</summary>
    public ListDefinition Copy() => new(_levels, Name);

    /// <summary>These levels with one changed, counted as a list of their own.</summary>
    public ListDefinition WithLevel(int level, ListLevel value)
    {
        var levels = (ListLevel[])_levels.Clone();
        levels[Math.Clamp(level, 0, MaxLevels - 1)] = value;
        return new ListDefinition(levels, Name);
    }
}

/// <summary>
/// The list a paragraph is numbered in, the level it is at (from nought), and
/// the number it restarts at, if it restarts.
/// </summary>
/// <remarks>
/// <see cref="RestartAt"/> is ODF's <c>text:start-value</c> on a list item: the
/// count carries on from there for the items after it.
/// </remarks>
public readonly record struct ListReference(ListDefinition Definition, int Level, int? RestartAt = null)
{
    /// <summary>The level this paragraph is at.</summary>
    public ListLevel LevelStyle => Definition.LevelAt(Level);

    /// <summary>The kind of decoration the level draws, for code that knows only <see cref="Model.ListKind"/>.</summary>
    public ListKind Kind => LevelStyle.Format switch
    {
        ListNumberFormat.None => ListKind.None,
        ListNumberFormat.Bullet => ListKind.Bullet,
        _ => ListKind.Numbered,
    };
}
