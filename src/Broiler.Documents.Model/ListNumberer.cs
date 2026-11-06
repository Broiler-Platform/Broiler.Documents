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

    private sealed class CounterState(ListDefinition starts)
    {
        public int?[] Values { get; } = new int?[ListDefinition.MaxLevels];
        public ListDefinition Starts { get; set; } = starts;
    }
    private readonly Dictionary<ListDefinition, CounterState> _counters;
    private readonly HashSet<ListDefinition> _started;
    private readonly List<int> _legacy = [];

    public ListNumberer()
        : this(new Dictionary<ListDefinition, CounterState>(ReferenceEqualityComparer.Instance), new(ReferenceEqualityComparer.Instance))
    {
    }

    private ListNumberer(Dictionary<ListDefinition, CounterState> counters, HashSet<ListDefinition> started)
    {
        _counters = counters;
        _started = started;
    }

    /// <summary>
    /// A numberer for a flow inside this one - a table cell's paragraphs - that
    /// counts on in the same lists, and numbers a list with no definition from
    /// one, as a cell always did.
    /// </summary>
    public ListNumberer ForNestedFlow() => new(_counters, _started);

    /// <summary>An independent copy for measuring ahead without consuming list labels.</summary>
    public ListNumberer Snapshot()
    {
        var counters = new Dictionary<ListDefinition, CounterState>(ReferenceEqualityComparer.Instance);
        foreach (var entry in _counters)
        {
            var state = new CounterState(entry.Value.Starts);
            entry.Value.Values.CopyTo(state.Values, 0);
            counters.Add(entry.Key, state);
        }
        var copy = new ListNumberer(counters, new HashSet<ListDefinition>(_started, ReferenceEqualityComparer.Instance));
        copy._legacy.AddRange(_legacy);
        return copy;
    }


    /// <summary>
    /// Counts <paramref name="style"/>'s paragraph and returns its label, or null
    /// when it draws none.
    /// </summary>
    public string? Advance(ParagraphStyle style) =>
        style.List is ListReference list ? Advance(list) : AdvanceLegacy(style);

    private string? Advance(ListReference list)
    {
        ListDefinition definition = list.Definition;
        if (!_counters.TryGetValue(definition.CounterGroup, out CounterState? state))
        {
            state = new CounterState(definition.CounterGroup);
            _counters[definition.CounterGroup] = state;
        }
        int?[] counters = state.Values;
        if (_started.Add(definition) && !ReferenceEquals(definition, definition.CounterGroup))
        {
            Array.Clear(counters);
            state.Starts = definition;
        }

        int level = Math.Clamp(list.Level, 0, ListDefinition.MaxLevels - 1);
        ListLevel style = definition.LevelAt(level);
        counters[level] = list.RestartAt ?? (counters[level] is int value ? value + 1 : state.Starts.LevelAt(level).Start);

        // A level above this one that has had no item yet is taken to have had
        // its first, so the next item there is its second.
        for (int above = 0; above < level; above++)
            counters[above] ??= state.Starts.LevelAt(above).Start;

        for (int deeper = level + 1; deeper < ListDefinition.MaxLevels; deeper++)
        {
            int restartAfter = definition.LevelAt(deeper).RestartAfter ?? deeper - 1;
            if (restartAfter >= 0 && level <= restartAfter)
                counters[deeper] = null;
        }

        return Label(definition, level, counters, state.Starts);
    }

    /// <summary>The label of an item at <paramref name="level"/> with the counters as they stand.</summary>
    private static string? Label(ListDefinition definition, int level, int?[] counters, ListDefinition starts)
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
                int number = counters[referenced] ?? starts.LevelAt(referenced).Start;
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
        ListNumberFormat.LowerAlphabetic => Alphabetic(value),
        ListNumberFormat.UpperAlphabetic => Alphabetic(value, "ABCDEFGHIJKLMNOPQRSTUVWXYZ"),
        ListNumberFormat.ArabicAlpha => Alphabetic(value, "أبتثجحخدذرزسشصضطظعغفقكلمنهوى"),
        ListNumberFormat.ArabicAbjad => Alphabetic(value, "ابجدهوزحطيكلمنسعفصقرشتثخذضظغ"),
        ListNumberFormat.HebrewAlphabetic => Alphabetic(value, "אבגדהוזחטיכלמנסעפצקרשת"),
        ListNumberFormat.LowerRoman => Roman(value, upper: false),
        ListNumberFormat.UpperRoman => Roman(value, upper: true),
        ListNumberFormat.CardinalText => EnglishNumber(value, ordinal: false),
        ListNumberFormat.OrdinalText => EnglishNumber(value, ordinal: true),
        ListNumberFormat.OldHungarian => OldHungarian(value),
        ListNumberFormat.Ordinal => Ordinal(value),
        ListNumberFormat.Chicago => value is > 0 and <= 116
            ? new string("*†‡§"[(value - 1) % 4], ((value - 1) / 4) + 1) : string.Empty,
        ListNumberFormat.IdeographLegalTraditional => TraditionalFinancial(value),
        ListNumberFormat.IdeographCounting => ChineseCounting(value),
        ListNumberFormat.TianGan => TianGan(value),
        ListNumberFormat.DiZi => DiZi(value),
        ListNumberFormat.KoreanDigital2 => KoreanDigital2(value),
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

    private static string Alphabetic(int value, string alphabet = "abcdefghijklmnopqrstuvwxyz")
    {
        if (value <= 0)
            return value.ToString(CultureInfo.InvariantCulture);

        var builder = new StringBuilder();
        while (value > 0)
        {
            value--;
            builder.Insert(0, alphabet[value % alphabet.Length]);
            value /= alphabet.Length;
        }

        return builder.ToString();
    }

    private static string Ordinal(int value)
    {
        long magnitude = Math.Abs((long)value);
        string suffix = magnitude % 100 is >= 11 and <= 13 ? "th" : (magnitude % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th",
        };
        return value.ToString(CultureInfo.InvariantCulture) + suffix;
    }


    private static string ChineseCounting(int value)
    {
        if (value is < 1 or > 99999)
            return value.ToString(CultureInfo.InvariantCulture);

        const string digits = "零一二三四五六七八九";
        if (value < 10)
            return digits[value].ToString();

        if (value < 20)
            return "十" + (value == 10 ? string.Empty : digits[value % 10].ToString());

        if (value < 100)
        {
            int units = value % 10;
            return digits[value / 10] + "十" + (units == 0 ? string.Empty : digits[units].ToString());
        }

        if (value >= 10000)
        {
            int remainder = value % 10000;
            return ChineseCounting(value / 10000) + "萬" + (remainder switch
            {
                0 => string.Empty,
                < 1000 => "零" + ChineseCounting(remainder),
                _ => ChineseCounting(remainder),
            });
        }

        (int place, string unit) = value >= 1000 ? (1000, "千") : (100, "百");
        int tail = value % place;
        string head = digits[value / place] + unit;
        if (tail == 0)
            return head;

        return head + (tail < place / 10 ? "零" : string.Empty) + ChineseCounting(tail);
    }

    private static string TianGan(int value)
    {
        const string stems = "甲乙丙丁戊己庚辛壬癸";
        return value is >= 1 and <= 10
            ? stems[value - 1].ToString()
            : value.ToString(CultureInfo.InvariantCulture);
    }

    private static string KoreanDigital2(int value)
    {
        if (value <= 0)
            return value.ToString(CultureInfo.InvariantCulture);

        const string digits = "〇一二三四五六七八九";
        string s = value.ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            sb.Append(digits[c - '0']);
        return sb.ToString();
    }

    private static string DiZi(int value)
    {
        const string branches = "子丑寅卯辰巳午未申酉戌亥";
        return value is >= 1 and <= 12
            ? branches[value - 1].ToString()
            : value.ToString(CultureInfo.InvariantCulture);
    }

    private static string TraditionalFinancial(int value)
    {
        if (value is < 0 or > 999999)
            return string.Empty;

        const string digits = "零壹貳參肆伍陸柒捌玖";
        if (value < 10)
            return digits[value].ToString();

        if (value >= 10000)
        {
            int remainder = value % 10000;
            return TraditionalFinancial(value / 10000) + "萬" + (remainder switch
            {
                0 => string.Empty,
                < 1000 => "零" + TraditionalFinancial(remainder),
                _ => TraditionalFinancial(remainder),
            });
        }

        (int place, string unit) = value switch
        {
            >= 1000 => (1000, "仟"),
            >= 100 => (100, "佰"),
            _ => (10, "拾"),
        };
        int tail = value % place;
        string head = digits[value / place] + unit;
        if (tail == 0)
            return head;

        return head + (tail < place / 10 ? "零" : string.Empty) + TraditionalFinancial(tail);
    }

    private static readonly string[] CardinalUnits =
        ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
         "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"];

    private static readonly string[] OrdinalUnits =
        ["zeroth", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth",
         "tenth", "eleventh", "twelfth", "thirteenth", "fourteenth", "fifteenth", "sixteenth", "seventeenth", "eighteenth", "nineteenth"];

    private static readonly string[] Tens =
        ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    private static string EnglishNumber(int value, bool ordinal)
    {
        string words = value < 0 ? "minus " + SpellNumber(-(long)value, ordinal) : SpellNumber(value, ordinal);
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static string SpellNumber(long value, bool ordinal)
    {
        if (value < 20)
            return (ordinal ? OrdinalUnits : CardinalUnits)[value];

        if (value < 100)
        {
            string tens = Tens[value / 10];
            long units = value % 10;
            return units > 0 ? tens + "-" + SpellNumber(units, ordinal)
                : ordinal ? tens[..^1] + "ieth" : tens;
        }

        (long scale, string name) = value switch
        {
            >= 1_000_000_000 => (1_000_000_000, "billion"),
            >= 1_000_000 => (1_000_000, "million"),
            >= 1_000 => (1_000, "thousand"),
            _ => (100, "hundred"),
        };
        string head = SpellNumber(value / scale, ordinal: false) + " " + name;
        long remainder = value % scale;
        return remainder > 0 ? head + " " + SpellNumber(remainder, ordinal)
            : ordinal ? head + "th" : head;
    }

    private static string OldHungarian(int value)
    {
        if (value <= 0)
            return value.ToString(CultureInfo.InvariantCulture);

        var result = new StringBuilder();
        // Hundreds use a multiplier. Each successive group of three digits
        // adds one thousand sign to the group's suffix.
        ReadOnlySpan<int> scales = [1_000_000_000, 1_000_000, 1_000, 100];
        for (int i = 0; i < scales.Length; i++)
        {
            int count = value / scales[i];
            if (count == 0)
                continue;

            if (count > 1)
                result.Append(OldHungarian(count));
            if (i == 3)
                result.Append("\U00010CFE");
            else
                for (int sign = i; sign < 3; sign++)
                    result.Append("\U00010CFF");
            value %= scales[i];
        }

        ReadOnlySpan<int> units = [1, 5, 10, 50];
        for (int i = units.Length - 1; i >= 0; i--)
        {
            for (int count = value / units[i]; count > 0; count--)
                result.Append(char.ConvertFromUtf32(0x10CFA + i));
            value %= units[i];
        }

        return result.ToString();
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
