using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Broiler.Documents.Office;

/// <summary>
/// The upstream samples that are fuzzer output rather than documents, read from
/// <c>tests/office/extras-crash-regressions.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// LibreOffice keeps these as crash regressions, and the test for each asserts
/// only that LibreOffice survives it. What it draws from one is its handling of
/// the damage, which is not a reference: the first ranked run was three of them,
/// and every fix that read them more faithfully pushed them further up, because
/// the pages LibreOffice adds are the ones it invents.
/// </para>
/// <para>
/// So a matching sample is set aside rather than ranked. It is not converted by
/// LibreOffice at all, and <c>broilerdoc</c> is still held to handling it - a
/// crash or a hang on fuzz input is a defect whatever the reference says.
/// </para>
/// </remarks>
internal sealed class CrashRegressions
{
    private readonly IReadOnlyList<(string Id, Regex Pattern)> _families;

    private CrashRegressions(IReadOnlyList<(string Id, Regex Pattern)> families) => _families = families;

    /// <summary>The family ids, in the order the file lists them.</summary>
    public IReadOnlyList<string> FamilyIds => [.. _families.Select(family => family.Id)];

    public static CrashRegressions Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The crash-regression families file does not exist: " + path, path);

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            var families = new List<(string, Regex)>();
            foreach (JsonElement family in document.RootElement.GetProperty("families").EnumerateArray())
            {
                string id = family.GetProperty("id").GetString() ?? string.Empty;
                string pattern = family.GetProperty("pattern").GetString() ?? string.Empty;
                if (id.Length == 0 || pattern.Length == 0)
                    throw new InvalidDataException(path + " has a family with no id or no pattern.");

                families.Add((id, new Regex(
                    pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1))));
            }

            return new CrashRegressions(families);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or
                                              InvalidOperationException or ArgumentException)
        {
            throw new InvalidDataException(path + " could not be read: " + exception.Message, exception);
        }
    }

    /// <summary>
    /// The family a sample belongs to, or null for a document. Matched against
    /// the file name, so a directory called <c>forcepoint</c> would not set aside
    /// everything in it.
    /// </summary>
    public string? FamilyOf(ExtrasSample sample)
    {
        string name = Path.GetFileName(sample.Id);
        foreach ((string id, Regex pattern) in _families)
        {
            if (pattern.IsMatch(name))
                return id;
        }

        return null;
    }
}
