using System;
using Broiler.Documents.Model;

namespace Broiler.Documents.FormatCodes;

/// <summary>Measured Phase 1 guidance for choosing a synchronous or background rebuild.</summary>
public static class FormatCodeProjectionPolicy
{
    public const int MaxSynchronousSourceCharacters = 100_000;
    public const int MaxSynchronousStructuralUnits = 10_000;

    /// <summary>
    /// Returns true when a host should project an immutable snapshot away from
    /// its UI path. The projector itself remains synchronous and deterministic.
    /// </summary>
    public static bool RecommendBackgroundProjection(RichTextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        long sourceCharacters = Math.Max(0, document.ParagraphCount - 1);
        long structuralUnits = document.ParagraphCount;
        foreach (RichTextParagraph paragraph in document.Paragraphs)
        {
            sourceCharacters += paragraph.Length;
            structuralUnits += paragraph.Runs.Count;
            if (sourceCharacters > MaxSynchronousSourceCharacters ||
                structuralUnits > MaxSynchronousStructuralUnits)
            {
                return true;
            }
        }

        return false;
    }
}
