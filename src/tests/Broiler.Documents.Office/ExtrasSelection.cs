using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Broiler.Documents.Office;

/// <summary>Exact, portable extras IDs; never filesystem or download destinations supplied unchecked.</summary>
internal static class ExtrasSelection
{
    public static IReadOnlySet<string> Load(string path) => Validate(File.ReadAllLines(path)
        .Select(line => line.Trim()).Where(line => line.Length > 0 && line != "#" && !line.StartsWith("# ", StringComparison.Ordinal)));

    public static IReadOnlySet<string> Validate(IEnumerable<string> ids)
    {
        var selected = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || id.StartsWith('/') || id.Contains('\\') ||
                id.Any(c => char.IsControl(c) || c is ':' or '?') ||
                id.Split('/').Any(part => part.Length == 0 || part is "." or "..") ||
                ExtrasConformance.FormatForExtension(id) is null)
                throw new InvalidDataException("Invalid relative extras sample ID: " + id);
            selected.Add(id);
        }
        if (selected.Count == 0)
            throw new InvalidDataException("The sample list contains no supported extras IDs.");
        return selected;
    }

    public static void RequireAll(IReadOnlySet<string>? selected, IEnumerable<ExtrasSample> samples)
    {
        if (selected is null)
            return;
        var found = samples.Select(sample => sample.Id).ToHashSet(StringComparer.Ordinal);
        string[] missing = selected.Where(id => !found.Contains(id)).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException("Selected extras samples were not found: " + string.Join(", ", missing));
    }
}
