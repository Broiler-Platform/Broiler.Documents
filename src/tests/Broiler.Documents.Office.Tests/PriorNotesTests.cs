using Broiler.Documents.Corpus;
using Broiler.Documents.Office;

namespace Broiler.Documents.Office.Tests;

public sealed class PriorNotesTests : IDisposable
{
    private static readonly ExtrasSample Sample = new("ooxmlexport/data/tdf1.docx", ".docx", "docx", "tdf1.docx", null);
    private readonly string _notes = Path.Combine(Path.GetTempPath(), "office-prior-notes-tests", Guid.NewGuid().ToString("N"));

    public PriorNotesTests()
    {
        Directory.CreateDirectory(_notes);
        File.WriteAllText(Path.Combine(_notes, "b-issue.md"), "The first difference was `ooxmlexport/data/tdf1.docx`.");
        File.WriteAllText(Path.Combine(_notes, "a-short.md"), "Measured tdf1.docx on 22 pages.");
        File.WriteAllText(Path.Combine(_notes, "c-other.md"), "Only tdf11.docx, xtdf1.docx and tdf1.docx-like names.");
        File.WriteAllText(Path.Combine(_notes, "d-text.txt"), "tdf1.docx, but not a Markdown note.");
    }

    [Fact]
    public void A_Note_Matches_The_File_Name_Standing_Alone()
    {
        PriorNotes notes = PriorNotes.Load(_notes, null);
        Assert.Equal(["a-short.md", "b-issue.md"], notes.For(Sample));
        Assert.Empty(notes.For(Sample with { Id = "ooxmlexport/data/tdf2.docx" }));
        Assert.Empty(PriorNotes.Load(Path.Combine(_notes, "missing"), null).For(Sample));
    }

    [Fact]
    public void Notes_Are_Links_When_The_Commit_Is_Known()
    {
        Assert.Equal("`docs/notes/a-short.md`", PriorNotes.Load(_notes, null).Format("a-short.md"));
        Assert.Equal("[a-short.md](https://github.com/o/r/blob/abc/docs/notes/a-short.md)",
            PriorNotes.Load(_notes, "https://github.com/o/r/blob/abc/docs/notes/").Format("a-short.md"));
    }

    [Fact]
    public void The_Report_Names_Earlier_Notes_And_An_Accepted_Reference_Mismatch()
    {
        var render = new RenderRow(Sample.Id, "docx", 1, 4, 22,
            new RenderBands("far", "far", "far", "far", "heavy"),
            new RenderObserved(10, 0.5, 0.2, 0.3), BaselineState.SuspectedDefect, null, "", null, null);
        var difference = new ExtrasDifference(Sample, false, 0, null, false, null, render, null, 500, "pages differ",
            [CheckResult.Fail("pixel", "pixel/" + Sample.Id, "failed")]);
        var header = new RunHeader("tool", "LibreOffice", "poppler", null, "windows", 96, true,
            ["platform linux, here windows"]);

        string markdown = ExtrasConformance.FormatIssueMarkdown("source", header, [Sample], [difference],
            notes: PriorNotes.Load(_notes, null));

        Assert.Contains("- **Earlier Notes**: `docs/notes/a-short.md`, `docs/notes/b-issue.md`", markdown);
        Assert.Contains("| **Reference Toolchain** | **not this run's**", markdown);
        Assert.Contains("`platform linux, here windows`", markdown);
        Assert.DoesNotContain("Reference Toolchain", ExtrasConformance.FormatIssueMarkdown(
            "source", header with { ReferenceMismatches = null }, [Sample], [difference]));
    }

    public void Dispose() => Directory.Delete(_notes, recursive: true);
}
