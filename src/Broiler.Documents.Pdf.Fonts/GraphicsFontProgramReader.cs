using System;
using System.Collections.Generic;
using Broiler.Documents.Pdf.Text;
using Broiler.Graphics.Text;

namespace Broiler.Documents.Pdf.Fonts;

/// <summary>
/// Reads embedded sfnt font programs — TrueType and OpenType — through the
/// read-safe inspector in <c>Broiler.Graphics</c>. Not composed by default: a
/// caller opts in by putting it into <see cref="PdfCodecServices"/>.
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
/// and <c>FontFile3</c> with an <c>OpenType</c> subtype, and of those only what
/// <see cref="BFontProgramInspector"/>'s pinned tuple accepts — no WOFF, no font
/// collection, no variable font, no CFF2, no colour or bitmap glyphs, no
/// Graphite or AAT. Type 1 (<c>FontFile</c>) and bare CFF (<c>FontFile3</c> with
/// <c>Type1C</c> or <c>CIDFontType0C</c>) carry their glyph names in structures
/// the inspector does not read; those return null and keep reporting as
/// uninspected, which is honest.
/// </para>
/// <para>
/// <strong>Why the inspector and not the renderer's parser.</strong> This reads
/// a program that arrived inside somebody else's document. The renderer's parser
/// is written for fonts a caller provisioned and repairs what it can — it follows
/// a WOFF container, takes the first face of a collection, and reads a short
/// table as zeros so a slightly wrong font still draws. Every one of those turns
/// a malformed program into plausible output instead of a refusal, which is the
/// wrong trade on this side of the boundary (PDF roadmap §6.5).
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
        PdfFontProgramContext context) =>
        Read(program, descriptorKey, subtype, context, out _);

    public PdfFontProgramMap? Read(
        ReadOnlySpan<byte> program,
        string descriptorKey,
        string? subtype,
        PdfFontProgramContext context,
        out string? declined)
    {
        ArgumentNullException.ThrowIfNull(descriptorKey);
        ArgumentNullException.ThrowIfNull(context);
        declined = null;

        context.CancellationToken.ThrowIfCancellationRequested();

        if (program.Length == 0 || program.Length > context.MaxBytes)
        {
            declined = program.Length == 0 ? "the program is empty" : "the program is past the read's font ceiling";
            return null;
        }

        // A bare CFF names its glyphs rather than mapping them from characters,
        // so it is read for names and the codec decides what they say.
        if (CffFormat(descriptorKey, subtype) is string cff)
        {
            if (CffGlyphNames.Read(program, context.CancellationToken) is IReadOnlyDictionary<int, string> names)
                return new PdfFontProgramMap(cff, new Dictionary<int, string>(), names);

            declined = "the CFF charset could not be read";
            return null;
        }

        string? format = SfntFormat(descriptorKey, subtype);
        if (format is null)
        {
            declined = string.Equals(descriptorKey, "FontFile", StringComparison.Ordinal)
                ? "it is a Type 1 program, which this reader does not parse"
                : "it is not a format this reader parses";
            return null;
        }

        var limits = new BFontInspectionLimits
        {
            // Clamped rather than cast: the codec states its budget in long, and a
            // value past int.MaxValue would wrap into a small one that refuses
            // every program. A font program never approaches either number.
            MaxBytes = (int)Math.Min(context.MaxBytes, int.MaxValue),
            MaxMappings = MaxGlyphs,
        };

        // A refusal is the expected outcome for a malformed or out-of-tuple
        // program, not an exception to catch: the inspector validates rather than
        // repairs, so there is nothing here to fall over. The catch stays anyway,
        // because a boundary that relies on a parser never having a bug is not a
        // boundary — it just costs this font's text rather than the read.
        try
        {
            if (!BFontProgramInspector.TryInspect(
                    program, limits, out BFontProgramInspection? font, out BFontProgramRejection rejection))
            {
                declined = Describe(rejection);
                return null;
            }

            return new PdfFontProgramMap(format, BuildGlyphText(font!, context));
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
        {
            declined = "the inspector faulted on it (" + ex.GetType().Name + ")";
            return null;
        }
    }

    /// <summary>
    /// The inspector's refusal as a phrase a reader can act on - or know not to.
    /// </summary>
    /// <remarks>
    /// The distinction that matters most here is <c>NoCharacterMap</c>. A
    /// subsetted symbolic font is routinely shipped without one, because the PDF
    /// maps its codes through the font dictionary's encoding and the program is
    /// never asked what its glyphs spell. Nothing composed into this codec can
    /// recover text a program does not carry, so a host meeting that reason
    /// knows the answer is in the document's ToUnicode map or nowhere - which is
    /// a different fact from a program this reader merely declines to parse.
    /// </remarks>
    private static string Describe(BFontProgramRejection rejection) => rejection switch
    {
        BFontProgramRejection.Empty => "the program is empty",
        BFontProgramRejection.TooLarge => "the program is past the inspector's ceiling",
        BFontProgramRejection.NotSfnt => "the program is not an sfnt",
        BFontProgramRejection.UnsupportedContainer => "the program is in a container this build does not open",
        BFontProgramRejection.MalformedDirectory => "the program's table directory is malformed",
        BFontProgramRejection.TableOutOfBounds => "a table lies outside the program",
        BFontProgramRejection.ExcludedTable => "the program uses a table outside the approved subset",
        BFontProgramRejection.NoCharacterMap => "the program carries no character map, so it says nothing about what its glyphs spell",
        BFontProgramRejection.MalformedCharacterMap => "the program's character map is malformed",
        _ => "the inspector declined it",
    };

    /// <summary>The sfnt format this descriptor key names, or null when it is not one.</summary>
    private static string? SfntFormat(string descriptorKey, string? subtype) => descriptorKey switch
    {
        "FontFile2" => "TrueType",
        "FontFile3" when string.Equals(subtype, "OpenType", StringComparison.Ordinal) => "OpenType",
        _ => null,
    };

    /// <summary>
    /// The bare-CFF subtypes. <c>FontFile</c> is Type 1, which is a different
    /// format with its own eexec-encrypted private structures, and stays
    /// unread — the limit there is still the parser surface, not the register.
    /// </summary>
    private static string? CffFormat(string descriptorKey, string? subtype) =>
        string.Equals(descriptorKey, "FontFile3", StringComparison.Ordinal) &&
        subtype is "Type1C" or "CIDFontType0C"
            ? subtype
            : null;

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
    /// <summary>
    /// What each glyph spells, inverted from the program's character map.
    /// </summary>
    /// <remarks>
    /// The first character to reach a glyph wins. A font that maps several
    /// characters to one glyph — a ligature slot, a duplicated dash — gets the
    /// lowest of them, which is arbitrary but stable, and stability is what
    /// matters for text that has to compare equal across two reads of one file.
    /// </remarks>
    private static Dictionary<int, string> BuildGlyphText(
        BFontProgramInspection font,
        PdfFontProgramContext context)
    {
        var glyphText = new Dictionary<int, string>();
        var lowestCodepoint = new Dictionary<int, int>();

        int seen = 0;
        foreach (KeyValuePair<int, int> mapping in font.Mappings)
        {
            if ((++seen & CancellationCheckMask) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int codepoint = mapping.Key;
            int glyph = mapping.Value;
            if (glyph <= 0 || codepoint < FirstCodepoint)
                continue;

            if (codepoint is >= FirstSurrogate and <= LastSurrogate)
                continue;

            if (lowestCodepoint.TryGetValue(glyph, out int already) && already <= codepoint)
                continue;

            lowestCodepoint[glyph] = codepoint;
            glyphText[glyph] = char.ConvertFromUtf32(codepoint);
        }

        return glyphText;
    }
}
