using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Broiler.Documents.Model;

/// <summary>
/// Counts list items through a run of paragraphs and produces each one's label.
/// </summary>
/// <remarks>
/// <para>
/// One numberer walks one flow - a body, a cell's paragraphs - in order, and
/// every consumer that draws or writes labels uses it, so the page, the PDF and
/// the text dump agree on what item four is called.
/// </para>
/// <para>
/// A paragraph with a <see cref="ParagraphStyle.List"/> is counted in its list:
/// one counter per level of each <see cref="ListDefinition"/>, a level's first
/// item at its start value, deeper levels started again when a shallower one
/// appears (or as <see cref="ListLevel.RestartAfter"/> says), and nothing reset
/// by the paragraphs in between, because a list in Word and LibreOffice counts
/// on across them. A level above the paragraph's that has not had an item yet
/// is taken to have had its first, which is how LibreOffice numbers a heading
/// at level two with no level one before it: "1.1", and the level-one heading
/// after it "2".
/// </para>
/// <para>
/// A paragraph without one has only <see cref="ParagraphStyle.ListKind"/> and an
/// indent, and is numbered the way it always was: a counter per indent level,
/// deeper levels reset when a shallower item appears, and every counter reset at
/// the first paragraph that is not a list item. A list item's indent level counts
/// from one, every reader putting a top-level item at level 1, and the marker's
/// style is chosen by how deep the item is nested below that, so a top-level list
/// is numbered 1, 2, 3 and bulleted with a solid bullet.
/// </para>
/// </remarks>
public sealed class ListNumberer
{
    private const string Bullets = "•◦▪";

    private readonly Dictionary<ListDefinition, int?[]> _counters;
    private readonly List<int> _legacy = [];

    public ListNumberer()
        : this(new Dictionary<ListDefinition, int?[]>(ReferenceEqualityComparer.Instance))
    {
    }

    private ListNumberer(Dictionary<ListDefinition, int?[]> counters) => _counters = counters;

    /// <summary>
    /// A numberer for a flow inside this one - a table cell's paragraphs - that
    /// counts on in the same lists, and numbers a list with no definition from
    /// one, as a cell always did.
    /// </summary>
    public ListNumberer ForNestedFlow() => new(_counters);

    /// <summary>
    /// Counts <paramref name="style"/>'s paragraph and returns its label, or null
    /// when it draws none.
    /// </summary>
    public string? Advance(ParagraphStyle style) =>
        style.List is ListReference list ? Advance(list) : AdvanceLegacy(style);

    private string? Advance(ListReference list)
    {
        ListDefinition definition = list.Definition;
        if (!_counters.TryGetValue(definition, out int?[]? counters))
        {
            counters = new int?[ListDefinition.MaxLevels];
            _counters[definition] = counters;
        }

        int level = Math.Clamp(list.Level, 0, ListDefinition.MaxLevels - 1);
        ListLevel style = definition.LevelAt(level);
        counters[level] = list.RestartAt ?? (counters[level] is int value ? value + 1 : style.Start);

        // A level above this one that has had no item yet is taken to have had
        // its first, so the next item there is its second.
        for (int above = 0; above < level; above++)
            counters[above] ??= definition.LevelAt(above).Start;

        for (int deeper = level + 1; deeper < ListDefinition.MaxLevels; deeper++)
        {
            int restartAfter = definition.LevelAt(deeper).RestartAfter ?? deeper - 1;
            if (restartAfter >= 0 && level <= restartAfter)
                counters[deeper] = null;
        }

        return Label(definition, level, counters);
    }

    /// <summary>The label of an item at <paramref name="level"/> with the counters as they stand.</summary>
    private static string? Label(ListDefinition definition, int level, int?[] counters)
    {
        ListLevel style = definition.LevelAt(level);
        if (style.Format == ListNumberFormat.Bullet)
            return string.IsNullOrWhiteSpace(style.Template) ? null : style.Template;

        string template = style.Template;
        var label = new StringBuilder(template.Length + 8);
        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            if (c == '%' && i + 1 < template.Length && template[i + 1] is >= '1' and <= '9')
            {
                int referenced = template[i + 1] - '1';
                ListLevel levelStyle = definition.LevelAt(referenced);
                int number = counters[referenced] ?? levelStyle.Start;
                ListNumberFormat format = style.Legal && levelStyle.Format != ListNumberFormat.None
                    ? ListNumberFormat.Decimal
                    : levelStyle.Format;
                label.Append(Format(number, format));
                i++;
                continue;
            }

            label.Append(c);
        }

        string text = label.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private string? AdvanceLegacy(ParagraphStyle style)
    {
        int level = Math.Max(0, style.IndentLevel);
        int depth = Math.Max(0, level - 1);

        if (style.ListKind == ListKind.None)
        {
            _legacy.Clear();
            return null;
        }

        if (style.ListKind == ListKind.Bullet)
        {
            Truncate(level);
            return Bullets[depth % Bullets.Length].ToString();
        }

        Truncate(level);
        while (_legacy.Count <= level)
            _legacy.Add(0);

        _legacy[level]++;
        ListNumberFormat format = (depth % 3) switch
        {
            0 => ListNumberFormat.Decimal,
            1 => ListNumberFormat.LowerLetter,
            _ => ListNumberFormat.LowerRoman,
        };

        // The legacy letters count on past z as a spreadsheet's columns do,
        // which is what this numbering always drew.
        string number = format == ListNumberFormat.LowerLetter ? Alphabetic(_legacy[level]) : Format(_legacy[level], format);
        return number + ".";
    }

    private void Truncate(int level)
    {
        if (_legacy.Count > level + 1)
            _legacy.RemoveRange(level + 1, _legacy.Count - level - 1);
    }

    /// <summary>One number written in <paramref name="format"/>.</summary>
    public static string Format(int value, ListNumberFormat format) => format switch
    {
        ListNumberFormat.None or ListNumberFormat.Bullet => string.Empty,
        ListNumberFormat.DecimalZero => value is >= 0 and < 10
            ? "0" + value.ToString(CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture),
        ListNumberFormat.LowerLetter => Letters(value, 'a'),
        ListNumberFormat.UpperLetter => Letters(value, 'A'),
        ListNumberFormat.LowerRoman => Roman(value, upper: false),
        ListNumberFormat.UpperRoman => Roman(value, upper: true),
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>a ... z, then aa, bb ... zz, then aaa: ECMA-376's <c>lowerLetter</c>, and LibreOffice's.</summary>
    private static string Letters(int value, char first)
    {
        if (value <= 0)
            return value.ToString(CultureInfo.InvariantCulture);

        int repeat = ((value - 1) / 26) + 1;
        return new string((char)(first + ((value - 1) % 26)), repeat);
    }

    private static string Alphabetic(int value)
    {
        var builder = new StringBuilder();
        while (value > 0)
        {
            value--;
            builder.Insert(0, (char)('a' + (value % 26)));
            value /= 26;
        }

        return builder.Length == 0 ? "a" : builder.ToString();
    }

    private static string Roman(int value, bool upper)
    {
        if (value <= 0 || value >= 4000)
            return value.ToString(CultureInfo.InvariantCulture);

        int[] values = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        string[] symbols = ["m", "cm", "d", "cd", "c", "xc", "l", "xl", "x", "ix", "v", "iv", "i"];

        var builder = new StringBuilder();
        for (int i = 0; i < values.Length; i++)
        {
            while (value >= values[i])
            {
                builder.Append(symbols[i]);
                value -= values[i];
            }
        }

        string text = builder.ToString();
        return upper ? text.ToUpperInvariant() : text;
    }
}
