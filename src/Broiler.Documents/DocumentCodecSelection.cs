using System;

namespace Broiler.Documents;

/// <summary>
/// The outcome of <see cref="DocumentCodecCatalog.SelectAndRead"/>: which codec
/// was chosen, if any, and what reading with it produced.
/// </summary>
/// <remarks>
/// The two failures a caller has to tell apart are "nothing recognized this" and
/// "something recognized it and then could not read it". Both arrive here as a
/// rejected <see cref="Result"/>, but only the second has a <see cref="Match"/>,
/// so a host can name the format it declined to open.
/// </remarks>
public sealed class DocumentCodecSelection
{
    public DocumentCodecSelection(DocumentCodecMatch? match, DocumentReadResult result)
    {
        Match = match;
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    /// <summary>The chosen codec and its probe verdict, or null when none matched.</summary>
    public DocumentCodecMatch? Match { get; }

    /// <summary>The read outcome.</summary>
    public DocumentReadResult Result { get; }

    /// <summary>The chosen codec, or null when none matched.</summary>
    public DocumentCodec? Codec => Match?.Codec;

    /// <summary>True when a codec matched and its result is usable.</summary>
    public bool IsUsable => Match is not null && Result.IsUsable;
}
