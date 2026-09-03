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
/// embedded program's own character map where a reader is composed and the code
/// is a glyph index, then the declared simple encoding with its
/// <c>/Differences</c>, then nothing. No step guesses: an unmapped code yields
/// <see cref="PdfDiagnosticCodes.TextMappingMissing"/> rather than a plausible
/// wrong character, and the program map is used only for the composite encodings
/// where a code <em>is</em> a glyph index by definition rather than by
/// supposition.
/// </para>
/// <para>
/// An embedded font program is read only through a composed
/// <see cref="IPdfFontProgramReader"/>. Without one it is detected and left
/// alone, and <see cref="HasUnreadableProgram"/> reports the gap.
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
    private readonly IReadOnlyDictionary<int, string>? _glyphText;

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
        PdfCMap? codeMap,
        IReadOnlyDictionary<int, string>? glyphText = null)
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
        _glyphText = glyphText;
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

        // 2. The embedded program's own character map. Only ever populated for a
        //    composite font on an identity encoding, where the code is the glyph
        //    index by definition — this is a lookup, not an inference.
        if (_glyphText is not null && code <= int.MaxValue &&
            _glyphText.TryGetValue((int)code, out string? fromProgram) && fromProgram.Length > 0)
        {
            text = fromProgram;
            return true;
        }

        // 3. A /Differences entry overrides the base encoding for one code.
        if (_differences is not null && code <= int.MaxValue &&
            _differences.TryGetValue((int)code, out string? difference))
        {
            text = difference;
            return difference.Length > 0;
        }

        // 4. The declared single-byte encoding.
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
                "A Type 3 font was detected. Its glyph procedures draw the glyphs and are never executed, so its text is " +
                "mapped only where ToUnicode or the font's own /Differences names say what a code means, and never by " +
                "assuming a standard encoding it does not have. Its advances are taken through /FontMatrix, which is " +
                "where a Type 3 font states its glyph-space scale rather than using the fixed one every other font has.");
        }

        PdfDictionary? descriptor = store.Resolve(dictionary["FontDescriptor"]) as PdfDictionary;
        (bool bold, bool italic) = ReadStyle(store, descriptor, baseFont);
        bool symbolic = ReadFlag(store, descriptor, bit: 3);

        char[]? encoding = ReadSimpleEncoding(
            store, dictionary, symbolic, StripSubsetPrefix(baseFont), isType3, out Dictionary<int, string>? differences);
        PdfCMap? toUnicode = ReadToUnicode(store, dictionary);

        // Every simple font but a Type 3 measures its glyphs in thousandths of
        // text space. A Type 3 states its own scale in /FontMatrix, and the
        // default happens to be that same thousandth — which is why assuming it
        // was right until a font said otherwise.
        double glyphScale = isType3 ? ReadType3GlyphScale(store, dictionary) : 0.001;

        Dictionary<uint, double> widths = ReadSimpleWidths(store, dictionary, glyphScale);
        double missingWidth = descriptor is not null && store.Resolve(descriptor["MissingWidth"]) is PdfNumber missing
            ? missing.Value * glyphScale
            : isType3 ? 0 : 0.5;

        string? program = DescribeEmbeddedProgram(store, descriptor);
        bool unreadableProgram = program is not null;
        if (program is not null)
        {
            // Deliberately not inspected. A simple font's code reaches a glyph
            // through the program's own cmap under rules that depend on which
            // subtable it selected, and recovering text from it would be a guess
            // where the composite path is a lookup (IP-012 notes). A composed
            // reader is therefore never offered this program — which is a
            // different thing from having no reader to offer it to, and the two
            // are reported apart.
            store.Features.NoteFontProgram(new PdfFontProgram(
                program,
                Composite: false,
                symbolic,
                HasToUnicode: toUnicode is not null,
                store.FontProgramReader is null
                    ? PdfFontProgramInspection.NotComposed
                    : PdfFontProgramInspection.NotOffered));
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

        string? program = DescribeEmbeddedProgram(store, descriptor);

        // Worth the work only where the document has not already said what its
        // codes mean. ToUnicode is the producer's own statement and outranks
        // anything recovered from the program.
        PdfFontProgramMap? inspected = program is not null && toUnicode is null
            ? InspectProgram(store, descriptor)
            : null;

        bool recovered = inspected is { IsEmpty: false };
        bool unreadableProgram = program is not null && !recovered;

        if (program is not null)
        {
            // Four outcomes, and the note has to keep them apart: no reader to
            // ask, a reader not asked because ToUnicode already answered, a
            // reader asked that recovered nothing, and a reader that read it.
            PdfFontProgramInspection inspection =
                store.FontProgramReader is null ? PdfFontProgramInspection.NotComposed
                : toUnicode is not null ? PdfFontProgramInspection.NotOffered
                : recovered ? PdfFontProgramInspection.Read
                : PdfFontProgramInspection.Unread;

            store.Features.NoteFontProgram(new PdfFontProgram(
                program,
                Composite: true,
                ReadFlag(store, descriptor, bit: 3),
                HasToUnicode: toUnicode is not null,
                inspection));
        }

        if (toUnicode is null && !recovered)
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
            codeMap ?? PdfCMap.IdentityTwoByte,
            recovered ? inspected!.GlyphText : null);
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
        string family,
        bool isType3,
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

        // A Type 3 font has no built-in encoding to fall back to. Its glyphs are
        // procedures the document drew and named, and a name is all there is: the
        // standard-14 tables below describe fonts this one is not, and
        // StandardEncoding would answer for arbitrary drawn shapes with confident
        // Latin text. Where /Differences and ToUnicode both say nothing, nothing
        // is the honest answer, and the unmapped-glyph diagnostic reports it.
        if (isType3)
            return null;

        // One symbolic font's built-in encoding is a property of the format rather
        // than of an embedded program: a standard-14 Symbol carries no program to
        // read one from, and the format says what its codes mean.
        if (PdfEncodings.ForStandardFont(family) is char[] builtIn)
            return builtIn;

        // The other one is recognized so that it can be refused. Left to the Latin
        // fallback below, a ZapfDingbats font extracts confident nonsense — "ab"
        // for two ornaments — which is worse than extracting nothing.
        if (PdfEncodings.HasNonLatinBuiltInEncoding(family))
        {
            store.Diagnostics.Skipped(
                PdfDiagnosticCodes.TextMappingMissing,
                $"A font uses {family}'s built-in encoding, whose mapping data this build does not carry.");
            return null;
        }

        // Every other symbolic font keeps its encoding inside its own program,
        // which this build does not read. Falling back to StandardEncoding there
        // would invent Latin text for arbitrary symbols, so nothing is assumed.
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

    /// <summary>
    /// The horizontal glyph-space scale a Type 3 font declares in
    /// <c>/FontMatrix</c>, or the 1/1000 default where it states none.
    /// </summary>
    /// <remarks>
    /// Only the first element is taken. A glyph's displacement is the vector
    /// (w, 0) through the matrix, so its horizontal component is w times that
    /// element, and the rotated and skewed matrices where the rest of the matrix
    /// would matter do not describe a horizontal advance for this pass to apply.
    /// A scale that is zero, not finite, or absurd enough to place a single glyph
    /// off any page is treated as unstated rather than honoured.
    /// </remarks>
    private static double ReadType3GlyphScale(PdfObjectStore store, PdfDictionary dictionary)
    {
        const double Default = 0.001;

        if (store.Resolve(dictionary["FontMatrix"]) is not PdfArray matrix || matrix.Count < 6)
            return Default;

        if (store.Resolve(matrix[0]) is not PdfNumber scale ||
            !double.IsFinite(scale.Value) ||
            scale.Value == 0 ||
            Math.Abs(scale.Value) > 100)
        {
            return Default;
        }

        return scale.Value;
    }

    private static Dictionary<uint, double> ReadSimpleWidths(
        PdfObjectStore store,
        PdfDictionary dictionary,
        double glyphScale)
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
                widths[(uint)code] = width.Value * glyphScale;
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

    /// <summary>
    /// Reads the descriptor's embedded program through the composed reader.
    /// Returns null when no reader is composed, the program cannot be decoded or
    /// is past its ceiling, or the reader does not handle that format.
    /// </summary>
    /// <remarks>
    /// The program goes through the ordinary filter pipeline and the ordinary
    /// budget, and a reader that faults costs the font rather than the document.
    /// A font parser meets hostile input by definition, and this is the boundary
    /// where that stops being the document's problem.
    /// </remarks>
    private static PdfFontProgramMap? InspectProgram(PdfObjectStore store, PdfDictionary? descriptor)
    {
        if (store.FontProgramReader is not IPdfFontProgramReader programReader || descriptor is null)
            return null;

        (string key, PdfStream? stream) = FindProgramStream(store, descriptor);
        if (stream is null)
            return null;

        PdfStreamDecodeResult decoded = store.Filters.Decode(stream, store.Resolve, store.Budget);
        if (!decoded.Succeeded || decoded.Data is not byte[] bytes)
            return null;

        long ceiling = store.Budget.Limits.MaxFontProgramBytes;
        if (bytes.LongLength > ceiling)
        {
            store.Diagnostics.Skipped(
                PdfDiagnosticCodes.FontProgramNotComposed,
                $"An embedded font program of {bytes.LongLength} bytes is past the font-program ceiling of {ceiling} and was not inspected.");
            return null;
        }

        string? subtype = string.Equals(key, "FontFile3", StringComparison.Ordinal)
            ? (store.Resolve(stream.Dictionary["Subtype"]) as PdfName)?.Value
            : null;

        try
        {
            return programReader.Read(bytes, key, subtype, new PdfFontProgramContext(ceiling, store.Budget.Cancellation));
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
        {
            store.Diagnostics.Skipped(
                PdfDiagnosticCodes.FontProgramNotComposed,
                $"The composed font-program reader failed on an embedded program ({ex.GetType().Name}); the font was mapped without it.");
            return null;
        }
    }

    /// <summary>The descriptor's embedded program stream and the key it arrived under.</summary>
    private static (string Key, PdfStream? Stream) FindProgramStream(PdfObjectStore store, PdfDictionary descriptor)
    {
        foreach (string key in ProgramKeys)
        {
            if (store.Resolve(descriptor[key]) is PdfStream stream)
                return (key, stream);
        }

        return (string.Empty, null);
    }

    private static readonly string[] ProgramKeys = ["FontFile", "FontFile2", "FontFile3"];

    /// <summary>
    /// Names the format of the embedded font program, or null when the descriptor
    /// embeds none.
    /// </summary>
    /// <remarks>
    /// The descriptor key <em>is</em> the format: <c>FontFile</c> is Type 1,
    /// <c>FontFile2</c> is TrueType, and <c>FontFile3</c> declares its own
    /// subtype, where <c>Type1C</c> and <c>CIDFontType0C</c> are CFF and
    /// <c>OpenType</c> is a whole font file. Which one a document uses decides
    /// which part of IP-012 a font inspector would sit under, so the diagnostic
    /// names it rather than reporting an anonymous "program". The font's own name
    /// is deliberately not reported: that is a value, and a diagnostic names
    /// constructs.
    /// </remarks>
    private static string? DescribeEmbeddedProgram(PdfObjectStore store, PdfDictionary? descriptor)
    {
        if (descriptor is null)
            return null;

        if (store.Resolve(descriptor["FontFile"]) is not null)
            return "FontFile (Type 1)";

        if (store.Resolve(descriptor["FontFile2"]) is not null)
            return "FontFile2 (TrueType)";

        PdfObject? third = store.Resolve(descriptor["FontFile3"]);
        if (third is null)
            return null;

        string subtype = third is PdfStream program
            ? (store.Resolve(program.Dictionary["Subtype"]) as PdfName)?.Value ?? string.Empty
            : string.Empty;

        return subtype.Length > 0 ? "FontFile3 /" + subtype : "FontFile3";
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
