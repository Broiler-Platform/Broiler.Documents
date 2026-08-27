using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Broiler.Documents;

/// <summary>
/// Identifies a document format by name, MIME types, and file extensions.
/// Extensions are normalized to a leading-dot, lower-case form (for example
/// <c>.rtf</c>). Modelled on <c>Broiler.Media</c>'s <c>MediaFormatDescriptor</c>.
/// </summary>
public sealed class DocumentFormatDescriptor
{
    public DocumentFormatDescriptor(
        string name,
        IEnumerable<string>? mimeTypes = null,
        IEnumerable<string>? fileExtensions = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A document format name cannot be empty.", nameof(name));

        Name = name.Trim();
        MimeTypes = NormalizeList(mimeTypes, nameof(mimeTypes), NormalizeMimeType);
        FileExtensions = NormalizeList(fileExtensions, nameof(fileExtensions), NormalizeExtension);
    }

    public string Name { get; }

    public IReadOnlyList<string> MimeTypes { get; }

    public IReadOnlyList<string> FileExtensions { get; }

    /// <summary>True when <paramref name="extension"/> (with or without a dot) is one of ours.</summary>
    public bool MatchesExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        string normalized = NormalizeExtension(extension);
        return FileExtensions.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>True when <paramref name="mimeType"/> is one of ours (case-insensitive).</summary>
    public bool MatchesMimeType(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
            return false;

        string normalized = NormalizeMimeType(mimeType);
        return MimeTypes.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeExtension(string value)
    {
        string trimmed = value.Trim().TrimStart('*');
        if (!trimmed.StartsWith('.'))
            trimmed = "." + trimmed;
        return trimmed.ToLowerInvariant();
    }

    private static string NormalizeMimeType(string value) => value.Trim().ToLowerInvariant();

    private static ReadOnlyCollection<string> NormalizeList(
        IEnumerable<string>? values,
        string parameterName,
        Func<string, string> normalize)
    {
        if (values is null)
            return Array.AsReadOnly(Array.Empty<string>());

        string[] normalized = values
            .Select(value =>
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("Format descriptors cannot contain empty values.", parameterName);
                return normalize(value);
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Array.AsReadOnly(normalized);
    }
}
