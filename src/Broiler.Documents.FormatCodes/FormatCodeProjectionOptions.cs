using Broiler.Documents.Model;

namespace Broiler.Documents.FormatCodes;

/// <summary>Resource limits and optional non-canonical overlays for one projection.</summary>
public sealed record FormatCodeProjectionOptions
{
    public const int DefaultMaxOutputCharacters = 16 * 1024 * 1024;
    public const int DefaultMaxTokens = 2_000_000;
    public const int DefaultMaxQuotedValueCharacters = 8_192;

    public static FormatCodeProjectionOptions Default { get; } = new();

    public int MaxOutputCharacters { get; init; } = DefaultMaxOutputCharacters;

    public int MaxTokens { get; init; } = DefaultMaxTokens;

    public int MaxQuotedValueCharacters { get; init; } = DefaultMaxQuotedValueCharacters;

    /// <summary>
    /// Optional caret-only formatting state. It is returned through
    /// <see cref="FormatCodeProjection.PendingTokens"/> and never changes canonical
    /// <see cref="FormatCodeProjection.Text"/>.
    /// </summary>
    public FormatCodePendingStyle? PendingStyle { get; init; }
}

/// <summary>A transient caret style that has not yet changed the document.</summary>
public readonly record struct FormatCodePendingStyle(RichTextPosition Caret, InlineStyle Style);
