using System;

namespace Broiler.Documents;

/// <summary>
/// Hard limits enforced while reading a document, guarding against the
/// denial-of-service vectors called out in ADR 0004 (nesting bombs, oversized
/// input, run/paragraph floods, huge binary payloads).
/// </summary>
public sealed class DocumentLimits
{
    /// <summary>Default maximum bytes read for signature probing (4096 bytes).</summary>
    public const int DefaultMaxProbeBytes = 4096;
    /// <summary>Default maximum total input bytes consumed (64 MB).</summary>
    public const long DefaultMaxDocumentBytes = 64L * 1024 * 1024;
    /// <summary>Default maximum nesting depth of groups (256).</summary>
    public const int DefaultMaxGroupDepth = 256;
    /// <summary>Default maximum characters in a single text run (1,048,576 chars).</summary>
    public const int DefaultMaxRunLength = 1 << 20;
    /// <summary>Default maximum number of paragraphs a document may produce (1,048,576 paragraphs).</summary>
    public const int DefaultMaxParagraphCount = 1 << 20;
    /// <summary>Default maximum bytes consumed by a single binary payload (16 MB).</summary>
    public const long DefaultMaxBinBytes = 16L * 1024 * 1024;

    /// <summary>Default document limits.</summary>
    public static DocumentLimits Default { get; } = new();

    /// <summary>
    /// Initializes a new instance of <see cref="DocumentLimits"/> with explicit bounds.
    /// </summary>
    /// <param name="maxProbeBytes">Maximum bytes read for signature probing.</param>
    /// <param name="maxDocumentBytes">Maximum total input bytes a read will consume.</param>
    /// <param name="maxGroupDepth">Maximum nesting depth of groups.</param>
    /// <param name="maxRunLength">Maximum characters in a single text run.</param>
    /// <param name="maxParagraphCount">Maximum number of paragraphs a document may produce.</param>
    /// <param name="maxBinBytes">Maximum bytes consumed by a single binary payload.</param>
    public DocumentLimits(int maxProbeBytes = DefaultMaxProbeBytes, long maxDocumentBytes = DefaultMaxDocumentBytes, int maxGroupDepth = DefaultMaxGroupDepth,
        int maxRunLength = DefaultMaxRunLength, int maxParagraphCount = DefaultMaxParagraphCount, long maxBinBytes = DefaultMaxBinBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxProbeBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDocumentBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxGroupDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRunLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxParagraphCount);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBinBytes);

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
