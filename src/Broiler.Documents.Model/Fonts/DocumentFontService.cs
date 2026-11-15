using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Broiler.Graphics.Text;

namespace Broiler.Documents.Model.Fonts;

/// <summary>
/// Default implementation of an operation-scoped font service.
/// Provides explicit font resolution, directory scanning with exact name tables,
/// fallback tracking, and scoped caching.
/// </summary>
public sealed class DocumentFontService : IOperationFontService
{
    private static readonly string[] FontExtensions = [".ttf", ".otf", ".ttc", ".otc"];

    private readonly Dictionary<string, FaceProgramSet> _explicit = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FaceProgramSet> _directoryScanned = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<FontFaceKey, (ResolvedFontFace Face, FontResolutionOutcome Outcome)> _cache = new();
    private readonly HashSet<string> _unmapped = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _notes = [];
    private readonly List<FontResolutionRecord> _records = [];
    private string? _hostFallbackFile;
    private bool _disposed;

    public DocumentFontService(string? hostFallbackFile = null)
    {
        _hostFallbackFile = hostFallbackFile;
    }

    public IReadOnlyList<string> MappedFamilies =>
        _explicit.Keys.Concat(_directoryScanned.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(k => k, StringComparer.Ordinal).ToList();

    public IReadOnlyList<string> UnmappedRequests =>
        _unmapped.OrderBy(name => name, StringComparer.Ordinal).ToList();

    public IReadOnlyList<string> Notes => _notes;

    public IReadOnlyList<FontResolutionRecord> Records => _records;

    /// <summary>Sets or updates the host fallback face file.</summary>
    public void SetHostFallback(string? path)
    {
        _hostFallbackFile = !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
    }

    /// <summary>Explicitly maps a font file for a family and optional face style.</summary>
    public void AddFontFile(string family, string filePath, bool? bold = null, bool? italic = null)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Font file not found: " + filePath, filePath);

        FaceProgramSet set = GetOrAddExplicit(family);
        FontProgramSource source = FontProgramSource.FromFile(filePath);

        if (bold.HasValue || italic.HasValue)
            set.Set(bold ?? false, italic ?? false, source);
        else
            set.SetIfAbsent(false, false, source);

        _notes.Add($"mapped {family} (bold={bold}, italic={italic}) to {filePath}");
    }

    /// <summary>Explicitly maps in-memory font program bytes for a family and optional style.</summary>
    public void AddFontProgram(string family, ReadOnlyMemory<byte> programBytes, bool? bold = null, bool? italic = null, string? subfamily = null)
    {
        ArgumentNullException.ThrowIfNull(family);
        if (programBytes.Length == 0)
            throw new ArgumentException("Font program bytes cannot be empty.", nameof(programBytes));

        FaceProgramSet set = GetOrAddExplicit(family);
        FontProgramSource source = FontProgramSource.FromBytes(programBytes, family, subfamily ?? (bold == true ? (italic == true ? "Bold Italic" : "Bold") : (italic == true ? "Italic" : "Regular")));

        if (bold.HasValue || italic.HasValue)
            set.Set(bold ?? false, italic ?? false, source);
        else
            set.SetIfAbsent(false, false, source);

        _notes.Add($"mapped {family} in-memory program ({programBytes.Length} bytes)");
    }

    /// <summary>
    /// Scans a directory and indexes font files by reading each file's exact sfnt name table.
    /// </summary>
    public int AddFontDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return 0;

        string[] files = [.. Directory
            .EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
            .Where(path => FontExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)];

        int count = 0;
        foreach (string file in files)
        {
            FontMetadataInfo? meta = FontMetadataReader.ReadFromFile(file);
            if (meta is null || string.IsNullOrWhiteSpace(meta.Family))
                continue;

            FaceProgramSet set = GetOrAddDirectory(meta.Family);
            FontProgramSource source = FontProgramSource.FromFile(file, meta);
            set.SetIfAbsent(meta.IsBold, meta.IsItalic, source);
            count++;

            // If TTC collection, also index further faces
            for (int i = 1; i < meta.TotalFacesInCollection; i++)
            {
                FontMetadataInfo? faceMeta = FontMetadataReader.ReadFromFile(file, i);
                if (faceMeta is not null && !string.IsNullOrWhiteSpace(faceMeta.Family))
                {
                    FaceProgramSet colSet = GetOrAddDirectory(faceMeta.Family);
                    colSet.SetIfAbsent(faceMeta.IsBold, faceMeta.IsItalic, FontProgramSource.FromFile(file, faceMeta, i));
                }
            }
        }

        _notes.Add($"scanned font directory {directoryPath}: indexed {count} face files");
        return count;
    }

    public ResolvedFontFace? ResolveFace(string? family, bool bold = false, bool italic = false) =>
        ResolveWithOutcome(family, bold, italic).Face;

    public (ResolvedFontFace? Face, FontResolutionOutcome Outcome) ResolveWithOutcome(string? family, bool bold = false, bool italic = false)
    {
        ThrowIfDisposed();

        string keyFamily = string.IsNullOrWhiteSpace(family) ? "sans-serif" : family.Trim();
        var cacheKey = new FontFaceKey(keyFamily, bold, italic);

        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return (cached.Face, cached.Outcome);
        }

        // 1. Check explicit mappings
        if (_explicit.TryGetValue(keyFamily, out FaceProgramSet? explicitFaces))
        {
            FontProgramSource? source = explicitFaces.Pick(bold, italic, out bool styleMatched);
            if (source is not null)
            {
                ResolvedFontFace face = CreateFace(source, bold, italic);
                var outcome = styleMatched ? FontResolutionOutcome.ExactMatch : FontResolutionOutcome.StyleMatch;
                _cache[cacheKey] = (face, outcome);
                Record(keyFamily, bold, italic, outcome, face, $"Explicit mapping from {source.Description}");
                return (face, outcome);
            }
        }

        // 2. Check directory scanned fonts
        if (_directoryScanned.TryGetValue(keyFamily, out FaceProgramSet? dirFaces))
        {
            FontProgramSource? source = dirFaces.Pick(bold, italic, out _);
            if (source is not null)
            {
                ResolvedFontFace face = CreateFace(source, bold, italic);
                var outcome = FontResolutionOutcome.DirectoryMatch;
                _cache[cacheKey] = (face, outcome);
                Record(keyFamily, bold, italic, outcome, face, $"Discovered from {source.Description}");
                return (face, outcome);
            }
        }

        // 3. Mark unmapped
        _unmapped.Add(keyFamily);

        // 4. Try host fallback
        if (!string.IsNullOrEmpty(_hostFallbackFile) && File.Exists(_hostFallbackFile))
        {
            FontProgramSource fallbackSource = FontProgramSource.FromFile(_hostFallbackFile);
            ResolvedFontFace face = CreateFace(fallbackSource, bold, italic);
            var outcome = FontResolutionOutcome.HostFallback;
            _cache[cacheKey] = (face, outcome);
            Record(keyFamily, bold, italic, outcome, face, $"Host fallback {_hostFallbackFile}");
            return (face, outcome);
        }

        // 5. Synthetic / Approximate model fallback
        ResolvedFontFace approxFace = CreateApproximateFace(keyFamily, bold, italic);
        _cache[cacheKey] = (approxFace, FontResolutionOutcome.ApproximateModel);
        Record(keyFamily, bold, italic, FontResolutionOutcome.ApproximateModel, approxFace, "Approximate metric model fallback");
        return (approxFace, FontResolutionOutcome.ApproximateModel);
    }

    public bool HasItalicFace(string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
            return false;

        string trimmed = family.Trim();
        if (_explicit.TryGetValue(trimmed, out var explicitFaces) && (explicitFaces.Italic is not null || explicitFaces.BoldItalic is not null))
            return true;

        if (_directoryScanned.TryGetValue(trimmed, out var dirFaces) && (dirFaces.Italic is not null || dirFaces.BoldItalic is not null))
            return true;

        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cache.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DocumentFontService));
    }

    private FaceProgramSet GetOrAddExplicit(string family)
    {
        if (!_explicit.TryGetValue(family, out FaceProgramSet? set))
        {
            set = new FaceProgramSet();
            _explicit[family] = set;
        }

        return set;
    }

    private FaceProgramSet GetOrAddDirectory(string family)
    {
        if (!_directoryScanned.TryGetValue(family, out FaceProgramSet? set))
        {
            set = new FaceProgramSet();
            _directoryScanned[family] = set;
        }

        return set;
    }

    private void Record(string family, bool bold, bool italic, FontResolutionOutcome outcome, ResolvedFontFace? face, string details)
    {
        _records.Add(new FontResolutionRecord(family, bold, italic, outcome, face, details));
    }

    private static ResolvedFontFace CreateFace(FontProgramSource source, bool requestedBold, bool requestedItalic)
    {
        TrueTypeFont? trueType = null;
        FontMetadataInfo? metadata = source.Metadata;

        if (source.FilePath is not null)
        {
            try
            {
                trueType = TrueTypeFont.LoadFromFile(source.FilePath);
            }
            catch
            {
                // Non-sfnt or load error
            }

            metadata ??= FontMetadataReader.ReadFromFile(source.FilePath, source.FaceIndex);
        }
        else if (!source.ProgramBytes.IsEmpty)
        {
            metadata ??= FontMetadataReader.ReadFromBytes(source.ProgramBytes, source.FaceIndex);
        }

        metadata ??= new FontMetadataInfo(
            Family: source.Family,
            Subfamily: requestedBold ? (requestedItalic ? "Bold Italic" : "Bold") : (requestedItalic ? "Italic" : "Regular"),
            FullName: null,
            PostScriptName: null,
            UnitsPerEm: 1000,
            TypographicAscender: 800,
            TypographicDescender: -200,
            TypographicLineGap: 0,
            WinAscent: 800,
            WinDescent: 200,
            CapHeight: 700,
            XHeight: 500,
            UnderlinePosition: -100,
            UnderlineThickness: 50,
            EmbeddingRights: BFontEmbeddingRights.FromFsType(0),
            IsBold: requestedBold,
            IsItalic: requestedItalic,
            FaceIndex: source.FaceIndex);

        FontFaceIdentity identity = source.FilePath is not null
            ? FontFaceIdentity.FromFilePath(source.FilePath, metadata.Family, metadata.Subfamily, source.FaceIndex, requestedBold, requestedItalic)
            : FontFaceIdentity.FromProgramBytes(source.ProgramBytes.Span, metadata.Family, metadata.Subfamily, source.FaceIndex, requestedBold, requestedItalic);

        return new ResolvedFontFace(identity, metadata, trueType, source.FilePath, source.ProgramBytes);
    }

    private static ResolvedFontFace CreateApproximateFace(string family, bool bold, bool italic)
    {
        var identity = FontFaceIdentity.ForFallback(family, bold, italic);
        var metadata = new FontMetadataInfo(
            Family: family,
            Subfamily: bold ? (italic ? "Bold Italic" : "Bold") : (italic ? "Italic" : "Regular"),
            FullName: null,
            PostScriptName: null,
            UnitsPerEm: 1000,
            TypographicAscender: 800,
            TypographicDescender: -200,
            TypographicLineGap: 0,
            WinAscent: 800,
            WinDescent: 200,
            CapHeight: 700,
            XHeight: 500,
            UnderlinePosition: -100,
            UnderlineThickness: 50,
            EmbeddingRights: BFontEmbeddingRights.FromFsType(0),
            IsBold: bold,
            IsItalic: italic);

        return new ResolvedFontFace(identity, metadata);
    }

    private readonly record struct FontFaceKey(string Family, bool Bold, bool Italic);

    private sealed class FontProgramSource
    {
        public string? FilePath { get; private init; }
        public ReadOnlyMemory<byte> ProgramBytes { get; private init; }
        public string Family { get; private init; } = string.Empty;
        public int FaceIndex { get; private init; }
        public FontMetadataInfo? Metadata { get; private init; }

        public string Description => FilePath ?? $"Memory program ({ProgramBytes.Length} bytes)";

        public static FontProgramSource FromFile(string path, FontMetadataInfo? metadata = null, int faceIndex = 0) =>
            new() { FilePath = path, Family = metadata?.Family ?? Path.GetFileNameWithoutExtension(path), Metadata = metadata, FaceIndex = faceIndex };

        public static FontProgramSource FromBytes(ReadOnlyMemory<byte> bytes, string family, string subfamily = "Regular", int faceIndex = 0) =>
            new() { ProgramBytes = bytes, Family = family, FaceIndex = faceIndex };
    }

    private sealed class FaceProgramSet
    {
        public FontProgramSource? Regular;
        public FontProgramSource? Bold;
        public FontProgramSource? Italic;
        public FontProgramSource? BoldItalic;

        public FontProgramSource? Pick(bool bold, bool italic, out bool exactMatch)
        {
            exactMatch = true;
            if (bold && italic)
            {
                if (BoldItalic is not null) return BoldItalic;
                exactMatch = false;
                return Bold ?? Italic ?? Regular;
            }
            if (bold)
            {
                if (Bold is not null) return Bold;
                exactMatch = false;
                return Regular ?? BoldItalic ?? Italic;
            }
            if (italic)
            {
                if (Italic is not null) return Italic;
                exactMatch = false;
                return Regular ?? BoldItalic ?? Bold;
            }

            if (Regular is not null) return Regular;
            exactMatch = false;
            return Bold ?? Italic ?? BoldItalic;
        }

        public void Set(bool bold, bool italic, FontProgramSource source)
        {
            if (bold && italic) BoldItalic = source;
            else if (bold) Bold = source;
            else if (italic) Italic = source;
            else Regular = source;
        }

        public void SetAll(FontProgramSource source)
        {
            Regular = source;
            Bold = source;
            Italic = source;
            BoldItalic = source;
        }

        public void SetIfAbsent(bool bold, bool italic, FontProgramSource source)
        {
            if (bold && italic) BoldItalic ??= source;
            else if (bold) Bold ??= source;
            else if (italic) Italic ??= source;
            else Regular ??= source;
        }
    }
}
