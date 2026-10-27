using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace Broiler.Documents.Office;

/// <summary>Atomic, explicitly incomplete results independent of the final reports.</summary>
internal static class ExtrasCheckpoint
{
    public static void Write(string reportPath, string source, RunHeader header,
        IReadOnlyList<ExtrasSample> samples, IReadOnlyList<ExtrasDifference> completed,
        int topCount, double threshold)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(reportPath))!;
        Directory.CreateDirectory(directory);
        string jsonPath = Path.Combine(directory, "office-partial-results.json");
        string temporary = jsonPath + ".tmp";
        OfficeReporting.WriteJson(temporary,
            completed.SelectMany(d => d.Checks).ToArray(),
            completed.Where(d => d.Semantic is not null).Select(d => d.Semantic!).ToArray(),
            completed.Where(d => d.Render is not null).Select(d => d.Render!).ToArray(),
            header, ExtrasConformance.TopDifferences(completed, topCount), threshold);
        JsonObject json = JsonNode.Parse(File.ReadAllText(temporary))!.AsObject();
        json["runComplete"] = false;
        json["completedSamples"] = completed.Count;
        json["totalSamples"] = samples.Count;
        json["checkpointUtc"] = DateTimeOffset.UtcNow.ToString("O");
        File.WriteAllText(temporary, json.ToJsonString(), new UTF8Encoding(false));
        File.Move(temporary, jsonPath, overwrite: true);

        string markdownPath = Path.Combine(directory, "office-partial-differences.md");
        string markdown = "# Incomplete Office conformance run\n\n" +
            $"**Checkpoint only: {completed.Count}/{samples.Count} documents completed.** " +
            "These findings cover only completed documents, not the full corpus.\n\n" +
            ExtrasConformance.FormatIssueMarkdown(source, header,
                completed.Select(d => d.Sample).ToArray(), completed, topCount, threshold);
        File.WriteAllText(markdownPath + ".tmp", markdown, new UTF8Encoding(false));
        File.Move(markdownPath + ".tmp", markdownPath, overwrite: true);
    }
}
