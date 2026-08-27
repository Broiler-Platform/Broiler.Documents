using System;

namespace Broiler.Documents.Model;

/// <summary>
/// A maximal contiguous span of characters sharing one <see cref="InlineStyle"/>.
/// Runs carry a length rather than absolute offsets; the owning
/// <see cref="RichTextParagraph"/> keeps them contiguous, normalized, and covering
/// exactly its text.
/// </summary>
public readonly record struct StyleRun
{
    public StyleRun(int length, InlineStyle style)
    {
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length), "Run length must be positive.");

        Length = length;
        Style = style;
    }

    public int Length { get; }

    public InlineStyle Style { get; }
}
