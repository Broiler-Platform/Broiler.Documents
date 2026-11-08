using System;
using System.IO;
using System.Linq;
using Broiler.Documents.Office;
using Xunit;

namespace Broiler.Documents.Office.Tests;

public sealed class IncrementalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "office-incremental-tests", Guid.NewGuid().ToString("N"));

    public IncrementalTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Options_Parse_Incremental_Sets_Flags()
    {
        var options = Program.Options.Parse(["--incremental"]);
        Assert.True(options.Incremental);
        Assert.True(options.IsExtrasRun);
        Assert.Null(options.FailedDocumentsPath);
    }

    [Fact]
    public void Options_Parse_Incremental_With_Custom_Path()
    {
        var options = Program.Options.Parse(["--incremental", "--failed-documents", "path/to/failed.txt"]);
        Assert.True(options.Incremental);
        Assert.True(options.IsExtrasRun);
        Assert.Equal("path/to/failed.txt", options.FailedDocumentsPath);
    }

    [Fact]
    public void Options_Parse_Incremental_Mutually_Exclusive_With_Sample_And_SampleList()
    {
        var ex1 = Assert.Throws<ArgumentException>(() => Program.Options.Parse(["--incremental", "--sample", "doc.docx"]));
        Assert.Contains("mutually exclusive", ex1.Message, StringComparison.OrdinalIgnoreCase);

        var ex2 = Assert.Throws<ArgumentException>(() => Program.Options.Parse(["--incremental", "--sample-list", "cases.txt"]));
        Assert.Contains("mutually exclusive", ex2.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Options_Parse_Incremental_Cannot_Update_Global_GitHub_Issue()
    {
        var ex = Assert.Throws<ArgumentException>(() => Program.Options.Parse(["--incremental", "--create-github-issue"]));
        Assert.Contains("cannot update the global GitHub ranking issue", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Options_Parse_Incremental_Cannot_Be_Used_With_Seed_Corpus_Or_Update_Baseline()
    {
        var ex1 = Assert.Throws<ArgumentException>(() => Program.Options.Parse(["--incremental", "--seed-corpus"]));
        Assert.Contains("extras runs", ex1.Message, StringComparison.OrdinalIgnoreCase);

        var ex2 = Assert.Throws<ArgumentException>(() => Program.Options.Parse(["--incremental", "--update-baseline"]));
        Assert.Contains("extras runs", ex2.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailedDocuments_Load_NonExistent_Returns_Empty()
    {
        string missingPath = Path.Combine(_root, "does-not-exist.txt");
        var result = FailedDocuments.Load(missingPath);
        Assert.Empty(result);
    }

    [Fact]
    public void FailedDocuments_Load_And_Save_RoundTrip()
    {
        string file = Path.Combine(_root, "failed-docs.txt");
        string[] samples =
        [
            "layout/data/tdf123163-1.docx",
            "ooxmlexport/data/tdf163894.docx",
            "cjk/data/tdf115079.odt"
        ];

        FailedDocuments.Save(file, samples);
        Assert.True(File.Exists(file));

        var loaded = FailedDocuments.Load(file);
        Assert.Equal(3, loaded.Count);
        Assert.Equal(samples.OrderBy(s => s, StringComparer.Ordinal).ToArray(), loaded.ToArray());

        string content = File.ReadAllText(file);
        Assert.StartsWith("#", content);
        Assert.Contains("cjk/data/tdf115079.odt", content);
    }

    [Theory]
    [InlineData("/absolute/path/sample.docx")]
    [InlineData("layout\\windows\\backslash.docx")]
    [InlineData("unsupported/file.exe")]
    [InlineData("../escape/traversal.docx")]
    public void FailedDocuments_Load_Throws_On_Invalid_Sample_Id(string invalidId)
    {
        string file = Path.Combine(_root, "invalid.txt");
        File.WriteAllLines(file, [invalidId]);

        Assert.Throws<InvalidDataException>(() => FailedDocuments.Load(file));
    }

    [Fact]
    public void FailedDocuments_UpdateTracked_Prunes_Passing_And_Keeps_Failing()
    {
        string[] previouslyTracked = ["doc1.docx", "doc2.docx", "doc3.odt"];
        string[] evaluatedIds = ["doc1.docx", "doc2.docx"]; // doc3.odt was not re-evaluated
        string[] failedIds = ["doc2.docx", "doc4.docx"]; // doc1 passed! doc2 failed again, doc4 newly failed

        var updated = FailedDocuments.UpdateTracked(previouslyTracked, evaluatedIds, failedIds);

        // doc1 was evaluated and passed -> removed!
        Assert.DoesNotContain("doc1.docx", updated);
        // doc2 was evaluated and failed -> retained!
        Assert.Contains("doc2.docx", updated);
        // doc3 was not evaluated -> preserved!
        Assert.Contains("doc3.odt", updated);
        // doc4 was newly failed -> added!
        Assert.Contains("doc4.docx", updated);

        Assert.Equal(3, updated.Count);
    }

    [Fact]
    public void FailedDocuments_DefaultPath_Points_To_Office_Folder()
    {
        string defaultPath = FailedDocuments.DefaultPath(_root);
        Assert.Equal(Path.Combine(_root, "tests", "office", "failed-documents.txt"), defaultPath);
    }

    [Fact]
    public void Tracked_Failed_Documents_In_Repo_Loads_Cleanly()
    {
        string repoRoot = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repoRoot, "Broiler.Documents.slnx")))
            repoRoot = Path.GetDirectoryName(repoRoot) ?? throw new DirectoryNotFoundException("repository root");

        string path = FailedDocuments.DefaultPath(repoRoot);
        Assert.True(File.Exists(path), "tests/office/failed-documents.txt should exist in the repository.");

        var tracked = FailedDocuments.Load(path);
        Assert.NotEmpty(tracked);
        Assert.All(tracked, id =>
        {
            Assert.False(id.StartsWith('/'));
            Assert.DoesNotContain('\\', id);
            Assert.NotNull(ExtrasConformance.FormatForExtension(id));
        });
    }
}
