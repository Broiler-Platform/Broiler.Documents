using System;
using System.Diagnostics.CodeAnalysis;
using Broiler.Documents.Model;
using Broiler.Graphics;

namespace Broiler.Documents;

/// <summary>
/// The preflight a writer runs before embedding a font, and the one place the
/// fail-closed rule lives.
/// </summary>
/// <remarks>
/// <para>
/// PDF roadmap §11.3 asks for two things this implements together. A caller's
/// explicit licence disposition, which is the conversion-context entry, and a
/// refusal on anything restricted, ambiguous or legally unknown. Both must hold;
/// neither substitutes for the other.
/// </para>
/// <para>
/// <strong>Why both.</strong> A permissive <c>fsType</c> is not a licence — a
/// font may forbid in its EULA what it permits in its table — so the context's
/// decision is required whatever the file says. And a caller's decision does not
/// make a restricted declaration go away, because a policy is written once for a
/// conversion while the declaration belongs to one particular font. Requiring
/// both means neither a wide-open file nor a broad policy can carry a font past
/// the other.
/// </para>
/// <para>
/// <strong>Nothing here embeds anything.</strong> Embedding is outside IP-012
/// and blocked until that row is re-opened. This is the decision the writer will
/// ask for when it is, written now so the answer is not invented in a hurry
/// later.
/// </para>
/// </remarks>
public static class DocumentFontEmbedding
{
    /// <summary>
    /// Whether <paramref name="font"/> may be embedded, and subsetted if asked.
    /// </summary>
    /// <param name="subsetting">
    /// True when the writer intends to subset rather than embed the whole
    /// program. A font may permit one and not the other.
    /// </param>
    /// <param name="refusal">
    /// Null when permitted. Otherwise a phrase naming what refused, suitable for
    /// a diagnostic: it describes the decision and never the font's contents.
    /// </param>
    public static bool MayEmbed(
        DocumentFontResource font,
        DocumentResourceId id,
        DocumentConversionContext context,
        bool subsetting,
        [NotNullWhen(false)] out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(context);

        DocumentResourceOperations intended = subsetting
            ? DocumentResourceOperations.EmbedOrSubset | DocumentResourceOperations.Transform
            : DocumentResourceOperations.EmbedOrSubset;

        if (!context.IsAllowed(id, intended, font))
        {
            refusal = context.ExplainDenial(id, intended, font);
            return false;
        }

        // The caller has decided. The font's own declaration still gets a say,
        // because the caller decided about a conversion and this is one font.
        switch (font.DeclaredRights.Permission)
        {
            case BFontEmbeddingPermission.Unknown:
                refusal = "the font declares no embedding permission, and an unreadable declaration is not a permissive one";
                return false;

            case BFontEmbeddingPermission.Restricted:
                refusal = "the font declares restricted-licence embedding";
                return false;
        }

        if (subsetting && font.DeclaredRights.NoSubsetting)
        {
            refusal = "the font permits embedding but forbids subsetting";
            return false;
        }

        // Bitmap-only embedding with no bitmaps to embed is §11.3's named
        // ambiguous case. This build emits no bitmap font program, so the
        // condition can never be satisfied and the honest answer is no.
        if (font.DeclaredRights.BitmapEmbeddingOnly)
        {
            refusal = "the font permits bitmap embedding only, and this writer emits no bitmap font program";
            return false;
        }

        refusal = null;
        return true;
    }

    /// <summary>
    /// Whether a font read out of a document may be embedded into another one.
    /// </summary>
    /// <remarks>
    /// Always false, and not as a limitation. §11.3's rule is that a font
    /// extracted from an input document is not caller-supplied export authority:
    /// opening a file that happens to contain a font grants nothing about
    /// putting that font into a different file, and an import-to-export
    /// conversion resolves a new approved font resource instead. Kept as a named
    /// method rather than an inline check so the rule has somewhere to be read.
    /// </remarks>
    public static bool MayReExport(DocumentResourceProvenance provenance) =>
        provenance == DocumentResourceProvenance.CallerSupplied;
}
