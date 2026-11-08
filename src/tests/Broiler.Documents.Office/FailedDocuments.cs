using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Broiler.Documents.Office;

/// <summary>
/// Persistence and resolution of tracked failed documents in the repository.
/// Used by the incremental conformance workflow to only recheck failing documents.
/// </summary>
internal static class FailedDocuments
{
    public const string DefaultRelativePath = "tests/office/failed-documents.txt";

    public static string DefaultPath(string repositoryRoot) =>
        Path.Combine(repositoryRoot, "tests", "office", "failed-documents.txt");

    public static IReadOnlySet<string> Load(string path)
    {
        if (!File.Exists(path))
            return new SortedSet<string>(StringComparer.Ordinal);

        string[] lines = File.ReadAllLines(path);
        var ids = lines
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && line != "#" && !line.StartsWith("# ", StringComparison.Ordinal));

        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || id.StartsWith('/') || id.Contains('\\') ||
                id.Any(c => char.IsControl(c) || c is ':' or '?') ||
                id.Split('/').Any(part => part.Length == 0 || part is "." or "..") ||
                ExtrasConformance.FormatForExtension(id) is null)
            {
                throw new InvalidDataException("Invalid relative extras sample ID in tracked failed documents: " + id);
            }

            set.Add(id);
        }

        return set;
    }

    public static void Save(string path, IEnumerable<string> failedSampleIds)
    {
        var sorted = failedSampleIds
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.AppendLine("# Broiler.Documents Office Conformance - Tracked Failed Documents");
        sb.AppendLine("# This file tracks extras corpus sample documents with conformance failures.");
        sb.AppendLine("# Rechecked when running the office conformance suite with --incremental.");
        foreach (string id in sorted)
        {
            sb.AppendLine(id);
        }

        File.WriteAllText(path, sb.ToString().ReplaceLineEndings("\n"), new UTF8Encoding(false));
    }

    public static IReadOnlySet<string> UpdateTracked(
        IEnumerable<string> previouslyTracked,
        IEnumerable<string> evaluatedIds,
        IEnumerable<string> failedIds)
    {
        var evaluatedSet = evaluatedIds.ToHashSet(StringComparer.Ordinal);
        var failedSet = failedIds.ToHashSet(StringComparer.Ordinal);

        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string id in previouslyTracked)
        {
            if (!evaluatedSet.Contains(id))
            {
                result.Add(id);
            }
            else if (failedSet.Contains(id))
            {
                result.Add(id);
            }
        }

        foreach (string id in failedSet)
        {
            result.Add(id);
        }

        return result;
    }
}
