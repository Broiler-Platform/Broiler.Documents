namespace Broiler.Documents.Model;

/// <summary>
/// Where a raised or lowered run (superscript or subscript) stands against the
/// line's baseline.
/// </summary>
public enum BaselinePosition
{
    /// <summary>
    /// Moved by <see cref="BaselineShift.Offset"/>, a fraction of the run's
    /// full-size font height.
    /// </summary>
    Offset = 0,

    /// <summary>
    /// Raised so that the top of the smaller text meets the top of the
    /// full-size text: LibreOffice's automatic superscript
    /// (<c>DFLT_ESC_AUTO_SUPER</c>, ODF's <c>style:text-position="super"</c>).
    /// </summary>
    AutoSuperscript,

    /// <summary>
    /// Lowered so that the foot of the smaller text meets the foot of the
    /// full-size text: LibreOffice's automatic subscript
    /// (<c>DFLT_ESC_AUTO_SUB</c>, ODF's <c>style:text-position="sub"</c>).
    /// </summary>
    AutoSubscript,
}

/// <summary>
/// A run set smaller and off the baseline: superscript, subscript, or text
/// moved by a stated amount.
/// </summary>
/// <param name="Position">How the run is placed.</param>
/// <param name="Offset">
/// For <see cref="BaselinePosition.Offset"/>, how far the baseline moves as a
/// fraction of the full-size font's height: positive raises, negative lowers.
/// Ignored by the automatic positions.
/// </param>
/// <param name="Scale">
/// The run's size as a fraction of its font size (<see cref="InlineStyle.FontSize"/>
/// is the full size). The line keeps at least the full size's height.
/// </param>
public readonly record struct BaselineShift(BaselinePosition Position, double Offset, double Scale)
{
    /// <summary>LibreOffice's default proportional size of raised and lowered text, 58%.</summary>
    public const double DefaultScale = 0.58;

    /// <summary>An automatic superscript at <paramref name="scale"/> of the font size.</summary>
    public static BaselineShift Superscript(double scale = DefaultScale) => new(BaselinePosition.AutoSuperscript, 0, scale);

    /// <summary>An automatic subscript at <paramref name="scale"/> of the font size.</summary>
    public static BaselineShift Subscript(double scale = DefaultScale) => new(BaselinePosition.AutoSubscript, 0, scale);

    /// <summary>True when the shift neither moves nor resizes the run.</summary>
    public bool IsNone => Position == BaselinePosition.Offset && Offset == 0 && Scale == 1;
}
