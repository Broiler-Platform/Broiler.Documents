using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Broiler.Documents;

/// <summary>
/// The fonts a caller has explicitly provisioned for writing, and the only place
/// a writer may get one from.
/// </summary>
/// <remarks>
/// <para>
/// This is PDF roadmap §11.3's operational path, chosen on 2026-09-02: a writer
/// requires an explicitly configured font set and fails with a preflight
/// diagnostic when one is absent. The project bundles no fallback font and holds
/// no font licence; the caller supplies fonts and holds their terms.
/// </para>
/// <para>
/// <strong>Nothing is discovered.</strong> A font reaches a document because
/// somebody put it in this set, never because it was installed on the machine
/// that happened to run the conversion. §11.3 forbids ambient selection for
/// export and forbids substituting an OS font for one that cannot be embedded,
/// and the reason is not tidiness: a document exported on a machine with a font
/// and one without it would differ, and nothing would say so.
/// </para>
/// <para>
/// <strong>Empty is the default and is not an error.</strong> A caller that
/// provisions nothing gets a writer that says what it could not write instead of
/// one that guesses — which is the whole of the failure experience this path
/// asks for.
/// </para>
/// </remarks>
public sealed class DocumentFontSet
{
    private readonly ReadOnlyCollection<DocumentFontResource> _fonts;

    public DocumentFontSet(IEnumerable<DocumentFontResource> fonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        _fonts = Array.AsReadOnly(fonts.Where(static font => font is not null).ToArray());
    }

    /// <summary>
    /// No fonts provisioned. The default for every write, and what a host that
    /// has not been configured passes.
    /// </summary>
    public static DocumentFontSet None { get; } = new(Array.Empty<DocumentFontResource>());

    /// <summary>The provisioned fonts, in the order the caller supplied them.</summary>
    public IReadOnlyList<DocumentFontResource> Fonts => _fonts;

    /// <summary>True when nothing is provisioned.</summary>
    public bool IsEmpty => _fonts.Count == 0;

    /// <summary>
    /// The provisioned font for <paramref name="family"/>, if the caller
    /// supplied one.
    /// </summary>
    /// <remarks>
    /// An exact, ordinal-insensitive family match and nothing cleverer. Fuzzy
    /// matching is how a document ends up written in a face nobody chose, and
    /// the failure it produces — slightly wrong letterforms — is one a reader
    /// notices long after the conversion.
    /// </remarks>
    public bool TryFind(string family, [NotNullWhen(true)] out DocumentFontResource? font)
    {
        font = null;
        if (string.IsNullOrWhiteSpace(family))
            return false;

        foreach (DocumentFontResource candidate in _fonts)
        {
            if (string.Equals(candidate.Family, family, StringComparison.OrdinalIgnoreCase))
            {
                font = candidate;
                return true;
            }
        }

        return false;
    }
}
