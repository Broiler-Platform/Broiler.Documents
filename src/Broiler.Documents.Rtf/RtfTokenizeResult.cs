using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Broiler.Documents.Rtf;

/// <summary>
/// The output of <see cref="RtfTokenizer.Tokenize"/>: the token list, whether the
/// input was truncated to honor a limit, and any diagnostics. The tokenizer never
/// throws — malformed input yields best-effort tokens, and hitting a hard limit
/// stops tokenization and records a diagnostic (ADR 0004).
/// </summary>
public sealed class RtfTokenizeResult
{
    public RtfTokenizeResult(
        IReadOnlyList<RtfToken> tokens,
        bool truncated,
        IEnumerable<DocumentDiagnostic>? diagnostics = null)
    {
        Tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        Truncated = truncated;
        Diagnostics = diagnostics is null
            ? EmptyDiagnostics
            : Array.AsReadOnly(diagnostics.ToArray());
    }

    private static readonly ReadOnlyCollection<DocumentDiagnostic> EmptyDiagnostics =
        Array.AsReadOnly(Array.Empty<DocumentDiagnostic>());

    public IReadOnlyList<RtfToken> Tokens { get; }

    /// <summary>True when a limit stopped tokenization before the end of input.</summary>
    public bool Truncated { get; }

    public IReadOnlyList<DocumentDiagnostic> Diagnostics { get; }
}
