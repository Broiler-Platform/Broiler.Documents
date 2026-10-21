using System;
using System.Collections.Concurrent;
using System.Text;
using Broiler.Documents.Text;

namespace Broiler.Documents.Rtf;

/// <summary>
/// Decodes single RTF bytes (<c>\'hh</c> and literal high bytes) to characters.
/// Windows-1252 (the RTF default) is decoded from the shared table. Any other
/// single-byte code page is decoded from the runtime's code-page data, asked for
/// through <see cref="CodePagesEncodingProvider.Instance"/> directly: registering
/// the provider would be process-wide state, which this component does not create
/// (ADR 0003). A double-byte code page cannot be decoded a byte at a time, and
/// falls back to Latin-1 for 0x80-0xFF (the reader reports this once as a
/// diagnostic). <c>\uN</c> Unicode escapes decode exactly whatever the code page.
/// </summary>
internal static class RtfCodePage
{
    // Each single-byte code page's 256 characters, or null for one the runtime
    // does not carry or that is not single-byte. Built once per code page.
    private static readonly ConcurrentDictionary<int, char[]?> Tables = new();

    /// <summary>True when <paramref name="codePage"/> decodes byte-for-byte without loss here.</summary>
    public static bool IsFullySupported(int codePage) =>
        codePage is 0 or 1 or 1252 || TableFor(codePage) is not null;

    public static char DecodeByte(byte value, int codePage)
    {
        if (codePage is 0 or 1 or 1252)
            return Windows1252.Decode(value);

        if (value >= 0x80 && TableFor(codePage) is { } table)
            return table[value];

        // Other code pages approximate via Latin-1, which matches them below 0x80.
        return (char)value;
    }

    private static char[]? TableFor(int codePage) =>
        codePage <= 0 ? null : Tables.GetOrAdd(codePage, BuildTable);

    private static char[]? BuildTable(int codePage)
    {
        Encoding? encoding = CodePagesEncodingProvider.Instance.GetEncoding(codePage);
        if (encoding is null || !encoding.IsSingleByte)
            return null;

        Span<byte> bytes = stackalloc byte[256];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)i;

        var table = new char[256];
        return encoding.GetChars(bytes, table) == table.Length ? table : null;
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
