using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Broiler.Documents.Corpus;

/// <summary>
/// The render pipeline, checked for the properties that hold on any host.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here asserts a pixel. Without a font mapping the renderer draws every
/// family with whatever face the host has, so two machines disagree about a
/// document neither of them got wrong - CI's own comment says the two legs are
/// expected to look different, and it is right. A suite that asserted a width in
/// pixels would fail on the leg that was working perfectly.
/// </para>
/// <para>
/// What does hold everywhere: the command reaches a verdict, the manifest
/// describes what it drew, the file exists, and rendering the same document
/// twice on the same host produces the same bytes. The last one is the valuable
/// one. It is what makes <c>compare --render</c> mean anything at all, because a
/// renderer that varied between runs would report differences that came from
/// itself rather than from the documents.
/// </para>
/// </remarks>
internal static class RenderChecks
{
    private const string Group = "render";

    public static async Task<IReadOnlyList<CheckResult>> RunAsync(
        BroilerDocTool tool,
        CorpusDocument document,
        string scratch,
        bool fontsAvailable)
    {
        string label = document.Label;
        if (!fontsAvailable)
        {
            return
            [
                CheckResult.Skip(Group, "render/" + label,
                    "the tool reports no host text font, so this machine cannot rasterize text at all. " +
                    "Skipped rather than failed: that is a property of the runner, not of this component."),
            ];
        }

        var results = new List<CheckResult>();
        string first = Path.Combine(scratch, "render-" + document.Id + "-" + document.Format.Key + "-1.png");
        string second = Path.Combine(scratch, "render-" + document.Id + "-" + document.Format.Key + "-2.png");

        // --continuous throughout. With pagination on, one extra line before a
        // page break shifts every later page, so a one-line difference reads as
        // a whole-document difference - and this suite would then be unable to
        // tell a layout change from a wrapping change.
        string[] arguments = ["render", document.Path, "--out", first, "--continuous", "--json"];
        ToolRun run = await tool.RunAsync(arguments, standardInput: null).ConfigureAwait(false);
        results.Add(CheckResult.ExpectExit(Group, "render/" + label, 0, run));
        if (run.ExitCode != 0)
            return results;

        JsonElement? json = run.Json();
        if (json is null || !json.Value.TryGetProperty("render", out JsonElement render))
        {
            results.Add(CheckResult.Fail(Group, "render/" + label + "/manifest",
                "the --json payload carried no render object.", run.CommandLine()));
            return results;
        }

        results.AddRange(Manifest(render, label, run));

        ToolRun again = await tool.RunAsync(
                ["render", document.Path, "--out", second, "--continuous", "--quiet"], standardInput: null)
            .ConfigureAwait(false);

        if (again.ExitCode != 0)
        {
            results.Add(CheckResult.ExpectExit(Group, "render/" + label + "/deterministic", 0, again));
            return results;
        }

        byte[] left = await File.ReadAllBytesAsync(first).ConfigureAwait(false);
        byte[] right = await File.ReadAllBytesAsync(second).ConfigureAwait(false);

        results.Add(left.AsSpan().SequenceEqual(right)
            ? CheckResult.Pass(Group, "render/" + label + "/deterministic", again.Duration)
            : CheckResult.Fail(Group, "render/" + label + "/deterministic",
                "rendering the same document twice on this host produced different bytes. Every pixel " +
                "comparison in this component assumes the renderer contributes none of the difference.",
                again.CommandLine()));

        // Both sides of a comparison go through one render with one set of
        // options, so a document rendered against itself must reach the positive
        // verdict. If it does not, no pixel comparison anywhere means anything.
        ToolRun self = await tool.RunAsync(
                ["compare", document.Path, document.Path, "--render", "--continuous", "--quiet"],
                standardInput: null)
            .ConfigureAwait(false);

        results.Add(CheckResult.ExpectExit(Group, "render/" + label + "/self-compare", 0, self));

        return results;
    }

    private static IEnumerable<CheckResult> Manifest(JsonElement render, string label, ToolRun run)
    {
        var results = new List<CheckResult>();

        int pageCount = render.TryGetProperty("renderedPageCount", out JsonElement count) ? count.GetInt32() : 0;
        results.Add(pageCount > 0
            ? CheckResult.Pass(Group, "render/" + label + "/pages")
            : CheckResult.Fail(Group, "render/" + label + "/pages",
                "the render reported success and drew no pages.", run.CommandLine()));

        if (!render.TryGetProperty("pages", out JsonElement pages))
        {
            results.Add(CheckResult.Fail(Group, "render/" + label + "/geometry",
                "the manifest carried no pages array.", run.CommandLine()));
            return results;
        }

        // Positive, and nothing more. The actual numbers depend on the host's
        // fonts; that they are positive depends on the layout having run.
        var bad = pages.EnumerateArray()
            .Select((page, index) => (index,
                width: page.GetProperty("widthPixels").GetInt32(),
                height: page.GetProperty("heightPixels").GetInt32(),
                path: page.GetProperty("path").GetString()))
            .Where(page => page.width <= 0 || page.height <= 0 ||
                           page.path is null || !File.Exists(page.path))
            .ToArray();

        results.Add(bad.Length == 0
            ? CheckResult.Pass(Group, "render/" + label + "/geometry")
            : CheckResult.Fail(Group, "render/" + label + "/geometry",
                "page(s) with no size or no file: " +
                string.Join(", ", bad.Select(page => "#" + page.index + " " + page.width + "x" + page.height)) + ".",
                run.CommandLine()));

        // Reported, never asserted. A family the document asks for and the host
        // cannot supply is drawn in the fallback face, which is a fact about the
        // machine; it belongs in the report so a reader can explain a difference,
        // not in a check that would fail the machine for its font set.
        if (render.TryGetProperty("fonts", out JsonElement fonts) &&
            fonts.TryGetProperty("unmappedFamilies", out JsonElement unmapped) &&
            unmapped.GetArrayLength() > 0)
        {
            results.Add(CheckResult.Skip(Group, "render/" + label + "/fonts",
                "drawn with the host fallback for: " +
                string.Join(", ", unmapped.EnumerateArray().Select(family => family.GetString())) +
                ". Reported, not asserted - the font set is the machine's."));
        }

        return results;
    }

    /// <summary>
    /// Whether this host can rasterize text at all, read from the tool rather
    /// than guessed from the operating system.
    /// </summary>
    public static async Task<bool> FontsAvailableAsync(BroilerDocTool tool)
    {
        ToolRun run = await tool.RunAsync("version", "--json").ConfigureAwait(false);
        JsonElement? json = run.Json();
        if (json is null || !json.Value.TryGetProperty("fallbackTextFont", out JsonElement font))
            return false;

        return font.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(font.GetString());
    }
}
