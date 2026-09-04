using System;
using System.Globalization;

namespace Broiler.Documents;

/// <summary>
/// Reading and writing the W3C-DTF timestamps that DOCX and ODT state their
/// document properties in.
/// </summary>
/// <remarks>
/// <para>
/// The whole of this type exists for one rule: a timestamp that stated no UTC
/// offset is carried without one, and none is invented (PDF roadmap §6.2).
/// Parsing such a value into a zoned type would silently attribute the converting
/// machine's zone to the document, and a round trip would then write back a
/// statement its author never made. Which zone a file was saved in is not always
/// knowable, and "not stated" is the accurate answer when it is not.
/// </para>
/// <para>
/// PDF states its dates in its own <c>D:YYYYMMDDHHmmSS</c> syntax and parses them
/// in the PDF codec; what it shares with these two is the
/// <see cref="DocumentDate"/> the result carries, not the grammar.
/// </para>
/// </remarks>
public static class DocumentTimestamp
{
    /// <summary>
    /// Parses a W3C-DTF timestamp, recording whether it stated a zone.
    /// </summary>
    /// <returns>False when the value is not a timestamp this build reads.</returns>
    public static bool TryParse(string? value, out DocumentDate date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (StatesZone(value))
        {
            if (!DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTimeOffset offset))
            {
                return false;
            }

            date = DocumentDate.WithOffset(offset);
            return true;
        }

        if (!DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime local))
        {
            return false;
        }

        date = DocumentDate.WithoutOffset(local);
        return true;
    }

    /// <summary>
    /// Renders a timestamp back to W3C-DTF, in the form it arrived in. A
    /// zone-less value is written zone-less, which the profile permits.
    /// </summary>
    public static string ToW3cdtf(DocumentDate date)
    {
        if (!date.HasUtcOffset)
            return date.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

        return date.Value.Offset == TimeSpan.Zero
            ? date.Value.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture)
            : date.Value.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Whether the value carries an explicit zone: a trailing <c>Z</c>, or a
    /// signed offset after the time. A date with no time states neither.
    /// </summary>
    private static bool StatesZone(string value)
    {
        int t = value.IndexOf('T', StringComparison.Ordinal);
        if (t < 0)
            return false;

        ReadOnlySpan<char> time = value.AsSpan(t + 1);
        return time.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
               time.Contains('+') ||
               time.LastIndexOf('-') > 0;
    }
}
