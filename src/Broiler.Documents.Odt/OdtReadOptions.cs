using System;

namespace Broiler.Documents.Odt;

/// <summary>
/// Lets a host supply bytes for pictures a package links beside itself, once it
/// has authorized reading them.
/// </summary>
/// <remarks>
/// A relative link in a package resolves against the package as a folder (ODF
/// 1.2 part 3, §3.7), so <c>../photo.jpg</c> names a file in the document's own
/// directory. The reader asks <paramref name="resolveLinkedImage"/> only for a
/// link of exactly that form, passing the bare file name: nothing deeper, no
/// other traversal, and never for a flat document, which has no package to be
/// a folder. The default options ask nothing (ADR 0004). A link the resolver
/// cannot answer reads as it does without one.
/// </remarks>
public sealed class OdtReadOptions(
    DocumentReadOptions defaults,
    Func<string, ReadOnlyMemory<byte>?> resolveLinkedImage) :
    DocumentReadOptions(defaults.Limits, defaults.DefaultCodePage, defaults.ResourcePolicy)
{
    internal Func<string, ReadOnlyMemory<byte>?> ResolveLinkedImage { get; } = resolveLinkedImage;
}
