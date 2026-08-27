namespace Broiler.Documents.Rtf;

/// <summary>
/// Decodes single RTF bytes (<c>\'hh</c> and literal high bytes) to characters.
/// Windows-1252 (the RTF default) is fully supported without any encoding-provider
/// dependency; other single-byte code pages fall back to Latin-1 for the
/// 0x80-0xFF range (the reader reports this once as a diagnostic). Non-Latin text
/// in real-world RTF is carried by <c>\uN</c> Unicode escapes, which decode exactly.
/// </summary>
internal static class RtfCodePage
{
    // Windows-1252 code points for 0x80-0x9F (where it differs from Latin-1).
    // The five positions undefined in CP1252 pass the byte value through unchanged.
    private static readonly char[] Windows1252High =
    [
        (char)0x20AC, (char)0x0081, (char)0x201A, (char)0x0192,
        (char)0x201E, (char)0x2026, (char)0x2020, (char)0x2021,
        (char)0x02C6, (char)0x2030, (char)0x0160, (char)0x2039,
        (char)0x0152, (char)0x008D, (char)0x017D, (char)0x008F,
        (char)0x0090, (char)0x2018, (char)0x2019, (char)0x201C,
        (char)0x201D, (char)0x2022, (char)0x2013, (char)0x2014,
        (char)0x02DC, (char)0x2122, (char)0x0161, (char)0x203A,
        (char)0x0153, (char)0x009D, (char)0x017E, (char)0x0178,
    ];

    /// <summary>True when <paramref name="codePage"/> decodes byte-for-byte without loss here.</summary>
    public static bool IsFullySupported(int codePage) => codePage is 0 or 1 or 1252;

    public static char DecodeByte(byte value, int codePage)
    {
        if (value < 0x80)
            return (char)value;

        if (IsFullySupported(codePage) && value <= 0x9F)
            return Windows1252High[value - 0x80];

        // 0xA0-0xFF under CP1252 equals Latin-1; other code pages approximate via Latin-1.
        return (char)value;
    }

    /// <summary>Maps an RTF <c>\fcharsetN</c> value to a Windows code page (best-effort).</summary>
    public static int CharsetToCodePage(int charset) => charset switch
    {
        0 or 1 => 1252,
        77 => 10000,
        128 => 932,
        129 => 949,
        130 => 1361,
        134 => 936,
        136 => 950,
        161 => 1253,
        162 => 1254,
        163 => 1258,
        177 => 1255,
        178 => 1256,
        186 => 1257,
        204 => 1251,
        222 => 874,
        238 => 1250,
        255 => 437,
        _ => 1252,
    };
}
