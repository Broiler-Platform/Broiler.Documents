using System.IO.Compression;
using System.Text;
using Broiler.Documents.Model;
using Xunit;

namespace Broiler.Documents.TestSupport;

/// <summary>Small deterministic regressions, not a sustained fuzz campaign.</summary>
public abstract class ParserRobustnessTests
{
    protected abstract DocumentCodec CreateCodec();
    protected abstract byte[] Seed();

    private static readonly DocumentReadOptions Bounded = new(new DocumentLimits(
        maxDocumentBytes: 32768, maxParagraphCount: 32, maxGroupDepth: 32,
        maxRunLength: 1024, maxBinBytes: 1024));

    protected DocumentReadResult Read(DocumentCodec codec, byte[] bytes, DocumentReadOptions? options = null)
    {
        using DocumentInput input = DocumentInput.FromBytes(bytes);
        DocumentReadResult result = codec.Read(new DocumentReadRequest(input, options ?? Bounded));
        Assert.InRange(result.Document.ParagraphCount, 1, (options ?? Bounded).Limits.MaxParagraphCount);
        foreach (RichTextParagraph paragraph in result.Document.Paragraphs)
            Assert.Equal(paragraph.Length, paragraph.Runs.Sum(run => run.Length));
        return result;
    }

    [Fact]
    public void Truncated_Seeds_Return_Structured_Results_Without_Poisoning_The_Next_Read()
    {
        byte[] seed = Seed();
        DocumentCodec codec = CreateCodec();
        DocumentReadResult baseline = Read(codec, seed);
        Assert.True(baseline.IsUsable);
        Assert.Contains("Alpha", baseline.Document.PlainText);
        // All short prefixes plus evenly spaced cuts through larger packages.
        foreach (int length in Enumerable.Range(0, Math.Min(seed.Length, 128))
            .Concat(Enumerable.Range(0, 65).Select(i => (int)((long)seed.Length * i / 64))).Distinct())
        {
            Read(codec, seed[..length]);
            SameResult(baseline, Read(codec, seed));
        }
    }

    [Fact]
    public void Seeded_Byte_Mutations_Are_Deterministic_And_Leave_Valid_Reads_Intact()
    {
        byte[] seed = Seed();
        var random = new Random(104729);
        DocumentCodec codec = CreateCodec();
        DocumentReadResult baseline = Read(codec, seed);
        for (int trial = 0; trial < 128; trial++)
        {
            byte[] payload = (byte[])seed.Clone();
            payload[random.Next(payload.Length)] = (byte)random.Next(256);
            SameResult(Read(codec, payload), Read(CreateCodec(), payload));
            SameResult(baseline, Read(codec, seed));
        }
    }

    [Fact]
    public void Seeded_Random_Bytes_Return_Structured_Results()
    {
        var random = new Random(65537);
        DocumentCodec codec = CreateCodec();
        for (int trial = 0; trial < 128; trial++)
        {
            byte[] payload = new byte[random.Next(512)];
            random.NextBytes(payload);
            Read(codec, payload);
        }
    }

    [Fact]
    public void Byte_Limit_Reports_Loss_And_PreCancelled_Read_Is_Rejected()
    {
        byte[] seed = Seed();
        DocumentCodec codec = CreateCodec();
        DocumentReadResult limited = Read(codec, seed,
            new DocumentReadOptions(new DocumentLimits(maxDocumentBytes: 8)));
        Assert.NotEqual(DocumentResultStatus.Success, limited.Status);
        Assert.NotEmpty(limited.Diagnostics);
        using DocumentInput input = DocumentInput.FromBytes(seed);
        DocumentReadResult cancelled = codec.Read(new DocumentReadRequest(input, Bounded, new CancellationToken(true)));
        Assert.Equal(DocumentResultStatus.Rejected, cancelled.Status);
        Assert.Contains(cancelled.Diagnostics, diagnostic => diagnostic.Code == DocumentDiagnosticCodes.Cancelled);
    }

    [Fact]
    public void Paragraph_Flood_Is_Capped_And_Reported()
    {
        DocumentCodec codec = CreateCodec();
        using var output = new MemoryStream();
        DocumentWriteResult written = codec.Write(RichTextDocument.FromPlainText(
            string.Join("\n", Enumerable.Range(0, 80).Select(i => "Alpha " + i))), output);
        Assert.NotEqual(DocumentResultStatus.Rejected, written.Status);
        DocumentReadResult result = Read(codec, output.ToArray(),
            new DocumentReadOptions(new DocumentLimits(maxParagraphCount: 4)));
        Assert.NotEqual(DocumentResultStatus.Success, result.Status);
        Assert.NotEmpty(result.Diagnostics);
        Assert.DoesNotContain("Alpha 79", result.Document.PlainText);
    }

    protected static void SameResult(DocumentReadResult expected, DocumentReadResult actual)
    {
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.Document.PlainText, actual.Document.PlainText);
        Assert.Equal(expected.Document.ParagraphCount, actual.Document.ParagraphCount);
        Assert.Equal(expected.Diagnostics.Select(d => (d.Code, d.Severity)), actual.Diagnostics.Select(d => (d.Code, d.Severity)));
    }
}

public abstract class PackageParserRobustnessTests : ParserRobustnessTests
{
    protected abstract string ContentEntry { get; }

    protected override byte[] Seed()
    {
        using var output = new MemoryStream();
        DocumentWriteResult written = CreateCodec().Write(RichTextDocument.FromPlainText("Alpha\nBeta café"), output);
        Assert.NotEqual(DocumentResultStatus.Rejected, written.Status);
        return output.ToArray();
    }

    [Fact]
    public void Truncated_Xml_Inside_A_Valid_Package_Is_Reported_Without_Throwing()
    {
        byte[] seed = Seed();
        using var source = new MemoryStream(seed);
        using var original = new ZipArchive(source, ZipArchiveMode.Read);
        using var content = new MemoryStream();
        using (Stream entry = original.GetEntry(ContentEntry)!.Open())
            entry.CopyTo(content);
        byte[] xml = content.ToArray();
        DocumentCodec codec = CreateCodec();
        DocumentReadResult baseline = Read(codec, seed);
        for (int cut = 0; cut < xml.Length; cut += Math.Max(1, xml.Length / 32))
        {
            using var output = new MemoryStream();
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (ZipArchiveEntry entry in original.Entries)
                {
                    using Stream destination = archive.CreateEntry(entry.FullName).Open();
                    if (entry.FullName == ContentEntry)
                        destination.Write(xml.AsSpan(0, cut));
                    else
                    {
                        using Stream input = entry.Open();
                        input.CopyTo(destination);
                    }
                }
            }
            DocumentReadResult result = Read(codec, output.ToArray());
            Assert.NotEqual(DocumentResultStatus.Success, result.Status);
            Assert.NotEmpty(result.Diagnostics);
            SameResult(baseline, Read(codec, seed));
        }
    }
}
