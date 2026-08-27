using System;

namespace Broiler.Documents;

/// <summary>A codec paired with the positive probe result that selected it.</summary>
public sealed class DocumentCodecMatch
{
    public DocumentCodecMatch(DocumentCodec codec, DocumentProbeResult result)
    {
        Codec = codec ?? throw new ArgumentNullException(nameof(codec));
        Result = result ?? throw new ArgumentNullException(nameof(result));
        if (!result.IsMatch)
            throw new ArgumentException("A codec match requires a positive probe result.", nameof(result));
    }

    public DocumentCodec Codec { get; }

    public DocumentProbeResult Result { get; }
}
