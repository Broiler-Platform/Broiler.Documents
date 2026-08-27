namespace Broiler.Documents.Model;

/// <summary>
/// How a run's letters are cased when drawn. This is presentation only — the
/// stored text keeps the author's casing, so turning capitalization off restores
/// exactly what was typed.
/// </summary>
public enum TextCapitalization
{
    /// <summary>Draw the text as stored.</summary>
    None = 0,

    /// <summary>Draw every letter as a capital.</summary>
    AllCaps,

    /// <summary>
    /// Draw every letter as a capital, with letters that were typed in lower
    /// case drawn smaller than those that were typed as capitals.
    /// </summary>
    SmallCaps,
}
