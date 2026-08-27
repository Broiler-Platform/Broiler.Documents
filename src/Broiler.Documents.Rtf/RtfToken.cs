namespace Broiler.Documents.Rtf;

/// <summary>
/// A single lexical token from the RTF stream. This is a pure syntax token — no
/// semantic meaning is attached here (that mapping is Phase 2/3). Build tokens
/// with the factory methods; the fields not relevant to a token's
/// <see cref="Type"/> carry neutral defaults.
/// </summary>
public readonly struct RtfToken
{
    private RtfToken(RtfTokenType type, string keyword, bool hasParameter, int parameter, char symbol, string text)
    {
        Type = type;
        Keyword = keyword;
        HasParameter = hasParameter;
        Parameter = parameter;
        Symbol = symbol;
        Text = text;
    }

    public RtfTokenType Type { get; }

    /// <summary>Control-word keyword (letters only), or the empty string.</summary>
    public string Keyword { get; }

    /// <summary>True when a <see cref="RtfTokenType.ControlWord"/> carried a numeric parameter.</summary>
    public bool HasParameter { get; }

    /// <summary>Control-word parameter, or the byte value for <see cref="RtfTokenType.HexByte"/>.</summary>
    public int Parameter { get; }

    /// <summary>The symbol character for a <see cref="RtfTokenType.ControlSymbol"/>.</summary>
    public char Symbol { get; }

    /// <summary>The literal characters for a <see cref="RtfTokenType.Text"/> token, or the empty string.</summary>
    public string Text { get; }

    public static RtfToken GroupStart { get; } =
        new(RtfTokenType.GroupStart, string.Empty, false, 0, '\0', string.Empty);

    public static RtfToken GroupEnd { get; } =
        new(RtfTokenType.GroupEnd, string.Empty, false, 0, '\0', string.Empty);

    public static RtfToken ControlWord(string keyword, bool hasParameter, int parameter) =>
        new(RtfTokenType.ControlWord, keyword, hasParameter, parameter, '\0', string.Empty);

    public static RtfToken ControlSymbol(char symbol) =>
        new(RtfTokenType.ControlSymbol, string.Empty, false, 0, symbol, string.Empty);

    public static RtfToken HexByte(int value) =>
        new(RtfTokenType.HexByte, string.Empty, true, value, '\0', string.Empty);

    public static RtfToken TextRun(string text) =>
        new(RtfTokenType.Text, string.Empty, false, 0, '\0', text);

    public override string ToString() => Type switch
    {
        RtfTokenType.GroupStart => "{",
        RtfTokenType.GroupEnd => "}",
        RtfTokenType.ControlWord => HasParameter ? $"\\{Keyword}{Parameter}" : $"\\{Keyword}",
        RtfTokenType.ControlSymbol => $"\\{Symbol}",
        RtfTokenType.HexByte => $"\\'{Parameter:x2}",
        RtfTokenType.Text => Text,
        _ => base.ToString() ?? string.Empty,
    };
}
