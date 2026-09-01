using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace Broiler.Documents.Pdf.Text;

/// <summary>
/// The budget and cancellation handed to a font-program reader.
/// </summary>
/// <remarks>
/// A font program is untrusted input from the document, and a font parser is one
/// of the two largest attack surfaces a PDF reader has. The ceiling is checked by
/// the codec before the program is handed over and is meant to be checked again
/// by the reader before it allocates.
/// </remarks>
public sealed class PdfFontProgramContext
{
    public PdfFontProgramContext(long maxBytes, CancellationToken cancellationToken = default)
    {
        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));

        MaxBytes = maxBytes;
        CancellationToken = cancellationToken;
    }

    /// <summary>Hard ceiling on the program bytes a reader may examine.</summary>
    public long MaxBytes { get; }

    public CancellationToken CancellationToken { get; }
}

/// <summary>
/// What a reader recovered from one embedded font program.
/// </summary>
/// <remarks>
/// Deliberately not a font. The codec has no use for outlines, hinting, or
/// layout tables, and asking for them would put a rendering-shaped API in a
/// text-extraction codec. What it needs is the one thing a PDF's own structures
/// may fail to supply: what the glyphs a document draws actually <em>say</em>.
/// </remarks>
public sealed class PdfFontProgramMap
{
    /// <summary>An empty map, for a program that was read and yielded nothing.</summary>
    public static PdfFontProgramMap Empty { get; } = new(string.Empty, new Dictionary<int, string>());

    public PdfFontProgramMap(string format, IReadOnlyDictionary<int, string> glyphText)
    {
        ArgumentNullException.ThrowIfNull(glyphText);

        Format = format ?? string.Empty;
        GlyphText = glyphText as ReadOnlyDictionary<int, string> ??
            new ReadOnlyDictionary<int, string>(new Dictionary<int, string>(glyphText));
    }

    /// <summary>The program format the reader recognized, for the diagnostic.</summary>
    public string Format { get; }

    /// <summary>
    /// Glyph index to the text that glyph stands for. Empty when the program was
    /// read but carries no character map to recover it from.
    /// </summary>
    public IReadOnlyDictionary<int, string> GlyphText { get; }

    public bool IsEmpty => GlyphText.Count == 0;
}

/// <summary>
/// Reads an embedded font program far enough to say what its glyphs mean.
/// </summary>
/// <remarks>
/// <para>
/// The codec's second composition point after <see cref="Filters.IPdfStreamFilter"/>,
/// and it exists for one failure that structure alone cannot fix. A PDF that
/// embeds a subset font, marks it symbolic, and supplies no <c>ToUnicode</c> map
/// has told a reader where to draw glyphs and nothing whatever about what they
/// say. The encodings are not applicable, the glyph names are inside the program,
/// and a reader without one extracts either nothing or a guess.
/// </para>
/// <para>
/// Implementations must be pure and instance-owned, must respect
/// <see cref="PdfFontProgramContext.MaxBytes"/> before allocating, must observe
/// the cancellation token, and must return <see langword="null"/> rather than
/// throwing for a program they cannot read. A font parser meets hostile input by
/// definition; a fault inside one must cost the font, not the document.
/// </para>
/// <para>
/// Nothing composed here authorizes anything on the write side. Reading a program
/// to recover text is not embedding it, and this release embeds no fonts at all
/// (IP-012).
/// </para>
/// </remarks>
public interface IPdfFontProgramReader
{
    /// <summary>
    /// Reads one embedded program, or returns null when it is not a format this
    /// reader inspects.
    /// </summary>
    /// <param name="program">The decoded program bytes.</param>
    /// <param name="descriptorKey">
    /// The font descriptor key the program arrived under — <c>FontFile</c> for
    /// Type 1, <c>FontFile2</c> for TrueType, <c>FontFile3</c> otherwise.
    /// </param>
    /// <param name="subtype">The <c>FontFile3</c> subtype, or null for the other keys.</param>
    /// <param name="context">The byte ceiling and cancellation for this read.</param>
    PdfFontProgramMap? Read(
        ReadOnlySpan<byte> program,
        string descriptorKey,
        string? subtype,
        PdfFontProgramContext context);
}
