using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Broiler.Documents.Office;

/// <summary>
/// One row of the external-tool register, reduced to the fields a runner has
/// to act on.
/// </summary>
/// <remarks>
/// The row on disk carries a good deal more than this - the licence
/// obligations, the acquisition recipe, the isolation the process runs under -
/// and none of it changes what the runner does with the tool. It stays in the
/// file, where a reviewer reads it, rather than being copied into a type that
/// would then have to be kept in step with the file for no gain.
/// </remarks>
internal sealed record RegisteredTool(
    string Id,
    string Role,
    string Name,
    string? Version,
    string Licence,
    string ReviewState,
    string? Reviewer,
    string? Decided,
    string Why)
{
    /// <summary>
    /// True only when the row says approved and names both the person who
    /// approved it and the day they did.
    /// </summary>
    /// <remarks>
    /// The three fields are read together on purpose. A state of "approved"
    /// over a null reviewer is what a row looks like when somebody changed
    /// the word and left the decision to be filled in later, and reading that
    /// as an approval would defeat the whole file. The schema refuses to
    /// validate such a row and the PDF suite's
    /// <c>No_Tool_Is_Approved_Merely_By_Being_Written_Down</c> fails on it;
    /// this is the same rule in the one place that decides whether a process
    /// is actually spawned.
    /// </remarks>
    public bool IsApproved =>
        string.Equals(ReviewState, "approved", StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(Reviewer)
        && !string.IsNullOrWhiteSpace(Decided);
}

/// <summary>
/// The external tools this suite is permitted to drive, as
/// <c>tests/office/tools/manifest.json</c> records them.
/// </summary>
/// <remarks>
/// <para>
/// The rule the register encodes is the one the PDF manifest already states:
/// a tool absent from the file may not run in CI, and no entry in it may
/// become a product reference. This is a documents-scoped file rather than a
/// second set of rows in <c>tests/pdf/tools/manifest.json</c>, because that
/// schema asks a tool for a commit hash, build flags and the ICC profiles the
/// build enabled - fair questions for something compiled from source against
/// the PDF codec, and unanswerable ones for whatever LibreOffice a
/// distribution installed. ADR 0013 gives the other half of the reason: one
/// format's pending row must not gate another format's suite.
/// </para>
/// <para>
/// Note where the rule bites - CI, not a developer's machine. So this type
/// decides nothing. It answers a query, <see cref="Blocked"/>, and the runner
/// gates its exit code on the answer while a developer passes
/// <c>--allow-pending-tools</c> and drives the tool anyway. Moving the decision in
/// here, by refusing to hand back a pending row at all, would have produced a
/// suite nobody could run until a review completed that nobody was blocked
/// on, which is a slower way of making the suite worse.
/// </para>
/// </remarks>
internal sealed class ToolRegister
{
    /// <summary>Where the register lives, relative to the component root.</summary>
    public const string RelativePath = "tests/office/tools/manifest.json";

    private readonly bool _found;
    private readonly Dictionary<string, RegisteredTool> _tools;

    private ToolRegister(string manifestPath, bool found, Dictionary<string, RegisteredTool> tools)
    {
        ManifestPath = manifestPath;
        _found = found;
        _tools = tools;
    }

    /// <summary>The file this register was read from, named in every reason it gives.</summary>
    public string ManifestPath { get; }

    /// <summary>Every row, in no particular order, for a report that lists them.</summary>
    public IReadOnlyCollection<RegisteredTool> Tools => _tools.Values;

    public static string PathIn(string componentRoot) =>
        Path.Combine(componentRoot, RelativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Reads the register, or an empty one when the file is not there.</summary>
    /// <remarks>
    /// A missing register is not thrown for. Its absence has an obvious
    /// meaning - no tool has been written down, so no tool may be driven -
    /// and that is exactly what an empty register goes on to say through
    /// <see cref="Blocked"/>, naming the absolute path it looked in.
    /// Throwing here would turn a suite that should skip and say why into one
    /// that dies before it prints anything, which is the failure mode this
    /// component keeps deciding against.
    /// </remarks>
    public static ToolRegister Load(string path)
    {
        string full = Path.GetFullPath(path);
        var tools = new Dictionary<string, RegisteredTool>(StringComparer.Ordinal);

        if (!File.Exists(full))
            return new ToolRegister(full, found: false, tools);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(full));
        if (document.RootElement.TryGetProperty("tools", out JsonElement rows)
            && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement row in rows.EnumerateArray())
            {
                RegisteredTool tool = ReadRow(row);

                // Two rows for one id is a review problem, and the guard
                // tests are where a review problem gets reported. The runner
                // keeps the first and carries on, because falling over here
                // would suppress every other thing the run had to say.
                tools.TryAdd(tool.Id, tool);
            }
        }

        return new ToolRegister(full, found: true, tools);
    }

    public RegisteredTool? Find(string id) =>
        _tools.TryGetValue(id, out RegisteredTool? tool) ? tool : null;

    /// <summary>
    /// Null when the tool may be driven, otherwise the reason it may not,
    /// phrased for a skip line that names whose decision is outstanding and
    /// where the row is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A run that skips every one of its checks because a row is still pending
    /// is a correct suite reporting its state, not a broken one. Both of the
    /// rows this suite drives were approved on 2026-09-06, so that is no longer
    /// the state a reader will meet - but the code has to keep behaving that way
    /// for the next tool somebody adds, and the reasons below are still written
    /// to be read on their own, without this file open, so that a report says
    /// whose decision is outstanding rather than only that something was
    /// skipped. A green run with a large skip count is exactly the observation a
    /// reader files a bug about, and it deserves an answer in the report rather
    /// than in a comment.
    /// </para>
    /// <para>
    /// <paramref name="allowPending"/> is the local switch. It reaches a
    /// pending row and a missing one - the two states that mean nobody has
    /// decided yet - and deliberately does not reach a rejected row, because
    /// a rejection is a decision that was taken rather than one that is
    /// outstanding, and a switch for working while a review is open has no
    /// business overriding a review that closed. CI passes false and gates on
    /// the result.
    /// </para>
    /// </remarks>
    public string? Blocked(string id, bool allowPending)
    {
        RegisteredTool? tool = Find(id);

        if (tool is not null && tool.IsApproved)
            return null;

        if (tool is not null && string.Equals(tool.ReviewState, "rejected", StringComparison.Ordinal))
        {
            string who = string.IsNullOrWhiteSpace(tool.Reviewer) ? string.Empty : " by " + tool.Reviewer;
            string when = string.IsNullOrWhiteSpace(tool.Decided) ? string.Empty : " on " + tool.Decided;
            return tool.Name + " was reviewed and rejected" + who + when + ": " + tool.Why +
                   " The row is in " + ManifestPath + ", and a rejection is not something " +
                   "--allow-pending-tools overrides.";
        }

        if (allowPending)
            return null;

        if (tool is null)
        {
            string missing =
                "'" + id + "' is not in the external-tool register at all, so CI may not drive it. " +
                "The register is " + ManifestPath + ", and a row there is where a tool's licence, " +
                "its acquisition and its isolation get written down for somebody to review.";

            // Naming the path is not quite enough when the file itself is
            // absent: that answers the same way for every tool, and a reader
            // chasing a review that was never needed would waste the trip.
            return _found
                ? missing
                : missing + " That file was not found, which reads more like a checkout or a " +
                  "working-directory problem than a review one.";
        }

        if (string.Equals(tool.ReviewState, "approved", StringComparison.Ordinal))
        {
            string absent = string.IsNullOrWhiteSpace(tool.Reviewer) ? "no reviewer" : "no decision date";
            return tool.Name + "'s row says approved but names " + absent + ", which is not an " +
                   "approval. Until the row in " + ManifestPath + " carries both, the decision is " +
                   "still outstanding and CI may not drive the tool.";
        }

        return tool.Name + "'s review state is '" + tool.ReviewState + "': the project reviewer's " +
               "decision is outstanding, and the row in " + ManifestPath + " is where it gets " +
               "recorded. Pass --allow-pending-tools to drive it locally; CI does not pass it, and this " +
               "skip is what the suite is supposed to do until the decision is taken.";
    }

    private static RegisteredTool ReadRow(JsonElement row)
    {
        JsonElement identity = Child(row, "identity");
        JsonElement licensing = Child(row, "licensing");
        JsonElement review = Child(row, "review");

        return new RegisteredTool(
            Text(row, "id"),
            Text(row, "role"),
            Text(identity, "name"),
            Optional(identity, "version"),
            Text(licensing, "licence"),
            Text(review, "state"),
            Optional(review, "reviewer"),
            Optional(review, "decided"),
            Text(review, "why"));
    }

    // Reading is forgiving about shape and strict about nothing, because the
    // schema and the guard tests are what judge a row. A loader that also
    // judged it would report the same fault twice, in a place where the only
    // thing it could do about it is throw.
    private static JsonElement Child(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) ? value : default;

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string? Optional(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
