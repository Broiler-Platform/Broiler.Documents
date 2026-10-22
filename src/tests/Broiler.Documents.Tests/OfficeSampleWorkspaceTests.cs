using Broiler.Documents.Office;

namespace Broiler.Documents.Tests;

public sealed class OfficeSampleWorkspaceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Disposal_removes_generated_files_even_when_evaluation_throws(bool throws)
    {
        string parent = Path.Combine(Path.GetTempPath(), "office-workspace-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        string source = Path.Combine(parent, "source.docx");
        File.WriteAllText(source, "source must survive");
        string? sampleRoot = null;
        try
        {
            try
            {
                using var sample = new OfficeSampleWorkspace(parent, keep: false);
                sampleRoot = sample.Root;
                string profile = sample.NextProfile();
                Directory.CreateDirectory(profile);
                File.WriteAllText(Path.Combine(profile, "settings"), "temporary");
                Assert.NotEqual(profile, sample.NextProfile());
                if (throws)
                    throw new InvalidOperationException("conversion failed");
            }
            catch (InvalidOperationException) when (throws) { }
            Assert.False(Directory.Exists(sampleRoot));
            Assert.Equal("source must survive", File.ReadAllText(source));
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    [Fact]
    public void Concurrent_samples_are_isolated_and_keep_preserves_files()
    {
        string parent = Path.Combine(Path.GetTempPath(), "office-workspace-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try
        {
            using var first = new OfficeSampleWorkspace(parent, keep: false);
            using var second = new OfficeSampleWorkspace(parent, keep: true);
            Directory.CreateDirectory(first.Root);
            Directory.CreateDirectory(second.Root);
            Assert.NotEqual(first.NextProfile(), second.NextProfile());
            first.Dispose();
            Assert.False(Directory.Exists(first.Root));
            second.Dispose();
            Assert.True(Directory.Exists(second.Root));
        }
        finally { Directory.Delete(parent, recursive: true); }
    }
}
