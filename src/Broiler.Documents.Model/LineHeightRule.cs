namespace Broiler.Documents.Model;

/// <summary>
/// How a paragraph's lines are made tall: in proportion to their text, or to a
/// height the paragraph states.
/// </summary>
/// <remarks>
/// The three rules every format here can state. DOCX spells them
/// <c>w:lineRule="auto"</c>, <c>"atLeast"</c> and <c>"exact"</c> (ECMA-376
/// §17.3.1.33); ODF spells them as a percentage <c>fo:line-height</c>,
/// <c>style:line-height-at-least</c> and a length <c>fo:line-height</c>; RTF as
/// <c>\slmult1</c>, and a positive or negative <c>\sl</c> with <c>\slmult0</c>.
/// </remarks>
public enum LineHeightRule
{
    /// <summary>The line's natural height times <see cref="ParagraphStyle.LineSpacing"/>.</summary>
    Proportional,

    /// <summary>The line's natural height, or <see cref="ParagraphStyle.LineHeight"/> where that is more.</summary>
    AtLeast,

    /// <summary><see cref="ParagraphStyle.LineHeight"/>, whatever the text is.</summary>
    Exact,
}
