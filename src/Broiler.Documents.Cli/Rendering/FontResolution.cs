using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Broiler.Documents.Cli.Infrastructure;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.Text;
using Broiler.Documents.Model.Fonts;
using Broiler.Documents.Pagination.Fonts;

namespace Broiler.Documents.Cli.Rendering;

/// <summary>
/// Decides which font file each family draws with, and records the decision.
/// </summary>
/// <remarks>
/// <para>
/// This is the single largest source of render drift between two machines, so it
/// is explicit rather than ambient. Broiler.Graphics resolves a family through
/// <see cref="BSystemFontFiles"/> when a resolver is installed and otherwise
/// draws <em>every</em> family with one host face - which is fine for a preview
/// and useless for a comparison, because two boxes with different font sets then
/// disagree about a document neither of them got wrong.
/// </para>
/// <para>
/// A caller that needs matching output across machines passes
/// <c>--font-file</c> or <c>--font-dir</c> and pins the faces. A caller that just
/// wants to look at a page passes nothing and gets the host's fonts. Either way
/// the resolved mapping is reported, so a diff that turns out to be a font
/// difference is identifiable as one instead of being mistaken for a codec gap.
/// </para>
/// <para>
/// The resolver must be installed before anything measures or draws: the
/// graphics layer caches the face it resolves per (family, bold, italic) exactly
/// so measurement and drawing cannot disagree, and that cache is process-wide.
/// </para>
/// </remarks>
public sealed class FontResolution
{
    private static readonly string[] FontExtensions = [".ttf", ".otf", ".ttc"];

    private static readonly string[] FaceMarkers = ["BoldItalic", "BoldOblique", "Bold", "Italic", "Oblique", "Regular", "Book"];

    private readonly Dictionary<string, FontFaceSet> _families;
    private readonly List<string> _notes = [];
    private readonly HashSet<string> _unmapped = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TrueTypeFont?> _metricFonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int?> _metricLineGaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _hostFallback;
    private readonly DocumentFontService _fontService;
    private readonly PaginationFontAdapter _textMeasurer;

    private FontResolution(Dictionary<string, FontFaceSet> families, string? hostFallback)
    {
        _families = families;
        _hostFallback = hostFallback;
        _fontService = new DocumentFontService(hostFallback);
        _textMeasurer = new PaginationFontAdapter(_fontService);
    }

    /// <summary>True when at least one family was pinned to a file.</summary>
    /// <summary>The operation-scoped font service.</summary>
    public IOperationFontService FontService => _fontService;

    /// <summary>The operation-scoped text measurer adapter.</summary>
    public IDocumentTextMeasurer TextMeasurer => _textMeasurer;

    public bool HasMappings => _families.Count > 0;

    /// <summary>Families that were explicitly mapped, ordered for a stable report.</summary>
    public IReadOnlyList<string> MappedFamilies => [.. _families.Keys.OrderBy(name => name, StringComparer.Ordinal)];

    /// <summary>What happened while building the mapping, for the render manifest.</summary>
    public IReadOnlyList<string> Notes => _notes;

    /// <summary>Families a document asked for that had no mapping, so fell back to the host face.</summary>
    public IReadOnlyList<string> UnmappedRequests => [.. _unmapped.OrderBy(name => name, StringComparer.Ordinal)];

    /// <summary>
    /// Builds a mapping from <c>--font-file family=path</c> and <c>--font-dir</c>
    /// options. An empty mapping is legal and means "use whatever the host has".
    /// </summary>
    public static FontResolution FromCommandLine(CommandLine line) => FromCommandLine(line, HostFallbackFile());

    /// <summary>
    /// Builds a mapping as <see cref="FromCommandLine(CommandLine)"/> does, with
    /// <paramref name="hostFallback"/> standing for the file the host draws an
    /// unmapped family with.
    /// </summary>
    internal static FontResolution FromCommandLine(CommandLine line, string? hostFallback)
    {
        ArgumentNullException.ThrowIfNull(line);

        var resolution = new FontResolution(
            new Dictionary<string, FontFaceSet>(StringComparer.OrdinalIgnoreCase),
            hostFallback);

        foreach (string directory in line.GetAll("font-dir"))
            resolution.AddDirectory(directory);

        // Explicit files last, so they win over anything a scanned directory
        // guessed from a filename.
        foreach (string mapping in line.GetAll("font-file"))
            resolution.AddMapping(mapping);

        return resolution;
    }

    /// <summary>
    /// Installs this mapping process-wide. Call once, before any layout runs.
    /// An empty mapping clears any previous resolver rather than leaving a stale
    /// one behind.
    /// </summary>
    public void Install()
    {
        if (_families.Count == 0)
        {
            BSystemFontFiles.Clear();
            return;
        }

        BSystemFontFiles.Use(TryResolve);
    }

    /// <summary>A stable description of the mapping, for the manifest and for <c>--verbose</c>.</summary>
    public IReadOnlyList<string> Describe()
    {
        var lines = new List<string>();
        foreach (string family in MappedFamilies)
        {
            FontFaceSet faces = _families[family];
            lines.Add($"{family} -> regular={Describe(faces.Regular)} bold={Describe(faces.Bold)} italic={Describe(faces.Italic)} bold-italic={Describe(faces.BoldItalic)}");
        }

        return lines;
    }

    /// <summary>
    /// True when a genuine italic (or bold-italic) face is mapped for this
    /// family, so the renderer will draw real italic outlines rather than the
    /// upright ones.
    /// </summary>
    /// <remarks>
    /// Broiler.Graphics does not synthesize a slant: with no italic face mapped,
    /// an italic run is drawn in the upright face and is indistinguishable from
    /// the text around it. That matters here more than it would in a preview - a
    /// codec that silently drops italic would produce an identical picture, and
    /// the comparison would report a pass it did not earn. The layout uses this
    /// to decide whether to shear the glyphs itself.
    /// </remarks>
    public bool HasItalicFace(string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
            return false;

        return _families.TryGetValue(family, out FontFaceSet? faces) &&
            (faces.Italic is not null || faces.BoldItalic is not null);
    }

    /// <summary>The face Broiler.Graphics falls back to when a family is not mapped.</summary>
    public static string DescribeHostFallback() => BImageRenderer.DescribeSystemTextFont();

    /// <summary>
    /// Vertical metrics from the face a run is drawn with, or null when no face
    /// file can be named.
    /// </summary>
    /// <remarks>
    /// A family with no mapping is drawn with the host's fallback face, so that
    /// face is the one measured. Leaving it unmeasured gave every such run the
    /// layout's estimate - 80% ascent and a rounded 125% line, 13 points at 10 -
    /// while the glyphs were the fallback's: DejaVu Sans on the conformance host,
    /// whose line at 10 points is 11.64. A document set in a family the mapping
    /// does not name, such as Arial Narrow, ran a tenth longer than its text.
    /// </remarks>
    public (double Ascent, double Descent)? VerticalMetrics(BFontStyle font)
    {
        if (MetricPath(font) is not string path)
            return null;
        TrueTypeFont? face = MetricFace(path);
        if (face is null || face.UnitsPerEm <= 0 || face.Ascender <= 0 || face.Descender > 0)
            return null;
        double scale = font.Size / face.UnitsPerEm;
        return (face.Ascender * scale, -face.Descender * scale);
    }

    /// <summary>The hhea line gap from the face a run is drawn with, in points.</summary>
    public double? LineGap(BFontStyle font)
    {
        if (MetricPath(font) is not string path)
            return null;

        TrueTypeFont? face = MetricFace(path);
        if (face is null || face.UnitsPerEm <= 0)
            return null;
        if (!_metricLineGaps.TryGetValue(path, out int? lineGap))
        {
            lineGap = ReadHheaLineGap(path);
            _metricLineGaps[path] = lineGap;
        }

        return lineGap is >= 0 ? lineGap.Value * font.Size / face.UnitsPerEm : null;
    }

    /// <summary>
    /// The file a run in <paramref name="font"/> is drawn with: its mapped face,
    /// or the host fallback when the family has none.
    /// </summary>
    /// <remarks>
    /// The fallback is one face for every weight and slant, as the renderer's
    /// own fallback is, so a bold run in an unmapped family is measured with the
    /// regular face. DejaVu Sans states the same ascent and descent for both.
    /// </remarks>
    internal string? MetricPath(BFontStyle font) =>
        TryResolve(font.FamilyName, font.Weight >= BFontWeight.Bold,
            font.Slant is BFontSlant.Italic or BFontSlant.Oblique, out string path)
            ? path
            : _hostFallback;

    /// <summary>
    /// The file the host draws an unmapped family with, or null when the renderer
    /// names none that exists - a host with no fonts draws with a built-in face
    /// that has no file to measure.
    /// </summary>
    private static string? HostFallbackFile()
    {
        string described = DescribeHostFallback();
        return File.Exists(described) ? described : null;
    }

    private TrueTypeFont? MetricFace(string path)
    {
        if (!_metricFonts.TryGetValue(path, out TrueTypeFont? face))
        {
            face = TrueTypeFont.LoadFromFile(path);
            _metricFonts[path] = face;
        }

        return face;
    }

    /// <summary>Reads the signed lineGap field from the first face's hhea table.</summary>
    internal static int? ReadHheaLineGap(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            long faceOffset = 0;
            uint signature = ReadUInt32BigEndian(reader);
            if (signature == 0x74746366) // ttcf
            {
                _ = ReadUInt32BigEndian(reader); // collection version
                uint faceCount = ReadUInt32BigEndian(reader);
                if (faceCount == 0)
                    return null;
                faceOffset = ReadUInt32BigEndian(reader);
                if (faceOffset < 0 || faceOffset > stream.Length - 12)
                    return null;
                stream.Position = faceOffset;
                _ = ReadUInt32BigEndian(reader);
            }

            ushort tableCount = ReadUInt16BigEndian(reader);
            if (tableCount > 4096 || faceOffset > stream.Length - 12)
                return null;
            stream.Position = faceOffset + 12;

            for (int i = 0; i < tableCount; i++)
            {
                uint tag = ReadUInt32BigEndian(reader);
                _ = ReadUInt32BigEndian(reader); // checksum
                uint offset = ReadUInt32BigEndian(reader);
                uint length = ReadUInt32BigEndian(reader);
                if (tag != 0x68686561) // hhea
                    continue;
                if (length < 10 || offset > stream.Length - 10)
                    return null;
                stream.Position = offset + 8;
                return unchecked((short)ReadUInt16BigEndian(reader));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    private static ushort ReadUInt16BigEndian(BinaryReader reader) =>
        (ushort)((reader.ReadByte() << 8) | reader.ReadByte());

    private static uint ReadUInt32BigEndian(BinaryReader reader) =>
        ((uint)reader.ReadByte() << 24) |
        ((uint)reader.ReadByte() << 16) |
        ((uint)reader.ReadByte() << 8) |
        reader.ReadByte();

    private static string Describe(string? path) => path ?? "(none)";

    private bool TryResolve(string? family, bool bold, bool italic, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(family))
            return false;

        if (!_families.TryGetValue(family, out FontFaceSet? faces))
        {
            _unmapped.Add(family);
            return false;
        }

        string? resolved = faces.Pick(bold, italic);
        if (resolved is null)
            return false;

        path = resolved;
        return true;
    }

    private void AddMapping(string mapping)
    {
        int separator = mapping.IndexOf('=');
        if (separator <= 0 || separator == mapping.Length - 1)
        {
            throw new UsageException(
                "--font-file expects family=path, not " + Quote(mapping) +
                ". Add :bold, :italic, or :bolditalic to the family to map a single face.");
        }

        string familyToken = mapping[..separator].Trim();
        string path = mapping[(separator + 1)..].Trim();

        if (!File.Exists(path))
            throw new UsageException("Font file not found: " + path);

        (string family, bool bold, bool italic, bool explicitFace) = SplitFaceToken(familyToken);
        if (family.Length == 0)
            throw new UsageException("--font-file expects family=path, not " + Quote(mapping) + ".");

        FontFaceSet faces = GetOrAdd(family);
        if (explicitFace)
        {
            faces.Set(bold, italic, path);
            _fontService.AddFontFile(family, path, bold, italic);
        }
        else
        {
            faces.SetIfAbsent(false, false, path);
            _fontService.AddFontFile(family, path);
        }

        _notes.Add("mapped " + familyToken + " to " + path);
    }

    private void AddDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            throw new UsageException("Font directory not found: " + directory);

        string[] files = [.. Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => FontExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            // Ordinal, so two machines that enumerate in different orders still
            // build the same mapping from the same directory.
            .OrderBy(path => path, StringComparer.Ordinal)];

        int mapped = 0;
        foreach (string file in files)
        {
            string family = string.Empty;
            bool bold = false;
            bool italic = false;
            FontMetadataInfo? meta = FontMetadataReader.ReadFromFile(file);
            if (meta is not null && !string.IsNullOrWhiteSpace(meta.Family))
            {
                family = meta.Family;
                bold = meta.IsBold;
                italic = meta.IsItalic;
            }
            else
            {
                (family, bold, italic) = GuessFace(Path.GetFileNameWithoutExtension(file));
            }
            if (family.Length == 0)
                continue;

            GetOrAdd(family).SetIfAbsent(bold, italic, file);
            _fontService.AddFontFile(family, file, bold, italic);
            mapped++;
        }

        _notes.Add($"scanned {directory}: {files.Length} font file(s), {mapped} face(s) mapped");
    }

    private FontFaceSet GetOrAdd(string family)
    {
        if (!_families.TryGetValue(family, out FontFaceSet? faces))
        {
            faces = new FontFaceSet();
            _families[family] = faces;
        }

        return faces;
    }

    private static string Quote(string value) => "\"" + value + "\"";

    /// <summary>Splits a token such as <c>Calibri:bold</c> into a family and the face it names.</summary>
    private static (string Family, bool Bold, bool Italic, bool Explicit) SplitFaceToken(string token)
    {
        int colon = token.LastIndexOf(':');
        if (colon <= 0)
            return (token, false, false, false);

        string suffix = token[(colon + 1)..].Trim().ToLowerInvariant();
        return suffix switch
        {
            "regular" => (token[..colon].Trim(), false, false, true),
            "bold" => (token[..colon].Trim(), true, false, true),
            "italic" => (token[..colon].Trim(), false, true, true),
            "bolditalic" => (token[..colon].Trim(), true, true, true),
            // Anything else is part of the family name. A family legitimately can
            // contain a colon, and guessing wrong would silently drop the mapping.
            _ => (token, false, false, false),
        };
    }

    /// <summary>Reads a family and a face out of a font file's name.</summary>
    /// <remarks>
    /// Filename parsing is a heuristic and is treated as one: it only ever fills
    /// a face nothing else claimed, so an explicit <c>--font-file</c> always
    /// wins. Reading the family from the font's own name table belongs behind
    /// the Graphics font inspector the PDF roadmap 6.5 tracks, not in a filename
    /// parser here.
    /// </remarks>
    private static (string Family, bool Bold, bool Italic) GuessFace(string fileName)
    {
        string name = fileName.Replace('_', '-');
        bool bold = false;
        bool italic = false;

        foreach (string marker in FaceMarkers)
        {
            int index = name.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                continue;

            bold |= marker.StartsWith("Bold", StringComparison.Ordinal);
            italic |= marker.Contains("Italic", StringComparison.Ordinal) ||
                marker.Contains("Oblique", StringComparison.Ordinal);
            name = name.Remove(index, marker.Length);
        }

        return (name.Trim('-', ' ', '.'), bold, italic);
    }

    /// <summary>The four faces of one family, each optional.</summary>
    private sealed class FontFaceSet
    {
        public string? Regular { get; private set; }

        public string? Bold { get; private set; }

        public string? Italic { get; private set; }

        public string? BoldItalic { get; private set; }

        public void SetAll(string path)
        {
            Regular = path;
            Bold = path;
            Italic = path;
            BoldItalic = path;
        }

        public void Set(bool bold, bool italic, string path)
        {
            if (bold && italic)
                BoldItalic = path;
            else if (bold)
                Bold = path;
            else if (italic)
                Italic = path;
            else
                Regular = path;
        }

        public void SetIfAbsent(bool bold, bool italic, string path)
        {
            if (bold && italic)
                BoldItalic ??= path;
            else if (bold)
                Bold ??= path;
            else if (italic)
                Italic ??= path;
            else
                Regular ??= path;
        }

        /// <summary>
        /// The best available face for a weight and slant. Falling back to the
        /// regular face is deliberate: drawing bold text in the regular face is
        /// wrong but recoverable, while returning nothing sends the family to a
        /// completely different host font and moves every glyph on the line.
        /// </summary>
        public string? Pick(bool bold, bool italic) => (bold, italic) switch
        {
            (true, true) => BoldItalic ?? Bold ?? Italic ?? Regular,
            (true, false) => Bold ?? Regular ?? BoldItalic,
            (false, true) => Italic ?? Regular ?? BoldItalic,
            _ => Regular ?? Bold ?? Italic ?? BoldItalic,
        };
    }
}
