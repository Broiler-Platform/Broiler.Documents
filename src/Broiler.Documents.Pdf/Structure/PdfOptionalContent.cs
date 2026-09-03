using System.Collections.Generic;
using Broiler.Documents.Pdf.Syntax;

namespace Broiler.Documents.Pdf.Structure;

/// <summary>
/// The document's default optional-content configuration: which layers it says
/// are off when it is opened with no user having chosen otherwise.
/// </summary>
/// <remarks>
/// <para>
/// This is not a visibility judgement, and the distinction is the whole reason
/// it is safe to act on. Rendering mode 3 asks whether a reader would see some
/// glyphs, which needs a renderer and a claim this release refuses to make (see
/// <see cref="PdfDiagnosticCodes.TextVisibilityUncertain"/>). A configuration in
/// <c>/OCProperties</c> is the document stating, in its own catalog, which of
/// its layers make up the default presentation. Reading that is reading the
/// format; ignoring it means a page's alternate-language layer, its CAD
/// underlay, or a template's draft stamp all arrive in the text as though the
/// document had shown them at once.
/// </para>
/// <para>
/// Only the default configuration <c>/D</c> is read. The alternates in
/// <c>/Configs</c> exist for a user to choose between, and there is no user
/// here; picking one would be inventing a choice rather than reading a
/// declaration. Usage application dictionaries (<c>/AS</c>), which vary a group
/// by zoom level or by whether the page is being printed, are for the same
/// reason not applied.
/// </para>
/// <para>
/// Groups are identified by the object they resolve to. The store caches by
/// object number, so two references to one group yield the same instance, and a
/// group written directly into a content stream rather than referenced is simply
/// not one of the catalog's — which is correct, since the configuration can only
/// have named the ones it could refer to.
/// </para>
/// </remarks>
internal sealed class PdfOptionalContent
{
    /// <summary>Nothing declared: every layer is part of the default presentation.</summary>
    public static PdfOptionalContent None { get; } = new([], 0, enforced: true);

    private readonly HashSet<PdfDictionary> _off;

    private PdfOptionalContent(HashSet<PdfDictionary> off, int groups, bool enforced)
    {
        _off = off;
        GroupCount = groups;
        Enforced = enforced;
    }

    /// <summary>
    /// Whether membership actually withholds content. False when the caller asked
    /// for every layer: the configuration is still read and still reported, so a
    /// caller who takes everything is told what they took, rather than the
    /// document's own statement about itself going unmentioned.
    /// </summary>
    public bool Enforced { get; }

    /// <summary>How many groups the catalog declares.</summary>
    public int GroupCount { get; }

    /// <summary>How many of them the default configuration turns off.</summary>
    public int OffGroupCount => _off.Count;

    /// <summary>True when the document declares optional content at all.</summary>
    public bool IsDeclared => GroupCount > 0;

    /// <summary>
    /// Reads the catalog's default configuration, or <see cref="None"/> where the
    /// document declares no optional content.
    /// </summary>
    /// <param name="enforced">
    /// False to read and report the configuration without letting it withhold
    /// anything, which is what a caller asking for every layer gets.
    /// </param>
    public static PdfOptionalContent Read(PdfObjectStore store, PdfDictionary catalog, bool enforced)
    {
        if (store.Resolve(catalog["OCProperties"]) is not PdfDictionary properties)
            return None;

        List<PdfDictionary> groups = Groups(store, properties["OCGs"]);
        if (groups.Count == 0)
            return None;

        var off = new HashSet<PdfDictionary>();
        PdfDictionary? configuration = store.Resolve(properties["D"]) as PdfDictionary;

        // A missing default configuration is not a broken document: the base
        // state is ON, so every declared group stays part of the presentation
        // and nothing here changes what is extracted.
        if (configuration is not null)
        {
            if ((store.Resolve(configuration["BaseState"]) as PdfName)?.Value == "OFF")
            {
                foreach (PdfDictionary group in groups)
                    off.Add(group);
            }

            // The two arrays are applied after the base state, and in this order,
            // so a group named in both ends up off — which is what a reader that
            // applies them in sequence would also conclude.
            foreach (PdfDictionary group in Groups(store, configuration["ON"]))
                off.Remove(group);

            foreach (PdfDictionary group in Groups(store, configuration["OFF"]))
                off.Add(group);
        }

        return new PdfOptionalContent(off, groups.Count, enforced);
    }

    /// <summary>
    /// Whether content belonging to <paramref name="entry"/> is outside the
    /// default presentation. <paramref name="undecidable"/> is set when the
    /// membership dictionary states a visibility expression, which this build
    /// does not evaluate and therefore does not act on.
    /// </summary>
    public bool IsHidden(PdfObjectStore store, PdfObject? entry, out bool undecidable)
    {
        undecidable = false;

        if (_off.Count == 0 || store.Resolve(entry) is not PdfDictionary dictionary)
            return false;

        if ((store.Resolve(dictionary["Type"]) as PdfName)?.Value != "OCMD")
            return _off.Contains(dictionary);

        // A visibility expression is a nested boolean tree over the groups. It
        // outranks /OCGs and /P where present, so honouring those instead would
        // answer a question the document did not ask. The content is kept and the
        // fact is reported, which is the one honest pair.
        if (dictionary["VE"] is not null)
        {
            undecidable = true;
            return false;
        }

        List<PdfDictionary> members = Groups(store, dictionary["OCGs"]);
        if (members.Count == 0)
            return false;

        int on = 0;
        foreach (PdfDictionary member in members)
        {
            if (!_off.Contains(member))
                on++;
        }

        // The policy says which combination makes the content visible; this is
        // its negation, because the caller asked whether to hide.
        return ((store.Resolve(dictionary["P"]) as PdfName)?.Value ?? "AnyOn") switch
        {
            "AllOn" => on < members.Count,
            "AnyOff" => on == members.Count,
            "AllOff" => on > 0,
            _ => on == 0,
        };
    }

    /// <summary>
    /// The group dictionaries an entry names, whether it is a single group or an
    /// array of them. Entries that do not resolve to a dictionary are dropped
    /// rather than standing for anything.
    /// </summary>
    private static List<PdfDictionary> Groups(PdfObjectStore store, PdfObject? entry)
    {
        var groups = new List<PdfDictionary>();

        switch (store.Resolve(entry))
        {
            case PdfDictionary single:
                groups.Add(single);
                break;

            case PdfArray array:
                foreach (PdfObject item in array)
                {
                    if (store.Resolve(item) is PdfDictionary group)
                        groups.Add(group);
                }

                break;
        }

        return groups;
    }
}
