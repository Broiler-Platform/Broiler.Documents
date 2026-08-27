using System;
using System.Collections.Generic;
using System.Globalization;
using Broiler.Documents.Pdf.Syntax;

namespace Broiler.Documents.Pdf.Structure;

/// <summary>A PDF version declaration, such as 1.7 or 2.0.</summary>
public readonly struct PdfVersion : IEquatable<PdfVersion>, IComparable<PdfVersion>
{
    public PdfVersion(int major, int minor)
    {
        Major = major;
        Minor = minor;
    }

    /// <summary>No usable declaration was found.</summary>
    public static PdfVersion Unknown => default;

    /// <summary>The version this codec reads and writes.</summary>
    public static PdfVersion Pdf17 => new(1, 7);

    public int Major { get; }

    public int Minor { get; }

    public bool IsKnown => Major > 0;

    /// <summary>True for a PDF 2.x declaration, which this release tolerates but does not implement.</summary>
    public bool IsPdf2OrLater => Major >= 2;

    public bool Equals(PdfVersion other) => Major == other.Major && Minor == other.Minor;

    public override bool Equals(object? obj) => obj is PdfVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor);

    public int CompareTo(PdfVersion other) =>
        Major != other.Major ? Major.CompareTo(other.Major) : Minor.CompareTo(other.Minor);

    public static bool operator ==(PdfVersion left, PdfVersion right) => left.Equals(right);

    public static bool operator !=(PdfVersion left, PdfVersion right) => !left.Equals(right);

    public static bool operator >(PdfVersion left, PdfVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(PdfVersion left, PdfVersion right) => left.CompareTo(right) < 0;

    public static bool operator >=(PdfVersion left, PdfVersion right) => left.CompareTo(right) >= 0;

    public static bool operator <=(PdfVersion left, PdfVersion right) => left.CompareTo(right) <= 0;

    public override string ToString() =>
        IsKnown ? string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}") : "unknown";

    /// <summary>Parses <c>%PDF-M.m</c> at <paramref name="headerOffset"/>.</summary>
    internal static PdfVersion ParseHeader(byte[] data, int headerOffset)
    {
        int i = headerOffset + 5;
        if (i >= data.Length)
            return Unknown;

        int major = ReadDigits(data, ref i);
        if (i >= data.Length || data[i] != (byte)'.')
            return Unknown;
        i++;
        int minor = ReadDigits(data, ref i);
        return major <= 0 ? Unknown : new PdfVersion(major, minor);
    }

    /// <summary>Parses the <c>/Version</c> name form, <c>1.7</c>.</summary>
    internal static PdfVersion ParseName(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return Unknown;

        int dot = value.IndexOf('.');
        if (dot <= 0 || dot == value.Length - 1)
            return Unknown;

        return int.TryParse(value[..dot], NumberStyles.None, CultureInfo.InvariantCulture, out int major) &&
               int.TryParse(value[(dot + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int minor)
            ? new PdfVersion(major, minor)
            : Unknown;
    }

    private static int ReadDigits(byte[] data, ref int index)
    {
        int value = 0;
        int digits = 0;
        while (index < data.Length && data[index] >= (byte)'0' && data[index] <= (byte)'9' && digits < 4)
        {
            value = value * 10 + (data[index] - (byte)'0');
            index++;
            digits++;
        }

        return digits == 0 ? -1 : value;
    }
}

/// <summary>
/// A developer-extension declaration found in the Catalog's <c>/Extensions</c>.
/// </summary>
/// <remarks>
/// Extensions are inventory, never feature enablement. The reader records what a
/// document claims so a diagnostic can name it, and dispatch stays keyed to the
/// approved feature matrix alone (PDF roadmap §8.1).
/// </remarks>
public sealed class PdfExtensionDeclaration
{
    public PdfExtensionDeclaration(string prefix, PdfVersion baseVersion, int extensionLevel)
    {
        Prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
        BaseVersion = baseVersion;
        ExtensionLevel = extensionLevel;
    }

    /// <summary>The registered developer prefix, for example <c>ADBE</c>.</summary>
    public string Prefix { get; }

    public PdfVersion BaseVersion { get; }

    public int ExtensionLevel { get; }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix} (base {BaseVersion}, level {ExtensionLevel})");
}

/// <summary>
/// The version a document effectively declares, and the extensions it claims.
/// </summary>
internal static class PdfVersionResolver
{
    /// <summary>
    /// Resolves header against Catalog <c>/Version</c>. Per ISO 32000-1 the
    /// Catalog wins when it names a <em>later</em> version than the header; an
    /// earlier one is ignored rather than treated as a downgrade.
    /// </summary>
    public static PdfVersion Resolve(PdfVersion header, PdfVersion catalog)
    {
        if (!catalog.IsKnown)
            return header;
        if (!header.IsKnown)
            return catalog;
        return catalog > header ? catalog : header;
    }

    public static IReadOnlyList<PdfExtensionDeclaration> ReadExtensions(PdfObjectStore store, PdfDictionary catalog)
    {
        if (store.Resolve(catalog["Extensions"]) is not PdfDictionary extensions)
            return Array.Empty<PdfExtensionDeclaration>();

        var declarations = new List<PdfExtensionDeclaration>();
        foreach (string prefix in extensions.Keys)
        {
            if (store.Resolve(extensions[prefix]) is not PdfDictionary entry)
                continue;

            PdfVersion baseVersion = PdfVersion.ParseName((store.Resolve(entry["BaseVersion"]) as PdfName)?.Value);
            int level = store.Resolve(entry["ExtensionLevel"]) is PdfNumber number ? number.ToInt32() : 0;
            declarations.Add(new PdfExtensionDeclaration(prefix, baseVersion, level));
        }

        declarations.Sort(static (left, right) => string.CompareOrdinal(left.Prefix, right.Prefix));
        return declarations;
    }
}
