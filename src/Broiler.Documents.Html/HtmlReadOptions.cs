using System;

namespace Broiler.Documents.Html;

/// <summary>Lets a host supply bytes for image links it has already authorized.</summary>
public sealed class HtmlReadOptions(
    DocumentReadOptions defaults,
    Func<string, ReadOnlyMemory<byte>?> resolveImage) :
    DocumentReadOptions(defaults.Limits, defaults.DefaultCodePage, defaults.ResourcePolicy)
{
    internal Func<string, ReadOnlyMemory<byte>?> ResolveImage { get; } = resolveImage;
}
