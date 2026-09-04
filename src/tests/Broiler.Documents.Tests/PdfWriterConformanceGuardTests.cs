using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Broiler.Documents.Tests;

/// <summary>
/// Binds the writer conformance checklist to the writer.
/// </summary>
/// <remarks>
/// <para>
/// A checklist nothing checks is a document that was accurate on the day it was
/// written. These guards make it a control: a key the writer emits and the
/// checklist does not list fails the build, and so does a row citing a test or a
/// source that is not there.
/// </para>
/// <para>
/// What they deliberately do <em>not</em> check is the clause half. Nothing here
/// can tell whether a clause reference is right, and a guard that pretended to
/// would be worse than none — it would make an unreviewed row look reviewed.
/// </para>
/// </remarks>
public sealed class PdfWriterConformanceGuardTests
{
    private const string Checklist = "tests/pdf/writer-conformance.json";
    private const string Writer = "src/Broiler.Documents.Pdf/Writing/PdfWriter.cs";

    /// <summary>
    /// Names that appear in the writer's source but are not dictionary keys it
    /// emits: filter and type values, encoding names, and the standard font
    /// names it writes after <c>/BaseFont</c>.
    /// </summary>
    private static readonly HashSet<string> NotKeys = new(StringComparer.Ordinal)
    {
        "/Catalog", "/Pages", "/Page", "/Font", "/Annot", "/Link", "/Square",
        "/Type1", "/WinAnsiEncoding", "/FlateDecode", "/URI",
    };

    private static JsonDocument Load() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(PdfGuardRoots.Component, Checklist)));

    private static IEnumerable<JsonElement> Constructs(JsonDocument checklist) =>
        checklist.RootElement.GetProperty("constructs").EnumerateArray();

    [Fact(Timeout = 600000)]
    public void Every_Key_The_Writer_Emits_Is_On_The_Checklist()
    {
        // The binding that matters. Add a key to the writer without a row here
        // and this fails, which is the only thing standing between a checklist
        // and a stale inventory.
        string source = File.ReadAllText(Path.Combine(PdfGuardRoots.Component, Writer));
        HashSet<string> emitted = Regex.Matches(source, @"/[A-Z][A-Za-z0-9]*")
            .Select(match => match.Value)
            .Where(name => !NotKeys.Contains(name))
            .ToHashSet(StringComparer.Ordinal);

        using JsonDocument checklist = Load();
        HashSet<string> listed = Constructs(checklist)
            .SelectMany(construct =>
            {
                JsonElement emits = construct.GetProperty("emits");
                IEnumerable<string> keys = emits.GetProperty("keys").EnumerateArray()
                    .Select(key => key.GetString() ?? string.Empty);
                if (emits.TryGetProperty("conditionalKeys", out JsonElement conditional))
                {
                    keys = keys.Concat(conditional.EnumerateArray()
                        .Select(key => key.GetString() ?? string.Empty));
                }

                return keys;
            })
            .ToHashSet(StringComparer.Ordinal);

        string[] unlisted = emitted.Except(listed, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unlisted.Length == 0,
            "The writer emits keys the conformance checklist does not list: " +
            string.Join(", ", unlisted));
    }

    [Fact(Timeout = 600000)]
    public void Every_Row_Cites_A_Source_That_Exists()
    {
        string source = File.ReadAllText(Path.Combine(PdfGuardRoots.Component, Writer));

        using JsonDocument checklist = Load();
        foreach (JsonElement construct in Constructs(checklist))
        {
            string cited = construct.GetProperty("emits").GetProperty("source").GetString() ?? string.Empty;

            // The row names a method; the method has to be in the writer. A row
            // pointing at code that was renamed away is a row nobody maintained.
            // The first run of this guard caught exactly that: a row citing a
            // method name that had never existed.
            string method = cited.Split('.').ElementAtOrDefault(1)?.Split(',')[0].Trim() ?? string.Empty;
            if (method.Length == 0)
                continue;

            Assert.True(
                source.Contains(method, StringComparison.Ordinal),
                $"The checklist cites {cited}, which the writer does not define.");
        }
    }

    [Fact(Timeout = 600000)]
    public void Every_Row_Cites_Evidence_That_Exists()
    {
        string tests = File.ReadAllText(Path.Combine(
            PdfGuardRoots.Component,
            "src/tests/Broiler.Documents.Pdf.Tests/PdfWriterTests.cs"));

        using JsonDocument checklist = Load();
        foreach (JsonElement construct in Constructs(checklist))
        {
            foreach (JsonElement evidence in construct.GetProperty("emits").GetProperty("evidence").EnumerateArray())
            {
                string cited = evidence.GetString() ?? string.Empty;
                string method = cited.Split('.').Last();

                Assert.True(
                    tests.Contains(method, StringComparison.Ordinal),
                    $"The checklist cites {cited} as evidence, and no such test exists.");
            }
        }
    }

    [Fact(Timeout = 600000)]
    public void Every_Row_Carries_Evidence_At_All()
    {
        using JsonDocument checklist = Load();
        foreach (JsonElement construct in Constructs(checklist))
        {
            string id = construct.GetProperty("id").GetString() ?? "?";
            Assert.True(
                construct.GetProperty("emits").GetProperty("evidence").GetArrayLength() > 0,
                $"The construct {id} claims no test proves what it emits.");
        }
    }

    [Fact(Timeout = 600000)]
    public void A_Recorded_Clause_Names_Who_Recorded_It()
    {
        // The one thing worth checking about the clause half: that a row which
        // claims to have been reviewed says by whom and when. Whether the
        // reference is correct is not a question this or any other test can
        // answer, and the checklist's own note says so.
        using JsonDocument checklist = Load();
        foreach (JsonElement construct in Constructs(checklist))
        {
            JsonElement clause = construct.GetProperty("clause");
            if ((clause.GetProperty("state").GetString() ?? string.Empty) != "recorded")
                continue;

            foreach (string field in new[] { "reference", "reviewer", "decided" })
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(clause.GetProperty(field).GetString()),
                    $"A recorded clause states its {field}.");
            }
        }
    }
}
