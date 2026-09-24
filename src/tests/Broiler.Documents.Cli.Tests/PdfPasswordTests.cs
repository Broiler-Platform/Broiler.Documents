using System.Text;
using Broiler.Documents.Pdf.Tests;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// Encrypted PDFs through the tool: the password arrives on a file, reaches the
/// PDF codec alone, and never reaches an output.
/// </summary>
public sealed class PdfPasswordTests : IDisposable
{
    private const string Text = "Opened with a password";
    private const string Password = "Needle-9vK";

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    [Fact]
    public void Reads_An_Encrypted_Pdf_With_The_Password_On_A_File()
    {
        string path = Locked();
        string passwordFile = PasswordFile(Password);

        CliRun run = CliHarness.RunExpecting(ExitCode.Ok, "dump", path, "--password-file", passwordFile);

        Assert.Contains(Text, run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_The_Password_The_Read_Fails_And_Says_Why()
    {
        CliRun run = CliHarness.RunExpecting(ExitCode.Read, "dump", Locked());

        Assert.Contains("user password", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(Text, run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Wrong_Password_Is_Refused()
    {
        CliRun run = CliHarness.RunExpecting(ExitCode.Read, "dump", Locked(), "--password-file", PasswordFile("not-it"));

        Assert.Contains("neither the document's user password nor its owner password", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Password_Reaches_No_Output()
    {
        string passwordFile = PasswordFile(Password);

        foreach (string[] arguments in new[]
                 {
                     new[] { "info", Locked(), "--password-file", passwordFile, "--json" },
                     new[] { "info", Locked(), "--password-file", passwordFile },
                     new[] { "dump", Locked(), "--password-file", PasswordFile(Password + "-wrong") },
                 })
        {
            CliRun run = CliHarness.Run(arguments);
            Assert.DoesNotContain("Needle", run.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("Needle", run.Error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_Info_Report_Says_The_Document_Was_Decrypted()
    {
        CliRun run = CliHarness.RunExpecting(ExitCode.Ok, "info", Locked(), "--password-file", PasswordFile(Password), "--json");

        Assert.Contains("pdf.encryption.decrypted", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Password_File_That_Cannot_Be_Read_Is_A_Usage_Error()
    {
        CliHarness.RunExpecting(ExitCode.Usage, "dump", Locked(), "--password-file", _cli.Path("missing.txt"));
    }

    private string PasswordFile(string password)
    {
        string path = _cli.Path(Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, password + Environment.NewLine);
        return path;
    }

    /// <summary>
    /// A one-page PDF protected with <see cref="Password"/> under RC4-128: its
    /// content stream encrypted, the encryption dictionary and identifier in the
    /// trailer.
    /// </summary>
    private string Locked()
    {
        PdfTestEncryption encryption = PdfTestEncryption.Rc4(3, 128, Password, "owner-" + Password);
        byte[] content = encryption.EncryptStream(Encoding.Latin1.GetBytes($"BT /F1 12 Tf 72 720 Td ({Text}) Tj ET"), 5, 0);

        var output = new MemoryStream();
        var offsets = new List<long>();
        void Write(string text) => output.Write(Encoding.Latin1.GetBytes(text));
        void Object(string body)
        {
            offsets.Add(output.Length);
            Write($"{offsets.Count} 0 obj\n{body}\nendobj\n");
        }

        Write("%PDF-1.7\n");
        Object("<< /Type /Catalog /Pages 2 0 R >>");
        Object("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Object("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>");
        Object("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        offsets.Add(output.Length);
        Write($"5 0 obj\n<< /Length {content.Length} >>\nstream\n");
        output.Write(content);
        Write("\nendstream\nendobj\n");

        Object(encryption.Dictionary);

        long xref = output.Length;
        Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (long offset in offsets)
            Write($"{offset:D10} 00000 n \n");

        string id = PdfTestEncryption.Hex(encryption.FileIdentifier);
        Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R /Encrypt 6 0 R /ID [<{id}> <{id}>] >>\nstartxref\n{xref}\n%%EOF\n");

        string path = _cli.Path(Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(path, output.ToArray());
        return path;
    }
}
