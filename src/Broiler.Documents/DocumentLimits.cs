using System;

namespace Broiler.Documents;

/// <summary>
/// Hard limits enforced while reading a document, guarding against the
/// denial-of-service vectors called out in ADR 0004 (nesting bombs, oversized
/// input, run/paragraph floods, huge binary payloads).
/// </summary>
public sealed class DocumentLimits
{
    public const int DefaultMaxProbeBytes = 4096;
    public const long DefaultMaxDocumentBytes = 64L * 1024 * 1024;
    public const int DefaultMaxGroupDepth = 256;
    public const int DefaultMaxRunLength = 1 << 20;
    public const int DefaultMaxParagraphCount = 1 << 20;
    public const long DefaultMaxBinBytes = 16L * 1024 * 1024;

    public static DocumentLimits Default { get; } = new();

    public DocumentLimits(
        int maxProbeBytes = DefaultMaxProbeBytes,
        long maxDocumentBytes = DefaultMaxDocumentBytes,
        int maxGroupDepth = DefaultMaxGroupDepth,
        int maxRunLength = DefaultMaxRunLength,
        int maxParagraphCount = DefaultMaxParagraphCount,
        long maxBinBytes = DefaultMaxBinBytes)
    {
        if (maxProbeBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxProbeBytes));
        if (maxDocumentBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDocumentBytes));
        if (maxGroupDepth <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxGroupDepth));
        if (maxRunLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRunLength));
        if (maxParagraphCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxParagraphCount));
        if (maxBinBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maxBinBytes));

        MaxProbeBytes = maxProbeBytes;
        MaxDocumentBytes = maxDocumentBytes;
        MaxGroupDepth = maxGroupDepth;
        MaxRunLength = maxRunLength;
        MaxParagraphCount = maxParagraphCount;
        MaxBinBytes = maxBinBytes;
    }

    /// <summary>Maximum bytes read for signature probing.</summary>
    public int MaxProbeBytes { get; }

    /// <summary>Maximum total input bytes a read will consume.</summary>
    public long MaxDocumentBytes { get; }

    /// <summary>Maximum nesting depth of <c>{ … }</c> groups.</summary>
    public int MaxGroupDepth { get; }

    /// <summary>Maximum characters in a single text run.</summary>
    public int MaxRunLength { get; }

    /// <summary>Maximum number of paragraphs a document may produce.</summary>
    public int MaxParagraphCount { get; }

    /// <summary>Maximum bytes consumed by a single binary (<c>\bin</c>) payload.</summary>
    public long MaxBinBytes { get; }
}
