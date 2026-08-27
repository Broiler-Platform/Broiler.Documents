using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Broiler.Documents.Pdf.Syntax;

internal enum PdfTokenType
{
    EndOfData,
    Integer,
    Real,
    Name,
    LiteralString,
    HexString,
    ArrayStart,
    ArrayEnd,
    DictionaryStart,
    DictionaryEnd,

    /// <summary>A bare keyword: <c>obj</c>, <c>R</c>, <c>true</c>, or a content operator.</summary>
    Keyword,
}

/// <summary>
/// One lexical token. Values are materialized eagerly (numbers parsed, escapes
/// resolved) because every token in a bounded input is small by construction —
/// <see cref="PdfLimits.MaxTokenLength"/> rejects anything else before it is
/// allocated.
/// </summary>
internal readonly struct PdfToken
{
    public PdfToken(PdfTokenType type, int start, int length)
    {
        Type = type;
        Start = start;
        Length = length;
        Number = 0;
        Text = string.Empty;
        Bytes = null;
    }

    public PdfToken(PdfTokenType type, int start, int length, double number)
        : this(type, start, length)
    {
        Number = number;
    }

    public PdfToken(PdfTokenType type, int start, int length, string text)
        : this(type, start, length)
    {
        Text = text;
    }

    public PdfToken(PdfTokenType type, int start, int length, byte[] bytes)
        : this(type, start, length)
    {
        Bytes = bytes;
    }

    public PdfTokenType Type { get; }

    /// <summary>Offset of the token's first byte in the source buffer.</summary>
    public int Start { get; }

    public int Length { get; }

    /// <summary>The numeric value for <see cref="PdfTokenType.Integer"/>/<see cref="PdfTokenType.Real"/>.</summary>
    public double Number { get; }

    /// <summary>The decoded text for a name or keyword.</summary>
    public string Text { get; }

    /// <summary>The decoded bytes for a literal or hexadecimal string.</summary>
    public byte[]? Bytes { get; }

    public bool IsKeyword(string keyword) =>
        Type == PdfTokenType.Keyword && string.Equals(Text, keyword, StringComparison.Ordinal);
}

/// <summary>
/// A bounded tokenizer for PDF syntax (ISO 32000-1 clause 7.2). The same lexer
/// serves file bodies and content streams: content operators arrive as
/// <see cref="PdfTokenType.Keyword"/> tokens.
/// </summary>
/// <remarks>
/// Every loop that consumes input is bounded by the buffer length and by
/// <see cref="PdfLimits.MaxTokenLength"/>. There is no unbounded scan for a
/// terminator anywhere, which is what keeps a truncated or hostile file from
/// turning into a hang.
/// </remarks>
internal sealed class PdfLexer
{
    private readonly byte[] _data;
    private readonly int _end;
    private readonly PdfLimits _limits;

    public PdfLexer(byte[] data, PdfLimits limits, int start = 0, int? end = null)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        _end = Math.Min(end ?? data.Length, data.Length);
        Position = Math.Clamp(start, 0, _end);
    }

    public int Position { get; set; }

    public int End => _end;

    public byte[] Data => _data;

    public static bool IsWhitespace(byte b) => b is 0 or 9 or 10 or 12 or 13 or 32;

    public static bool IsDelimiter(byte b) =>
        b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']'
            or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

    public static bool IsRegular(byte b) => !IsWhitespace(b) && !IsDelimiter(b);

    /// <summary>Skips whitespace and <c>%</c> comments up to the next token.</summary>
    public void SkipWhitespace()
    {
        while (Position < _end)
        {
            byte b = _data[Position];
            if (IsWhitespace(b))
            {
                Position++;
                continue;
            }

            if (b == (byte)'%')
            {
                while (Position < _end && _data[Position] is not 10 and not 13)
                    Position++;
                continue;
            }

            return;
        }
    }

    /// <summary>Reads the next token, or an <see cref="PdfTokenType.EndOfData"/> token at the end.</summary>
    public PdfToken ReadToken()
    {
        SkipWhitespace();
        if (Position >= _end)
            return new PdfToken(PdfTokenType.EndOfData, Position, 0);

        int start = Position;
        byte b = _data[Position];

        switch (b)
        {
            case (byte)'[':
                Position++;
                return new PdfToken(PdfTokenType.ArrayStart, start, 1);
            case (byte)']':
                Position++;
                return new PdfToken(PdfTokenType.ArrayEnd, start, 1);
            case (byte)'/':
                return ReadName();
            case (byte)'(':
                return ReadLiteralString();
            case (byte)'<':
                if (Position + 1 < _end && _data[Position + 1] == (byte)'<')
                {
                    Position += 2;
                    return new PdfToken(PdfTokenType.DictionaryStart, start, 2);
                }

                return ReadHexString();
            case (byte)'>':
                if (Position + 1 < _end && _data[Position + 1] == (byte)'>')
                {
                    Position += 2;
                    return new PdfToken(PdfTokenType.DictionaryEnd, start, 2);
                }

                // A lone '>' is malformed; consume it so the parser advances.
                Position++;
                return new PdfToken(PdfTokenType.Keyword, start, 1, ">");
            case (byte)'{':
            case (byte)'}':
            case (byte)')':
                // PostScript-calculator braces and stray parentheses are not part of
                // the supported subset; surface them as keywords for the caller to skip.
                Position++;
                return new PdfToken(PdfTokenType.Keyword, start, 1, ((char)b).ToString());
        }

        if (b is (byte)'+' or (byte)'-' or (byte)'.' || (b >= (byte)'0' && b <= (byte)'9'))
            return ReadNumber();

        return ReadKeyword();
    }

    /// <summary>Reads a token and puts the lexer back where it was.</summary>
    public PdfToken PeekToken()
    {
        int saved = Position;
        PdfToken token = ReadToken();
        Position = saved;
        return token;
    }

    private PdfToken ReadName()
    {
        int start = Position;
        Position++; // the leading solidus

        var builder = new StringBuilder();
        while (Position < _end && IsRegular(_data[Position]))
        {
            if (builder.Length >= _limits.MaxTokenLength)
                throw PdfWorkBudget.Exceeded(nameof(PdfLimits.MaxTokenLength), _limits.MaxTokenLength);

            byte b = _data[Position];
            if (b == (byte)'#' && Position + 2 < _end &&
                TryHexDigit(_data[Position + 1], out int high) &&
                TryHexDigit(_data[Position + 2], out int low))
            {
                builder.Append((char)((high << 4) | low));
                Position += 3;
                continue;
            }

            builder.Append((char)b);
            Position++;
        }

        return new PdfToken(PdfTokenType.Name, start, Position - start, builder.ToString());
    }

    private PdfToken ReadNumber()
    {
        int start = Position;
        bool real = false;

        while (Position < _end && IsRegular(_data[Position]))
        {
            byte b = _data[Position];
            if (b == (byte)'.')
                real = true;
            else if (b is not (byte)'+' and not (byte)'-' && (b < (byte)'0' || b > (byte)'9'))
            {
                // A regular character that is not numeric: PDF producers emit forms
                // like "0000000000n"; stop the number here and let the next token
                // pick up the remainder rather than failing the whole object.
                break;
            }

            Position++;
            if (Position - start > _limits.MaxTokenLength)
                throw PdfWorkBudget.Exceeded(nameof(PdfLimits.MaxTokenLength), _limits.MaxTokenLength);
        }

        string text = Latin1(_data, start, Position - start);
        if (!real && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long integer))
            return new PdfToken(PdfTokenType.Integer, start, Position - start, integer);

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) &&
            !double.IsNaN(value) && !double.IsInfinity(value))
            return new PdfToken(PdfTokenType.Real, start, Position - start, value);

        // Forms like "--5" or "4." that some producers emit: recover the leading
        // numeric prefix rather than rejecting the object.
        return new PdfToken(PdfTokenType.Real, start, Position - start, RecoverNumber(text));
    }

    private static double RecoverNumber(string text)
    {
        var builder = new StringBuilder(text.Length);
        bool seenDigitOrDot = false;
        foreach (char c in text)
        {
            if (c is '+' or '-')
            {
                if (seenDigitOrDot)
                    break;
                if (builder.Length == 0 && c == '-')
                    builder.Append(c);
                continue;
            }

            if (c is '.' or >= '0' and <= '9')
            {
                seenDigitOrDot = true;
                builder.Append(c);
                continue;
            }

            break;
        }

        return double.TryParse(builder.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) &&
               !double.IsNaN(value) && !double.IsInfinity(value)
            ? value
            : 0d;
    }

    private PdfToken ReadLiteralString()
    {
        int start = Position;
        Position++; // '('

        var bytes = new List<byte>();
        int depth = 1;

        while (Position < _end)
        {
            if (bytes.Count > _limits.MaxTokenLength)
                throw PdfWorkBudget.Exceeded(nameof(PdfLimits.MaxTokenLength), _limits.MaxTokenLength);

            byte b = _data[Position++];
            switch (b)
            {
                case (byte)'(':
                    depth++;
                    bytes.Add(b);
                    continue;
                case (byte)')':
                    if (--depth == 0)
                        return new PdfToken(PdfTokenType.LiteralString, start, Position - start, bytes.ToArray());
                    bytes.Add(b);
                    continue;
                case (byte)'\\':
                    AppendEscape(bytes);
                    continue;
                case 13:
                    // CR and CRLF in a literal string both mean a single LF.
                    if (Position < _end && _data[Position] == 10)
                        Position++;
                    bytes.Add(10);
                    continue;
                default:
                    bytes.Add(b);
                    continue;
            }
        }

        // Unterminated at end of input: return what was read so the caller can
        // record a malformed-object diagnostic instead of looping.
        return new PdfToken(PdfTokenType.LiteralString, start, Position - start, bytes.ToArray());
    }

    private void AppendEscape(List<byte> bytes)
    {
        if (Position >= _end)
            return;

        byte b = _data[Position++];
        switch (b)
        {
            case (byte)'n': bytes.Add(10); return;
            case (byte)'r': bytes.Add(13); return;
            case (byte)'t': bytes.Add(9); return;
            case (byte)'b': bytes.Add(8); return;
            case (byte)'f': bytes.Add(12); return;
            case (byte)'(': bytes.Add((byte)'('); return;
            case (byte)')': bytes.Add((byte)')'); return;
            case (byte)'\\': bytes.Add((byte)'\\'); return;
            case 13:
                // Line continuation: backslash before EOL emits nothing.
                if (Position < _end && _data[Position] == 10)
                    Position++;
                return;
            case 10:
                return;
        }

        if (b >= (byte)'0' && b <= (byte)'7')
        {
            int value = b - (byte)'0';
            for (int i = 0; i < 2 && Position < _end; i++)
            {
                byte next = _data[Position];
                if (next < (byte)'0' || next > (byte)'7')
                    break;
                value = (value << 3) | (next - (byte)'0');
                Position++;
            }

            bytes.Add((byte)(value & 0xFF));
            return;
        }

        // Any other escaped character stands for itself.
        bytes.Add(b);
    }

    private PdfToken ReadHexString()
    {
        int start = Position;
        Position++; // '<'

        var bytes = new List<byte>();
        int pending = -1;

        while (Position < _end)
        {
            if (bytes.Count > _limits.MaxTokenLength)
                throw PdfWorkBudget.Exceeded(nameof(PdfLimits.MaxTokenLength), _limits.MaxTokenLength);

            byte b = _data[Position++];
            if (b == (byte)'>')
                break;
            if (!TryHexDigit(b, out int digit))
                continue; // whitespace and stray characters are ignored inside <>

            if (pending < 0)
            {
                pending = digit;
                continue;
            }

            bytes.Add((byte)((pending << 4) | digit));
            pending = -1;
        }

        // An odd final digit is padded with a trailing zero (clause 7.3.4.3).
        if (pending >= 0)
            bytes.Add((byte)(pending << 4));

        return new PdfToken(PdfTokenType.HexString, start, Position - start, bytes.ToArray());
    }

    private PdfToken ReadKeyword()
    {
        int start = Position;
        while (Position < _end && IsRegular(_data[Position]))
        {
            Position++;
            if (Position - start > _limits.MaxTokenLength)
                throw PdfWorkBudget.Exceeded(nameof(PdfLimits.MaxTokenLength), _limits.MaxTokenLength);
        }

        if (Position == start)
        {
            // A delimiter we do not otherwise handle; consume one byte to advance.
            Position++;
            return new PdfToken(PdfTokenType.Keyword, start, 1, Latin1(_data, start, 1));
        }

        return new PdfToken(PdfTokenType.Keyword, start, Position - start, Latin1(_data, start, Position - start));
    }

    internal static bool TryHexDigit(byte b, out int value)
    {
        switch (b)
        {
            case >= (byte)'0' and <= (byte)'9':
                value = b - (byte)'0';
                return true;
            case >= (byte)'a' and <= (byte)'f':
                value = b - (byte)'a' + 10;
                return true;
            case >= (byte)'A' and <= (byte)'F':
                value = b - (byte)'A' + 10;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    /// <summary>
    /// Reinterprets bytes as Latin-1 characters. PDF keywords and numbers are
    /// ASCII by construction; this avoids pulling an encoding provider into the
    /// hot path and never fails on a stray high byte.
    /// </summary>
    internal static string Latin1(byte[] data, int start, int length)
    {
        if (length <= 0)
            return string.Empty;

        var builder = new StringBuilder(length);
        int end = Math.Min(start + length, data.Length);
        for (int i = start; i < end; i++)
            builder.Append((char)data[i]);
        return builder.ToString();
    }
}
