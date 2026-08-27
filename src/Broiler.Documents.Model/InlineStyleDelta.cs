using Broiler.Graphics;

namespace Broiler.Documents.Model;

/// <summary>
/// A partial change to an <see cref="InlineStyle"/>. Nullable toggle/color
/// attributes are left unchanged when <see langword="null"/>; the string/size
/// attributes use an explicit <c>Set*</c> flag so that "set to default"
/// (<see langword="null"/>) is distinct from "leave unchanged".
/// </summary>
public readonly record struct InlineStyleDelta
{
    public bool? Bold { get; init; }

    public bool? Italic { get; init; }

    public bool? Underline { get; init; }

    public bool? Strikethrough { get; init; }

    public TextCapitalization? Capitalization { get; init; }

    public BColor? Foreground { get; init; }

    public BColor? Background { get; init; }

    public bool SetFontFamily { get; init; }

    public string? FontFamily { get; init; }

    public bool SetFontSize { get; init; }

    public float? FontSize { get; init; }

    public bool SetLink { get; init; }

    public string? LinkHref { get; init; }

    /// <summary>
    /// Applies this delta over <paramref name="style"/>. An
    /// <see cref="InlineStyle.Image"/> is content rather than formatting and is
    /// never touched here — clearing formatting over a picture must not delete
    /// the picture.
    /// </summary>
    public InlineStyle Apply(InlineStyle style) => style with
    {
        Bold = Bold ?? style.Bold,
        Italic = Italic ?? style.Italic,
        Underline = Underline ?? style.Underline,
        Strikethrough = Strikethrough ?? style.Strikethrough,
        Capitalization = Capitalization ?? style.Capitalization,
        Foreground = Foreground ?? style.Foreground,
        Background = Background ?? style.Background,
        FontFamily = SetFontFamily ? FontFamily : style.FontFamily,
        FontSize = SetFontSize ? FontSize : style.FontSize,
        LinkHref = SetLink ? LinkHref : style.LinkHref,
    };

    public static InlineStyleDelta ToggleBold(bool on) => new() { Bold = on };

    public static InlineStyleDelta ToggleItalic(bool on) => new() { Italic = on };

    public static InlineStyleDelta ToggleUnderline(bool on) => new() { Underline = on };

    public static InlineStyleDelta ToggleStrikethrough(bool on) => new() { Strikethrough = on };

    public static InlineStyleDelta WithCapitalization(TextCapitalization capitalization) =>
        new() { Capitalization = capitalization };

    public static InlineStyleDelta WithForeground(BColor color) => new() { Foreground = color };

    public static InlineStyleDelta WithBackground(BColor color) => new() { Background = color };

    public static InlineStyleDelta WithFontFamily(string? family) => new() { SetFontFamily = true, FontFamily = family };

    public static InlineStyleDelta WithFontSize(float? size) => new() { SetFontSize = true, FontSize = size };

    public static InlineStyleDelta WithLink(string? href) => new() { SetLink = true, LinkHref = href };

    /// <summary>Resets every inline attribute to its default (clear formatting).</summary>
    public static InlineStyleDelta Clear => new()
    {
        Bold = false,
        Italic = false,
        Underline = false,
        Strikethrough = false,
        Capitalization = TextCapitalization.None,
        Foreground = BColor.Empty,
        Background = BColor.Empty,
        SetFontFamily = true,
        FontFamily = null,
        SetFontSize = true,
        FontSize = null,
        SetLink = true,
        LinkHref = null,
    };
}
