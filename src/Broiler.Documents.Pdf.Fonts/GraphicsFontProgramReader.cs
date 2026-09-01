using System;
using System.Collections.Generic;
using Broiler.Documents.Pdf.Text;
using Broiler.Graphics;

namespace Broiler.Documents.Pdf.Fonts;

/// <summary>
/// Reads embedded sfnt font programs — TrueType and OpenType — by composing the
/// font parser from <c>Broiler.Graphics</c>. Not composed by default: a caller
/// opts in by putting it into <see cref="PdfCodecServices"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What it recovers.</strong> One thing: the text each glyph stands for,
/// taken from the program's own character map. That is what a PDF with a
/// subsetted, symbolic, <c>ToUnicode</c>-less font fails to say, and it is the
/// difference between extracting the document's words and extracting nothing.
/// </para>
/// <para>
/// <strong>What it does not do.</strong> No outlines, no hinting, no shaping, no
/// metrics, and above all no embedding: reading a program to recover text is not
/// redistributing the font, this release embeds no fonts at all, and the font's
/// own embedding permissions (OpenType <c>OS/2</c> <c>fsType</c>) are a write-side
/// obligation that arises only when a writer gains embedding (IP-012).
/// </para>
/// <para>
/// <strong>Which formats.</strong> sfnt-shaped programs only: <c>FontFile2</c>,
/// and <c>FontFile3</c> with an <c>OpenType</c> subtype. Type 1 (<c>FontFile</c>)
/// and bare CFF (<c>FontFile3</c> with <c>Type1C</c> or <c>CIDFontType0C</c>)
/// carry their glyph names in structures the composed parser does not expose —
/// it is a renderer, and a renderer never needs to know what a glyph is called.
/// Those return null and keep reporting as uninspected, which is honest: the
/// limit is the parser's surface, not the register.
/// </para>
/// </remarks>
public sealed class GraphicsFontProgramReader : IPdfFontProgramReader
{
    /// <summary>Lowest code point probed when inverting the character map.</summary>
    private const int FirstCodepoint = 0x0020;

    /// <summary>Highest code point probed: the end of the Basic Multilingual Plane.</summary>
    private const int LastCodepoint = 0xFFFF;

    /// <summary>Surrogates are not characters; probing them would be meaningless.</summary>
    private const int FirstSurrogate = 0xD800;

    private const int LastSurrogate = 0xDFFF;

    /// <summary>
    /// How many glyphs one program may contribute. Well past a full CJK face, and
    /// a hard stop on a program that claims more.
    /// </summary>
    private const int MaxGlyphs = 65_536;

    /// <summary>How often the probe loop checks for cancellation.</summary>
    private const int CancellationCheckMask = 0xFFF;

    public PdfFontProgramMap? Read(
        ReadOnlySpan<byte> program,
        string descriptorKey,
        string? subtype,
        PdfFontProgramContext context)
    {
        ArgumentNullException.ThrowIfNull(descriptorKey);
        ArgumentNullException.ThrowIfNull(context);

        context.CancellationToken.ThrowIfCancellationRequested();

        string? format = SfntFormat(descriptorKey, subtype);
        if (format is null || program.Length == 0 || program.Length > context.MaxBytes)
            return null;

        // The guard has to cover the probing as well as the load. The composed
        // parser builds its character map lazily, on the first lookup, so a
        // malformed cmap table faults inside BuildGlyphText and not inside Load —
        // which is exactly the shape of bug a boundary like this exists to stop
        // reaching the caller.
        try
        {
            TrueTypeFont? font = TrueTypeFont.Load(program.ToArray());
            return font is null ? null : new PdfFontProgramMap(format, BuildGlyphText(font, context));
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
        {
            // The boundary between an untrusted document and a parser written for
            // trusted system fonts. A malformed program costs this font's text,
            // never the read.
            return null;
        }
    }

    /// <summary>The sfnt format this descriptor key names, or null when it is not one.</summary>
    private static string? SfntFormat(string descriptorKey, string? subtype) => descriptorKey switch
    {
        "FontFile2" => "TrueType",
        "FontFile3" when string.Equals(subtype, "OpenType", StringComparison.Ordinal) => "OpenType",
        _ => null,
    };

    /// <summary>
    /// Inverts the program's character map into glyph-to-text by probing it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The composed parser answers "which glyph draws this character", which is
    /// the question a renderer asks. Extraction asks the opposite one, and the
    /// parser exposes no way to enumerate the map, so the inverse is built by
    /// asking the forward question once per code point in the BMP.
    /// </para>
    /// <para>
    /// It is a bounded loop over a fixed range — about sixty-five thousand
    /// dictionary lookups, once per font, and never a function of the document's
    /// size. Crude, and honest about being crude: the alternative is a second
    /// font parser, and this repository would rather probe a reviewed one than
    /// write an unreviewed one.
    /// </para>
    /// <para>
    /// The lowest code point mapping to a glyph wins, so a glyph reachable as both
    /// <c>A</c> and a compatibility form resolves to <c>A</c>.
    /// </para>
    /// </remarks>
    private static Dictionary<int, string> BuildGlyphText(TrueTypeFont font, PdfFontProgramContext context)
    {
        var glyphText = new Dictionary<int, string>();

        for (int codepoint = FirstCodepoint; codepoint <= LastCodepoint; codepoint++)
        {
            if ((codepoint & CancellationCheckMask) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            if (codepoint is >= FirstSurrogate and <= LastSurrogate)
                continue;

            int glyph = font.GetGlyphIndex(codepoint);
            if (glyph <= 0 || glyphText.ContainsKey(glyph))
                continue;

            glyphText[glyph] = char.ConvertFromUtf32(codepoint);
            if (glyphText.Count >= MaxGlyphs)
                break;
        }

        return glyphText;
    }
}
