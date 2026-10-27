using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Documents.Office;

// Bump the schema when conversion filters or fallback semantics change.
internal sealed record ReferenceEnvironment(int SchemaVersion, string Image, string LibreOffice,
    string Platform, string? FontSet, string FontsSha256, double TimeoutSeconds);
internal sealed record ReferenceOutput(string Status, string? File, string? Sha256,
    int ExitCode, bool TimedOut, string? Filter, string Error);
internal sealed record ReferenceSample(string Id, string InputSha256, string? CrashFamily,
    ReferenceOutput? Text, ReferenceOutput? Pdf);
internal sealed record ReferenceManifest(ReferenceEnvironment Environment, string CreatedUtc,
    string Source, string CorpusSha256, IReadOnlyList<ReferenceSample> Samples);

/// <summary>Immutable, validated LibreOffice text/PDF projections; never caches broilerdoc output.</summary>
internal sealed class OfficeReferences
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _root;
    private readonly Dictionary<string, ReferenceSample> _samples;
    private OfficeReferences(string root, IEnumerable<ReferenceSample> samples)
    {
        _root = root;
        _samples = samples.ToDictionary(sample => sample.Id, StringComparer.Ordinal);
    }

    public static ReferenceEnvironment EnvironmentStamp(string image, string libreOffice,
        string platform, string? fontSet, IReadOnlyList<string> fontDirectories, TimeSpan timeout)
    {
        int digestStart = image?.LastIndexOf("@sha256:", StringComparison.Ordinal) ?? -1;
        if (digestStart < 1 || image!.Length != digestStart + 8 + 64 ||
            !image[(digestStart + 8)..].All(Uri.IsHexDigit))
            throw new InvalidDataException("References require --reference-image with a pinned image digest.");
        if (fontDirectories.Count == 0)
            throw new InvalidDataException("References require explicit pinned font directories.");
        var fonts = new StringBuilder();
        foreach (string directory in fontDirectories)
        {
            if (!Directory.Exists(directory))
                throw new InvalidDataException("Missing reference font directory: " + directory);
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                         .OrderBy(file => Path.GetRelativePath(directory, file), StringComparer.Ordinal))
                fonts.Append(Path.GetRelativePath(directory, file).Replace('\\', '/')).Append('\0')
                    .Append(HashFile(file)).Append('\n');
        }
        return new ReferenceEnvironment(1, image, libreOffice, platform, fontSet,
            HashBytes(Encoding.UTF8.GetBytes(fonts.ToString())), timeout.TotalSeconds);
    }

    internal static string HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
    private static string HashBytes(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static async Task PrepareAsync(string directory, string workspace, string source,
        IReadOnlyList<ExtrasSample> samples, CrashRegressions crashRegressions,
        ReferenceEnvironment environment, LibreOfficeTool tool, TimeSpan timeout, int jobs)
    {
        string root = Path.GetFullPath(directory);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new InvalidDataException("Reference output directory must be empty: " + root);
        if (samples.Count == 0)
            throw new InvalidDataException("Cannot prepare an empty reference corpus.");
        Directory.CreateDirectory(root);
        var entries = new List<ReferenceSample>();
        int complete = 0;
        await Parallelism.ForEachAsync(samples, jobs, sample =>
        {
            string hash = HashFile(sample.LocalPath);
            string? family = crashRegressions.FamilyOf(sample);
            ReferenceOutput? text = null, pdf = null;
            if (family is null)
            {
                using var area = new OfficeSampleWorkspace(workspace, keep: false);
                string key = HashBytes(Encoding.UTF8.GetBytes(sample.Id));
                text = SaveOutput(root, key + ".txt", Text(tool, sample, area.Root, area.NextProfile, timeout));
                pdf = SaveOutput(root, key + ".pdf", Pdf(tool, sample, area.Root, area.NextProfile, timeout));
            }
            lock (entries) entries.Add(new ReferenceSample(sample.Id, hash, family, text, pdf));
            int count = Interlocked.Increment(ref complete);
            if (count % 40 == 0 || count == samples.Count)
                Console.WriteLine("references  " + count.ToString(CultureInfo.InvariantCulture) + "/" + samples.Count);
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        ReferenceSample[] ordered = entries.OrderBy(entry => entry.Id, StringComparer.Ordinal).ToArray();
        if (!ordered.Any(entry => entry.Text?.Status == "available" || entry.Pdf?.Status == "available"))
            throw new InvalidDataException("LibreOffice produced no reference outputs; refusing to publish.");
        var manifest = new ReferenceManifest(environment, DateTimeOffset.UtcNow.ToString("O"), source,
            HashBytes(Encoding.UTF8.GetBytes(string.Join("\n", ordered.Select(entry => entry.Id + "\0" + entry.InputSha256)))), ordered);
        // Written last: interrupted generations never look complete.
        File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(manifest, Json));
        Console.WriteLine("references  unavailable projections: text=" +
            ordered.Count(entry => entry.Text?.Status == "unavailable") + ", PDF=" +
            ordered.Count(entry => entry.Pdf?.Status == "unavailable") +
            "; crash-regression inputs set aside=" + ordered.Count(entry => entry.CrashFamily is not null));
    }

    internal static ReferenceOutput SaveOutput(string root, string name, LibreOfficeRun run)
    {
        if (run.ExitCode == -1 && !run.TimedOut)
            throw new IOException("LibreOffice could not start during reference generation: " + run.StandardError);
        bool available = !run.TimedOut && run.ExitCode == 0 && run.OutputPath is not null && File.Exists(run.OutputPath);
        string? file = null, hash = null;
        if (available)
        {
            file = name;
            File.Copy(run.OutputPath!, Path.Combine(root, file));
            hash = HashFile(Path.Combine(root, file));
        }
        return new ReferenceOutput(available ? "available" : "unavailable", file, hash,
            run.ExitCode, run.TimedOut, run.FilterUsed,
            run.StandardError.Length > 4096 ? run.StandardError[..4096] : run.StandardError);
    }

    public static OfficeReferences Load(string directory, ReferenceEnvironment expected,
        IReadOnlyList<ExtrasSample> samples, CrashRegressions crashRegressions)
    {
        string root = Path.GetFullPath(directory);
        ReferenceManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ReferenceManifest>(File.ReadAllText(Path.Combine(root, "manifest.json")))
                ?? throw new InvalidDataException("Empty reference manifest.");
            if (manifest.Environment != expected)
                throw new InvalidDataException("Reference toolchain mismatch (image, LibreOffice, platform, fonts, filters or timeout). Regenerate references.");
            if (manifest.Samples is null || manifest.Samples.Count == 0)
                throw new InvalidDataException("Reference manifest contains no samples.");
            var store = new OfficeReferences(root, manifest.Samples);
            foreach (ExtrasSample sample in samples)
            {
                if (!store._samples.TryGetValue(sample.Id, out ReferenceSample? entry))
                    throw new InvalidDataException("Reference is missing sample: " + sample.Id);
                if (entry.InputSha256 != HashFile(sample.LocalPath))
                    throw new InvalidDataException("Reference input hash mismatch: " + sample.Id);
                if (entry.CrashFamily != crashRegressions.FamilyOf(sample))
                    throw new InvalidDataException("Reference crash-regression classification changed: " + sample.Id);
                if (entry.CrashFamily is null)
                {
                    store.Validate(entry.Text, sample.Id + " text");
                    store.Validate(entry.Pdf, sample.Id + " PDF");
                }
            }
            return store;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NullReferenceException)
        {
            throw new InvalidDataException("Invalid reference manifest: " + exception.Message, exception);
        }
    }

    private void Validate(ReferenceOutput? output, string label)
    {
        if (output is null || output.Status is not ("available" or "unavailable"))
            throw new InvalidDataException("Missing/invalid reference status: " + label);
        if (output.Status == "unavailable")
        {
            if (output.File is not null || output.Sha256 is not null)
                throw new InvalidDataException("Unavailable reference contains an output: " + label);
            return;
        }
        if (string.IsNullOrEmpty(output.File) || output.File != Path.GetFileName(output.File) ||
            output.File.Contains('\\') || output.File.Contains('/') || output.File.Contains(':') || output.File is "." or ".." ||
            output.ExitCode != 0 || output.TimedOut)
            throw new InvalidDataException("Invalid reference output: " + label);
        string path = Path.Combine(_root, output.File);
        if (!File.Exists(path) || output.Sha256 != HashFile(path))
            throw new InvalidDataException("Reference output is missing or corrupt: " + label);
    }

    public LibreOfficeRun Get(ExtrasSample sample, bool pdf)
    {
        ReferenceOutput output = (pdf ? _samples[sample.Id].Pdf : _samples[sample.Id].Text)!;
        return new LibreOfficeRun([], output.ExitCode, string.Empty, output.Error, TimeSpan.Zero,
            output.Filter, output.File is null ? null : Path.Combine(_root, output.File)) { TimedOut = output.TimedOut };
    }

    // Shared by live evaluation and generation so their filters/fallbacks cannot drift.
    public static LibreOfficeRun Text(LibreOfficeTool tool, ExtrasSample sample, string workspace,
        Func<string> nextProfile, TimeSpan timeout) => tool.Convert(sample.LocalPath, LibreOfficeFilters.Text,
        Path.Combine(workspace, "lo-text"), nextProfile(), timeout, LibreOfficeTool.InputFilterFor(sample.Extension));

    public static LibreOfficeRun Pdf(LibreOfficeTool tool, ExtrasSample sample, string workspace,
        Func<string> nextProfile, TimeSpan timeout)
    {
        string output = Path.Combine(workspace, "lo-pdf");
        LibreOfficeRun run = tool.Convert(sample.LocalPath,
            sample.Extension == ".pdf" ? "pdf" : LibreOfficeFilters.Pdf, output, nextProfile(), timeout,
            LibreOfficeTool.InputFilterFor(sample.Extension));
        bool available = !run.TimedOut && run.ExitCode == 0 && run.OutputPath is not null && File.Exists(run.OutputPath);
        if (sample.Extension == ".pdf")
            return available ? run : run with { ExitCode = 0, TimedOut = false, OutputPath = sample.LocalPath };
        if (!available)
            run = tool.Convert(sample.LocalPath, "pdf", output, nextProfile(), timeout,
                LibreOfficeTool.InputFilterFor(sample.Extension));
        return run;
    }
}
