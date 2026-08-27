namespace Broiler.Documents.Rtf;

/// <summary>The kind of a single <see cref="RtfToken"/>.</summary>
public enum RtfTokenType
{
    /// <summary><c>{</c> — start of a group.</summary>
    GroupStart,

    /// <summary><c>}</c> — end of a group.</summary>
    GroupEnd,

    /// <summary><c>\keyword</c> with an optional signed numeric parameter.</summary>
    ControlWord,

    /// <summary><c>\x</c> where x is a single non-letter (for example <c>\*</c>, <c>\\</c>, <c>\{</c>).</summary>
    ControlSymbol,

    /// <summary><c>\'hh</c> — one hex-encoded byte, its value in <see cref="RtfToken.Parameter"/>.</summary>
    HexByte,

    /// <summary>A run of literal characters (line breaks in the RTF stream are dropped).</summary>
    Text,
}
