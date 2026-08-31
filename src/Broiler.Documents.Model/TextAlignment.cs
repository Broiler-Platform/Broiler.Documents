namespace Broiler.Documents.Model;

/// <summary>Horizontal alignment of a paragraph's text.</summary>
public enum TextAlignment
{
    Left = 0,
    Center,
    Right,

    /// <summary>
    /// Both edges flush: the line's slack is distributed into its inter-word
    /// gaps rather than left at one end. The last line of a paragraph is set
    /// like <see cref="Left"/>, or a two-word closing line would be stretched
    /// across the whole column.
    /// </summary>
    Justify,
}
