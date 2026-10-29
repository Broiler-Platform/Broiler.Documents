using System.Text.Json;
using Broiler.Documents.Office;

namespace Broiler.Documents.Office.Tests;

public sealed class ReferenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "office-reference-tests", Guid.NewGuid().ToString("N"));
    private readonly string _references;
    private readonly ExtrasSample _sample;
    private readonly CrashRegressions _families;
    private readonly ReferenceEnvironment _environment;
    private readonly ReferenceSample _entry;

    public ReferenceTests()
    {
        Directory.CreateDirectory(_root);
        _references = Path.Combine(_root, "references");
        Directory.CreateDirectory(_references);
        string input = Path.Combine(_root, "sample.fodt");
        File.WriteAllText(input, "stable document input");
        _sample = new ExtrasSample("directory/sample.fodt", ".fodt", "odt", input, null);
        string families = Path.Combine(_root, "families.json");
        File.WriteAllText(families, "{\"families\":[]}");
        _families = CrashRegressions.Load(families);
        string fonts = Path.Combine(_root, "fonts");
        Directory.CreateDirectory(fonts);
        File.WriteAllText(Path.Combine(fonts, "font.ttf"), "pinned font bytes");
        _environment = OfficeReferences.EnvironmentStamp("github-hosted:ubuntu-24.04",
            "LibreOffice test", "linux", "test-fonts", [fonts], TimeSpan.FromSeconds(120));
        var run = new LibreOfficeRun([], 0, "", "", TimeSpan.FromSeconds(1), "text filter", input);
        ReferenceOutput text = OfficeReferences.SaveOutput(_references, "expected.txt", run);
        ReferenceOutput pdf = OfficeReferences.SaveOutput(_references, "expected.pdf", run);
        _entry = new ReferenceSample(_sample.Id, OfficeReferences.HashFile(input), null, text, pdf);
        Write(_entry);
    }

    private void Write(params ReferenceSample[] entries) => File.WriteAllText(Path.Combine(_references, "manifest.json"),
        JsonSerializer.Serialize(new ReferenceManifest(_environment, "2026-10-27T00:00:00Z", "pinned corpus", "hash", entries)));

    private OfficeReferences Load(ReferenceEnvironment? stamp = null, params ExtrasSample[] samples) =>
        OfficeReferences.Load(_references, stamp ?? _environment, samples.Length == 0 ? [_sample] : samples, _families);

    [Fact]
    public void Valid_References_Are_Read_Without_A_LibreOffice_Process()
    {
        OfficeReferences references = Load();
        Assert.Equal("stable document input", File.ReadAllText(references.Get(_sample, pdf: false).OutputPath!));
        Assert.Equal(0, references.Get(_sample, pdf: true).ExitCode);
        Assert.Equal(TimeSpan.Zero, references.Get(_sample, pdf: true).Duration);
    }

    [Fact]
    public void Explicit_Unavailable_Projection_Remains_A_Failure_To_Produce_Output()
    {
        var unavailable = new ReferenceOutput("unavailable", null, null, 1, false, null, "unsupported input");
        Write(_entry with { Text = unavailable });
        LibreOfficeRun run = Load().Get(_sample, pdf: false);
        Assert.Null(run.OutputPath);
        Assert.Equal(1, run.ExitCode);
        Assert.Equal("unsupported input", run.StandardError);
    }

    [Fact]
    public void Timeouts_Are_Preserved_And_Start_Failures_Are_Not_Published()
    {
        var run = new LibreOfficeRun([], -1, "", "timeout", TimeSpan.Zero, null, null) { TimedOut = true };
        ReferenceOutput output = OfficeReferences.SaveOutput(_references, "timeout.txt", run);
        Write(_entry with { Text = output });
        Assert.True(Load().Get(_sample, pdf: false).TimedOut);
        Assert.Throws<IOException>(() => OfficeReferences.SaveOutput(_references, "failed.txt", run with { TimedOut = false }));
    }

    [Fact]
    public void Changed_Source_Bytes_Are_Rejected()
    {
        File.AppendAllText(_sample.LocalPath, "modified");
        Assert.Contains("input hash mismatch", Assert.Throws<InvalidDataException>(() => Load()).Message);
    }

    [Theory]
    [InlineData("fonts")]
    [InlineData("timeout")]
    [InlineData("schema")]
    public void Changed_Toolchains_Are_Rejected(string component)
    {
        ReferenceEnvironment changed = component switch
        {
            "fonts" => _environment with { FontsSha256 = "another font hash" },
            "timeout" => _environment with { TimeoutSeconds = 60 },
            _ => _environment with { SchemaVersion = 99 },
        };
        Assert.Contains("toolchain mismatch", Assert.Throws<InvalidDataException>(() => Load(changed)).Message);
    }

    [Fact]
    public void An_Accepted_Mismatch_Loads_And_Names_Each_Differing_Field()
    {
        ReferenceEnvironment windows = _environment with { Platform = "windows", FontSet = null, TimeoutSeconds = 60 };
        string message = Assert.Throws<InvalidDataException>(() => Load(windows)).Message;
        Assert.Contains("platform linux, here windows", message);
        Assert.Contains("--accept-reference-mismatch", message);

        OfficeReferences references = OfficeReferences.Load(_references, windows, [_sample], _families, acceptMismatch: true);
        Assert.Equal(["platform linux, here windows", "font set test-fonts, here none", "timeout 120 s, here 60 s"],
            references.Mismatches);
        Assert.Empty(Load().Mismatches);
    }

    [Fact]
    public void An_Accepted_Mismatch_Still_Checks_Schema_And_Inputs()
    {
        Assert.Throws<InvalidDataException>(() => OfficeReferences.Load(_references,
            _environment with { SchemaVersion = 99 }, [_sample], _families, acceptMismatch: true));
        File.AppendAllText(_sample.LocalPath, "modified");
        Assert.Contains("input hash mismatch", Assert.Throws<InvalidDataException>(() => OfficeReferences.Load(_references,
            _environment with { Platform = "windows" }, [_sample], _families, acceptMismatch: true)).Message);
    }

    [Fact]
    public void Font_Content_Is_Fingerprinted()
    {
        string fonts = Path.Combine(_root, "fonts");
        File.AppendAllText(Path.Combine(fonts, "font.ttf"), "new font version");
        var changed = OfficeReferences.EnvironmentStamp(_environment.Producer, _environment.LibreOffice,
            _environment.Platform, _environment.FontSet, [fonts], TimeSpan.FromSeconds(120));
        Assert.NotEqual(_environment.FontsSha256, changed.FontsSha256);
        Assert.Throws<InvalidDataException>(() => Load(changed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_Or_Corrupt_Outputs_Are_Rejected(bool corrupt)
    {
        string path = Path.Combine(_references, _entry.Pdf!.File!);
        if (corrupt) File.AppendAllText(path, "modified");
        else File.Delete(path);
        Assert.Contains("missing or corrupt", Assert.Throws<InvalidDataException>(() => Load()).Message);
    }

    [Fact]
    public void Missing_Samples_And_Duplicate_Ids_Are_Rejected()
    {
        Assert.Contains("missing sample", Assert.Throws<InvalidDataException>(() =>
            Load(null, _sample with { Id = "new-input.fodt" })).Message);
        Write(_entry, _entry);
        Assert.Throws<InvalidDataException>(() => Load());
    }

    [Fact]
    public void Selected_Subsets_Can_Reuse_A_Full_Reference_Set()
    {
        Write(_entry, _entry with { Id = "another.fodt" });
        Assert.NotNull(Load());
    }

    [Fact]
    public void Changed_Crash_Classification_Is_Rejected()
    {
        Write(_entry with { CrashFamily = "fuzzer" });
        Assert.Contains("classification changed", Assert.Throws<InvalidDataException>(() => Load()).Message);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("..\\outside.txt")]
    [InlineData("/absolute.txt")]
    public void Output_Paths_Cannot_Escape_The_Reference_Directory(string file)
    {
        Write(_entry with { Text = _entry.Text! with { File = file } });
        Assert.Throws<InvalidDataException>(() => Load());
    }

    [Fact]
    public void Missing_Projection_Status_And_Broken_Manifests_Are_Rejected()
    {
        Write(_entry with { Text = null });
        Assert.Throws<InvalidDataException>(() => Load());
        File.WriteAllText(Path.Combine(_references, "manifest.json"), "unfinished JSON");
        Assert.Throws<InvalidDataException>(() => Load());
    }

    [Theory]
    [InlineData("--seed-corpus", "--references-dir", "references")]
    [InlineData("--prepare-references", "out", "--only", "one")]
    [InlineData("--prepare-references", "out", "--axis", "pixel")]
    [InlineData("--accept-reference-mismatch")]
    [InlineData("--references-dir", "references", "--accept-reference-mismatch", "--create-github-issue")]
    public void Invalid_Reference_Cli_Combinations_Are_Rejected(params string[] arguments) =>
        Assert.Throws<ArgumentException>(() => Program.Options.Parse(arguments));

    [Fact]
    public void Producer_Image_And_Installed_LibreOffice_Are_Not_Consumer_Requirements()
    {
        var consumer = _environment with { Producer = "a different container", LibreOffice = "different installed version" };
        OfficeReferences references = Load(consumer);
        Assert.Equal(_environment.Producer, references.Environment.Producer);
        Assert.Equal(_environment.LibreOffice, references.Environment.LibreOffice);
        Assert.True(Program.Options.Parse(["--references-dir", "references"]).IsExtrasRun);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
