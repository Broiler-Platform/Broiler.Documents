using System;
using System.Globalization;
using System.Security.Cryptography;
using Broiler.Graphics;

namespace Broiler.Documents;

/// <summary>Where a resource came from. Unknown denies.</summary>
public enum DocumentResourceProvenance
{
    /// <summary>Not stated. Treated as untrusted and permitted nothing.</summary>
    Unknown = 0,

    /// <summary>Read out of the document being converted.</summary>
    ReadFromSource,

    /// <summary>Handed to the codec by the caller, who therefore already has it.</summary>
    CallerSupplied,
}

/// <summary>What kind of resource an entry is about.</summary>
public enum DocumentResourceKind
{
    /// <summary>Not stated, which denies like every other unknown here.</summary>
    Unknown = 0,

    /// <summary>A picture.</summary>
    Image,

    /// <summary>A font program.</summary>
    Font,
}

/// <summary>How the source document held the resource. Unknown denies.</summary>
public enum DocumentResourceDisposition
{
    /// <summary>Not stated.</summary>
    Unknown = 0,

    /// <summary>The bytes were inside the document.</summary>
    Embedded,

    /// <summary>The document referred to the resource and did not contain it.</summary>
    Linked,

    /// <summary>The conversion produced the resource rather than finding it.</summary>
    Generated,
}

/// <summary>
/// What a conversion-context entry was approved <em>for</em>: the payload's
/// digest, its kind, its dimensions, and its format.
/// </summary>
/// <remarks>
/// <para>
/// This is the half of an entry that makes "an id alone is never authorization"
/// true rather than aspirational. An entry is looked up by id and then checked
/// against the payload in hand; a caller presenting a valid id with different
/// bytes gets a denial instead of someone else's permission. Without this a
/// resource id would be a bearer token, and a document that can name ids would be
/// able to mint authority by guessing one.
/// </para>
/// <para>
/// The digest is SHA-256 over the payload as the resource holds it — the encoded
/// bytes for an encoded resource, the RGBA samples for a decoded one. It is used
/// to compare payloads, never as a security boundary against a chosen-prefix
/// attacker, and never as an identifier a caller sees.
/// </para>
/// </remarks>
public sealed class DocumentResourceBinding : IEquatable<DocumentResourceBinding>
{
    private DocumentResourceBinding(
        DocumentResourceKind kind,
        string payloadDigest,
        BImagePayloadKind payloadKind,
        string? mediaType,
        int? pixelWidth,
        int? pixelHeight,
        string? fontFamily = null,
        BFontEmbeddingRights declaredRights = default)
    {
        Kind = kind;
        PayloadDigest = payloadDigest;
        PayloadKind = payloadKind;
        MediaType = mediaType;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        FontFamily = fontFamily;
        DeclaredRights = declaredRights;
    }

    /// <summary>Whether this describes a picture or a font program.</summary>
    public DocumentResourceKind Kind { get; }

    /// <summary>The family of a font resource, or null for an image.</summary>
    public string? FontFamily { get; }

    /// <summary>
    /// What a font resource's own table declared when the decision was made.
    /// </summary>
    /// <remarks>
    /// Part of the binding rather than beside it, so a font that declared one
    /// thing when it was approved and another when it is used does not pass the
    /// check. Swapping a permissively-marked program for a restricted one of the
    /// same family is exactly the substitution an entry has to survive.
    /// </remarks>
    public BFontEmbeddingRights DeclaredRights { get; }

    /// <summary>Lowercase hex SHA-256 of the payload.</summary>
    public string PayloadDigest { get; }

    /// <summary>Whether the payload is encoded bytes or decoded samples.</summary>
    public BImagePayloadKind PayloadKind { get; }

    /// <summary>The encoded payload's media type, or null for decoded samples.</summary>
    public string? MediaType { get; }

    /// <summary>Intrinsic width, or null when the resource never established one.</summary>
    public int? PixelWidth { get; }

    /// <summary>Intrinsic height, or null when the resource never established one.</summary>
    public int? PixelHeight { get; }

    /// <summary>Describes <paramref name="resource"/> so an entry can be bound to it.</summary>
    public static DocumentResourceBinding ForImage(BImageResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        string digest;
        if (resource.TryGetEncoded(out ReadOnlyMemory<byte> bytes, out _))
        {
            digest = Digest(bytes.Span);
        }
        else if (resource.TryGetPixels(out BPixelBuffer? pixels))
        {
            digest = Digest(pixels.Rgba);
        }
        else
        {
            // Neither payload is reachable, which no factory on BImageResource
            // can produce. Binding to a constant would silently make every such
            // resource interchangeable, so this refuses instead.
            throw new ArgumentException("The image resource carries no payload to bind to.", nameof(resource));
        }

        return new DocumentResourceBinding(
            DocumentResourceKind.Image,
            digest,
            resource.Kind,
            resource.MediaType,
            resource.PixelWidth,
            resource.PixelHeight);
    }

    /// <summary>Describes <paramref name="font"/> so an entry can be bound to it.</summary>
    public static DocumentResourceBinding ForFont(DocumentFontResource font)
    {
        ArgumentNullException.ThrowIfNull(font);

        return new DocumentResourceBinding(
            DocumentResourceKind.Font,
            Digest(font.Program.Span),
            BImagePayloadKind.Encoded,
            mediaType: null,
            pixelWidth: null,
            pixelHeight: null,
            font.Family,
            font.DeclaredRights);
    }

    /// <summary>
    /// True when <paramref name="other"/> describes the same payload in the same
    /// form at the same size. Every field participates: a resource that decodes
    /// to the same pixels through a different encoding is not the same resource,
    /// because what a writer would emit for it differs.
    /// </summary>
    public bool Equals(DocumentResourceBinding? other) =>
        other is not null &&
        Kind == other.Kind &&
        string.Equals(PayloadDigest, other.PayloadDigest, StringComparison.Ordinal) &&
        PayloadKind == other.PayloadKind &&
        string.Equals(MediaType, other.MediaType, StringComparison.Ordinal) &&
        PixelWidth == other.PixelWidth &&
        PixelHeight == other.PixelHeight &&
        string.Equals(FontFamily, other.FontFamily, StringComparison.Ordinal) &&
        DeclaredRights == other.DeclaredRights;

    public override bool Equals(object? obj) => Equals(obj as DocumentResourceBinding);

    public override int GetHashCode() =>
        HashCode.Combine(
            Kind,
            StringComparer.Ordinal.GetHashCode(PayloadDigest),
            PayloadKind,
            MediaType is null ? 0 : StringComparer.Ordinal.GetHashCode(MediaType),
            PixelWidth,
            PixelHeight,
            FontFamily is null ? 0 : StringComparer.Ordinal.GetHashCode(FontFamily),
            DeclaredRights);

    public override string ToString() =>
        Kind == DocumentResourceKind.Font
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"font {FontFamily} ({DeclaredRights.Describe()}) {PayloadDigest[..12]}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{PayloadKind} {MediaType ?? "rgba"} {PixelWidth?.ToString(CultureInfo.InvariantCulture) ?? "?"}x{PixelHeight?.ToString(CultureInfo.InvariantCulture) ?? "?"} {PayloadDigest[..12]}");

    private static string Digest(ReadOnlySpan<byte> payload) =>
        Convert.ToHexStringLower(SHA256.HashData(payload));
}
