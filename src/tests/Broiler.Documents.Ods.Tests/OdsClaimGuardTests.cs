using System.Reflection;
using System.Text.RegularExpressions;
using Broiler.Documents.TestSupport;

namespace Broiler.Documents.Ods.Tests;

/// <summary>
/// Binds what the ODS documents say to the ODS code (ADR 0013).
/// </summary>
public sealed partial class OdsClaimGuardTests
{
    private const string Register = "docs/ip/ods.md";
    private const string Note = "docs/notes/ods.md";

    [Fact]
    public void Every_Diagnostic_Code_The_Codec_Declares_Is_Described_In_Its_Note()
    {
        string note = File.ReadAllText(Path.Combine(RepositoryFiles.Root, Note));

        string[] undocumented = [.. DeclaredCodes()
            .Where(code => !note.Contains("`" + code + "`", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];

        Assert.Empty(undocumented);
    }

    [Fact]
    public void Every_Code_The_Source_Reports_Is_Declared()
    {
        string source = string.Concat(Directory
            .EnumerateFiles(Path.Combine(RepositoryFiles.Root, "src", "Broiler.Documents.Ods"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !RepositoryFiles.IsBuildOutput(path))
            .Where(path => Path.GetFileName(path) != "OdsDiagnosticCodes.cs")
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
        Assert.Equal("ODS", new OdsDocumentCodec().Descriptor.Name);

        string register = File.ReadAllText(Path.Combine(RepositoryFiles.Root, Register));
        Assert.Contains("| Format list | **ODS** |", register, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Note_Defers_To_The_Register_On_Rights()
    {
        string note = File.ReadAllText(Path.Combine(RepositoryFiles.Root, Note));

        Assert.Contains("../ip/ods.md", note, StringComparison.Ordinal);
    }

    private static IEnumerable<string> DeclaredCodes() =>
        typeof(OdsDiagnosticCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!);

    [GeneratedRegex(@"""ods\.[a-z0-9.\-]+""", RegexOptions.CultureInvariant)]
    private static partial Regex CodeLiteral();

    [GeneratedRegex(@"`(ods\.[a-z0-9.\-]+)`", RegexOptions.CultureInvariant)]
    private static partial Regex CodeMention();
}
