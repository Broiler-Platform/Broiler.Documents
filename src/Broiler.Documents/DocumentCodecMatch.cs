using System;

namespace Broiler.Documents;

/// <summary>A codec paired with the positive probe result that selected it.</summary>
public sealed class DocumentCodecMatch
{
    /// <summary>
    /// Initializes a new instance of <see cref="DocumentCodecMatch"/>.
    /// </summary>
    /// <param name="codec">The matched codec.</param>
    /// <param name="result">The positive probe result that matched.</param>
    public DocumentCodecMatch(DocumentCodec codec, DocumentProbeResult result)
    {
        Codec = codec ?? throw new ArgumentNullException(nameof(codec));
        Result = result ?? throw new ArgumentNullException(nameof(result));
        if (!result.IsMatch)
            throw new ArgumentException("A codec match requires a positive probe result.", nameof(result));
    }

    /// <summary>The matched codec.</summary>
    public DocumentCodec Codec { get; }

    /// <summary>The positive probe result that led to the match.</summary>
    public DocumentProbeResult Result { get; }
}
