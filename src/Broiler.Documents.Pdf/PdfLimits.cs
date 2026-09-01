using System;

namespace Broiler.Documents.Pdf;

/// <summary>
/// PDF-specific budgets. Every one is a hard ceiling checked <em>before</em> the
/// allocation or delegated decode it guards, so hostile input is rejected rather
/// than absorbed (PDF roadmap §6.3). Nothing here means "unlimited": a zero is
/// rejected by the constructor.
/// </summary>
/// <remarks>
/// These compose with, and never replace, the format-neutral
/// <see cref="DocumentLimits"/>. Where the two overlap the stricter remaining
/// budget wins, which <see cref="PdfWorkBudget"/> enforces.
/// </remarks>
public sealed class PdfLimits
{
    public const long DefaultMaxInputBytes = 64L * 1024 * 1024;
    public const int DefaultMaxTokenLength = 64 * 1024;
    public const int DefaultMaxObjectCount = 500_000;
    public const int DefaultMaxContainerEntries = 200_000;
    public const int DefaultMaxNestingDepth = 64;
    public const int DefaultMaxXrefSections = 256;
    public const int DefaultMaxPageCount = 20_000;
    public const int DefaultMaxPageTreeDepth = 64;
    public const long DefaultMaxDecodedStreamBytes = 96L * 1024 * 1024;
    public const long DefaultMaxSingleStreamBytes = 32L * 1024 * 1024;
    public const int DefaultMaxFilterChainDepth = 8;
    public const int DefaultMaxStreamExpansionRatio = 512;
    public const long DefaultMaxContentOperators = 4_000_000;
    public const int DefaultMaxFormRecursionDepth = 16;
    public const int DefaultMaxExtractedCharacters = 8_000_000;
    public const int DefaultMaxFontCount = 4096;
    public const int DefaultMaxCMapEntries = 200_000;
    public const int DefaultMaxAnnotationCount = 50_000;
    public const int DefaultMaxDiagnostics = 512;
    public const long DefaultMaxWorkUnits = 400_000_000;
    public const long DefaultMaxOutputBytes = 128L * 1024 * 1024;
    public const long DefaultMaxXmpBytes = 2L * 1024 * 1024;
    public const long DefaultMaxFontProgramBytes = 16L * 1024 * 1024;

    public static PdfLimits Default { get; } = new();

    public PdfLimits(
        long maxInputBytes = DefaultMaxInputBytes,
        int maxTokenLength = DefaultMaxTokenLength,
        int maxObjectCount = DefaultMaxObjectCount,
        int maxContainerEntries = DefaultMaxContainerEntries,
        int maxNestingDepth = DefaultMaxNestingDepth,
        int maxXrefSections = DefaultMaxXrefSections,
        int maxPageCount = DefaultMaxPageCount,
        int maxPageTreeDepth = DefaultMaxPageTreeDepth,
        long maxDecodedStreamBytes = DefaultMaxDecodedStreamBytes,
        long maxSingleStreamBytes = DefaultMaxSingleStreamBytes,
        int maxFilterChainDepth = DefaultMaxFilterChainDepth,
        int maxStreamExpansionRatio = DefaultMaxStreamExpansionRatio,
        long maxContentOperators = DefaultMaxContentOperators,
        int maxFormRecursionDepth = DefaultMaxFormRecursionDepth,
        int maxExtractedCharacters = DefaultMaxExtractedCharacters,
        int maxFontCount = DefaultMaxFontCount,
        int maxCMapEntries = DefaultMaxCMapEntries,
        int maxAnnotationCount = DefaultMaxAnnotationCount,
        int maxDiagnostics = DefaultMaxDiagnostics,
        long maxWorkUnits = DefaultMaxWorkUnits,
        long maxOutputBytes = DefaultMaxOutputBytes,
        long maxXmpBytes = DefaultMaxXmpBytes,
        long maxFontProgramBytes = DefaultMaxFontProgramBytes)
    {
        MaxInputBytes = Positive(maxInputBytes, nameof(maxInputBytes));
        MaxTokenLength = Positive(maxTokenLength, nameof(maxTokenLength));
        MaxObjectCount = Positive(maxObjectCount, nameof(maxObjectCount));
        MaxContainerEntries = Positive(maxContainerEntries, nameof(maxContainerEntries));
        MaxNestingDepth = Positive(maxNestingDepth, nameof(maxNestingDepth));
        MaxXrefSections = Positive(maxXrefSections, nameof(maxXrefSections));
        MaxPageCount = Positive(maxPageCount, nameof(maxPageCount));
        MaxPageTreeDepth = Positive(maxPageTreeDepth, nameof(maxPageTreeDepth));
        MaxDecodedStreamBytes = Positive(maxDecodedStreamBytes, nameof(maxDecodedStreamBytes));
        MaxSingleStreamBytes = Positive(maxSingleStreamBytes, nameof(maxSingleStreamBytes));
        MaxFilterChainDepth = Positive(maxFilterChainDepth, nameof(maxFilterChainDepth));
        MaxStreamExpansionRatio = Positive(maxStreamExpansionRatio, nameof(maxStreamExpansionRatio));
        MaxContentOperators = Positive(maxContentOperators, nameof(maxContentOperators));
        MaxFormRecursionDepth = Positive(maxFormRecursionDepth, nameof(maxFormRecursionDepth));
        MaxExtractedCharacters = Positive(maxExtractedCharacters, nameof(maxExtractedCharacters));
        MaxFontCount = Positive(maxFontCount, nameof(maxFontCount));
        MaxCMapEntries = Positive(maxCMapEntries, nameof(maxCMapEntries));
        MaxAnnotationCount = Positive(maxAnnotationCount, nameof(maxAnnotationCount));
        MaxDiagnostics = Positive(maxDiagnostics, nameof(maxDiagnostics));
        MaxWorkUnits = Positive(maxWorkUnits, nameof(maxWorkUnits));
        MaxOutputBytes = Positive(maxOutputBytes, nameof(maxOutputBytes));
        MaxXmpBytes = Positive(maxXmpBytes, nameof(maxXmpBytes));
        MaxFontProgramBytes = Positive(maxFontProgramBytes, nameof(maxFontProgramBytes));
    }

    /// <summary>Maximum bytes of input the reader will materialize.</summary>
    public long MaxInputBytes { get; }

    /// <summary>Maximum length of a single name, string, or numeric token.</summary>
    public int MaxTokenLength { get; }

    /// <summary>Maximum number of indirect objects the store will hold.</summary>
    public int MaxObjectCount { get; }

    /// <summary>Maximum entries in a single array or dictionary.</summary>
    public int MaxContainerEntries { get; }

    /// <summary>Maximum nesting depth of arrays and dictionaries.</summary>
    public int MaxNestingDepth { get; }

    /// <summary>Maximum cross-reference sections in a <c>/Prev</c> chain.</summary>
    public int MaxXrefSections { get; }

    public int MaxPageCount { get; }

    public int MaxPageTreeDepth { get; }

    /// <summary>Aggregate decoded bytes across every stream in one read.</summary>
    public long MaxDecodedStreamBytes { get; }

    /// <summary>Decoded bytes produced by any single stream.</summary>
    public long MaxSingleStreamBytes { get; }

    /// <summary>Maximum number of chained filters on one stream.</summary>
    public int MaxFilterChainDepth { get; }

    /// <summary>Maximum decoded:encoded ratio permitted per stage and overall.</summary>
    public int MaxStreamExpansionRatio { get; }

    /// <summary>Maximum content-stream operators interpreted per document.</summary>
    public long MaxContentOperators { get; }

    /// <summary>Maximum nesting of Form XObject invocations.</summary>
    public int MaxFormRecursionDepth { get; }

    /// <summary>Maximum characters extracted into the model.</summary>
    public int MaxExtractedCharacters { get; }

    public int MaxFontCount { get; }

    /// <summary>Maximum mappings loaded from all CMaps in one document.</summary>
    public int MaxCMapEntries { get; }

    public int MaxAnnotationCount { get; }

    /// <summary>Maximum diagnostics retained; beyond this the count is summarized.</summary>
    public int MaxDiagnostics { get; }

    /// <summary>
    /// Aggregate abstract work budget. Parsing, filtering, and interpretation all
    /// charge against it, so a document cannot stay under every individual limit
    /// while still costing unbounded time.
    /// </summary>
    public long MaxWorkUnits { get; }

    /// <summary>Maximum bytes a single write may emit.</summary>
    public long MaxOutputBytes { get; }

    /// <summary>
    /// Maximum decoded bytes of an XMP packet the metadata reader will parse.
    /// Well past any packet a producer writes, and far below the point where XML
    /// parsing one becomes the most expensive thing a read does.
    /// </summary>
    public long MaxXmpBytes { get; }

    /// <summary>
    /// Maximum decoded bytes of an embedded font program a composed reader will
    /// be handed. Large enough for a full CJK OpenType face, small enough that a
    /// document cannot make font inspection the most expensive thing a read does.
    /// </summary>
    public long MaxFontProgramBytes { get; }

    private static int Positive(int value, string name) =>
        value > 0 ? value : throw new ArgumentOutOfRangeException(name, value, "A PDF limit must be positive; zero never means unlimited.");

    private static long Positive(long value, string name) =>
        value > 0 ? value : throw new ArgumentOutOfRangeException(name, value, "A PDF limit must be positive; zero never means unlimited.");
}
