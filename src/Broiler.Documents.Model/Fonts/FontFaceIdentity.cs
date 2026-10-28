using System;
using System.Security.Cryptography;
using System.Text;

namespace Broiler.Documents.Model.Fonts;

/// <summary>
/// An immutable, unambiguous identifier for a concrete font face program.
/// </summary>
/// <remarks>
/// <para>
/// A family name alone must never identify the face used for output: two font programs
/// with the identical family name (such as a caller-supplied subset or a modified local version)
/// must remain distinct.
/// </para>
/// <para>
/// Face identity is established by the font program's content hash (or unique program URI/path),
/// collection face index, family name, subfamily, and style variation.
/// </para>
/// </remarks>
public sealed class FontFaceIdentity : IEquatable<FontFaceIdentity>
{
    public FontFaceIdentity(
        string programId,
        string family,
        string subfamily,
        int faceIndex = 0,
        bool isBold = false,
        bool isItalic = false)
    {
        ArgumentNullException.ThrowIfNull(programId);
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(subfamily);

        ProgramId = programId;
        Family = family.Trim();
        Subfamily = subfamily.Trim();
        FaceIndex = faceIndex;
        IsBold = isBold;
        IsItalic = isItalic;
    }

    /// <summary>
    /// Unique identifier for the underlying font program binary (e.g., SHA-256 digest or canonical source path).
    /// </summary>
    public string ProgramId { get; }

    /// <summary>The declared font family name (e.g., "Liberation Sans", "Arial").</summary>
    public string Family { get; }

    /// <summary>The typographic subfamily (e.g., "Regular", "Bold", "Italic").</summary>
    public string Subfamily { get; }

    /// <summary>The zero-based face index within a font collection (.ttc/.otc), or 0 for a standalone sfnt font.</summary>
    public int FaceIndex { get; }

    /// <summary>True when bold weight is represented or requested.</summary>
    public bool IsBold { get; }

    /// <summary>True when italic or oblique slant is represented or requested.</summary>
    public bool IsItalic { get; }

    /// <summary>Creates an identity from in-memory font program bytes by computing a SHA-256 digest.</summary>
    public static FontFaceIdentity FromProgramBytes(
        ReadOnlySpan<byte> programBytes,
        string family,
        string subfamily = "Regular",
        int faceIndex = 0,
        bool isBold = false,
        bool isItalic = false)
    {
        byte[] hash = SHA256.HashData(programBytes);
        string programId = "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
        return new FontFaceIdentity(programId, family, subfamily, faceIndex, isBold, isItalic);
    }

    /// <summary>Creates an identity from a file path and file modification attributes.</summary>
    public static FontFaceIdentity FromFilePath(
        string filePath,
        string family,
        string subfamily = "Regular",
        int faceIndex = 0,
        bool isBold = false,
        bool isItalic = false)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        string normalizedPath = System.IO.Path.GetFullPath(filePath).Replace('\\', '/');
        string programId = "file:" + normalizedPath;
        return new FontFaceIdentity(programId, family, subfamily, faceIndex, isBold, isItalic);
    }

    /// <summary>Creates a synthetic identity for a fallback face.</summary>
    public static FontFaceIdentity ForFallback(
        string fallbackName,
        bool isBold = false,
        bool isItalic = false)
    {
        ArgumentNullException.ThrowIfNull(fallbackName);
        return new FontFaceIdentity("fallback:" + fallbackName.ToLowerInvariant(), fallbackName, isBold ? (isItalic ? "Bold Italic" : "Bold") : (isItalic ? "Italic" : "Regular"), 0, isBold, isItalic);
    }

    public bool Equals(FontFaceIdentity? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return string.Equals(ProgramId, other.ProgramId, StringComparison.Ordinal) &&
               FaceIndex == other.FaceIndex &&
               IsBold == other.IsBold &&
               IsItalic == other.IsItalic &&
               string.Equals(Family, other.Family, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => Equals(obj as FontFaceIdentity);

    public override int GetHashCode() =>
        HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(ProgramId),
            FaceIndex,
            IsBold,
            IsItalic,
            StringComparer.OrdinalIgnoreCase.GetHashCode(Family));

    public override string ToString() =>
        $"{Family} ({Subfamily}) [Program: {ProgramId}, Index: {FaceIndex}, Bold={IsBold}, Italic={IsItalic}]";
}
