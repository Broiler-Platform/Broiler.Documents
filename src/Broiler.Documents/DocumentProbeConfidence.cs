namespace Broiler.Documents;

/// <summary>
/// How strongly a codec believes a byte prefix is its format. Mirrors
/// <c>Broiler.Media</c>'s probe confidence so the catalog can rank matches.
/// </summary>
public enum DocumentProbeConfidence
{
    None = 0,
    Low = 25,
    Medium = 50,
    High = 75,
    Certain = 100,
}
