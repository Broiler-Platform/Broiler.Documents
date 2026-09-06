using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Broiler.Documents.Office;

/// <summary>
/// The font mappings both sides of a pixel comparison are given.
/// </summary>
/// <remarks>
/// <para>
/// This is the most important twenty lines in the pixel axis, because the
/// failure it prevents is the one that makes every other number a lie. If
/// broilerdoc draws with one face and LibreOffice with another, the ink lands in
/// different places for a reason that has nothing to do with either
/// implementation, and the suite reports a difference in this component that is
/// really a difference between two machines' font sets.
/// </para>
/// <para>
/// <c>--font-dir</c> alone is not enough, and finding that out is what this type
/// exists to record. It maps faces by file name, so
/// <c>LiberationSerif-Regular.ttf</c> arrives as the family
/// <c>LiberationSerif</c> - and a document that asks for <c>Liberation Serif</c>,
/// with the space CSS and every office format write it with, does not match.
/// The render then draws with the host fallback and reports the family under
/// <c>fonts.unmappedFamilies</c>, which is how this was caught rather than
/// silently measured.
/// </para>
/// <para>
/// The second gap is the one a reader would not predict. A document that names
/// no family at all - which is what LibreOffice writes into HTML and RTF for
/// ordinary body text - is drawn in the renderer's default family,
/// <c>sans-serif</c>. That is a CSS generic rather than a face, so it needs a
/// mapping of its own or the default text on the page is the one thing not
/// pinned.
/// </para>
/// </remarks>
internal static class FontPinning
{
    /// <summary>
    /// The <c>--font-file</c> arguments that pin every family the corpus names,
    /// plus the three CSS generics a document can fall back to.
    /// </summary>
    public static IReadOnlyList<string> Arguments(
        IReadOnlyList<string> families, IReadOnlyList<string> directories)
    {
        var faces = Faces(directories);
        var arguments = new List<string>();
        var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string family in families)
        {
            string key = Squash(family);
            foreach ((string suffix, string style) in Styles)
            {
                if (!faces.TryGetValue(key + suffix, out string? path))
                    continue;

                arguments.Add("--font-file");
                arguments.Add(family + style + "=" + path);
                mapped.Add(family);
            }
        }

        // The generics, chosen from the pinned families by the only signal their
        // names carry. A heuristic, and a shallow one - but the alternative is a
        // manifest field stating three more names that would then have to be
        // kept in step with the four already there, and the families this corpus
        // pins are named by a convention that has held since the fonts were
        // published.
        foreach ((string generic, Func<string, bool> matches) in Generics)
        {
            string? family = families.FirstOrDefault(candidate => matches(candidate) && mapped.Contains(candidate))
                             ?? families.FirstOrDefault(mapped.Contains);

            if (family is null)
                continue;

            string key = Squash(family);
            foreach ((string suffix, string style) in Styles)
            {
                if (!faces.TryGetValue(key + suffix, out string? path))
                    continue;

                arguments.Add("--font-file");
                arguments.Add(generic + style + "=" + path);
            }
        }

        return arguments;
    }

    /// <summary>
    /// Every font file in the pinned directories, keyed by its bare file name.
    /// </summary>
    /// <remarks>
    /// Ordinal-sorted before it is folded into the dictionary, so two files that
    /// would map to the same key resolve the same way on every host. Two runs
    /// that disagreed about which of two faces won would be two runs measuring
    /// different documents.
    /// </remarks>
    private static Dictionary<string, string> Faces(IReadOnlyList<string> directories)
    {
        var faces = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string directory in directories)
        {
            if (!Directory.Exists(directory))
                continue;

            foreach (string file in Directory
                         .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                if (!Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    continue;

                faces.TryAdd(Path.GetFileNameWithoutExtension(file), file);
            }
        }

        return faces;
    }

    private static string Squash(string family) =>
        family.Replace(" ", string.Empty, StringComparison.Ordinal);

    private static readonly string[] Extensions = [".ttf", ".otf", ".ttc"];

    /// <summary>
    /// File-name suffix to the family suffix broilerdoc spells a face with.
    /// </summary>
    /// <remarks>
    /// The empty suffix is tried first and both <c>-Regular</c> and a bare name
    /// map to the same place, because the two conventions are both in use and a
    /// suite that only understood one would silently pin half a set. Oblique is
    /// accepted for italic: DejaVu names its slanted faces that way and the
    /// renderer has one italic slot.
    /// </remarks>
    private static readonly (string Suffix, string Style)[] Styles =
    [
        ("-Regular", ""),
        ("", ""),
        ("-Bold", ":bold"),
        ("-Italic", ":italic"),
        ("-Oblique", ":italic"),
        ("-BoldItalic", ":bolditalic"),
        ("-BoldOblique", ":bolditalic"),
    ];

    private static readonly (string Generic, Func<string, bool> Matches)[] Generics =
    [
        ("sans-serif", family => family.Contains("Sans", StringComparison.OrdinalIgnoreCase) &&
                                 !family.Contains("Mono", StringComparison.OrdinalIgnoreCase)),
        ("serif", family => family.Contains("Serif", StringComparison.OrdinalIgnoreCase) &&
                            !family.Contains("Sans", StringComparison.OrdinalIgnoreCase)),
        ("monospace", family => family.Contains("Mono", StringComparison.OrdinalIgnoreCase)),
    ];
}
