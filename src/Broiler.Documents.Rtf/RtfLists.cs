using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Broiler.Documents.Model;

namespace Broiler.Documents.Rtf;

/// <summary>Reads the supported subset of modern RTF list destinations.</summary>
internal sealed class RtfLists
{
    private readonly Dictionary<int, ListDefinition> _definitions = [];
    private readonly Dictionary<int, ListDefinition> _instances = [];

    public ListDefinition? Find(int id) => _instances.GetValueOrDefault(id);

    public static string? ReadCachedMarker(IReadOnlyList<RtfToken> tokens, ref int next)
    {
        int depth = 1;
        var text = new StringBuilder();
        bool valid = true;
        while (next < tokens.Count)
        {
            RtfToken token = tokens[next++];
            if (token.Type == RtfTokenType.GroupStart)
                depth++;
            else if (token.Type == RtfTokenType.GroupEnd && --depth == 0)
            {
                return valid ? text.ToString().Trim() : null;
            }
            else if (depth == 1 && token.Type == RtfTokenType.Text)
            {
                if (token.Text.Length > 32 - text.Length)
                    valid = false;
                else
                    text.Append(token.Text);
            }
            else if (token.Type is RtfTokenType.HexByte or RtfTokenType.ControlSymbol ||
                token.Keyword == "u")
                valid = false;
        }
        return null;
    }

    public static int? CachedOrdinal(string? label, ListReference list)
    {
        if (string.IsNullOrEmpty(label))
            return null;

        string template = list.LevelStyle.Template;
        string placeholder = "%" + (list.Level + 1).ToString(CultureInfo.InvariantCulture);
        int index = template.IndexOf(placeholder, StringComparison.Ordinal);
        if (index < 0 || template.IndexOf('%') != index || template.LastIndexOf('%') != index ||
            label.Length <= template.Length - placeholder.Length)
            return null;

        string prefix = template[..index];
        string suffix = template[(index + placeholder.Length)..];
        if (!label.StartsWith(prefix, StringComparison.Ordinal) || !label.EndsWith(suffix, StringComparison.Ordinal))
            return null;

        string value = label.Substring(prefix.Length, label.Length - prefix.Length - suffix.Length);
        var format = list.LevelStyle.Format;
        if (format == ListNumberFormat.Decimal &&
            int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int decimalValue))
            return decimalValue;

        if (format is not (ListNumberFormat.LowerRoman or ListNumberFormat.UpperRoman) || value.Length > 15)
            return null;

        int ordinal = 0;
        int previous = 0;
        for (int i = value.Length - 1; i >= 0; i--)
        {
            int current = char.ToUpperInvariant(value[i]) switch
            {
                'I' => 1, 'V' => 5, 'X' => 10, 'L' => 50, 'C' => 100, 'D' => 500, 'M' => 1000, _ => 0,
            };
            if (current == 0)
                return null;
            ordinal += current < previous ? -current : current;
            previous = current;
        }

        return ordinal is >= 1 and <= 3999 && ListNumberer.Format(ordinal, format) == value ? ordinal : null;
    }

    private static string? ReadSingleBullet(IReadOnlyList<RtfToken> tokens, ref int next, int codePage)
    {
        int start = next;
        int depth = 1;
        while (next < tokens.Count)
        {
            RtfToken token = tokens[next++];
            if (token.Type == RtfTokenType.GroupStart)
                depth++;
            else if (token.Type == RtfTokenType.GroupEnd && --depth == 0)
            {
                if (next - start == 4 && tokens[start].Type == RtfTokenType.HexByte &&
                    tokens[start].Parameter == 1 && tokens[start + 2].Type == RtfTokenType.Text &&
                    tokens[start + 2].Text == ";" && tokens[start + 1].Type == RtfTokenType.HexByte)
                {
                    char decoded = RtfCodePage.DecodeByte((byte)tokens[start + 1].Parameter, codePage);
                    return char.IsControl(decoded) ? null : decoded.ToString();
                }

                if (next - start == 3 && tokens[start].Type == RtfTokenType.HexByte &&
                    tokens[start].Parameter == 1 && tokens[start + 1].Type == RtfTokenType.Text &&
                    tokens[start + 1].Text is { Length: 2 } literal && literal[1] == ';' &&
                    !char.IsControl(literal[0]) && !char.IsSurrogate(literal[0]))
                    return literal[..1];

                if (next - start != 4 || tokens[start].Type != RtfTokenType.HexByte ||
                    tokens[start].Parameter != 1 || tokens[start + 2].Type != RtfTokenType.Text ||
                    tokens[start + 2].Text != "?;" || tokens[start + 1].Keyword != "u" ||
                    !tokens[start + 1].HasParameter)
                    return null;

                char marker = unchecked((char)tokens[start + 1].Parameter);
                return char.IsControl(marker) || char.IsSurrogate(marker) ? null : marker.ToString();
            }
        }
        return null;
    }

    private static string? ReadNumberedTemplate(IReadOnlyList<RtfToken> tokens, ref int next)
    {
        int depth = 1;
        int length = -1;
        var template = new StringBuilder();
        int units = 0;
        while (next < tokens.Count)
        {
            RtfToken token = tokens[next++];
            if (token.Type == RtfTokenType.GroupStart)
                depth++;
            else if (token.Type == RtfTokenType.GroupEnd && --depth == 0)
                return length >= 0 && units == length + 1 && template.Length > 0 && template[^1] == ';'
                    ? template.ToString(0, template.Length - 1) : null;
            else if (depth == 1 && token.Type == RtfTokenType.HexByte)
            {
                if (length < 0)
                    length = token.Parameter;
                else if (token.Parameter < ListDefinition.MaxLevels && units++ < 255)
                    template.Append('%').Append(token.Parameter + 1);
                else
                    units = 257;
            }
            else if (depth == 1 && token.Type == RtfTokenType.Text && length >= 0 &&
                token.Text.Length <= 256 - units)
            {
                template.Append(token.Text);
                units += token.Text.Length;
            }
            else
                units = 257;
        }

        return null;
    }

    private static ListNumberFormat? NumberFormat(int format) => format switch
    {
        0 => ListNumberFormat.Decimal,
        1 => ListNumberFormat.UpperRoman,
        2 => ListNumberFormat.LowerRoman,
        3 => ListNumberFormat.UpperLetter,
        4 => ListNumberFormat.LowerLetter,
        23 => ListNumberFormat.Bullet,
        255 => ListNumberFormat.None,
        _ => null,
    };

    public bool Read(IReadOnlyList<RtfToken> tokens, ref int next, bool overrides, int codePage)
    {
        int depth = 1;
        int id = 0;
        int instance = 0;
        int overrideCount = 0;
        int format = -1;
        int start = 1;
        string? template = null;
        bool valid = true;
        bool found = false;
        bool entry = false;
        bool listLevel = false;
        var levels = new List<ListLevel>();
        var levelOverrides = new List<(int? Start, ListNumberFormat? Format, string? Template)>();
        bool startOverride = false;
        bool formatOverride = false;
        int? overrideStart = null;
        bool overrideLevel = false;
        bool unsupportedOverride = false;

        while (next < tokens.Count)
        {
            RtfToken token = tokens[next++];
            if (token.Type == RtfTokenType.GroupStart)
            {
                if (entry && format >= 0 &&
                    ((!overrides && depth == 3 && listLevel) || (overrides && depth == 4 && overrideLevel && listLevel)) &&
                    next < tokens.Count && tokens[next].Keyword == "leveltext")
                {
                    next++;
                    template = format == 23 ? ReadSingleBullet(tokens, ref next, codePage) : ReadNumberedTemplate(tokens, ref next);
                    if (template is null && format != 255)
                        valid = false;
                    continue;
                }
                depth++;
                if (depth == 4 && overrides)
                    listLevel = next < tokens.Count && tokens[next].Keyword == "listlevel";
                if (depth == 2)
                {
                    entry = next < tokens.Count && tokens[next].Keyword == (overrides ? "listoverride" : "list");
                    id = instance = overrideCount = 0;
                    levels.Clear();
                    levelOverrides.Clear();
                    unsupportedOverride = false;
                }
                if (depth == 3)
                {
                    listLevel = next < tokens.Count && tokens[next].Keyword == "listlevel";
                    overrideLevel = next < tokens.Count && tokens[next].Keyword == "lfolevel";
                    startOverride = formatOverride = false;
                    overrideStart = null;
                    format = -1;
                    start = 1;
                    template = null;
                }
            }
            else if (token.Type == RtfTokenType.GroupEnd)
            {
                if (depth == 3 && entry && listLevel && !overrides && format >= 0)
                {
                    ListNumberFormat? kind = NumberFormat(format);
                    if (kind is null || levels.Count >= ListDefinition.MaxLevels)
                        valid = false;
                    if (levels.Count < ListDefinition.MaxLevels)
                    {
                        int level = levels.Count;
                        levels.Add(ListLevel.DecimalAt(level) with
                        {
                            Format = kind ?? ListNumberFormat.None,
                            Start = start,
                            Template = template ?? (kind == ListNumberFormat.Bullet
                                ? ListDefinition.PlainBullets.LevelAt(level).Template
                                : ListLevel.DecimalAt(level).Template),
                        });
                    }
                }
                if (depth == 3 && entry && overrides)
                {
                    ListNumberFormat? kind = formatOverride ? NumberFormat(format) : null;
                    if (overrideLevel && (startOverride || formatOverride) &&
                        (!startOverride || overrideStart.HasValue || listLevel && kind.HasValue) &&
                        (!formatOverride || kind.HasValue) && levelOverrides.Count < ListDefinition.MaxLevels)
                        levelOverrides.Add((startOverride ? overrideStart ?? start : null, kind, template));
                    else
                        unsupportedOverride = true;
                }
                if (depth == 2)
                {
                    if (entry && overrides && id != 0 && instance > 0 && !unsupportedOverride &&
                        overrideCount == levelOverrides.Count &&
                        _definitions.TryGetValue(id, out ListDefinition? definition))
                    {
                        ListDefinition resolved = definition.Copy();
                        for (int level = 0; level < levelOverrides.Count; level++)
                        {
                            var change = levelOverrides[level];
                            ListLevel original = resolved.LevelAt(level);
                            string fallback = change.Format == ListNumberFormat.Bullet
                                ? ListDefinition.PlainBullets.LevelAt(level).Template
                                : ListLevel.DecimalAt(level).Template;
                            resolved = resolved.WithLevel(level, original with
                            {
                                Start = change.Start ?? original.Start,
                                Format = change.Format ?? original.Format,
                                Template = change.Format.HasValue ? change.Template ?? fallback : original.Template,
                            });
                        }
                        _instances[instance] = resolved;
                        found = true;
                    }
                    else if (entry && !overrides && id != 0 && levels.Count > 0)
                    {
                        _definitions[id] = new ListDefinition(levels);
                        found = true;
                    }
                    else
                        valid = false;
                }
                if (--depth == 0)
                    return valid && found;
            }
            else if (token.Type == RtfTokenType.ControlWord && depth == 3 && overrides)
            {
                if (token.Keyword == "listoverridestartat")
                    startOverride = true;
                else if (token.Keyword == "listoverrideformat")
                    formatOverride = true;
                else if (token.Keyword == "levelstartat" && token.HasParameter)
                    overrideStart = token.Parameter;
            }
            else if (token.Type == RtfTokenType.ControlWord && token.HasParameter)
            {
                switch (depth, token.Keyword)
                {
                    case (2, "listid"): id = token.Parameter; break;
                    case (2, "ls"): instance = token.Parameter; break;
                    case (2, "listoverridecount"): overrideCount = token.Parameter; break;
                    case (4, "levelnfc") when overrides && overrideLevel && listLevel:
                    case (3, "levelnfc"): format = token.Parameter; break;
                    case (4, "levelstartat") when overrides && overrideLevel && listLevel:
                    case (3, "levelstartat"): start = token.Parameter; break;
                }
            }
        }
        return false;
    }
}
