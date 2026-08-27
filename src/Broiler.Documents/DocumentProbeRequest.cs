using System;

namespace Broiler.Documents;

/// <summary>
/// A request handed to <see cref="DocumentCodec.Probe"/>: a bounded byte prefix
/// of the source plus optional hints and the active limits.
/// </summary>
public sealed class DocumentProbeRequest
{
    public DocumentProbeRequest(
        ReadOnlyMemory<byte> prefix,
        DocumentSourceHints? hints = null,
        DocumentLimits? limits = null)
    {
        Prefix = prefix;
        Hints = hints ?? DocumentSourceHints.Empty;
        Limits = limits ?? DocumentLimits.Default;
    }

    public ReadOnlyMemory<byte> Prefix { get; }

    public DocumentSourceHints Hints { get; }

    public DocumentLimits Limits { get; }
}
