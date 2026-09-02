using System;

namespace Broiler.Documents;

/// <summary>
/// The distinct things that can be done with a document resource, each decided
/// on its own.
/// </summary>
/// <remarks>
/// <para>
/// These are separate flags because they are separate risks, and collapsing them
/// into one "may use images" switch is what makes resource handling go wrong.
/// Reading a picture's size to lay out a page, decoding it transiently to draw it
/// on screen, putting its bytes in a result model a caller can read, and writing
/// those bytes into a new file someone else will receive are four different acts
/// with four different consequences. A host that wants the third does not
/// necessarily want the fourth.
/// </para>
/// <para>
/// Nothing is implied by anything else. <see cref="ExtractToModel"/> does not
/// grant <see cref="ByteTransfer"/>, and a resource permitted on read carries no
/// permission on write. Absence is denial: an operation not named in an entry is
/// refused, which is what makes the unknown case safe.
/// </para>
/// </remarks>
[Flags]
public enum DocumentResourceOperations
{
    /// <summary>Nothing is permitted. The default, and what an unknown resource gets.</summary>
    None = 0,

    /// <summary>
    /// Read the resource's shape — its size, kind, and format — to place it in a
    /// layout, without its content crossing into anything the caller can read.
    /// </summary>
    SemanticProjection = 1 << 0,

    /// <summary>Read metadata attached to the resource into the normalized envelope.</summary>
    MetadataProjection = 1 << 1,

    /// <summary>
    /// Decode the payload in-process, for measurement or display, without the
    /// samples or the bytes being retained anywhere the caller reaches.
    /// </summary>
    TransientDecode = 1 << 2,

    /// <summary>
    /// Put the payload into the result model, where a caller can read it and keep
    /// it. This is durable extraction rather than processing, which is why an
    /// <c>InlineImage</c> carrying reachable bytes may not be constructed without
    /// it.
    /// </summary>
    ExtractToModel = 1 << 3,

    /// <summary>Write the payload's bytes into an output document unchanged.</summary>
    ByteTransfer = 1 << 4,

    /// <summary>Re-encode, rescale, or otherwise alter the payload on the way out.</summary>
    Transform = 1 << 5,

    /// <summary>
    /// Embed or subset the resource into an output document — the operation a
    /// font's own embedding permissions attach to, kept apart from
    /// <see cref="ByteTransfer"/> for exactly that reason.
    /// </summary>
    EmbedOrSubset = 1 << 6,

    /// <summary>Include the resource in a document intended for onward distribution.</summary>
    Redistribute = 1 << 7,
}
