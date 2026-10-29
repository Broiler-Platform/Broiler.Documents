using System;

namespace Broiler.Documents.Model.Text;

/// <summary>
/// The Symbol font's characters as Unicode: its built-in encoding, and the
/// private-use code points Word writes for text set in it.
/// </summary>
/// <remarks>
/// <para>
/// Symbol puts Greek letters and mathematical signs where Latin fonts put
/// Latin, and Word stores what a user typed in it as U+F020 to U+F0FF, the
/// slot plus U+F000. Drawn in any other face that is a missing-glyph box. A
/// face called Symbol is rarely there to draw it with - not on the Linux
/// machine the LibreOffice references are made on, and not among the fonts
/// they are compared with - and LibreOffice then recodes such text to the
/// characters it stands for (its <c>OpenSymbol</c> conversion table, used
/// when Symbol is replaced). A report's "≥ 0,5µm" column headers, written as
/// U+F0B3 in Symbol, showed two boxes each here.
/// </para>
/// <para>
/// The table is authored from the character each slot denotes rather than
/// transcribed from a glyph-list file (IP-021's position, applied to the data
/// IP-013 covers). The slots left unmapped are the ones that are not
/// characters: Symbol reserves a run of codes for the pieces large brackets,
/// parentheses, braces, and integral signs are assembled from. A piece of a
/// glyph has no text, so those stay empty rather than resolving to something
/// plausible.
/// </para>
/// </remarks>
public static class SymbolFontText
{
    /// <summary>The family whose text this recodes.</summary>
    public const string FamilyName = "Symbol";

    private static readonly char[] Table = Build();

    /// <summary>
    /// A copy of the built-in encoding: the character for each of the 256
    /// slots, or U+0000 for a slot that holds none.
    /// </summary>
    public static char[] CopyTable() => (char[])Table.Clone();

    /// <summary>True when <paramref name="family"/> names the Symbol font.</summary>
    public static bool IsSymbolFamily(string? family) =>
        string.Equals(family?.Trim(), FamilyName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <paramref name="text"/> set in Symbol, with each character that stands for
    /// one of its slots - the slot itself, or the slot plus U+F000 - replaced by
    /// the character the slot draws. Characters outside the font's range, and
    /// slots that hold none, are kept.
    /// </summary>
    public static string Recode(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        char[]? recoded = null;
        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];
            int slot = character is >= (char)0xF020 and <= (char)0xF0FF ? character - 0xF000
                : character is >= (char)0x20 and <= (char)0xFF ? character
                : -1;
            if (slot < 0 || Table[slot] == '\0' || Table[slot] == character)
                continue;

            recoded ??= text.ToCharArray();
            recoded[i] = Table[slot];
        }

        return recoded is null ? text : new string(recoded);
    }

    private static char[] Build()
    {
        var table = new char[256];

        // ASCII slots Symbol keeps, and the mathematics it puts in the rest.
        Assign(table, 32, " !∀#∃%&∋()∗+,−./0123456789:;<=>?");
        Assign(table, 64, "≅ΑΒΧΔΕΦΓΗΙϑΚΛΜΝΟ");
        Assign(table, 80, "ΠΘΡΣΤΥςΩΞΨΖ[∴]⊥_");
        Assign(table, 97, "αβχδεφγηιϕκλμνο");
        Assign(table, 112, "πθρστυϖωξψζ{|}∼");

        Assign(table, 160, "€ϒ′≤⁄∞ƒ♣♦♥♠↔←↑→↓");
        Assign(table, 176, "°±″≥×∝∂•÷≠≡≈…");
        table[191] = '↵';
        Assign(table, 192, "ℵℑℜ℘⊗⊕∅∩∪⊃⊇⊄⊂⊆∈∉");
        Assign(table, 208, "∠∇®©™∏√⋅¬∧∨⇔⇐⇑⇒⇓");
        Assign(table, 224, "◊〈®©™∑");
        table[241] = '〉';
        table[242] = '∫';
        table[243] = '⌠';
        table[245] = '⌡';

        return table;
    }

    private static void Assign(char[] table, int start, string values)
    {
        for (int i = 0; i < values.Length && start + i < table.Length; i++)
        {
            if (values[i] != '\0')
                table[start + i] = values[i];
        }
    }
}
