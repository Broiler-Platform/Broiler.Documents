using Broiler.Documents.Corpus;
using Broiler.Documents.Office;

namespace Broiler.Documents.Office.Tests;

/// <summary>
/// Samples whose reference is a LibreOffice defect a later release fixed:
/// which runs a row applies to, and how the report shows them.
/// </summary>
public sealed class ReferenceDefectTests
{
    private static readonly ExtrasSample Hidden = new(
        "ooxmlexport/data/tdf163894_hidden.docx", ".docx", "docx", "tdf163894_hidden.docx", null);

    private static readonly ExtrasSample Other = new("ooxmlexport/data/other.docx", ".docx", "docx", "other.docx", null);

    private static ReferenceDefects Load(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), "reference-defects-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        try
        {
            return ReferenceDefects.Load(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private const string File163894 =
        "{\"defects\":[{\"id\":\"tdf163894\",\"bug\":\"tdf#163894\",\"fixedIn\":\"26.8\",\"axes\":[\"pixel\"]," +
        "\"samples\":[\"ooxmlexport/data/tdf163894_hidden.docx\"],\"reference\":\"docs/notes/office-issue-170.md\"}]}";

    [Theory]
    [InlineData("LibreOffice 24.2.7.2 420(Build:2)", true)]
    [InlineData("LibreOffice 26.2.6.3 a1b2c3", true)]
    [InlineData("LibreOffice 26.8.1.1 a1b2c3", false)]
    [InlineData("LibreOffice 27.2.0.1", false)]
    [InlineData(null, false)]
    [InlineData("unknown", false)]
    public void A_Row_Applies_Only_While_The_Reference_Predates_The_Fix(string? libreOffice, bool applies)
    {
        ReferenceDefects defects = Load(File163894);

        Assert.Equal(applies, defects.For(Hidden, libreOffice) is not null);
        Assert.Null(defects.For(Other, libreOffice));
    }

    [Fact]
    public void A_Sample_Listed_Twice_Is_Refused()
    {
        string twice = File163894.Replace(
            "\"samples\":[\"ooxmlexport/data/tdf163894_hidden.docx\"]",
            "\"samples\":[\"ooxmlexport/data/tdf163894_hidden.docx\",\"ooxmlexport/data/tdf163894_hidden.docx\"]",
            StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => Load(twice));
    }

    [Fact]
    public void The_Repository_File_Loads_And_Covers_The_Issue_170_Samples()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Broiler.Documents.slnx")))
            root = Path.GetDirectoryName(root) ?? throw new DirectoryNotFoundException("repository root");

        ReferenceDefects defects = ReferenceDefects.Load(Path.Combine(root, "tests", "office", "extras-reference-defects.json"));

        ReferenceDefect? defect = defects.For(Hidden, "LibreOffice 24.2.7.2 420(Build:2)");
        Assert.NotNull(defect);
        Assert.Equal("tdf#163894", defect.Bug);
        Assert.True(defect.Covers("pixel"));
        Assert.False(defect.Covers("text"));
    }

    [Fact]
    public void The_Report_Lists_Known_Defects_And_Does_Not_Rank_A_Sample_For_Them()
    {
        ReferenceDefect defect = Load(File163894).For(Hidden, "LibreOffice 24.2.7.2")!;
        var difference = new ExtrasDifference(
            Hidden, false, 0, null, false, null, null, null, 0, "known defect",
            [CheckResult.Pass("read", "read/" + Hidden.Id), CheckResult.Pass("text", "text/" + Hidden.Id),
             CheckResult.Skip("pixel", "pixel/" + Hidden.Id, defect.Describe("LibreOffice 24.2.7.2"))],
            ReferenceDefect: defect);

        Assert.False(difference.HasDifference);
        Assert.Empty(ExtrasConformance.TopDifferences([difference]));

        string markdown = ExtrasConformance.FormatIssueMarkdown(
            "source", new RunHeader("tool", "LibreOffice 24.2.7.2", "poppler", null, "linux", 96, true), [Hidden], [difference]);
        Assert.Contains("**Known Reference Defects** | 1 |", markdown);
        Assert.Contains("### Known Reference Defects", markdown);
        Assert.Contains("`ooxmlexport/data/tdf163894_hidden.docx`: tdf#163894, fixed in LibreOffice 26.8; pixel not compared", markdown);
    }

    [Fact]
    public void A_Check_The_Defect_Does_Not_Cover_Still_Ranks()
    {
        ReferenceDefect defect = Load(File163894).For(Hidden, "LibreOffice 24.2.7.2")!;
        var difference = new ExtrasDifference(
            Hidden, false, 0, null, false, null, null, null, 100, "text differs",
            [CheckResult.Fail("text", "text/" + Hidden.Id, "3 line differences"),
             CheckResult.Skip("pixel", "pixel/" + Hidden.Id, defect.Describe("LibreOffice 24.2.7.2"))],
            ReferenceDefect: defect);

        Assert.Same(difference, Assert.Single(ExtrasConformance.TopDifferences([difference])));
    }

    [Theory]
    [InlineData("LibreOffice 24.2.7.2 420(Build:2)", true)]
    [InlineData("LibreOffice 7.6.4.1", true)]
    [InlineData("LibreOffice 24.8.0.3", false)]
    [InlineData("LibreOffice 26.2.6.3 a1b2c3", false)]
    [InlineData(null, false)]
    public void The_Text_Dump_Puts_Cells_On_Lines_Where_The_Reference_Does(string? libreOffice, bool lines)
    {
        // Text export joins a table row onto one line since 24.8 (tdf#144576).
        IReadOnlyList<string> arguments = TextProjection.DumpArguments(libreOffice);

        Assert.Equal(lines ? ["--cells", "lines"] : [], arguments);
    }
}
