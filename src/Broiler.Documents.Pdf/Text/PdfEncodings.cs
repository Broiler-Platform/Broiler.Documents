using System;
using System.Collections.Generic;
using System.Globalization;

namespace Broiler.Documents.Pdf.Text;

/// <summary>
/// The single-byte text encodings a simple font may name, expressed directly as
/// code-point tables, plus the glyph-name lookup that an <c>/Encoding</c>
/// <c>/Differences</c> array needs.
/// </summary>
/// <remarks>
/// <para>
/// The tables are authored from the character identities each encoding slot
/// denotes, not transcribed from a third-party glyph-list file, and they map code
/// to Unicode directly rather than code to glyph name to Unicode. That keeps the
/// data small enough to check by eye and keeps the codec free of any external
/// font-data asset (approved-sources record SRC-007).
/// </para>
/// <para>
/// <c>MacExpertEncoding</c> and the symbolic built-in encodings of Symbol and
/// ZapfDingbats are deliberately absent: mapping them needs font-specific data
/// this release does not carry, so a font using one reports
/// <see cref="PdfDiagnosticCodes.TextMappingMissing"/> instead of guessing.
/// </para>
/// </remarks>
internal static class PdfEncodings
{
    public const string Standard = "StandardEncoding";
    public const string WinAnsi = "WinAnsiEncoding";
    public const string MacRoman = "MacRomanEncoding";
    public const string PdfDoc = "PDFDocEncoding";
    public const string MacExpert = "MacExpertEncoding";

    private static readonly char[] StandardTable = BuildStandard();
    private static readonly char[] WinAnsiTable = BuildWinAnsi();
    private static readonly char[] MacRomanTable = BuildMacRoman();

    /// <summary>
    /// Returns the named encoding's table, or null when the name is unknown or
    /// deliberately unsupported.
    /// </summary>
    public static char[]? ForName(string? name) => name switch
    {
        WinAnsi => WinAnsiTable,
        MacRoman => MacRomanTable,
        Standard => StandardTable,
        PdfDoc => StandardTable,
        _ => null,
    };

    /// <summary>
    /// The encoding assumed when a simple font names none. StandardEncoding is
    /// what the format specifies for a non-symbolic font with no <c>/Encoding</c>.
    /// </summary>
    public static char[] Default => StandardTable;

    public static char[] WinAnsiEncoding => WinAnsiTable;

    // ---- glyph names ----------------------------------------------------------

    /// <summary>
    /// Maps a glyph name from a <c>/Differences</c> array to the text it stands
    /// for. Recognizes the algorithmic <c>uniXXXX</c> and <c>uXXXX[XX]</c> forms
    /// as well as the named Latin repertoire.
    /// </summary>
    public static bool TryMapGlyphName(string name, out string text)
    {
        text = string.Empty;
        if (string.IsNullOrEmpty(name))
            return false;

        // A name may carry a suffix, as in "a.sc" or "one.oldstyle"; the base name
        // determines the character.
        int dot = name.IndexOf('.');
        string bare = dot > 0 ? name[..dot] : name;

        if (GlyphNames.TryGetValue(bare, out string? mapped))
        {
            text = mapped;
            return true;
        }

        if (TryParseAlgorithmicName(bare, out text))
            return true;

        // "gNN" and "cidNN" name a glyph index, not a character: there is nothing
        // to map without the font program, and inventing one would be a silent
        // correctness claim.
        return false;
    }

    private static bool TryParseAlgorithmicName(string name, out string text)
    {
        text = string.Empty;

        if (name.Length >= 7 && name.StartsWith("uni", StringComparison.Ordinal))
        {
            // uniXXXX, optionally repeated for a sequence.
            string digits = name[3..];
            if (digits.Length % 4 != 0)
                return false;

            var builder = new System.Text.StringBuilder(digits.Length / 4);
            for (int i = 0; i < digits.Length; i += 4)
            {
                if (!ushort.TryParse(digits.AsSpan(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort unit))
                    return false;
                builder.Append((char)unit);
            }

            text = builder.ToString();
            return true;
        }

        if (name.Length is >= 5 and <= 7 && name[0] == 'u')
        {
            if (!int.TryParse(name.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int scalar))
                return false;
            if (scalar < 0 || scalar > 0x10FFFF || (scalar >= 0xD800 && scalar <= 0xDFFF))
                return false;
            text = char.ConvertFromUtf32(scalar);
            return true;
        }

        return false;
    }

    // ---- table construction ---------------------------------------------------

    private static char[] BuildAscii(char code39, char code96)
    {
        var table = new char[256];
        for (int i = 32; i <= 126; i++)
            table[i] = (char)i;
        table[39] = code39;
        table[96] = code96;
        return table;
    }

    private static char[] BuildStandard()
    {
        // StandardEncoding takes the typographic quotes at 39 and 96.
        char[] table = BuildAscii('’', '‘');

        Assign(table, 161, "¡¢£⁄¥ƒ§¤'“«‹›ﬁﬂ");
        Assign(table, 177, "–†‡·");
        Assign(table, 182, "¶•‚„”»…‰");
        table[191] = '¿';
        Assign(table, 193, "`´ˆ˜¯˘˙¨");
        Assign(table, 202, "˚¸");
        Assign(table, 205, "˝˛ˇ—");
        table[225] = 'Æ';
        table[227] = 'ª';
        Assign(table, 232, "ŁØŒº");
        table[241] = 'æ';
        table[245] = 'ı';
        Assign(table, 248, "łøœß");
        return table;
    }

    private static char[] BuildWinAnsi()
    {
        char[] table = BuildAscii('\'', '`');

        // 0x80–0x9F carry typographic characters rather than control codes. The
        // gaps (0x81, 0x8D, 0x8F, 0x90 and 0x9D) are undefined and must stay
        // unmapped, so the slots are assigned one by one rather than from a run.
        table[0x80] = '€';
        table[0x82] = '‚';
        table[0x83] = 'ƒ';
        table[0x84] = '„';
        table[0x85] = '…';
        table[0x86] = '†';
        table[0x87] = '‡';
        table[0x88] = 'ˆ';
        table[0x89] = '‰';
        table[0x8A] = 'Š';
        table[0x8B] = '‹';
        table[0x8C] = 'Œ';
        table[0x8E] = 'Ž';
        table[0x91] = '‘';
        table[0x92] = '’';
        table[0x93] = '“';
        table[0x94] = '”';
        table[0x95] = '•';
        table[0x96] = '–';
        table[0x97] = '—';
        table[0x98] = '˜';
        table[0x99] = '™';
        table[0x9A] = 'š';
        table[0x9B] = '›';
        table[0x9C] = 'œ';
        table[0x9E] = 'ž';
        table[0x9F] = 'Ÿ';

        // 0xA0–0xFF coincide with Latin-1.
        for (int i = 0xA0; i <= 0xFF; i++)
            table[i] = (char)i;

        return table;
    }

    private static char[] BuildMacRoman()
    {
        char[] table = BuildAscii('\'', '`');

        Assign(table, 0x80, "ÄÅÇÉÑÖÜáàâäãåçéè");
        Assign(table, 0x90, "êëíìîïñóòôöõúùûü");
        Assign(table, 0xA0, "†°¢£§•¶ß®©™´¨≠ÆØ");
        Assign(table, 0xB0, "∞±≤≥¥µ∂∑∏π∫ªºΩæø");
        Assign(table, 0xC0, "¿¡¬√ƒ≈∆«»… ÀÃÕŒœ");
        // 0xDB is currency in Adobe's MacRomanEncoding, where Mac OS Roman has the euro.
        Assign(table, 0xD0, "–—“”‘’÷◊ÿŸ⁄¤‹›ﬁﬂ");
        Assign(table, 0xE0, "‡·‚„‰ÂÊÁËÈÍÎÏÌÓÔ");
        // 0xF0 is unused by MacRomanEncoding (it is the Apple logo in Mac OS Roman).
        Assign(table, 0xF1, "ÒÚÛÙıˆ˜¯˘˙˚¸˝˛ˇ");
        return table;
    }

    private static void Assign(char[] table, int start, string values)
    {
        for (int i = 0; i < values.Length && start + i < table.Length; i++)
        {
            if (values[i] != '\0')
                table[start + i] = values[i];
        }
    }

    /// <summary>
    /// The Latin glyph-name repertoire, authored from the character each name
    /// denotes. Names outside it fall through to the algorithmic forms and then to
    /// an explicit "no mapping" result.
    /// </summary>
    private static readonly Dictionary<string, string> GlyphNames = BuildGlyphNames();

    private static Dictionary<string, string> BuildGlyphNames()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);

        void Add(string name, char value) => names[name] = value.ToString();

        // Letters and digits carry their own names.
        for (char c = 'A'; c <= 'Z'; c++)
            Add(c.ToString(), c);
        for (char c = 'a'; c <= 'z'; c++)
            Add(c.ToString(), c);
        string[] digitNames = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"];
        for (int i = 0; i < digitNames.Length; i++)
            Add(digitNames[i], (char)('0' + i));

        // ASCII punctuation.
        Add("space", ' ');
        Add("exclam", '!');
        Add("quotedbl", '"');
        Add("numbersign", '#');
        Add("dollar", '$');
        Add("percent", '%');
        Add("ampersand", '&');
        Add("quotesingle", '\'');
        Add("parenleft", '(');
        Add("parenright", ')');
        Add("asterisk", '*');
        Add("plus", '+');
        Add("comma", ',');
        Add("hyphen", '-');
        Add("period", '.');
        Add("slash", '/');
        Add("colon", ':');
        Add("semicolon", ';');
        Add("less", '<');
        Add("equal", '=');
        Add("greater", '>');
        Add("question", '?');
        Add("at", '@');
        Add("bracketleft", '[');
        Add("backslash", '\\');
        Add("bracketright", ']');
        Add("asciicircum", '^');
        Add("underscore", '_');
        Add("grave", '`');
        Add("braceleft", '{');
        Add("bar", '|');
        Add("braceright", '}');
        Add("asciitilde", '~');

        // Typographic and Latin-1 punctuation and symbols.
        Add("exclamdown", '¡');
        Add("cent", '¢');
        Add("sterling", '£');
        Add("currency", '¤');
        Add("yen", '¥');
        Add("brokenbar", '¦');
        Add("section", '§');
        Add("dieresis", '¨');
        Add("copyright", '©');
        Add("ordfeminine", 'ª');
        Add("guillemotleft", '«');
        Add("logicalnot", '¬');
        Add("registered", '®');
        Add("macron", '¯');
        Add("degree", '°');
        Add("plusminus", '±');
        Add("twosuperior", '²');
        Add("threesuperior", '³');
        Add("acute", '´');
        Add("mu", 'µ');
        Add("paragraph", '¶');
        Add("periodcentered", '·');
        Add("cedilla", '¸');
        Add("onesuperior", '¹');
        Add("ordmasculine", 'º');
        Add("guillemotright", '»');
        Add("onequarter", '¼');
        Add("onehalf", '½');
        Add("threequarters", '¾');
        Add("questiondown", '¿');
        Add("multiply", '×');
        Add("divide", '÷');
        Add("nbspace", ' ');
        Add("quoteleft", '‘');
        Add("quoteright", '’');
        Add("quotesinglbase", '‚');
        Add("quotedblleft", '“');
        Add("quotedblright", '”');
        Add("quotedblbase", '„');
        Add("dagger", '†');
        Add("daggerdbl", '‡');
        Add("bullet", '•');
        Add("endash", '–');
        Add("emdash", '—');
        Add("ellipsis", '…');
        Add("perthousand", '‰');
        Add("guilsinglleft", '‹');
        Add("guilsinglright", '›');
        Add("fraction", '⁄');
        Add("Euro", '€');
        Add("euro", '€');
        Add("trademark", '™');
        Add("minus", '−');
        Add("fi", 'ﬁ');
        Add("fl", 'ﬂ');
        Add("florin", 'ƒ');
        Add("circumflex", 'ˆ');
        Add("caron", 'ˇ');
        Add("breve", '˘');
        Add("dotaccent", '˙');
        Add("ring", '˚');
        Add("ogonek", '˛');
        Add("tilde", '˜');
        Add("hungarumlaut", '˝');
        Add("dotlessi", 'ı');

        // Accented Latin letters, upper case then lower case.
        AddPairs(names,
            [
                ("Agrave", 'À'), ("Aacute", 'Á'), ("Acircumflex", 'Â'), ("Atilde", 'Ã'),
                ("Adieresis", 'Ä'), ("Aring", 'Å'), ("AE", 'Æ'), ("Ccedilla", 'Ç'),
                ("Egrave", 'È'), ("Eacute", 'É'), ("Ecircumflex", 'Ê'), ("Edieresis", 'Ë'),
                ("Igrave", 'Ì'), ("Iacute", 'Í'), ("Icircumflex", 'Î'), ("Idieresis", 'Ï'),
                ("Eth", 'Ð'), ("Ntilde", 'Ñ'), ("Ograve", 'Ò'), ("Oacute", 'Ó'),
                ("Ocircumflex", 'Ô'), ("Otilde", 'Õ'), ("Odieresis", 'Ö'), ("Oslash", 'Ø'),
                ("Ugrave", 'Ù'), ("Uacute", 'Ú'), ("Ucircumflex", 'Û'), ("Udieresis", 'Ü'),
                ("Yacute", 'Ý'), ("Thorn", 'Þ'), ("germandbls", 'ß'),
                ("agrave", 'à'), ("aacute", 'á'), ("acircumflex", 'â'), ("atilde", 'ã'),
                ("adieresis", 'ä'), ("aring", 'å'), ("ae", 'æ'), ("ccedilla", 'ç'),
                ("egrave", 'è'), ("eacute", 'é'), ("ecircumflex", 'ê'), ("edieresis", 'ë'),
                ("igrave", 'ì'), ("iacute", 'í'), ("icircumflex", 'î'), ("idieresis", 'ï'),
                ("eth", 'ð'), ("ntilde", 'ñ'), ("ograve", 'ò'), ("oacute", 'ó'),
                ("ocircumflex", 'ô'), ("otilde", 'õ'), ("odieresis", 'ö'), ("oslash", 'ø'),
                ("ugrave", 'ù'), ("uacute", 'ú'), ("ucircumflex", 'û'), ("udieresis", 'ü'),
                ("yacute", 'ý'), ("thorn", 'þ'), ("ydieresis", 'ÿ'),
                ("Lslash", 'Ł'), ("lslash", 'ł'), ("OE", 'Œ'), ("oe", 'œ'),
                ("Scaron", 'Š'), ("scaron", 'š'), ("Ydieresis", 'Ÿ'),
                ("Zcaron", 'Ž'), ("zcaron", 'ž'),
            ]);

        return names;
    }

    private static void AddPairs(Dictionary<string, string> names, (string Name, char Value)[] pairs)
    {
        foreach ((string name, char value) in pairs)
            names[name] = value.ToString();
    }
}
