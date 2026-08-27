using System;
using System.Collections.Generic;

namespace Broiler.Documents.Pdf.Filters;

/// <summary>
/// The PDF filter names, their inline-image abbreviations, and the stable
/// diagnostic each not-yet-implemented filter reports.
/// </summary>
/// <remarks>
/// This table is the single place that knows a filter <em>exists</em>. Knowing a
/// filter exists is what lets the reader say "JBIG2 image, skipped" instead of
/// "unknown filter", and it is deliberately separate from knowing how to decode
/// one — decoding arrives only with a composed <see cref="IPdfStreamFilter"/>.
/// </remarks>
public static class PdfFilterNames
{
    public const string Flate = "FlateDecode";
    public const string Lzw = "LZWDecode";
    public const string AsciiHex = "ASCIIHexDecode";
    public const string Ascii85 = "ASCII85Decode";
    public const string RunLength = "RunLengthDecode";
    public const string CcittFax = "CCITTFaxDecode";
    public const string Jbig2 = "JBIG2Decode";
    public const string Dct = "DCTDecode";
    public const string Jpx = "JPXDecode";
    public const string Crypt = "Crypt";

    private static readonly Dictionary<string, string> Abbreviations = new(StringComparer.Ordinal)
    {
        ["AHx"] = AsciiHex,
        ["A85"] = Ascii85,
        ["LZW"] = Lzw,
        ["Fl"] = Flate,
        ["RL"] = RunLength,
        ["CCF"] = CcittFax,
        ["DCT"] = Dct,
    };

    private static readonly Dictionary<string, string> UnsupportedDiagnostics = new(StringComparer.Ordinal)
    {
        [Lzw] = PdfDiagnosticCodes.FilterLzwUnsupported,
        [CcittFax] = PdfDiagnosticCodes.FilterCcittUnsupported,
        [Jbig2] = PdfDiagnosticCodes.FilterJbig2Unsupported,
        [Dct] = PdfDiagnosticCodes.FilterDctUnsupported,
        [Jpx] = PdfDiagnosticCodes.FilterJpxUnsupported,
        [Crypt] = PdfDiagnosticCodes.FilterCryptUnsupported,
    };

    /// <summary>
    /// Filters whose output is image samples rather than a byte stream. A stream
    /// ending in one of these is an image: the object layer must not try to parse
    /// its bytes even if a decoder for it is composed.
    /// </summary>
    private static readonly HashSet<string> ImageFilters = new(StringComparer.Ordinal)
    {
        Dct, Jpx, Jbig2, CcittFax,
    };

    /// <summary>Expands an inline-image abbreviation to its full filter name.</summary>
    public static string Canonicalize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Abbreviations.TryGetValue(name, out string? full) ? full : name;
    }

    /// <summary>True when <paramref name="name"/> is a filter defined by the format.</summary>
    public static bool IsKnown(string name) =>
        Canonicalize(name) is Flate or Lzw or AsciiHex or Ascii85 or RunLength
            or CcittFax or Jbig2 or Dct or Jpx or Crypt;

    public static bool IsImageFilter(string name) => ImageFilters.Contains(Canonicalize(name));

    /// <summary>
    /// The diagnostic to report when <paramref name="name"/> is present but no
    /// implementation is composed. Filters with a dedicated code (each of which
    /// names its own IP-register row) keep it; anything else reports the generic
    /// not-composed code.
    /// </summary>
    public static string UnsupportedDiagnosticFor(string name) =>
        UnsupportedDiagnostics.TryGetValue(Canonicalize(name), out string? code)
            ? code
            : PdfDiagnosticCodes.FilterNotComposed;
}
