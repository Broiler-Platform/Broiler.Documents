using System;
using System.Collections.Generic;
using System.Text;
using Broiler.Documents.Pdf.Syntax;

namespace Broiler.Documents.Pdf.Text;

/// <summary>
/// A character-code map: the codespace ranges that say how many bytes a code
/// occupies, and the code-to-text mappings a <c>ToUnicode</c> CMap provides.
/// </summary>
/// <remarks>
/// <para>
/// <c>ToUnicode</c> is the primary and most trustworthy route from a PDF's bytes
/// to text, so it is parsed with its own bounded reader rather than being
/// inferred from an encoding. Only the operators that carry mappings are
/// honoured; the rest of the PostScript-flavoured CMap syntax is skipped.
/// </para>
/// <para>
/// Every mapping is charged against the document's CMap-entry budget, so a file
/// declaring millions of <c>bfrange</c> entries is rejected rather than absorbed.
/// </para>
/// </remarks>
internal sealed class PdfCMap
{
    private readonly List<CodespaceRange> _codespaces = [];
    private readonly Dictionary<uint, string> _singles = new();
    private readonly List<BfRange> _ranges = [];

    private PdfCMap()
    {
    }

    /// <summary>The two-byte identity map that <c>Identity-H</c> and <c>Identity-V</c> name.</summary>
    public static PdfCMap IdentityTwoByte { get; } = CreateIdentity();

    /// <summary>True when the map carries no code-to-text information.</summary>
    public bool IsEmpty => _singles.Count == 0 && _ranges.Count == 0;

    /// <summary>The number of bytes the code starting at <paramref name="offset"/> occupies.</summary>
    public int CodeLengthAt(byte[] data, int offset)
    {
        // Codespace ranges are matched shortest-first, which is the format's own
        // rule and also the only order that terminates on malformed overlaps.
        for (int length = 1; length <= 4 && offset + length <= data.Length; length++)
        {
            uint code = ReadCode(data, offset, length);
            foreach (CodespaceRange range in _codespaces)
            {
                if (range.ByteLength == length && code >= range.Low && code <= range.High)
                    return length;
            }
        }

        // No codespace matched. One byte is the format's default for a simple
        // font, and is the safe choice for a malformed map: it can never consume
        // bytes belonging to the next code.
        return _codespaces.Count == 0 ? 1 : Math.Min(_codespaces[0].ByteLength, Math.Max(1, data.Length - offset));
    }

    public bool TryMap(uint code, out string text)
    {
        if (_singles.TryGetValue(code, out string? mapped))
        {
            text = mapped;
            return true;
        }

        foreach (BfRange range in _ranges)
        {
            if (code < range.Low || code > range.High)
                continue;

            if (range.Destinations is { } destinations)
            {
                int index = (int)(code - range.Low);
                if (index >= 0 && index < destinations.Length)
                {
                    text = destinations[index];
                    return true;
                }

                continue;
            }

            text = Offset(range.BaseText, code - range.Low);
            return true;
        }

        text = string.Empty;
        return false;
    }

    /// <summary>
    /// Advances the last UTF-16 unit of a <c>bfrange</c> base value, which is how
    /// the format expresses a run of consecutive destinations.
    /// </summary>
    private static string Offset(string baseText, uint delta)
    {
        if (baseText.Length == 0 || delta == 0)
            return baseText;

        int last = baseText[^1] + (int)delta;
        if (last > char.MaxValue)
            return baseText;

        return baseText.Length == 1
            ? ((char)last).ToString()
            : string.Concat(baseText.AsSpan(0, baseText.Length - 1), ((char)last).ToString());
    }

    private static uint ReadCode(byte[] data, int offset, int length)
    {
        uint code = 0;
        for (int i = 0; i < length && offset + i < data.Length; i++)
            code = (code << 8) | data[offset + i];
        return code;
    }

    private static PdfCMap CreateIdentity()
    {
        var map = new PdfCMap();
        map._codespaces.Add(new CodespaceRange(2, 0, 0xFFFF));
        return map;
    }

    /// <summary>
    /// Parses a CMap program. <paramref name="resolveUseCMap"/> loads a map named
    /// by <c>usecmap</c>; it is bounded by the caller so the chain cannot recurse.
    /// </summary>
    public static PdfCMap Parse(byte[] data, PdfWorkBudget budget, Func<string, PdfCMap?>? resolveUseCMap = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(budget);

        var map = new PdfCMap();
        var lexer = new PdfLexer(data, budget.Limits);
        var operands = new List<PdfToken>();

        while (true)
        {
            budget.ThrowIfCancelled();
            PdfToken token = lexer.ReadToken();
            if (token.Type == PdfTokenType.EndOfData)
                break;

            if (token.Type != PdfTokenType.Keyword)
            {
                // Operands accumulate until an operator consumes them. The cap keeps
                // a malformed file from growing the list without bound.
                if (operands.Count < 64)
                    operands.Add(token);
                continue;
            }

            switch (token.Text)
            {
                case "begincodespacerange":
                    ReadCodespaceRanges(map, lexer, budget);
                    break;
                case "beginbfchar":
                    ReadBfChars(map, lexer, budget);
                    break;
                case "beginbfrange":
                    ReadBfRanges(map, lexer, budget);
                    break;
                case "usecmap":
                    ApplyUseCMap(map, operands, resolveUseCMap);
                    break;
            }

            operands.Clear();
            budget.ChargeWork(2);
        }

        if (map._codespaces.Count == 0)
        {
            // A ToUnicode map for a simple font often omits its codespace; single
            // bytes are the format's default and the only safe assumption.
            map._codespaces.Add(new CodespaceRange(1, 0, 0xFF));
        }

        return map;
    }

    private static void ApplyUseCMap(PdfCMap map, List<PdfToken> operands, Func<string, PdfCMap?>? resolve)
    {
        if (resolve is null)
            return;

        for (int i = operands.Count - 1; i >= 0; i--)
        {
            if (operands[i].Type != PdfTokenType.Name)
                continue;

            PdfCMap? parent = resolve(operands[i].Text);
            if (parent is null)
                return;

            map._codespaces.AddRange(parent._codespaces);
            foreach (KeyValuePair<uint, string> entry in parent._singles)
                map._singles.TryAdd(entry.Key, entry.Value);
            map._ranges.AddRange(parent._ranges);
            return;
        }
    }

    private static void ReadCodespaceRanges(PdfCMap map, PdfLexer lexer, PdfWorkBudget budget)
    {
        while (true)
        {
            PdfToken low = lexer.ReadToken();
            if (low.Type != PdfTokenType.HexString)
                return; // endcodespacerange, or malformed input

            PdfToken high = lexer.ReadToken();
            if (high.Type != PdfTokenType.HexString)
                return;

            byte[] lowBytes = low.Bytes ?? [];
            byte[] highBytes = high.Bytes ?? [];
            int length = Math.Max(1, Math.Min(4, lowBytes.Length));
            map._codespaces.Add(new CodespaceRange(length, ToCode(lowBytes), ToCode(highBytes)));
            budget.ChargeCMapEntries(1);

            if (map._codespaces.Count > 64)
                return;
        }
    }

    private static void ReadBfChars(PdfCMap map, PdfLexer lexer, PdfWorkBudget budget)
    {
        while (true)
        {
            PdfToken source = lexer.ReadToken();
            if (source.Type != PdfTokenType.HexString)
                return;

            PdfToken destination = lexer.ReadToken();
            string? text = DestinationText(destination);
            if (text is null)
                return;

            map._singles[ToCode(source.Bytes ?? [])] = text;
            budget.ChargeCMapEntries(1);
        }
    }

    private static void ReadBfRanges(PdfCMap map, PdfLexer lexer, PdfWorkBudget budget)
    {
        while (true)
        {
            PdfToken low = lexer.ReadToken();
            if (low.Type != PdfTokenType.HexString)
                return;

            PdfToken high = lexer.ReadToken();
            if (high.Type != PdfTokenType.HexString)
                return;

            uint lowCode = ToCode(low.Bytes ?? []);
            uint highCode = ToCode(high.Bytes ?? []);
            if (highCode < lowCode)
                (lowCode, highCode) = (highCode, lowCode);

            long span = (long)highCode - lowCode + 1;
            if (span > budget.Limits.MaxCMapEntries)
                throw PdfWorkBudget.Exceeded(nameof(PdfLimits.MaxCMapEntries), budget.Limits.MaxCMapEntries);

            PdfToken destination = lexer.ReadToken();
            if (destination.Type == PdfTokenType.ArrayStart)
            {
                var destinations = new List<string>();
                while (true)
                {
                    PdfToken entry = lexer.ReadToken();
                    if (entry.Type is PdfTokenType.ArrayEnd or PdfTokenType.EndOfData)
                        break;

                    string? text = DestinationText(entry);
                    if (text is null)
                        break;
                    destinations.Add(text);
                    if (destinations.Count > span)
                        break;
                }

                map._ranges.Add(new BfRange(lowCode, highCode, string.Empty, destinations.ToArray()));
                budget.ChargeCMapEntries(destinations.Count);
                continue;
            }

            string? baseText = DestinationText(destination);
            if (baseText is null)
                return;

            map._ranges.Add(new BfRange(lowCode, highCode, baseText, null));

            // A range costs one entry against the budget per code it can produce:
            // the mapping is lazy, but the promise it makes is not.
            budget.ChargeCMapEntries((int)Math.Min(span, int.MaxValue));
        }
    }

    // A bfchar/bfrange destination is a UTF-16BE hex string, or a glyph name.
    private static string? DestinationText(PdfToken token) => token.Type switch
    {
        PdfTokenType.HexString => Utf16BigEndian(token.Bytes ?? []),
        PdfTokenType.Name => PdfEncodings.TryMapGlyphName(token.Text, out string mapped) ? mapped : string.Empty,
        PdfTokenType.Integer => ((char)(int)Math.Clamp(token.Number, 0, char.MaxValue)).ToString(),
        _ => null,
    };

    private static string Utf16BigEndian(byte[] bytes)
    {
        if (bytes.Length == 0)
            return string.Empty;
        if (bytes.Length == 1)
            return ((char)bytes[0]).ToString();

        var builder = new StringBuilder(bytes.Length / 2);
        for (int i = 0; i + 1 < bytes.Length; i += 2)
            builder.Append((char)((bytes[i] << 8) | bytes[i + 1]));
        return builder.ToString();
    }

    private static uint ToCode(byte[] bytes)
    {
        uint code = 0;
        for (int i = 0; i < bytes.Length && i < 4; i++)
            code = (code << 8) | bytes[i];
        return code;
    }

    private readonly struct CodespaceRange
    {
        public CodespaceRange(int byteLength, uint low, uint high)
        {
            ByteLength = byteLength;
            Low = low;
            High = high;
        }

        public int ByteLength { get; }

        public uint Low { get; }

        public uint High { get; }
    }

    private readonly struct BfRange
    {
        public BfRange(uint low, uint high, string baseText, string[]? destinations)
        {
            Low = low;
            High = high;
            BaseText = baseText;
            Destinations = destinations;
        }

        public uint Low { get; }

        public uint High { get; }

        public string BaseText { get; }

        /// <summary>Explicit per-code destinations, when the range used an array form.</summary>
        public string[]? Destinations { get; }
    }
}
