using System.Text.Json;
using Broiler.Documents.Corpus;
using Broiler.Documents.Office;

namespace Broiler.Documents.Office.Tests;

public sealed class CheckpointTests
{
    [Fact]
    public void Checkpoints_Retain_Failures_And_Never_Claim_A_Complete_Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            string finalPath = Path.Combine(directory, "office-results.json");
            var sample = new ExtrasSample("sample.odt", ".odt", "odt", "sample.odt", null);
            var other = sample with { Id = "other.odt" };
            var header = new RunHeader("tool", "LibreOffice", "poppler", null, "test", 144, true);
            ExtrasCheckpoint.Write(finalPath, "source", header, [sample, other], [], 3, 80);
            using (var initial = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "office-partial-results.json"))))
                Assert.Equal(0, initial.RootElement.GetProperty("completedSamples").GetInt32());

            var failure = new ExtrasDifference(sample, false, 0, null, false, null, null,
                null, 100, "text differs", [CheckResult.Fail("text", "text/sample.odt", "text differs")]);
            ExtrasCheckpoint.Write(finalPath, "source", header, [sample, other], [failure], 3, 80);
            using var updated = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "office-partial-results.json")));
            Assert.False(updated.RootElement.GetProperty("runComplete").GetBoolean());
            Assert.Equal(1, updated.RootElement.GetProperty("completedSamples").GetInt32());
            Assert.Equal(2, updated.RootElement.GetProperty("totalSamples").GetInt32());
            Assert.Equal(1, updated.RootElement.GetProperty("failed").GetInt32());
            Assert.Equal("text/sample.odt", updated.RootElement.GetProperty("checks")[0].GetProperty("name").GetString());
            string markdown = File.ReadAllText(Path.Combine(directory, "office-partial-differences.md"));
            Assert.Contains("1/2 documents completed", markdown);
            Assert.Contains("sample.odt", markdown);
            Assert.False(File.Exists(finalPath));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
