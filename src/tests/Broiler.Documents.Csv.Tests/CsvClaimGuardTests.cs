using System.Reflection;
using System.Text.RegularExpressions;
using Broiler.Documents.TestSupport;

namespace Broiler.Documents.Csv.Tests;

/// <summary>
/// Binds what the CSV documents say to the CSV code (ADR 0013).
/// </summary>
/// <remarks>
/// The five checks every format shares - no data file beside the codec, no
/// third-party package, a description free of declined claims, a register with
/// a label section, and no committed document of the format - run for CSV in
/// <c>FormatClaimGuardTests</c>. These are the ones that need the codec's own
/// types: its codes are constants rather than literals scattered through the
/// source, so they are read by reflection.
/// </remarks>
public sealed partial class CsvClaimGuardTests
{
    private const string Register = "docs/ip/csv.md";
    private const string Note = "docs/notes/csv.md";

    [Fact]
    public void Every_Diagnostic_Code_The_Codec_Declares_Is_Described_In_Its_Note()
    {
        // A code is API: a host branches on it, and the note is where a host
        // learns it exists.
        string note = File.ReadAllText(Path.Combine(RepositoryFiles.Root, Note));

        string[] undocumented = [.. DeclaredCodes()
            .Where(code => !note.Contains("`" + code + "`", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];

        Assert.Empty(undocumented);
    }

    [Fact]
    public void Every_Code_The_Source_Reports_Is_Declared()
    {
        // The constants are the list the guard above checks, so a literal code
        // passed straight to a diagnostic would slip past it.
        string source = string.Concat(Directory
            .EnumerateFiles(Path.Combine(RepositoryFiles.Root, "src", "Broiler.Documents.Csv"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !RepositoryFiles.IsBuildOutput(path))
            .Where(path => Path.GetFileName(path) != "CsvDiagnosticCodes.cs")
            .Select(File.ReadAllText));

        Assert.Empty(CodeLiteral().Matches(source).Select(match => match.Value));
    }

    [Fact]
    public void The_Register_And_The_Note_Name_Only_Codes_That_Exist()
    {
        var declared = DeclaredCodes().ToHashSet(StringComparer.Ordinal);

        string[] missing = [.. new[] { Register, Note }
            .SelectMany(path => CodeMention().Matches(File.ReadAllText(Path.Combine(RepositoryFiles.Root, path))))
            .Select(match => match.Groups[1].Value)
            .Where(code => !declared.Contains(code))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

        Assert.Empty(missing);
    }

    [Fact]
    public void The_Format_Is_Named_As_The_Register_Proposes()
    {
        // "CSV" is the format-list label the register proposes. The descriptor
        // is where an application reads it from, so a vendor's product name here
        // would reach every host that lists the format.
        Assert.Equal("CSV", new CsvDocumentCodec().Descriptor.Name);

        string register = File.ReadAllText(Path.Combine(RepositoryFiles.Root, Register));
        Assert.Contains("| Format list | **CSV** |", register, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Note_Defers_To_The_Register_On_Rights()
    {
        string note = File.ReadAllText(Path.Combine(RepositoryFiles.Root, Note));

        Assert.Contains("../ip/csv.md", note, StringComparison.Ordinal);
    }

    private static IEnumerable<string> DeclaredCodes() =>
        typeof(CsvDiagnosticCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!);

    [GeneratedRegex(@"""csv\.[a-z0-9.\-]+""", RegexOptions.CultureInvariant)]
    private static partial Regex CodeLiteral();

    [GeneratedRegex(@"`(csv\.[a-z0-9.\-]+)`", RegexOptions.CultureInvariant)]
    private static partial Regex CodeMention();
}
