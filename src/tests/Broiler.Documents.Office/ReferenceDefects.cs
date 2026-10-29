using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Broiler.Documents.Office;

/// <summary>
/// One LibreOffice defect that makes the pinned reference wrong for some
/// samples, and the release that fixed it.
/// </summary>
/// <param name="Id">A stable name for the row.</param>
/// <param name="Bug">
/// The LibreOffice bug, <c>tdf#N</c>, or <c>gerrit#N</c>, the core review that
/// merged a fix that names no bug.
/// </param>
/// <param name="FixedIn">The first LibreOffice release, major.minor, whose rendering is right.</param>
/// <param name="Axes">The checks the defect makes meaningless: <c>pixel</c>, <c>text</c> or both.</param>
/// <param name="Reference">The repository note that shows the defect.</param>
internal sealed record ReferenceDefect(string Id, string Bug, Version FixedIn, IReadOnlySet<string> Axes, string Reference)
{
    /// <summary>True when the defect makes <paramref name="axis"/> meaningless.</summary>
    public bool Covers(string axis) => Axes.Contains(axis);

    /// <summary>Why a check was not compared, as a skipped check and the report say it.</summary>
    public string Describe(string libreOffice) =>
        libreOffice + " draws this sample with LibreOffice defect " + Bug + ", fixed in LibreOffice " +
        FixedIn.ToString(2) + ", so it is not a reference; see " + Reference + ".";
}

/// <summary>
/// The samples whose LibreOffice reference is a LibreOffice defect, read from
/// <c>tests/office/extras-reference-defects.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// The corpus is LibreOffice's master tree; the references come from the
/// LibreOffice the reference workflow pins, which is older. A sample added
/// with an upstream fix tests that fix, and the pinned release draws it the way
/// the fix corrected. Ranked against that, a sample <c>broilerdoc</c> draws as
/// the fixed LibreOffice and Word do stays at the top of every report, and
/// moving toward the reference would be moving toward the defect.
/// </para>
/// <para>
/// So a listed sample's named checks are not compared while the reference is
/// older than the fix. <c>broilerdoc</c> still reads and renders it and has to
/// do so with a documented exit code, and every other check runs as usual.
/// A row stops applying by itself once references come from a fixed release.
/// </para>
/// </remarks>
internal sealed class ReferenceDefects
{
    private readonly Dictionary<string, ReferenceDefect> _bySample;

    private ReferenceDefects(Dictionary<string, ReferenceDefect> bySample) => _bySample = bySample;

    /// <summary>An empty list, for a run that reads none.</summary>
    public static ReferenceDefects None { get; } = new(new Dictionary<string, ReferenceDefect>(StringComparer.Ordinal));

    /// <summary>How many samples the file lists.</summary>
    public int Count => _bySample.Count;

    public static ReferenceDefects Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The reference-defects file does not exist: " + path, path);

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            var bySample = new Dictionary<string, ReferenceDefect>(StringComparer.Ordinal);
            foreach (JsonElement row in document.RootElement.GetProperty("defects").EnumerateArray())
            {
                string id = row.GetProperty("id").GetString() ?? string.Empty;
                Version fixedIn = ParseRelease(row.GetProperty("fixedIn").GetString())
                    ?? throw new InvalidDataException(path + ": " + id + " has no major.minor fixedIn.");
                var defect = new ReferenceDefect(
                    id,
                    row.GetProperty("bug").GetString() ?? string.Empty,
                    fixedIn,
                    row.GetProperty("axes").EnumerateArray().Select(axis => axis.GetString() ?? string.Empty)
                        .ToHashSet(StringComparer.Ordinal),
                    row.GetProperty("reference").GetString() ?? string.Empty);

                foreach (JsonElement sample in row.GetProperty("samples").EnumerateArray())
                {
                    string name = sample.GetString() ?? string.Empty;
                    if (!bySample.TryAdd(name, defect))
                        throw new InvalidDataException(path + " lists " + name + " twice.");
                }
            }

            return new ReferenceDefects(bySample);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException(path + " could not be read: " + exception.Message, exception);
        }
    }

    /// <summary>
    /// The defect that makes <paramref name="sample"/>'s reference wrong when it
    /// was made by <paramref name="libreOffice"/>, or null where none does: the
    /// sample is not listed, or that LibreOffice already has the fix, or its
    /// version cannot be read, which compares as usual rather than hide a check.
    /// </summary>
    public ReferenceDefect? For(ExtrasSample sample, string? libreOffice)
    {
        if (!_bySample.TryGetValue(sample.Id, out ReferenceDefect? defect))
            return null;

        return ParseRelease(libreOffice) is Version version && version < defect.FixedIn ? defect : null;
    }

    /// <summary>
    /// The major.minor release in a version string such as
    /// <c>LibreOffice 24.2.7.2 420(Build:2)</c> or <c>26.8</c>.
    /// </summary>
    public static Version? ParseRelease(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        Match match = Regex.Match(text, @"(\d+)\.(\d+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success
            ? new Version(
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))
            : null;
    }
}
