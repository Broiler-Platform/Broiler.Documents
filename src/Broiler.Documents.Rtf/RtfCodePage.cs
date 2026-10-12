using Broiler.Documents.Text;

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
    /// <summary>True when <paramref name="codePage"/> decodes byte-for-byte without loss here.</summary>
    public static bool IsFullySupported(int codePage) => codePage is 0 or 1 or 1252;

    public static char DecodeByte(byte value, int codePage)
    {
        if (IsFullySupported(codePage))
            return Windows1252.Decode(value);

        // Other code pages approximate via Latin-1, which matches them below 0x80.
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
