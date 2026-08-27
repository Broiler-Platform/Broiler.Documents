using System;
using System.Collections.Generic;
using System.Text;

namespace Broiler.Documents.Rtf;

/// <summary>
/// Splits raw RTF bytes into <see cref="RtfToken"/>s: groups, control words (with
/// optional signed parameters and the single trailing-space delimiter), control
/// symbols, <c>\'hh</c> hex bytes, and literal text runs. No semantics are applied
/// — control words are not interpreted and text bytes are preserved verbatim
/// (each byte becomes a char via Latin-1) for the Phase 2 reader to decode against
/// the active code page.
///
/// The tokenizer is total and bounded: it always makes forward progress, never
/// throws, and honors <see cref="DocumentLimits"/> (oversized input and
/// over-deep nesting stop tokenization and record a diagnostic; ADR 0004).
/// </summary>
public static class RtfTokenizer
{
    public static RtfTokenizeResult Tokenize(ReadOnlyMemory<byte> content, DocumentLimits? limits = null)
    {
        DocumentLimits effective = limits ?? DocumentLimits.Default;
        ReadOnlySpan<byte> span = content.Span;

        var diagnostics = new List<DocumentDiagnostic>();
        bool truncated = false;

        int length = span.Length;
        if (length > effective.MaxDocumentBytes)
        {
            length = (int)Math.Min(effective.MaxDocumentBytes, int.MaxValue);
            truncated = true;
            diagnostics.Add(DocumentDiagnostic.Warning(
                "rtf.size", "Input exceeded MaxDocumentBytes and was truncated."));
        }

        var tokens = new List<RtfToken>();
        var textBytes = new List<byte>();
        int depth = 0;
        int i = 0;

        void FlushText()
        {
            if (textBytes.Count == 0)
                return;
            tokens.Add(RtfToken.TextRun(Encoding.Latin1.GetString(textBytes.ToArray())));
            textBytes.Clear();
        }

        while (i < length)
        {
            byte b = span[i];
            switch (b)
            {
                case (byte)'{':
                    FlushText();
                    depth++;
                    if (depth > effective.MaxGroupDepth)
                    {
                        truncated = true;
                        diagnostics.Add(DocumentDiagnostic.Warning(
                            "rtf.depth", "Group nesting exceeded MaxGroupDepth; tokenization stopped."));
                        i = length;
                        break;
                    }

                    tokens.Add(RtfToken.GroupStart);
                    i++;
                    break;

                case (byte)'}':
                    FlushText();
                    if (depth > 0)
                        depth--;
                    tokens.Add(RtfToken.GroupEnd);
                    i++;
                    break;

                case (byte)'\\':
                    FlushText();
                    i = ReadEscape(span, length, i, effective.MaxBinBytes, tokens, diagnostics);
                    break;

                case (byte)'\r':
                case (byte)'\n':
                    // Line breaks in the RTF stream are insignificant; drop them.
                    i++;
                    break;

                default:
                    textBytes.Add(b);
                    if (textBytes.Count >= effective.MaxRunLength)
                        FlushText();
                    i++;
                    break;
            }
        }

        FlushText();
        return new RtfTokenizeResult(tokens, truncated, diagnostics);
    }

    /// <summary>Reads one escape sequence starting at the backslash at <paramref name="at"/>; returns the next index.</summary>
    private static int ReadEscape(
        ReadOnlySpan<byte> span,
        int length,
        int at,
        long maxBinBytes,
        List<RtfToken> tokens,
        List<DocumentDiagnostic> diagnostics)
    {
        int i = at + 1;
        if (i >= length)
        {
            // Trailing backslash with nothing after it: treat as a literal control symbol.
            tokens.Add(RtfToken.ControlSymbol('\\'));
            return i;
        }

        byte c = span[i];

        if (IsAsciiLetter(c))
        {
            int start = i;
            while (i < length && IsAsciiLetter(span[i]))
                i++;
            string keyword = Encoding.Latin1.GetString(span.Slice(start, i - start));

            bool hasParameter = false;
            int parameter = 0;
            int sign = 1;
            if (i < length && span[i] == (byte)'-' && i + 1 < length && IsDigit(span[i + 1]))
            {
                sign = -1;
                i++;
            }

            if (i < length && IsDigit(span[i]))
            {
                hasParameter = true;
                long value = 0;
                while (i < length && IsDigit(span[i]))
                {
                    value = value * 10 + (span[i] - (byte)'0');
                    if (value > int.MaxValue)
                        value = int.MaxValue;
                    i++;
                }

                parameter = (int)(sign * value);
            }

            // A single space after a control word is the delimiter and is consumed.
            if (i < length && span[i] == (byte)' ')
                i++;

            tokens.Add(RtfToken.ControlWord(keyword, hasParameter, parameter));

            // \binN: the next N bytes are raw binary, not RTF — skip them so binary
            // content cannot corrupt the token stream (bounded by the remaining input).
            if (hasParameter && parameter > 0 && keyword == "bin")
            {
                int skip = (int)Math.Min((long)parameter, length - i);
                if (parameter > maxBinBytes)
                    diagnostics.Add(DocumentDiagnostic.Warning(
                        "rtf.bin", "A \\bin length exceeded MaxBinBytes; the binary data was skipped."));
                i += skip;
            }

            return i;
        }

        if (c == (byte)'\'')
        {
            i++;
            int hi = i < length ? HexValue(span[i]) : -1;
            if (hi >= 0)
                i++;
            int lo = i < length ? HexValue(span[i]) : -1;
            if (lo >= 0)
                i++;

            if (hi >= 0 && lo >= 0)
            {
                tokens.Add(RtfToken.HexByte((hi << 4) | lo));
            }
            else
            {
                diagnostics.Add(DocumentDiagnostic.Warning(
                    "rtf.hex", "Malformed \\'hh hex escape was skipped."));
            }

            return i;
        }

        // Any other single character is a control symbol (\\, \{, \}, \*, \~, \-, …).
        tokens.Add(RtfToken.ControlSymbol((char)c));
        return i + 1;
    }

    private static bool IsAsciiLetter(byte b) =>
        (b >= (byte)'a' && b <= (byte)'z') || (b >= (byte)'A' && b <= (byte)'Z');

    private static bool IsDigit(byte b) => b >= (byte)'0' && b <= (byte)'9';

    private static int HexValue(byte b) => b switch
    {
        >= (byte)'0' and <= (byte)'9' => b - (byte)'0',
        >= (byte)'a' and <= (byte)'f' => b - (byte)'a' + 10,
        >= (byte)'A' and <= (byte)'F' => b - (byte)'A' + 10,
        _ => -1,
    };
}
