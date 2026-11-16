namespace Broiler.Documents.Model;

/// <summary>
/// A page's line register ("page line spacing"): the reference paragraph style
/// whose line height sets the pitch that register-true paragraphs on the page
/// put their baselines on (<see cref="ParagraphStyle.RegisterTrue"/>).
/// </summary>
/// <remarks>
/// <para>
/// ODF's <c>style:register-truth-ref-style-name</c> on a page layout. Writer
/// works the pitch out from the reference style's font and line spacing
/// (<c>SwTextFrame::FillRegister</c>): the font's height for single or
/// proportional spacing, at least <see cref="LineHeight"/> for an at-least
/// rule, the font's height and the leading for a leading rule, and a fixed
/// height as it stands. Each line of a register-true paragraph in the body then
/// grows by what puts its baseline on the next line of that register, counted
/// from the top of the body (<c>SwTextFormatter::CalcRealHeight</c>).
/// </para>
/// <para>
/// The style is held resolved, its character properties in <see cref="Text"/>
/// and its line spacing in the other members, so the layout can measure it
/// with the fonts it sets text in.
/// </para>
/// </remarks>
public sealed record LineRegister(InlineStyle Text, LineHeightRule Rule, float LineSpacing, float LineHeight);
