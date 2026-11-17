using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Broiler.Documents.Office;

/// <summary>Exact encrypted inputs, never a blanket exemption for exit 3.</summary>
internal sealed class ReSaveRejections
{
    private readonly Dictionary<string, (string Hash, string Reason)> _samples = new(StringComparer.Ordinal);

    public static ReSaveRejections Load(string path)
    {
        try { return LoadPolicy(path); }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or
            InvalidOperationException or ArgumentException)
        {
            throw new InvalidDataException("Invalid re-save rejection policy: " + path, exception);
        }
    }

    private static ReSaveRejections LoadPolicy(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported re-save rejection policy version.");
        var policy = new ReSaveRejections();
        foreach (JsonElement row in document.RootElement.GetProperty("samples").EnumerateArray())
        {
            string id = row.GetProperty("id").GetString()!;
            ExtrasSelection.Validate([id]);
            string hash = row.GetProperty("sha256").GetString()!;
            string reason = row.GetProperty("reason").GetString()!;
            if (!Regex.IsMatch(hash, "^[0-9a-f]{64}$") || string.IsNullOrWhiteSpace(reason) ||
                row.GetProperty("expectedExitCode").GetInt32() != 3 ||
                !policy._samples.TryAdd(id, (hash, reason)))
                throw new InvalidDataException("Invalid or duplicate re-save rejection entry: " + id);
        }
        return policy;
    }

    public string? ReasonFor(ExtrasSample sample, int exitCode)
    {
        if (exitCode != 3 || !_samples.TryGetValue(sample.Id, out var entry)) return null;
        using var input = File.OpenRead(sample.LocalPath);
        return Convert.ToHexStringLower(SHA256.HashData(input)) == entry.Hash ? entry.Reason : null;
    }
}
