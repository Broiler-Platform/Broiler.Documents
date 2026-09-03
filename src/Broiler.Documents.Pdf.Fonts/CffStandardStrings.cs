namespace Broiler.Documents.Pdf.Fonts;

/// <summary>
/// The 391 standard strings a CFF font may name by index instead of carrying,
/// in the order the format fixes.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is a transcribed normative table, and the second one in this
/// repository.</strong> Every other table here was authored from an underlying
/// fact — an encoding from the character each slot denotes, a metric from a
/// letterform's proportions — and that was possible because the fact existed
/// independently of the document stating it. This one has no such fact behind it.
/// A string identifier is a position in a list, the list is arbitrary, and a font
/// that says SID 34 means the glyph the format's thirty-fifth entry names. An
/// implementation either reproduces the list or cannot resolve a single standard
/// name.
/// </para>
/// <para>
/// It is recorded as transcribed in SRC-016 rather than presented as authored,
/// for the same reason <c>CcittFaxTables</c> is recorded that way in SRC-017: the
/// register is only useful if it says which data came from where, and a table
/// that could not be derived is exactly the kind a reviewer must see.
/// </para>
/// <para>
/// Only the ordering comes from the specification. What each name <em>means</em>
/// is not decided here at all — the codec maps a name to text through its own
/// authored glyph-name data (SRC-010), so a name this table resolves may still
/// map to nothing, and the Expert-set entries from SID 229 onward mostly do.
/// Nothing here infers that <c>Asmall</c> is an "A".
/// </para>
/// </remarks>
internal static class CffStandardStrings
{
    /// <summary>How many strings the format defines. A SID at or above this indexes the font's own.</summary>
    public const int Count = 391;

    private static readonly string[] Names =
    [
        ".notdef", "space", "exclam", "quotedbl", "numbersign", "dollar", "percent", "ampersand",
        "quoteright", "parenleft", "parenright", "asterisk", "plus", "comma", "hyphen", "period",
        "slash", "zero", "one", "two", "three", "four", "five", "six",
        "seven", "eight", "nine", "colon", "semicolon", "less", "equal", "greater",
        "question", "at", "A", "B", "C", "D", "E", "F",
        "G", "H", "I", "J", "K", "L", "M", "N",
        "O", "P", "Q", "R", "S", "T", "U", "V",
        "W", "X", "Y", "Z", "bracketleft", "backslash", "bracketright", "asciicircum",
        "underscore", "quoteleft", "a", "b", "c", "d", "e", "f",
        "g", "h", "i", "j", "k", "l", "m", "n",
        "o", "p", "q", "r", "s", "t", "u", "v",
        "w", "x", "y", "z", "braceleft", "bar", "braceright", "asciitilde",
        "exclamdown", "cent", "sterling", "fraction", "yen", "florin", "section", "currency",
        "quotesingle", "quotedblleft", "guillemotleft", "guilsinglleft", "guilsinglright", "fi", "fl", "endash",
        "dagger", "daggerdbl", "periodcentered", "paragraph", "bullet", "quotesinglbase", "quotedblbase", "quotedblright",
        "guillemotright", "ellipsis", "perthousand", "questiondown", "grave", "acute", "circumflex", "tilde",
        "macron", "breve", "dotaccent", "dieresis", "ring", "cedilla", "hungarumlaut", "ogonek",
        "caron", "emdash", "AE", "ordfeminine", "Lslash", "Oslash", "OE", "ordmasculine",
        "ae", "dotlessi", "lslash", "oslash", "oe", "germandbls", "onesuperior", "logicalnot",
        "mu", "trademark", "Eth", "onehalf", "plusminus", "Thorn", "onequarter", "divide",
        "brokenbar", "degree", "thorn", "threequarters", "twosuperior", "registered", "minus", "eth",
        "multiply", "threesuperior", "copyright", "Aacute", "Acircumflex", "Adieresis", "Agrave", "Aring",
        "Atilde", "Ccedilla", "Eacute", "Ecircumflex", "Edieresis", "Egrave", "Iacute", "Icircumflex",
        "Idieresis", "Igrave", "Ntilde", "Oacute", "Ocircumflex", "Odieresis", "Ograve", "Otilde",
        "Scaron", "Uacute", "Ucircumflex", "Udieresis", "Ugrave", "Yacute", "Ydieresis", "Zcaron",
        "aacute", "acircumflex", "adieresis", "agrave", "aring", "atilde", "ccedilla", "eacute",
        "ecircumflex", "edieresis", "egrave", "iacute", "icircumflex", "idieresis", "igrave", "ntilde",
        "oacute", "ocircumflex", "odieresis", "ograve", "otilde", "scaron", "uacute", "ucircumflex",
        "udieresis", "ugrave", "yacute", "ydieresis", "zcaron", "exclamsmall", "Hungarumlautsmall", "dollaroldstyle",
        "dollarsuperior", "ampersandsmall", "Acutesmall", "parenleftsuperior", "parenrightsuperior", "twodotenleader", "onedotenleader", "zerooldstyle",
        "oneoldstyle", "twooldstyle", "threeoldstyle", "fouroldstyle", "fiveoldstyle", "sixoldstyle", "sevenoldstyle", "eightoldstyle",
        "nineoldstyle", "commasuperior", "threequartersemdash", "periodsuperior", "questionsmall", "asuperior", "bsuperior", "centsuperior",
        "dsuperior", "esuperior", "isuperior", "lsuperior", "msuperior", "nsuperior", "osuperior", "rsuperior",
        "ssuperior", "tsuperior", "ff", "ffi", "ffl", "parenleftinferior", "parenrightinferior", "Circumflexsmall",
        "hyphensuperior", "Gravesmall", "Asmall", "Bsmall", "Csmall", "Dsmall", "Esmall", "Fsmall",
        "Gsmall", "Hsmall", "Ismall", "Jsmall", "Ksmall", "Lsmall", "Msmall", "Nsmall",
        "Osmall", "Psmall", "Qsmall", "Rsmall", "Ssmall", "Tsmall", "Usmall", "Vsmall",
        "Wsmall", "Xsmall", "Ysmall", "Zsmall", "colonmonetary", "onefitted", "rupiah", "Tildesmall",
        "exclamdownsmall", "centoldstyle", "Lslashsmall", "Scaronsmall", "Zcaronsmall", "Dieresissmall", "Brevesmall", "Caronsmall",
        "Dotaccentsmall", "Macronsmall", "figuredash", "hypheninferior", "Ogoneksmall", "Ringsmall", "Cedillasmall", "questiondownsmall",
        "oneeighth", "threeeighths", "fiveeighths", "seveneighths", "onethird", "twothirds", "zerosuperior", "foursuperior",
        "fivesuperior", "sixsuperior", "sevensuperior", "eightsuperior", "ninesuperior", "zeroinferior", "oneinferior", "twoinferior",
        "threeinferior", "fourinferior", "fiveinferior", "sixinferior", "seveninferior", "eightinferior", "nineinferior", "centinferior",
        "dollarinferior", "periodinferior", "commainferior", "Agravesmall", "Aacutesmall", "Acircumflexsmall", "Atildesmall", "Adieresissmall",
        "Aringsmall", "AEsmall", "Ccedillasmall", "Egravesmall", "Eacutesmall", "Ecircumflexsmall", "Edieresissmall", "Igravesmall",
        "Iacutesmall", "Icircumflexsmall", "Idieresissmall", "Ethsmall", "Ntildesmall", "Ogravesmall", "Oacutesmall", "Ocircumflexsmall",
        "Otildesmall", "Odieresissmall", "OEsmall", "Oslashsmall", "Ugravesmall", "Uacutesmall", "Ucircumflexsmall", "Udieresissmall",
        "Yacutesmall", "Thornsmall", "Ydieresissmall", "001.000", "001.001", "001.002", "001.003", "Black",
        "Bold", "Book", "Light", "Medium", "Regular", "Roman", "Semibold",
    ];

    /// <summary>The name for a standard identifier, or null where it names none.</summary>
    public static string? Of(int sid) => sid >= 0 && sid < Names.Length ? Names[sid] : null;

    /// <summary>The table's own length, so a test can hold it to the count the format states.</summary>
    public static int Length => Names.Length;
}
