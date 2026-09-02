using System;
using Broiler.Graphics;

namespace Broiler.Documents;

/// <summary>
/// A font program a conversion has met, and what the file itself says about
/// embedding it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Nothing here is authority to embed anything.</strong>
/// <see cref="DeclaredRights"/> is what the font's own <c>OS/2</c> table
/// declares, which the roadmap calls a technical signal and an enforcement
/// input — not a substitute for the font's licence, and not a determination of
/// anyone's legal title. A caller's policy decides, and this is one of the facts
/// it decides on.
/// </para>
/// <para>
/// <strong>A font found inside a document is not export authority.</strong> That
/// is §11.3's rule and the reason <see cref="DocumentResourceProvenance"/>
/// travels with the request: a program extracted from an input carries
/// <see cref="DocumentResourceProvenance.ReadFromSource"/>, and a policy that
/// grants embedding to caller-supplied fonts must not grant it to those merely
/// because they arrived in a file the caller opened.
/// </para>
/// </remarks>
public sealed class DocumentFontResource
{
    public DocumentFontResource(
        ReadOnlyMemory<byte> program,
        string family,
        BFontEmbeddingRights declaredRights)
    {
        if (string.IsNullOrWhiteSpace(family))
            throw new ArgumentException("A font resource names its family.", nameof(family));

        Program = program;
        Family = family;
        DeclaredRights = declaredRights;
    }

    /// <summary>The font program's bytes, as the source held them.</summary>
    public ReadOnlyMemory<byte> Program { get; }

    /// <summary>The family the font names itself with.</summary>
    public string Family { get; }

    /// <summary>
    /// What the font's <c>OS/2</c> table declares. Reported, never enforced here.
    /// </summary>
    public BFontEmbeddingRights DeclaredRights { get; }

    /// <summary>
    /// True when the font's own declaration is anything other than a plain
    /// permission to embed it — restricted, silent, or carrying a condition.
    /// </summary>
    /// <remarks>
    /// Offered so a policy that fails closed can say so in one readable line
    /// rather than re-deriving the rule. It is deliberately not a decision: a
    /// caller holding a licence may embed a font whose <c>fsType</c> restricts
    /// it, and a caller holding none may not embed one whose <c>fsType</c> is
    /// wide open. Only the caller knows which it is.
    /// </remarks>
    public bool DeclarationNeedsAnExplicitDecision =>
        DeclaredRights.Permission is not BFontEmbeddingPermission.Installable ||
        DeclaredRights.NoSubsetting ||
        DeclaredRights.BitmapEmbeddingOnly;
}
