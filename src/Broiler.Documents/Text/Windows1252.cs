using System;
using System.Text;

namespace Broiler.Documents.Text;

/// <summary>
/// Decodes and encodes Windows-1252 bytes without an encoding provider.
/// </summary>
/// <remarks>
/// <para>
/// Windows-1252 is Latin-1 except for 0x80-0x9F, where it puts the euro sign,
/// curly quotes, dashes and a handful of letters that Latin-1 leaves as C1
/// control codes. Those 32 positions are the whole table. The five positions
/// the code page leaves undefined pass the byte value through unchanged.
/// </para>
/// <para>
/// .NET carries the code page only behind <c>CodePagesEncodingProvider</c>,
/// and registering a provider is process-wide state, which this component does
/// not create (ADR 0003). The table is shared rather than kept per codec
/// because two codecs need it: RTF, whose default code page it is, and CSV,
/// which falls back to it for files that are not UTF-8.
/// </para>
/// </remarks>
internal static class Windows1252
{
    // Windows-1252 code points for 0x80-0x9F (where it differs from Latin-1).
    private static readonly char[] High =
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

    /// <summary>An <see cref="Encoding"/> instance representing Windows-1252.</summary>
    public static readonly Encoding Instance = new Windows1252Encoding();

    /// <summary>The character one Windows-1252 byte stands for.</summary>
    public static char Decode(byte value) =>
        value is >= 0x80 and <= 0x9F ? High[value - 0x80] : (char)value;

    /// <summary>The Windows-1252 byte for a character, or '?' if unrepresentable.</summary>
    public static byte Encode(char c)
    {
        if (c <= 0x7F)
            return (byte)c;

        int index = Array.IndexOf(High, c);
        if (index >= 0)
            return (byte)(0x80 + index);

        if (c <= 0xFF)
            return (byte)c;

        return (byte)'?';
    }

    /// <summary>A run of Windows-1252 bytes as text.</summary>
    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return string.Empty;

        return string.Create(bytes.Length, bytes, static (chars, source) =>
        {
            for (int i = 0; i < source.Length; i++)
                chars[i] = Decode(source[i]);
        });
    }

    private sealed class Windows1252Encoding : Encoding
    {
        public override string WebName => "windows-1252";
        public override string EncodingName => "Western European (Windows)";
        public override int CodePage => 1252;

        public override int GetByteCount(char[] chars, int index, int count) => count;

        public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
        {
            ArgumentNullException.ThrowIfNull(chars);
            ArgumentNullException.ThrowIfNull(bytes);
            for (int i = 0; i < charCount; i++)
                bytes[byteIndex + i] = Encode(chars[charIndex + i]);
            return charCount;
        }

        public override int GetCharCount(byte[] bytes, int index, int count) => count;

        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentNullException.ThrowIfNull(chars);
            for (int i = 0; i < byteCount; i++)
                chars[charIndex + i] = Decode(bytes[byteIndex + i]);
            return byteCount;
        }

        public override int GetMaxByteCount(int charCount) => charCount;
        public override int GetMaxCharCount(int byteCount) => byteCount;
    }
}
