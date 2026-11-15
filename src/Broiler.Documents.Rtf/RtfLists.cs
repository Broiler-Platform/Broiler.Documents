using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Broiler.Documents.Model;

namespace Broiler.Documents.Rtf;

/// <summary>
/// Reads the modern RTF list destinations, <c>\listtable</c> and
/// <c>\listoverridetable</c>, as LibreOffice's RTF import reads them
/// (<c>writerfilter/source/rtftok</c>).
/// </summary>
/// <remarks>
/// <para>
/// A level's label comes from its <c>\leveltext</c> and <c>\levelnumbers</c>.
/// The level text is decoded in the document's code page first, so that a
/// double-byte character counts once, and its first character is the length of
/// the label that follows it. <c>\levelnumbers</c> lists the 1-based positions in
/// that label where a number stands; the n-th of k positions at level l is
/// numbered <c>%(n + l + 1 - k)</c>, which is LibreOffice's rule and draws
/// "1.1" at the second level for "\'01.\'01" with two positions. A level with no
/// <c>\leveltext</c> draws no label, and a <c>\levelnfc</c> LibreOffice does not
/// know draws no number but keeps the label's other characters - both measured
/// against LibreOffice 24.2.
/// </para>
/// <para>
/// Without <c>\levelnumbers</c> the label's control characters 0 to 8 are taken
/// as the level placeholders, as Word writes them.
/// </para>
/// </remarks>
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

    /// <summary>
    /// A <c>\levelnfc</c> (or <c>\pn</c>) number format: Word's NFC codes, as
    /// LibreOffice's RTF import maps them. The ones this model cannot draw are
    /// written in decimal, as the DOCX reader writes an unknown <c>w:numFmt</c>;
    /// a code LibreOffice does not map at all draws no number.
    /// </summary>
    internal static ListNumberFormat NumberFormat(int nfc) => nfc switch
    {
        0 or 14 or 15 or 17 or 19 or 26 or 27 or 32 or 36 or 40 => ListNumberFormat.Decimal,
        1 => ListNumberFormat.UpperRoman,
        2 => ListNumberFormat.LowerRoman,
        3 => ListNumberFormat.UpperLetter,
        4 => ListNumberFormat.LowerLetter,
        5 => ListNumberFormat.Ordinal,
        6 => ListNumberFormat.CardinalText,
        7 => ListNumberFormat.OrdinalText,
        10 or 11 or 33 or 35 or 37 or 39 => ListNumberFormat.IdeographCounting,
        12 => ListNumberFormat.AiueoHalfWidth,
        13 => ListNumberFormat.IrohaHalfWidth,
        16 => ListNumberFormat.JapaneseLegal,
        18 or 28 or 29 => ListNumberFormat.CircledNumber,
        20 => ListNumberFormat.AiueoFullWidth,
        21 => ListNumberFormat.IrohaFullWidth,
        22 => ListNumberFormat.DecimalZero,
        23 => ListNumberFormat.Bullet,
        24 => ListNumberFormat.HangulSyllable,
        25 => ListNumberFormat.HangulJamo,
        30 => ListNumberFormat.TianGan,
        31 => ListNumberFormat.DiZi,
        34 => ListNumberFormat.IdeographLegalTraditional,
        38 => ListNumberFormat.ChineseLegalSimplified,
        41 or 42 or 43 or 44 or 45 => ListNumberFormat.Decimal,
        46 => ListNumberFormat.ArabicAlpha,
        47 => ListNumberFormat.HebrewAlphabetic,
        48 => ListNumberFormat.ArabicAbjad,
        _ => ListNumberFormat.None,
    };

    /// <summary>
    /// A bullet in a symbol font as the character it draws: Word's symbol fonts
    /// put their glyphs at U+F000 plus the byte, which LibreOffice maps to the
    /// Unicode character, so the Symbol bullet <c>\'b7</c> is "•".
    /// </summary>
    internal static string SymbolBullet(string text)
    {
        var chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            int code = chars[i] < 0x100 ? 0xF000 + chars[i] : chars[i];
            chars[i] = code switch
            {
                0xF0B7 => (char)0x2022,
                0xF0A7 => (char)0x25AA,
                0xF0A8 => (char)0x25A1,
                0xF06E => (char)0x25A0,
                0xF0D8 => (char)0x27A2,
                0xF076 => (char)0x2756,
                0xF0FC => (char)0x2713,
                0xF02D => '-',
                >= 0xF000 and <= 0xF0FF => (char)0x2022,
                _ => chars[i],
            };
        }
        return new string(chars);
    }

    internal static bool IsSymbolFont(RtfFontTable? fonts, int? font)
    {
        if (fonts is null || font is not int index)
            return false;
        if (fonts.GetCharset(index) == 2)
            return true;
        string? name = fonts.GetName(index);
        return name is not null && (name.Equals("Symbol", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Wingdings", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Webdings", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reads a Word 6/95 <c>{\*\pn ...}</c> paragraph numbering group, whose
    /// opening brace and keyword were just read, as LibreOffice reads it: a
    /// bulleted (<c>\pnlvlblt</c>) or simply numbered (<c>\pnlvlbody</c>)
    /// one-level list whose label is <c>\pntxtb</c>, the number and
    /// <c>\pntxta</c>; a bullet is the first character of <c>\pntxtb</c>. All the
    /// paragraphs numbered alike share one counter. Null for the outline levels
    /// (<c>\pnlvlN</c>, <c>\pnlvlcont</c>), which are not read.
    /// </summary>
    public ListReference? ReadParagraphNumbering(IReadOnlyList<RtfToken> tokens, ref int next, int codePage,
        RtfFontTable? fonts)
    {
        bool? bullet = null;
        ListNumberFormat? format = null;
        int start = 1;
        int? font = null;
        string before = string.Empty;
        string after = string.Empty;
        while (next < tokens.Count)
        {
            RtfToken token = tokens[next++];
            if (token.Type == RtfTokenType.GroupEnd)
                break;
            if (token.Type == RtfTokenType.ControlWord)
            {
                switch (token.Keyword)
                {
                    case "pnlvlblt": bullet = true; break;
                    case "pnlvlbody": bullet = false; break;
                    case "pnlvl" or "pnlvlcont": bullet = null; break;
                    case "pndec": format = ListNumberFormat.Decimal; break;
                    case "pnucltr": format = ListNumberFormat.UpperLetter; break;
                    case "pnlcltr": format = ListNumberFormat.LowerLetter; break;
                    case "pnucrm": format = ListNumberFormat.UpperRoman; break;
                    case "pnlcrm": format = ListNumberFormat.LowerRoman; break;
                    case "pnord": format = ListNumberFormat.Ordinal; break;
                    case "pncard": format = ListNumberFormat.CardinalText; break;
                    case "pnordt": format = ListNumberFormat.OrdinalText; break;
                    case "pnstart" when token.HasParameter: start = token.Parameter; break;
                    case "pnf" when token.HasParameter: font = token.Parameter; break;
                }
            }
            else if (token.Type == RtfTokenType.GroupStart)
            {
                string? keyword = Keyword(tokens, next);
                if (keyword is "pntxtb" or "pntxta" && tokens[next].Type == RtfTokenType.ControlWord)
                {
                    next++;
                    string text = ReadNumberingText(tokens, ref next, codePage);
                    if (keyword == "pntxtb")
                        before = text;
                    else
                        after = text;
                }
                else
                    SkipGroup(tokens, ref next);
            }
        }

        if (bullet is not bool isBullet)
            return null;

        ListLevel level;
        if (isBullet)
        {
            string marker = before.Length > 0 ? before[..1] : string.Empty;
            level = new ListLevel
            {
                Format = ListNumberFormat.Bullet,
                Template = IsSymbolFont(fonts, font) ? SymbolBullet(marker) : marker,
                Start = start,
            };
        }
        else
        {
            level = new ListLevel
            {
                Format = format ?? ListNumberFormat.Decimal,
                Template = before.Replace("%", string.Empty, StringComparison.Ordinal) + "%1" +
                    after.Replace("%", string.Empty, StringComparison.Ordinal),
                Start = start,
            };
        }

        if (!_paragraphNumbering.TryGetValue(level, out ListDefinition? definition))
        {
            definition = new ListDefinition([level]);
            _paragraphNumbering.Add(level, definition);
        }
        return new ListReference(definition, 0);
    }

    private readonly Dictionary<ListLevel, ListDefinition> _paragraphNumbering = [];

    /// <summary>The text of a <c>\pntxtb</c> or <c>\pntxta</c> group.</summary>
    private static string ReadNumberingText(IReadOnlyList<RtfToken> tokens, ref int next, int codePage)
    {
        var text = new StringBuilder();
        int depth = 1;
        int skip = 0;
        while (next < tokens.Count)
        {
            RtfToken token = tokens[next++];
            switch (token.Type)
            {
                case RtfTokenType.GroupStart:
                    depth++;
                    break;
                case RtfTokenType.GroupEnd:
                    if (--depth == 0)
                        return text.ToString();
                    break;
                case RtfTokenType.HexByte:
                    if (skip > 0)
                        skip--;
                    else
                        text.Append(RtfCodePage.DecodeByte((byte)token.Parameter, codePage));
                    break;
                case RtfTokenType.Text:
                    foreach (char c in token.Text)
                    {
                        if (skip > 0)
                            skip--;
                        else
                            text.Append(c < 0x80 ? c : RtfCodePage.DecodeByte((byte)c, codePage));
                    }
                    break;
                case RtfTokenType.ControlWord:
                    switch (token.Keyword)
                    {
                        case "u" when token.HasParameter:
                            text.Append(unchecked((char)token.Parameter));
                            skip = 1;
                            break;
                        case "bullet": text.Append('•'); break;
                        case "endash": text.Append('–'); break;
                        case "emdash": text.Append('—'); break;
                        case "lquote": text.Append('‘'); break;
                        case "rquote": text.Append('’'); break;
                        case "ldblquote": text.Append('“'); break;
                        case "rdblquote": text.Append('”'); break;
                    }
                    break;
            }
        }
        return text.ToString();
    }

    public bool Read(IReadOnlyList<RtfToken> tokens, ref int next, bool overrides, int codePage, RtfFontTable? fonts = null)
    {
        bool valid = true;
        bool found = false;
        while (next < tokens.Count)
        {
            RtfToken token = tokens[next++];
            if (token.Type == RtfTokenType.GroupEnd)
                return valid && found;
            if (token.Type != RtfTokenType.GroupStart)
                continue;

            string? keyword = Keyword(tokens, next);
            if (!overrides && keyword == "list")
            {
                if (ReadList(tokens, ref next, codePage, fonts))
                    found = true;
                else
                    valid = false;
            }
            else if (overrides && keyword == "listoverride")
            {
                bool? read = ReadOverride(tokens, ref next, codePage, fonts);
                if (read != false)
                    found = true;
                if (read != true)
                    valid = false;
            }
            else
                SkipGroup(tokens, ref next);
        }
        return false;
    }

    private static string? Keyword(IReadOnlyList<RtfToken> tokens, int at)
    {
        if (at < tokens.Count && tokens[at].Type == RtfTokenType.ControlSymbol && tokens[at].Symbol == '*')
            at++;
        return at < tokens.Count && tokens[at].Type == RtfTokenType.ControlWord ? tokens[at].Keyword : null;
    }

    /// <summary>Skips the rest of a group whose opening brace was just read.</summary>
    private static void SkipGroup(IReadOnlyList<RtfToken> tokens, ref int next)
    {
        int depth = 1;
        while (next < tokens.Count)
        {
            RtfToken token = tokens[next++];
            if (token.Type == RtfTokenType.GroupStart)
                depth++;
            else if (token.Type == RtfTokenType.GroupEnd && --depth == 0)
                return;
        }
    }

    private bool ReadList(IReadOnlyList<RtfToken> tokens, ref int next, int codePage, RtfFontTable? fonts)
    {
        int? id = null;
        bool valid = true;
        var levels = new List<ListLevel>();
        while (next < tokens.Count)
        {
            RtfToken token = tokens[next++];
            if (token.Type == RtfTokenType.GroupEnd)
                break;
            if (token.Type == RtfTokenType.ControlWord && token.Keyword == "listid" && token.HasParameter)
                id = token.Parameter;
            else if (token.Type == RtfTokenType.GroupStart)
            {
                if (Keyword(tokens, next) == "listlevel" && tokens[next].Type == RtfTokenType.ControlWord)
                {
                    next++;
                    Level level = ReadLevel(tokens, ref next, codePage, fonts);
                    if (levels.Count < ListDefinition.MaxLevels)
                    {
                        levels.Add(level.Build(levels.Count, fonts, out bool built));
                        valid &= built;
                    }
                }
                else
                    SkipGroup(tokens, ref next);
            }
        }

        if (id is not int listId || levels.Count == 0)
            return false;
        _definitions[listId] = new ListDefinition(levels);
        return valid;
    }

    /// <summary>
    /// One <c>\listoverride</c>: true when read whole, false when it names no
    /// list, and null when it names one but some level override was not understood.
    /// </summary>
    private bool? ReadOverride(IReadOnlyList<RtfToken> tokens, ref int next, int codePage, RtfFontTable? fonts)
    {
        int? id = null;
        int? instance = null;
        bool understood = true;
        var changes = new List<(int? Start, Level? Level)>();
        while (next < tokens.Count)
        {
            RtfToken token = tokens[next++];
            if (token.Type == RtfTokenType.GroupEnd)
                break;
            if (token.Type == RtfTokenType.ControlWord && token.HasParameter)
            {
                if (token.Keyword == "listid")
                    id = token.Parameter;
                else if (token.Keyword == "ls")
                    instance = token.Parameter;
            }
            else if (token.Type == RtfTokenType.GroupStart)
            {
                if (Keyword(tokens, next) == "lfolevel" && tokens[next].Type == RtfTokenType.ControlWord)
                {
                    next++;
                    var change = ReadOverrideLevel(tokens, ref next, codePage, fonts, out bool levelUnderstood);
                    changes.Add(change);
                    understood &= levelUnderstood;
                }
                else
                    SkipGroup(tokens, ref next);
            }
        }

        if (id is not int listId || instance is not int ls || !_definitions.TryGetValue(listId, out ListDefinition? definition))
            return false;

        ListDefinition resolved = definition.Copy();
        for (int level = 0; level < changes.Count && level < ListDefinition.MaxLevels; level++)
        {
            var (start, replacement) = changes[level];
            ListLevel original = resolved.LevelAt(level);
            ListLevel changed = replacement?.Build(level, fonts, out _) is ListLevel built
                ? built with { Start = start ?? built.Start }
                : original with { Start = start ?? original.Start };
            resolved = resolved.WithLevel(level, changed);
        }
        _instances[ls] = resolved;
        return understood ? true : null;
    }

    private static (int? Start, Level? Level) ReadOverrideLevel(IReadOnlyList<RtfToken> tokens, ref int next,
        int codePage, RtfFontTable? fonts, out bool understood)
    {
        bool startOverride = false;
        bool formatOverride = false;
        int? start = null;
        Level? level = null;
        while (next < tokens.Count)
        {
            RtfToken token = tokens[next++];
            if (token.Type == RtfTokenType.GroupEnd)
                break;
            if (token.Type == RtfTokenType.ControlWord)
            {
                switch (token.Keyword)
                {
                    case "listoverridestartat": startOverride = true; break;
                    case "listoverrideformat": formatOverride = true; break;
                    case "levelstartat" when token.HasParameter: start = token.Parameter; break;
                }
            }
            else if (token.Type == RtfTokenType.GroupStart)
            {
                if (Keyword(tokens, next) == "listlevel" && tokens[next].Type == RtfTokenType.ControlWord)
                {
                    next++;
                    level = ReadLevel(tokens, ref next, codePage, fonts);
                }
                else
                    SkipGroup(tokens, ref next);
            }
        }

        understood = startOverride || formatOverride
            ? (!startOverride || start.HasValue || level?.Start is not null) && (!formatOverride || level is not null)
            : level is null && start is null;
        int? newStart = startOverride ? start ?? level?.Start : null;
        return (newStart, formatOverride ? level : null);
    }

    private static Level ReadLevel(IReadOnlyList<RtfToken> tokens, ref int next, int codePage, RtfFontTable? fonts)
    {
        var level = new Level();
        while (next < tokens.Count)
        {
            RtfToken token = tokens[next++];
            if (token.Type == RtfTokenType.GroupEnd)
                break;
            if (token.Type == RtfTokenType.ControlWord && token.HasParameter)
            {
                switch (token.Keyword)
                {
                    case "levelnfc": level.Nfc = token.Parameter; break;
                    case "levelnfcn": level.NfcN = token.Parameter; break;
                    case "levelstartat": level.Start = token.Parameter; break;
                    case "f": level.Font = token.Parameter; break;
                }
            }
            else if (token.Type == RtfTokenType.GroupStart)
            {
                string? keyword = Keyword(tokens, next);
                if (keyword == "leveltext" && tokens[next].Type == RtfTokenType.ControlWord)
                {
                    next++;
                    level.Text = ReadLevelText(tokens, ref next, codePage);
                }
                else if (keyword == "levelnumbers" && tokens[next].Type == RtfTokenType.ControlWord)
                {
                    next++;
                    level.Numbers = ReadLevelNumbers(tokens, ref next, out level.NumbersLost);
                }
                else
                    SkipGroup(tokens, ref next);
            }
        }
        return level;
    }

    /// <summary>
    /// The text of a <c>\leveltext</c> group, nested groups included, with its
    /// bytes decoded in <paramref name="codePage"/> and its <c>\uN</c> escapes
    /// read with their <c>\ucN</c> fallbacks skipped.
    /// </summary>
    private static string ReadLevelText(IReadOnlyList<RtfToken> tokens, ref int next, int codePage)
    {
        var text = new StringBuilder();
        var bytes = new List<byte>();
        var skips = new Stack<int>();
        int uc = 1;
        int skip = 0;

        void Flush()
        {
            if (bytes.Count == 0)
                return;
            if (RtfCodePage.IsDbcsCodePage(codePage) && RtfCodePage.EncodingFor(codePage) is { } encoding)
                text.Append(encoding.GetString([.. bytes]));
            else
                foreach (byte value in bytes)
                    text.Append(RtfCodePage.DecodeByte(value, codePage));
            bytes.Clear();
        }

        int depth = 1;
        while (next < tokens.Count)
        {
            RtfToken token = tokens[next++];
            switch (token.Type)
            {
                case RtfTokenType.GroupStart:
                    depth++;
                    skips.Push(uc);
                    break;
                case RtfTokenType.GroupEnd:
                    if (--depth == 0)
                    {
                        Flush();
                        return text.ToString();
                    }
                    uc = skips.Count > 0 ? skips.Pop() : uc;
                    skip = 0;
                    break;
                case RtfTokenType.HexByte:
                    if (skip > 0)
                        skip--;
                    else
                        bytes.Add((byte)token.Parameter);
                    break;
                case RtfTokenType.Text:
                    foreach (char c in token.Text)
                    {
                        if (skip > 0)
                            skip--;
                        else
                            bytes.Add((byte)c);
                    }
                    break;
                case RtfTokenType.ControlWord when token.Keyword == "uc" && token.HasParameter:
                    uc = Math.Max(0, token.Parameter);
                    break;
                case RtfTokenType.ControlWord when token.Keyword == "u" && token.HasParameter:
                    Flush();
                    text.Append(unchecked((char)token.Parameter));
                    skip = uc;
                    break;
            }
        }
        Flush();
        return text.ToString();
    }

    /// <summary>
    /// The positions a <c>\levelnumbers</c> group lists: each byte or character
    /// but the <c>;</c> that ends it. A <c>;</c> written as <c>\u59</c> leaves
    /// LibreOffice 24.2 with no label for the level at all, which
    /// <paramref name="lost"/> reports.
    /// </summary>
    private static List<int> ReadLevelNumbers(IReadOnlyList<RtfToken> tokens, ref int next, out bool lost)
    {
        var numbers = new List<int>();
        lost = false;
        int depth = 1;
        int uc = 1;
        int skip = 0;
        var skips = new Stack<int>();
        while (next < tokens.Count)
        {
            RtfToken token = tokens[next++];
            switch (token.Type)
            {
                case RtfTokenType.GroupStart:
                    depth++;
                    skips.Push(uc);
                    break;
                case RtfTokenType.GroupEnd:
                    if (--depth == 0)
                        return numbers;
                    uc = skips.Count > 0 ? skips.Pop() : uc;
                    skip = 0;
                    break;
                case RtfTokenType.HexByte:
                    if (skip > 0)
                        skip--;
                    else if (token.Parameter != ';')
                        numbers.Add(token.Parameter);
                    break;
                case RtfTokenType.Text:
                    foreach (char c in token.Text)
                    {
                        if (skip > 0)
                            skip--;
                        else if (c != ';')
                            numbers.Add(c);
                    }
                    break;
                case RtfTokenType.ControlWord when token.Keyword == "uc" && token.HasParameter:
                    uc = Math.Max(0, token.Parameter);
                    break;
                case RtfTokenType.ControlWord when token.Keyword == "u" && token.HasParameter:
                    if (token.Parameter == ';')
                        lost = true;
                    else
                        numbers.Add(token.Parameter);
                    skip = uc;
                    break;
            }
        }
        return numbers;
    }

    /// <summary>A <c>\listlevel</c> as read, before it is resolved to a <see cref="ListLevel"/>.</summary>
    private sealed class Level
    {
        public int? Nfc;
        public int? NfcN;
        public int? Start;
        public int? Font;
        public string? Text;
        public List<int>? Numbers;
        public bool NumbersLost;

        /// <summary>
        /// The level LibreOffice draws; <paramref name="valid"/> is false when the
        /// label could not be read and the level's default stands in for it.
        /// </summary>
        public ListLevel Build(int level, RtfFontTable? fonts, out bool valid)
        {
            valid = true;
            ListNumberFormat format = NumberFormat(Nfc ?? NfcN ?? 0);
            string label = Label(Text);
            string template;
            if (format == ListNumberFormat.Bullet)
                // A bullet level with no text of its own still draws LibreOffice's
                // default bullet (tdf122455.rtf).
                template = label.Length == 0 || label.All(char.IsControl)
                    ? ListDefinition.PlainBullets.LevelAt(level).Template
                    : IsSymbolFont(fonts, Font) ? SymbolBullet(label) : label;
            else if (Text is null || NumbersLost)
                template = string.Empty;
            else if (Numbers is not null)
                template = Placeholders(label, Numbers, level);
            else if (WordPlaceholders(label) is string word)
                template = word;
            else
            {
                valid = false;
                template = ListLevel.DecimalAt(level).Template;
            }

            return ListLevel.DecimalAt(level) with
            {
                Format = format,
                Start = Start ?? 1,
                Template = template,
            };
        }

        /// <summary>The label a level text holds: its first character is the label's length.</summary>
        private static string Label(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            int length = text[0];
            return length < text.Length ? text.Substring(1, length) : text;
        }

        private static string Placeholders(string label, List<int> numbers, int level)
        {
            var template = new StringBuilder(label.Length + 4);
            int replaced = 1;
            for (int i = 0; i < label.Length; i++)
            {
                if (numbers.Contains(i + 1))
                {
                    int number = replaced++ + level + 1 - numbers.Count;
                    if (number is < 1 or > ListDefinition.MaxLevels)
                        return string.Empty;
                    template.Append('%').Append((char)('0' + number));
                }
                else if (!char.IsControl(label[i]))
                    template.Append(label[i]);
            }
            return template.ToString();
        }

        private static string? WordPlaceholders(string label)
        {
            var template = new StringBuilder(label.Length + 4);
            foreach (char c in label)
            {
                if (c < ListDefinition.MaxLevels)
                    template.Append('%').Append((char)('1' + c));
                else if (char.IsControl(c))
                    return null;
                else
                    template.Append(c);
            }
            return template.ToString();
        }
    }
}
