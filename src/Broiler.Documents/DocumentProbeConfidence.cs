namespace Broiler.Documents;

/// <summary>
/// How strongly a codec believes a byte prefix is its format. Mirrors
/// <c>Broiler.Media</c>'s probe confidence so the catalog can rank matches.
/// </summary>
public enum DocumentProbeConfidence
{
    /// <summary>The byte prefix does not match this format.</summary>
    None = 0,
    /// <summary>Matched weak heuristic or filename/MIME hint alone.</summary>
    Low = 25,
    /// <summary>Matched partial magic numbers or structure.</summary>
    Medium = 50,
    /// <summary>Matched strong signature or valid container header.</summary>
    High = 75,
    /// <summary>Unambiguous match for this format specification.</summary>
    Certain = 100,
}
