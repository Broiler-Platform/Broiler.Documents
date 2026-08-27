using System;
using System.Collections.Generic;
using Broiler.Documents.Pdf.Filters;
using Broiler.Documents.Pdf.Structure;
using Broiler.Documents.Pdf.Syntax;

namespace Broiler.Documents.Pdf.Text;

/// <summary>One decoded glyph: the text it stands for and how far it advances.</summary>
internal readonly struct PdfGlyph
{
    public PdfGlyph(uint code, string text, double width, bool mapped)
    {
        Code = code;
        Text = text;
        Width = width;
        IsMapped = mapped;
    }

    public uint Code { get; }

    /// <summary>The Unicode text, empty when the code could not be mapped.</summary>
    public string Text { get; }

    /// <summary>Advance width in text-space units (em fractions, so 0.5 is half an em).</summary>
    public double Width { get; }

    /// <summary>False when no trustworthy mapping existed for this code.</summary>
    public bool IsMapped { get; }

    /// <summary>True when the glyph is a single space; word grouping keys off this.</summary>
    public bool IsSpace => Text.Length == 1 && Text[0] is ' ' or ' ';
}

/// <summary>
/// A font resource as far as logical text extraction needs one: the code-to-text
/// mapping and the widths. Glyph outlines are never touched.
/// </summary>
/// <remarks>
/// <para>
/// The mapping is chosen in confidence order — <c>ToUnicode</c> first, then the
/// declared simple encoding with its <c>/Differences</c>, then nothing. There is
/// no fourth step that guesses from a glyph index or a font program, because a
/// guess that looks like text is worse than an honest gap: an unmapped code
/// yields <see cref="PdfDiagnosticCodes.TextMappingMissing"/> rather than a
/// plausible wrong character.
/// </para>
/// <para>
/// An embedded font program is detected and left alone. Parsing one needs the
/// bounded font inspector that the roadmap places in Graphics, and this release
/// composes none, so <see cref="HasUnreadableProgram"/> reports the gap instead.
/// </para>
/// </remarks>
internal sealed class PdfFont
{
    private readonly char[]? _simpleEncoding;
    private readonly Dictionary<int, string>? _differences;
    private readonly Dictionary<uint, double> _widths;
    private readonly double _defaultWidth;
    private readonly PdfCMap? _toUnicode;
    private readonly PdfCMap? _codeMap;

    private PdfFont(
        string baseFont,
        string family,
        bool bold,
        bool italic,
        bool isType0,
        bool isType3,
        bool hasUnreadableProgram,
        char[]? simpleEncoding,
        Dictionary<int, string>? differences,
        Dictionary<uint, double> widths,
        double defaultWidth,
        PdfCMap? toUnicode,
        PdfCMap? codeMap)
    {
        BaseFont = baseFont;
        Family = family;
        IsBold = bold;
        IsItalic = italic;
        IsType0 = isType0;
        IsType3 = isType3;
        HasUnreadableProgram = hasUnreadableProgram;
        _simpleEncoding = simpleEncoding;
        _differences = differences;
        _widths = widths;
        _defaultWidth = defaultWidth;
        _toUnicode = toUnicode;
        _codeMap = codeMap;
    }

    /// <summary>The raw <c>/BaseFont</c> value, subset prefix included.</summary>
    public string BaseFont { get; }

    /// <summary>The family name with any subset prefix removed.</summary>
    public string Family { get; }

    public bool IsBold { get; }

    public bool IsItalic { get; }

    public bool IsType0 { get; }

    /// <summary>True for a Type 3 font, whose glyph procedures are never executed.</summary>
    public bool IsType3 { get; }

    /// <summary>True when the font embeds a program this build cannot inspect.</summary>
    public bool HasUnreadableProgram { get; }

    /// <summary>A font that can map nothing; used when a resource is missing.</summary>
    public static PdfFont Fallback { get; } = new(
        string.Empty,
        string.Empty,
        false,
        false,
        false,
        false,
        false,
        PdfEncodings.Default,
        null,
        [],
        0.5,
        null,
        null);

    /// <summary>Splits a byte string into glyphs, honouring the font's code width.</summary>
    public IEnumerable<PdfGlyph> Decode(byte[] bytes)
    {
        int offset = 0;
        while (offset < bytes.Length)
        {
            int length = CodeLength(bytes, offset);
            uint code = 0;
            for (int i = 0; i < length && offset + i < bytes.Length; i++)
                code = (code << 8) | bytes[offset + i];
            offset += length;

            bool mapped = TryMapText(code, out string text);
            yield return new PdfGlyph(code, text, WidthOf(code), mapped);
        }
    }

    private int CodeLength(byte[] bytes, int offset)
    {
        if (_codeMap is not null)
            return Math.Max(1, Math.Min(_codeMap.CodeLengthAt(bytes, offset), bytes.Length - offset));
        return 1;
    }

    private bool TryMapText(uint code, out string text)
    {
        // 1. ToUnicode is the font's own statement of what its codes mean.
        if (_toUnicode is not null && _toUnicode.TryMap(code, out text) && text.Length > 0)
            return true;

        // 2. A /Differences entry overrides the base encoding for one code.
        if (_differences is not null && code <= int.MaxValue &&
            _differences.TryGetValue((int)code, out string? difference))
        {
            text = difference;
            return difference.Length > 0;
        }

        // 3. The declared single-byte encoding.
        if (_simpleEncoding is not null && code < (uint)_simpleEncoding.Length)
        {
            char c = _simpleEncoding[code];
            if (c != '\0')
            {
                text = c.ToString();
                return true;
            }
        }

        text = string.Empty;
        return false;
    }

    private double WidthOf(uint code) =>
        _widths.TryGetValue(code, out double width) ? width : _defaultWidth;

    // ---- construction ---------------------------------------------------------

    public static PdfFont Load(PdfObjectStore store, PdfDictionary dictionary)
    {
        store.Budget.ChargeFont();

        string subtype = (store.Resolve(dictionary["Subtype"]) as PdfName)?.Value ?? string.Empty;
        string baseFont = (store.Resolve(dictionary["BaseFont"]) as PdfName)?.Value ?? string.Empty;

        if (subtype == "Type0")
            return LoadType0(store, dictionary, baseFont);

        bool isType3 = subtype == "Type3";
        if (isType3)
        {
            store.Diagnostics.Skipped(
                PdfDiagnosticCodes.FontType3Unsupported,
                "A Type 3 font was detected. Its glyph procedures are never executed, so its text is mapped only where ToUnicode or an encoding supplies a meaning.");
        }

        PdfDictionary? descriptor = store.Resolve(dictionary["FontDescriptor"]) as PdfDictionary;
        (bool bold, bool italic) = ReadStyle(store, descriptor, baseFont);
        bool symbolic = ReadFlag(store, descriptor, bit: 3);

        char[]? encoding = ReadSimpleEncoding(store, dictionary, symbolic, out Dictionary<int, string>? differences);
        PdfCMap? toUnicode = ReadToUnicode(store, dictionary);
        Dictionary<uint, double> widths = ReadSimpleWidths(store, dictionary);
        double missingWidth = descriptor is not null && store.Resolve(descriptor["MissingWidth"]) is PdfNumber missing
            ? missing.Value / 1000d
            : 0.5;

        bool unreadableProgram = HasEmbeddedProgram(store, descriptor);
        if (unreadableProgram)
        {
            store.Diagnostics.Skipped(
                PdfDiagnosticCodes.FontProgramNotComposed,
                "A font embeds a program this build does not inspect; text was mapped from ToUnicode and the declared encoding only.");
        }

        return new PdfFont(
            baseFont,
            StripSubsetPrefix(baseFont),
            bold,
            italic,
            isType0: false,
            isType3,
            unreadableProgram,
            encoding,
            differences,
            widths,
            missingWidth,
            toUnicode,
            codeMap: null);
    }

    private static PdfFont LoadType0(PdfObjectStore store, PdfDictionary dictionary, string baseFont)
    {
        PdfCMap? codeMap = ReadType0Encoding(store, dictionary);
        PdfCMap? toUnicode = ReadToUnicode(store, dictionary);

        PdfDictionary? descendant = null;
        if (store.Resolve(dictionary["DescendantFonts"]) is PdfArray descendants && descendants.Count > 0)
            descendant = store.Resolve(descendants[0]) as PdfDictionary;

        PdfDictionary? descriptor = descendant is not null
            ? store.Resolve(descendant["FontDescriptor"]) as PdfDictionary
            : null;

        (bool bold, bool italic) = ReadStyle(store, descriptor, baseFont);
        Dictionary<uint, double> widths = descendant is not null
            ? ReadCidWidths(store, descendant)
            : [];
        double defaultWidth = descendant is not null && store.Resolve(descendant["DW"]) is PdfNumber dw
            ? dw.Value / 1000d
            : 1.0;

        bool unreadableProgram = HasEmbeddedProgram(store, descriptor);
        if (unreadableProgram)
        {
            store.Diagnostics.Skipped(
                PdfDiagnosticCodes.FontProgramNotComposed,
                "A composite font embeds a program this build does not inspect; text was mapped from ToUnicode only.");
        }

        if (toUnicode is null)
        {
            store.Diagnostics.Skipped(
                PdfDiagnosticCodes.TextMappingMissing,
                "A composite font supplied no ToUnicode map, so its character codes have no reliable text meaning.");
        }

        return new PdfFont(
            baseFont,
            StripSubsetPrefix(baseFont),
            bold,
            italic,
            isType0: true,
            isType3: false,
            unreadableProgram,
            simpleEncoding: null,
            differences: null,
            widths,
            defaultWidth,
            toUnicode,
            codeMap ?? PdfCMap.IdentityTwoByte);
    }

    private static PdfCMap? ReadType0Encoding(PdfObjectStore store, PdfDictionary dictionary)
    {
        PdfObject? encoding = store.Resolve(dictionary["Encoding"]);

        if (encoding is PdfName name)
        {
            // Identity-H and Identity-V are two-byte identity maps. Every other
            // predefined CMap names a character collection this build does not
            // carry, so its codes fall back to two-byte reads and ToUnicode.
            if (name.Value is "Identity-H" or "Identity-V")
                return PdfCMap.IdentityTwoByte;

            store.Diagnostics.Skipped(
                PdfDiagnosticCodes.TextMappingMissing,
                $"A composite font names the predefined CMap {name.Value}, which this build does not carry.");
            return PdfCMap.IdentityTwoByte;
        }

        if (encoding is PdfStream stream && TryDecode(store, stream, out byte[]? data))
            return PdfCMap.Parse(data!, store.Budget);

        return null;
    }

    private static PdfCMap? ReadToUnicode(PdfObjectStore store, PdfDictionary dictionary)
    {
        if (store.Resolve(dictionary["ToUnicode"]) is not PdfStream stream)
            return null;
        if (!TryDecode(store, stream, out byte[]? data))
            return null;

        PdfCMap map = PdfCMap.Parse(data!, store.Budget);
        return map.IsEmpty ? null : map;
    }

    private static bool TryDecode(PdfObjectStore store, PdfStream stream, out byte[]? data)
    {
        PdfStreamDecodeResult decoded = store.Filters.Decode(stream, store.Resolve, store.Budget);
        if (decoded.Succeeded)
        {
            data = decoded.Data;
            return true;
        }

        store.Diagnostics.Skipped(
            decoded.DiagnosticCode ?? PdfDiagnosticCodes.FilterMalformed,
            decoded.Message ?? "A font's character map could not be decoded.");
        data = null;
        return false;
    }

    private static char[]? ReadSimpleEncoding(
        PdfObjectStore store,
        PdfDictionary dictionary,
        bool symbolic,
        out Dictionary<int, string>? differences)
    {
        differences = null;
        PdfObject? encoding = store.Resolve(dictionary["Encoding"]);

        char[]? table = null;
        if (encoding is PdfName name)
        {
            table = PdfEncodings.ForName(name.Value);
            if (table is null && name.Value == PdfEncodings.MacExpert)
            {
                store.Diagnostics.Skipped(
                    PdfDiagnosticCodes.TextMappingMissing,
                    "A font names MacExpertEncoding, whose mapping data this build does not carry.");
            }
        }
        else if (encoding is PdfDictionary encodingDictionary)
        {
            string? baseName = (store.Resolve(encodingDictionary["BaseEncoding"]) as PdfName)?.Value;
            table = PdfEncodings.ForName(baseName);
            differences = ReadDifferences(store, encodingDictionary);
        }

        if (table is not null)
            return table;

        // A symbolic font's built-in encoding lives in its font program, which this
        // build does not read. Falling back to StandardEncoding there would invent
        // Latin text for arbitrary symbols, so nothing is assumed.
        return symbolic && differences is null ? null : PdfEncodings.Default;
    }

    private static Dictionary<int, string>? ReadDifferences(PdfObjectStore store, PdfDictionary encoding)
    {
        if (store.Resolve(encoding["Differences"]) is not PdfArray array || array.Count == 0)
            return null;

        var differences = new Dictionary<int, string>();
        int code = 0;

        foreach (PdfObject entry in array)
        {
            switch (store.Resolve(entry))
            {
                case PdfNumber number:
                    code = number.ToInt32();
                    break;
                case PdfName name:
                    if (code is >= 0 and <= 0xFFFF)
                    {
                        // An unmappable name is recorded as an empty mapping so the
                        // code is known-unmapped rather than falling through to the
                        // base encoding and producing the wrong character.
                        differences[code] = PdfEncodings.TryMapGlyphName(name.Value, out string text) ? text : string.Empty;
                    }

                    code++;
                    break;
            }

            store.Budget.ChargeCMapEntries(1);
        }

        return differences;
    }

    private static Dictionary<uint, double> ReadSimpleWidths(PdfObjectStore store, PdfDictionary dictionary)
    {
        var widths = new Dictionary<uint, double>();
        if (store.Resolve(dictionary["Widths"]) is not PdfArray array)
            return widths;

        int first = store.Resolve(dictionary["FirstChar"]) is PdfNumber number ? number.ToInt32() : 0;
        for (int i = 0; i < array.Count; i++)
        {
            if (store.Resolve(array[i]) is not PdfNumber width || !double.IsFinite(width.Value))
                continue;
            long code = (long)first + i;
            if (code is >= 0 and <= 0xFFFF)
                widths[(uint)code] = width.Value / 1000d;
        }

        return widths;
    }

    /// <summary>
    /// Reads the composite-font <c>/W</c> array, which mixes two forms:
    /// <c>c [w1 w2 …]</c> and <c>cFirst cLast w</c>.
    /// </summary>
    private static Dictionary<uint, double> ReadCidWidths(PdfObjectStore store, PdfDictionary descendant)
    {
        var widths = new Dictionary<uint, double>();
        if (store.Resolve(descendant["W"]) is not PdfArray array)
            return widths;

        int index = 0;
        while (index < array.Count)
        {
            if (store.Resolve(array[index]) is not PdfNumber first)
                break;
            index++;
            if (index >= array.Count)
                break;

            PdfObject? next = store.Resolve(array[index]);
            if (next is PdfArray list)
            {
                index++;
                long code = first.ToInt64();
                foreach (PdfObject entry in list)
                {
                    if (store.Resolve(entry) is PdfNumber width && double.IsFinite(width.Value) && code is >= 0 and <= 0xFFFF)
                        widths[(uint)code] = width.Value / 1000d;
                    code++;
                    store.Budget.ChargeCMapEntries(1);
                }

                continue;
            }

            if (next is not PdfNumber last)
                break;
            index++;
            if (index >= array.Count || store.Resolve(array[index]) is not PdfNumber shared)
                break;
            index++;

            long from = first.ToInt64();
            long to = last.ToInt64();
            if (to < from || to - from > store.Budget.Limits.MaxCMapEntries)
                continue;

            store.Budget.ChargeCMapEntries((int)(to - from + 1));
            for (long code = from; code <= to && code <= 0xFFFF; code++)
            {
                if (code >= 0 && double.IsFinite(shared.Value))
                    widths[(uint)code] = shared.Value / 1000d;
            }
        }

        return widths;
    }

    private static (bool Bold, bool Italic) ReadStyle(PdfObjectStore store, PdfDictionary? descriptor, string baseFont)
    {
        if (descriptor is not null)
        {
            bool italic = ReadFlag(store, descriptor, bit: 7);
            if (!italic && store.Resolve(descriptor["ItalicAngle"]) is PdfNumber angle)
                italic = Math.Abs(angle.Value) > 0.5;

            bool bold = ReadFlag(store, descriptor, bit: 19);
            if (!bold && store.Resolve(descriptor["StemV"]) is PdfNumber stem)
                bold = stem.Value >= 120;
            if (!bold && store.Resolve(descriptor["FontWeight"]) is PdfNumber weight)
                bold = weight.Value >= 600;

            return (bold, italic);
        }

        // With no descriptor the font must be one of the fourteen standard names,
        // whose weight and slant the name itself defines. That is the format's own
        // metadata, not a substring guess at an arbitrary family name.
        if (PdfStandardFonts.TryParse(StripSubsetPrefix(baseFont), out PdfStandardFont standard))
        {
            return (
                standard is PdfStandardFont.HelveticaBold or PdfStandardFont.HelveticaBoldOblique
                    or PdfStandardFont.TimesBold or PdfStandardFont.TimesBoldItalic
                    or PdfStandardFont.CourierBold or PdfStandardFont.CourierBoldOblique,
                standard is PdfStandardFont.HelveticaOblique or PdfStandardFont.HelveticaBoldOblique
                    or PdfStandardFont.TimesItalic or PdfStandardFont.TimesBoldItalic
                    or PdfStandardFont.CourierOblique or PdfStandardFont.CourierBoldOblique);
        }

        return (false, false);
    }

    /// <summary>Reads a one-based bit from the descriptor's <c>/Flags</c> (Table 123).</summary>
    private static bool ReadFlag(PdfObjectStore store, PdfDictionary? descriptor, int bit)
    {
        if (descriptor is null || store.Resolve(descriptor["Flags"]) is not PdfNumber flags)
            return false;
        long value = flags.ToInt64();
        return (value & (1L << (bit - 1))) != 0;
    }

    private static bool HasEmbeddedProgram(PdfObjectStore store, PdfDictionary? descriptor)
    {
        if (descriptor is null)
            return false;
        return store.Resolve(descriptor["FontFile"]) is not null ||
               store.Resolve(descriptor["FontFile2"]) is not null ||
               store.Resolve(descriptor["FontFile3"]) is not null;
    }

    /// <summary>
    /// Removes the six-letter subset tag a subsetted font carries, as in
    /// <c>ABCDEF+Minion</c>. The shape of the tag is defined by the format, so
    /// this is a structural rule rather than a heuristic on the family name.
    /// </summary>
    internal static string StripSubsetPrefix(string baseFont)
    {
        if (baseFont.Length < 8 || baseFont[6] != '+')
            return baseFont;

        for (int i = 0; i < 6; i++)
        {
            if (baseFont[i] is < 'A' or > 'Z')
                return baseFont;
        }

        return baseFont[7..];
    }
}
