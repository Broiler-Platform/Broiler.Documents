using System.Collections.Generic;

namespace Broiler.Documents.Rtf;

/// <summary>
/// The document's <c>\fonttbl</c>: font family names and charsets indexed by
/// <c>\fN</c>. Used to resolve <c>\fN</c> to a family name and to pick the code
/// page for <c>\'hh</c> bytes in that font.
/// </summary>
internal sealed class RtfFontTable
{
    private readonly Dictionary<int, Entry> _fonts = [];

    public void Set(int index, string name, int charset) => _fonts[index] = new Entry(name, charset);

    /// <summary>The family name for <paramref name="index"/>, or <see langword="null"/> if unknown/blank.</summary>
    public string? GetName(int index) =>
        _fonts.TryGetValue(index, out Entry entry) && !string.IsNullOrWhiteSpace(entry.Name)
            ? entry.Name
            : null;

    /// <summary>Whether the table names no font, which is also what a document without one has.</summary>
    public bool IsEmpty => _fonts.Count == 0;

    /// <summary>
    /// The family text set in <paramref name="index"/> is drawn in. LibreOffice
    /// gives a run whose <c>\fN</c> the table does not name, or names blank, an
    /// empty font name, which it draws in its fallback face, a sans serif
    /// (DejaVu Sans with the reference fonts); the model's name for that is the
    /// generic <see cref="UnnamedFamily"/>.
    /// </summary>
    public string Resolve(int index) => GetName(index) ?? UnnamedFamily;

    /// <summary>The generic family a font the table does not name is drawn in.</summary>
    public const string UnnamedFamily = "sans-serif";

    /// <summary>The <c>\fcharset</c> for <paramref name="index"/>, or -1 if unknown.</summary>
    public int GetCharset(int index) =>
        _fonts.TryGetValue(index, out Entry entry) ? entry.Charset : -1;

    private readonly record struct Entry(string Name, int Charset);
}
