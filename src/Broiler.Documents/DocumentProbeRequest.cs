using System;

namespace Broiler.Documents;

/// <summary>
/// A request handed to <see cref="DocumentCodec.Probe"/>: a bounded byte prefix
/// of the source plus optional hints and the active limits.
/// </summary>
/// <param name="prefix">Bounded prefix of the document bytes to inspect.</param>
/// <param name="hints">Optional filename or MIME type hints.</param>
/// <param name="limits">Document limits in force.</param>
public sealed class DocumentProbeRequest(ReadOnlyMemory<byte> prefix, DocumentSourceHints? hints = null, DocumentLimits? limits = null)
{
    /// <summary>The leading byte prefix available for inspection.</summary>
    public ReadOnlyMemory<byte> Prefix { get; } = prefix;

    /// <summary>Source hints (filename, MIME type) provided to assist probing.</summary>
    public DocumentSourceHints Hints { get; } = hints ?? DocumentSourceHints.Empty;

    /// <summary>The active document limits.</summary>
    public DocumentLimits Limits { get; } = limits ?? DocumentLimits.Default;
}
