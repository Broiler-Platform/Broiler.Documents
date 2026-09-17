using System;

namespace Broiler.Documents;

/// <summary>
/// A request handed to <see cref="DocumentCodec.Probe"/>: a bounded byte prefix
/// of the source plus optional hints and the active limits.
/// </summary>
public sealed class DocumentProbeRequest(ReadOnlyMemory<byte> prefix, DocumentSourceHints? hints = null, DocumentLimits? limits = null)
{
    public ReadOnlyMemory<byte> Prefix { get; } = prefix;

    public DocumentSourceHints Hints { get; } = hints ?? DocumentSourceHints.Empty;

    public DocumentLimits Limits { get; } = limits ?? DocumentLimits.Default;
}
