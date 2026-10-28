using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Documents.Model;
using Xunit;

namespace Broiler.Documents.Tests;

public sealed class DocumentWriteCommitPolicyTests
{
    private static readonly RichTextDocument SampleDocument = RichTextDocument.FromPlainText("Hello world");

    private sealed class TestWritableCodec : DocumentCodec
    {
        public TestWritableCodec()
            : base(new DocumentFormatDescriptor("TEST", ["application/test"], [".test"]))
        {
        }

        public override bool CanRead => false;
        public override bool CanWrite => true;

        public override DocumentProbeResult Probe(DocumentProbeRequest request) => DocumentProbeResult.NoMatch();
        public override DocumentReadResult Read(Stream source, DocumentReadOptions? options = null) => throw new NotSupportedException();

        public override DocumentWriteResult Write(RichTextDocument document, Stream destination, DocumentWriteOptions? options = null)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(document.PlainText);
            destination.Write(bytes);
            return new DocumentWriteResult(bytes.Length, null, DocumentResultStatus.Success, DocumentDestinationState.Committed);
        }
    }

    [Fact]
    public void Request_Defaults_To_NonAtomic_And_Unstaged()
    {
        using var stream = new MemoryStream();
        var request = new DocumentWriteRequest(SampleDocument, stream);

        Assert.False(request.RequireAtomic);
        Assert.False(request.IsStaged);
        Assert.Same(SampleDocument, request.Document);
        Assert.Same(stream, request.Destination);
        Assert.Equal(DocumentWriteOptions.Default, request.Options);
    }

    [Fact]
    public void Request_With_Explicit_Flags_And_Copies()
    {
        using var stream = new MemoryStream();
        var request = new DocumentWriteRequest(
            SampleDocument,
            stream,
            requireAtomic: true,
            isStaged: true);

        Assert.True(request.RequireAtomic);
        Assert.True(request.IsStaged);

        var unstagedCopy = request.WithStaged(false);
        Assert.True(unstagedCopy.RequireAtomic);
        Assert.False(unstagedCopy.IsStaged);

        var nonAtomicCopy = request.WithRequireAtomic(false);
        Assert.False(nonAtomicCopy.RequireAtomic);
        Assert.True(nonAtomicCopy.IsStaged);
    }

    [Fact]
    public void Request_Direct_And_Staged_Factory_Methods()
    {
        using var stream = new MemoryStream();

        var direct = DocumentWriteRequest.Direct(SampleDocument, stream);
        Assert.False(direct.RequireAtomic);
        Assert.False(direct.IsStaged);

        var stagedDefault = DocumentWriteRequest.Staged(SampleDocument, stream);
        Assert.True(stagedDefault.RequireAtomic);
        Assert.True(stagedDefault.IsStaged);

        var stagedNonAtomic = DocumentWriteRequest.Staged(SampleDocument, stream, requireAtomic: false);
        Assert.False(stagedNonAtomic.RequireAtomic);
        Assert.True(stagedNonAtomic.IsStaged);
    }

    [Fact]
    public void RequireAtomic_Without_Staging_Rejects_Before_Output_Synchronous()
    {
        var codec = new TestWritableCodec();
        using var destination = new MemoryStream();

        var request = new DocumentWriteRequest(
            SampleDocument,
            destination,
            requireAtomic: true,
            isStaged: false);

        DocumentWriteResult result = codec.Write(request);

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Equal(DocumentDestinationState.NotStarted, result.DestinationState);
        Assert.Equal(0, result.BytesWritten);
        Assert.Equal(0, destination.Length);
        Assert.Contains(result.Diagnostics, d => d.Code == DocumentDiagnosticCodes.CommitAtomicRequired);
    }

    [Fact]
    public async Task RequireAtomic_Without_Staging_Rejects_Before_Output_Asynchronous()
    {
        var codec = new TestWritableCodec();
        using var destination = new MemoryStream();

        var request = new DocumentWriteRequest(
            SampleDocument,
            destination,
            requireAtomic: true,
            isStaged: false);

        DocumentWriteResult result = await codec.WriteAsync(request);

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Equal(DocumentDestinationState.NotStarted, result.DestinationState);
        Assert.Equal(0, result.BytesWritten);
        Assert.Equal(0, destination.Length);
        Assert.Contains(result.Diagnostics, d => d.Code == DocumentDiagnosticCodes.CommitAtomicRequired);
    }

    [Fact]
    public void RequireAtomic_With_Staging_Succeeds_And_Emits_Output()
    {
        var codec = new TestWritableCodec();
        using var destination = new MemoryStream();

        var request = DocumentWriteRequest.Staged(SampleDocument, destination);

        DocumentWriteResult result = codec.Write(request);

        Assert.Equal(DocumentResultStatus.Success, result.Status);
        Assert.Equal(DocumentDestinationState.Committed, result.DestinationState);
        Assert.True(result.BytesWritten > 0);
        Assert.True(destination.Length > 0);
    }

    [Fact]
    public void Unstaged_Without_RequireAtomic_Writes_Directly_To_Destination()
    {
        var codec = new TestWritableCodec();
        using var destination = new MemoryStream();

        var request = DocumentWriteRequest.Direct(SampleDocument, destination);

        DocumentWriteResult result = codec.Write(request);

        Assert.Equal(DocumentResultStatus.Success, result.Status);
        Assert.Equal(DocumentDestinationState.Committed, result.DestinationState);
        Assert.True(result.BytesWritten > 0);
        Assert.True(destination.Length > 0);
    }

    [Fact]
    public void PreCancelled_Write_Rejects_With_NotStarted_And_Zero_Bytes()
    {
        var codec = new TestWritableCodec();
        using var destination = new MemoryStream();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var request = new DocumentWriteRequest(SampleDocument, destination, cancellationToken: cts.Token);

        DocumentWriteResult result = codec.Write(request);

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Equal(DocumentDestinationState.NotStarted, result.DestinationState);
        Assert.Equal(0, result.BytesWritten);
        Assert.Equal(0, destination.Length);
        Assert.Contains(result.Diagnostics, d => d.Code == DocumentDiagnosticCodes.Cancelled);
    }

    [Fact]
    public void File_Host_Atomic_Staging_Pattern_Simulation()
    {
        // Simulates Phase 7 file host commit policy:
        // "File hosts stage in the destination directory and replace atomically.
        // Failure/cancellation removes the staged file and preserves any existing destination."
        string tempDir = Path.Combine(Path.GetTempPath(), "broiler-commit-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string targetPath = Path.Combine(tempDir, "document.txt");
            File.WriteAllText(targetPath, "ORIGINAL UNTOUCHED CONTENT");

            string stagePath = Path.Combine(tempDir, "document.txt.tmp");

            // Case 1: Write fails / rejected preflight -> staged file is removed, target untouched
            {
                using (var stageStream = new FileStream(stagePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    // Simulated failed preflight
                    var cancelledCts = new CancellationTokenSource();
                    cancelledCts.Cancel();
                    var request = DocumentWriteRequest.Staged(SampleDocument, stageStream, cancellationToken: cancelledCts.Token);

                    var codec = new TestWritableCodec();
                    DocumentWriteResult result = codec.Write(request);
                    Assert.Equal(DocumentResultStatus.Rejected, result.Status);
                }

                // Host cleans up staged file on failure
                if (File.Exists(stagePath))
                    File.Delete(stagePath);

                Assert.False(File.Exists(stagePath));
                Assert.Equal("ORIGINAL UNTOUCHED CONTENT", File.ReadAllText(targetPath));
            }

            // Case 2: Write succeeds -> staged file atomically replaces target
            {
                var codec = new TestWritableCodec();
                using (var stageStream = new FileStream(stagePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var request = DocumentWriteRequest.Staged(SampleDocument, stageStream);
                    DocumentWriteResult result = codec.Write(request);
                    Assert.Equal(DocumentResultStatus.Success, result.Status);
                }

                // Atomic replace
                File.Move(stagePath, targetPath, overwrite: true);

                Assert.False(File.Exists(stagePath));
                string newContent = File.ReadAllText(targetPath);
                Assert.Equal("Hello world", newContent);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }
}
