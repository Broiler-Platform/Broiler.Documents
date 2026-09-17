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
public sealed class PdfLimits(
    long maxInputBytes = PdfLimits.DefaultMaxInputBytes,
    int maxTokenLength = PdfLimits.DefaultMaxTokenLength,
    int maxObjectCount = PdfLimits.DefaultMaxObjectCount,
    int maxContainerEntries = PdfLimits.DefaultMaxContainerEntries,
    int maxNestingDepth = PdfLimits.DefaultMaxNestingDepth,
    int maxXrefSections = PdfLimits.DefaultMaxXrefSections,
    int maxPageCount = PdfLimits.DefaultMaxPageCount,
    int maxPageTreeDepth = PdfLimits.DefaultMaxPageTreeDepth,
    long maxDecodedStreamBytes = PdfLimits.DefaultMaxDecodedStreamBytes,
    long maxSingleStreamBytes = PdfLimits.DefaultMaxSingleStreamBytes,
    int maxFilterChainDepth = PdfLimits.DefaultMaxFilterChainDepth,
    int maxStreamExpansionRatio = PdfLimits.DefaultMaxStreamExpansionRatio,
    long maxContentOperators = PdfLimits.DefaultMaxContentOperators,
    int maxFormRecursionDepth = PdfLimits.DefaultMaxFormRecursionDepth,
    int maxExtractedCharacters = PdfLimits.DefaultMaxExtractedCharacters,
    int maxFontCount = PdfLimits.DefaultMaxFontCount,
    int maxCMapEntries = PdfLimits.DefaultMaxCMapEntries,
    int maxAnnotationCount = PdfLimits.DefaultMaxAnnotationCount,
    int maxDiagnostics = PdfLimits.DefaultMaxDiagnostics,
    long maxWorkUnits = PdfLimits.DefaultMaxWorkUnits,
    long maxOutputBytes = PdfLimits.DefaultMaxOutputBytes,
    long maxXmpBytes = PdfLimits.DefaultMaxXmpBytes,
    long maxFontProgramBytes = PdfLimits.DefaultMaxFontProgramBytes,
    long maxDescribedImageBytes = PdfLimits.DefaultMaxDescribedImageBytes)
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
    public const long DefaultMaxDescribedImageBytes = 8L * 1024 * 1024;

    public static PdfLimits Default { get; } = new();

    /// <summary>Maximum bytes of input the reader will materialize.</summary>
    public long MaxInputBytes { get; } = Positive(maxInputBytes, nameof(maxInputBytes));

    /// <summary>Maximum length of a single name, string, or numeric token.</summary>
    public int MaxTokenLength { get; } = Positive(maxTokenLength, nameof(maxTokenLength));

    /// <summary>Maximum number of indirect objects the store will hold.</summary>
    public int MaxObjectCount { get; } = Positive(maxObjectCount, nameof(maxObjectCount));

    /// <summary>Maximum entries in a single array or dictionary.</summary>
    public int MaxContainerEntries { get; } = Positive(maxContainerEntries, nameof(maxContainerEntries));

    /// <summary>Maximum nesting depth of arrays and dictionaries.</summary>
    public int MaxNestingDepth { get; } = Positive(maxNestingDepth, nameof(maxNestingDepth));

    /// <summary>Maximum cross-reference sections in a <c>/Prev</c> chain.</summary>
    public int MaxXrefSections { get; } = Positive(maxXrefSections, nameof(maxXrefSections));

    public int MaxPageCount { get; } = Positive(maxPageCount, nameof(maxPageCount));

    public int MaxPageTreeDepth { get; } = Positive(maxPageTreeDepth, nameof(maxPageTreeDepth));

    /// <summary>Aggregate decoded bytes across every stream in one read.</summary>
    public long MaxDecodedStreamBytes { get; } = Positive(maxDecodedStreamBytes, nameof(maxDecodedStreamBytes));

    /// <summary>Decoded bytes produced by any single stream.</summary>
    public long MaxSingleStreamBytes { get; } = Positive(maxSingleStreamBytes, nameof(maxSingleStreamBytes));

    /// <summary>Maximum number of chained filters on one stream.</summary>
    public int MaxFilterChainDepth { get; } = Positive(maxFilterChainDepth, nameof(maxFilterChainDepth));

    /// <summary>Maximum decoded:encoded ratio permitted per stage and overall.</summary>
    public int MaxStreamExpansionRatio { get; } = Positive(maxStreamExpansionRatio, nameof(maxStreamExpansionRatio));

    /// <summary>Maximum content-stream operators interpreted per document.</summary>
    public long MaxContentOperators { get; } = Positive(maxContentOperators, nameof(maxContentOperators));

    /// <summary>Maximum nesting of Form XObject invocations.</summary>
    public int MaxFormRecursionDepth { get; } = Positive(maxFormRecursionDepth, nameof(maxFormRecursionDepth));

    /// <summary>Maximum characters extracted into the model.</summary>
    public int MaxExtractedCharacters { get; } = Positive(maxExtractedCharacters, nameof(maxExtractedCharacters));

    public int MaxFontCount { get; } = Positive(maxFontCount, nameof(maxFontCount));

    /// <summary>Maximum mappings loaded from all CMaps in one document.</summary>
    public int MaxCMapEntries { get; } = Positive(maxCMapEntries, nameof(maxCMapEntries));

    public int MaxAnnotationCount { get; } = Positive(maxAnnotationCount, nameof(maxAnnotationCount));

    /// <summary>Maximum diagnostics retained; beyond this the count is summarized.</summary>
    public int MaxDiagnostics { get; } = Positive(maxDiagnostics, nameof(maxDiagnostics));

    /// <summary>
    /// Aggregate abstract work budget. Parsing, filtering, and interpretation all
    /// charge against it, so a document cannot stay under every individual limit
    /// while still costing unbounded time.
    /// </summary>
    public long MaxWorkUnits { get; } = Positive(maxWorkUnits, nameof(maxWorkUnits));

    /// <summary>Maximum bytes a single write may emit.</summary>
    public long MaxOutputBytes { get; } = Positive(maxOutputBytes, nameof(maxOutputBytes));

    /// <summary>
    /// Maximum decoded bytes of an XMP packet the metadata reader will parse.
    /// Well past any packet a producer writes, and far below the point where XML
    /// parsing one becomes the most expensive thing a read does.
    /// </summary>
    public long MaxXmpBytes { get; } = Positive(maxXmpBytes, nameof(maxXmpBytes));

    /// <summary>
    /// Maximum decoded bytes of an embedded font program a composed reader will
    /// be handed. Large enough for a full CJK OpenType face, small enough that a
    /// document cannot make font inspection the most expensive thing a read does.
    /// </summary>
    public long MaxFontProgramBytes { get; } = Positive(maxFontProgramBytes, nameof(maxFontProgramBytes));

    /// <summary>
    /// Maximum encoded bytes of an image stream this build will decode purely to
    /// describe it. The logical model carries no images, so an image decode is
    /// diagnostic work: it earns a better sentence and nothing else. This bounds
    /// what one image may spend on that, and <see cref="MaxDecodedStreamBytes"/>
    /// still bounds the whole read.
    /// </summary>
    public long MaxDescribedImageBytes { get; } = Positive(maxDescribedImageBytes, nameof(maxDescribedImageBytes));

    private static int Positive(int value, string name) =>
        value > 0 ? value : throw new ArgumentOutOfRangeException(name, value, "A PDF limit must be positive; zero never means unlimited.");

    private static long Positive(long value, string name) =>
        value > 0 ? value : throw new ArgumentOutOfRangeException(name, value, "A PDF limit must be positive; zero never means unlimited.");
}
