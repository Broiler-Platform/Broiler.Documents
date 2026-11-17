using System;
using System.Collections.Generic;

namespace Broiler.Documents.Model;

/// <summary>A reusable, resolved set of paragraph properties and default typography.</summary>
public sealed record NamedParagraphStyle
{
    public NamedParagraphStyle(string name, ParagraphStyle paragraph, InlineStyle text)
    {
        if (!IsValidName(name))
            throw new ArgumentException("Invalid paragraph style name.", nameof(name));

        Name = name;
        Paragraph = new ParagraphStyle
        {
            Alignment = paragraph.Alignment,
            SpacingBefore = paragraph.SpacingBefore,
            SpacingAfter = paragraph.SpacingAfter,
            LineSpacing = paragraph.LineSpacing,
            LineHeightRule = paragraph.LineHeightRule,
            LineHeight = paragraph.LineHeight,
            LeftIndent = paragraph.LeftIndent,
            RightIndent = paragraph.RightIndent,
            FirstLineIndent = paragraph.FirstLineIndent,
            Background = paragraph.Background,
            Border = paragraph.Border,
            TabStops = paragraph.TabStops,
            HeadingLevel = paragraph.HeadingLevel,
        };
        Text = new InlineStyle { FontFamily = text.FontFamily, FontSize = text.FontSize, Bold = text.Bold, Italic = text.Italic };
    }

    public string Name { get; }
    public ParagraphStyle Paragraph { get; }
    public InlineStyle Text { get; }

    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128)
            return false;
        for (int index = 0; index < name.Length; index++)
        {
            char character = name[index];
            if (char.IsControl(character) || character is '\uFFFE' or '\uFFFF')
                return false;
            if (char.IsSurrogate(character))
            {
                if (index + 1 >= name.Length || !char.IsSurrogatePair(character, name[++index]))
                    return false;
            }
        }
        return true;
    }

    internal ParagraphStyle ApplyTo(ParagraphStyle style, NamedParagraphStyle? previous)
    {
        T Choose<T>(T current, T updated, Func<ParagraphStyle, T> read) =>
            previous is null ? updated : Pick(current, updated, read(previous.Paragraph));

        return style with
        {
            StyleName = Name,
            Alignment = Choose(style.Alignment, Paragraph.Alignment, old => old.Alignment),
            SpacingBefore = Choose(style.SpacingBefore, Paragraph.SpacingBefore, old => old.SpacingBefore),
            SpacingAfter = Choose(style.SpacingAfter, Paragraph.SpacingAfter, old => old.SpacingAfter),
            LineSpacing = Choose(style.LineSpacing, Paragraph.LineSpacing, old => old.LineSpacing),
            LineHeightRule = Choose(style.LineHeightRule, Paragraph.LineHeightRule, old => old.LineHeightRule),
            LineHeight = Choose(style.LineHeight, Paragraph.LineHeight, old => old.LineHeight),
            LeftIndent = Choose(style.LeftIndent, Paragraph.LeftIndent, old => old.LeftIndent),
            RightIndent = Choose(style.RightIndent, Paragraph.RightIndent, old => old.RightIndent),
            FirstLineIndent = Choose(style.FirstLineIndent, Paragraph.FirstLineIndent, old => old.FirstLineIndent),
            Background = Choose(style.Background, Paragraph.Background, old => old.Background),
            Border = Choose(style.Border, Paragraph.Border, old => old.Border),
            TabStops = Choose(style.TabStops, Paragraph.TabStops, old => old.TabStops),
            HeadingLevel = Choose(style.HeadingLevel, Paragraph.HeadingLevel, old => old.HeadingLevel),
        };
    }

    private static T Pick<T>(T actual, T updated, T old) =>
        EqualityComparer<T>.Default.Equals(actual, old) ? updated : actual;

    internal InlineStyle ApplyText(InlineStyle actual, InlineStyle inherited) => actual with
    {
        FontFamily = Pick(actual.FontFamily, Text.FontFamily, inherited.FontFamily),
        FontSize = Pick(actual.FontSize, Text.FontSize, inherited.FontSize),
        Bold = Pick(actual.Bold, Text.Bold, inherited.Bold),
        Italic = Pick(actual.Italic, Text.Italic, inherited.Italic),
    };
}
