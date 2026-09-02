using System;
using System.Diagnostics.CodeAnalysis;
using Broiler.Documents.Model;

namespace Broiler.Documents;

/// <summary>
/// The one place a writer asks whether it may put a resource's bytes into its
/// output.
/// </summary>
/// <remarks>
/// <para>
/// Five writers need this decision and they must not each make it. A check
/// written five times is a check that differs five ways, and the way it differs
/// is that one of them forgets — so the DOCX writer emits a picture the RTF
/// writer would have refused, for no reason anyone chose.
/// </para>
/// <para>
/// It also puts the two failure modes in one place, and they are genuinely
/// different. A resource can be refused by policy, which is a decision someone
/// made and a caller can change; or it can have no bytes to emit at all, because
/// its payload is decoded samples and this format needs an encoding. Both drop
/// the picture; only one is about permission, and a host that cannot tell them
/// apart cannot fix either.
/// </para>
/// </remarks>
public static class DocumentResourceGate
{
    /// <summary>
    /// The encoded bytes for <paramref name="image"/>, when the context permits
    /// <paramref name="intended"/> and the payload has an encoding.
    /// </summary>
    /// <param name="denial">
    /// Null on success. Otherwise a phrase naming why, suitable for the message
    /// of a diagnostic: it describes the decision and never the content.
    /// </param>
    public static bool TryTakeEncodedBytes(
        InlineImage image,
        DocumentConversionContext context,
        DocumentResourceOperations intended,
        out ReadOnlyMemory<byte> data,
        [NotNullWhen(true)] out string? contentType,
        [NotNullWhen(false)] out string? denial)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(context);

        data = default;
        contentType = null;

        if (!context.IsAllowed(image.ResourceId, intended, image.Resource))
        {
            denial = context.ExplainDenial(image.ResourceId, intended, image.Resource);
            return false;
        }

        if (!image.TryGetEncoded(out data, out contentType))
        {
            // The payload is decoded samples. Nothing re-encodes it here: the
            // bytes would not be the document's, the format and quality would be
            // this writer's guess, and a lossy round trip would change the
            // picture. A caller that wants those bytes encodes them deliberately.
            denial = "the image holds decoded samples and this format needs encoded bytes";
            data = default;
            contentType = null;
            return false;
        }

        denial = null;
        return true;
    }

    /// <summary>
    /// Whether <paramref name="image"/> may be drawn or measured — the weaker
    /// question, for a writer that lays a picture out without copying its bytes.
    /// </summary>
    public static bool MayProject(InlineImage image, DocumentConversionContext context)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(context);

        return context.IsAllowed(
            image.ResourceId,
            DocumentResourceOperations.SemanticProjection,
            image.Resource);
    }
}
