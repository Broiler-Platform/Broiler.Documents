using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Broiler.Documents.Office;

/// <summary>
/// The repository notes under <c>docs/notes/</c> that name a sample, so the
/// report can point whoever picks up a ranked sample at what earlier triage found.
/// </summary>
/// <remarks>
/// The long-running top samples come back report after report, and each time
/// the triage started again from nothing although a note already traced them.
/// A note names a sample by its file name, with or without the corpus
/// directory, so the match is on the file name standing alone: <c>a.docx</c>
/// matches <c>data/a.docx</c> but not <c>tdf1a.docx</c>. A file name used in
/// two corpus directories matches the notes about either.
/// </remarks>
internal sealed class PriorNotes
{
    private readonly IReadOnlyList<(string Name, string Text)> _notes;
    private readonly string? _linkBase;

    private PriorNotes(IReadOnlyList<(string Name, string Text)> notes, string? linkBase)
    {
        _notes = notes;
        _linkBase = linkBase;
    }

    /// <summary>No notes, for a run without a repository checkout.</summary>
    public static PriorNotes None { get; } = new([], null);

    /// <summary>The Markdown notes directly in <paramref name="directory"/>.</summary>
    /// <param name="linkBase">
    /// The URL the note file names are appended to, or <c>null</c> to print them as paths.
    /// </param>
    public static PriorNotes Load(string directory, string? linkBase)
    {
        if (!Directory.Exists(directory))
            return None;
        return new([.. Directory.EnumerateFiles(directory, "*.md")
            .Order(StringComparer.Ordinal)
            .Select(path => (Path.GetFileName(path), File.ReadAllText(path)))], linkBase);
    }

    /// <summary>
    /// The blob URL of <c>docs/notes/</c> at the commit a GitHub Actions run checked
    /// out, or <c>null</c> outside one.
    /// </summary>
    public static string? GitHubLinkBase()
    {
        string? server = Environment.GetEnvironmentVariable("GITHUB_SERVER_URL");
        string? repository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        string? commit = Environment.GetEnvironmentVariable("GITHUB_SHA");
        return string.IsNullOrEmpty(server) || string.IsNullOrEmpty(repository) || string.IsNullOrEmpty(commit)
            ? null
            : server.TrimEnd('/') + "/" + repository + "/blob/" + commit + "/docs/notes/";
    }

    /// <summary>The names of the notes that mention <paramref name="sample"/>, in name order.</summary>
    public IReadOnlyList<string> For(ExtrasSample sample)
    {
        var name = new Regex(@"(?<![\w.-])" + Regex.Escape(Path.GetFileName(sample.Id)) + @"(?![\w-])",
            RegexOptions.CultureInvariant);
        return [.. _notes.Where(note => name.IsMatch(note.Text)).Select(note => note.Name)];
    }

    /// <summary>A note as the issue report shows it: a link when the commit is known, else its path.</summary>
    public string Format(string note) =>
        _linkBase is null ? "`docs/notes/" + note + "`" : "[" + note + "](" + _linkBase + note + ")";
}
