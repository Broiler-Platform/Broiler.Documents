using System.Text.RegularExpressions;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// Constraints on the CLI head itself, checked against its project file and its
/// source rather than against a reviewer's memory.
/// </summary>
/// <remarks>
/// The PDF guards here are the component-local counterpart of the aggregate's
/// <c>Only_The_Enabled_Heads_Name_The_Pdf_Codec</c>. This head lives in this
/// repository, so unlike the aggregate's heads it can be checked from here, and
/// it must be: a command line is exactly the surface an automated system would
/// come to depend on, so a PDF capability composed here by accident would be
/// hard to withdraw later.
/// </remarks>
public sealed class CliArchitectureTests
{
    private static readonly string ComponentRoot = FindComponentRoot();

    private static string CliProjectPath =>
        Path.Combine(ComponentRoot, "src", "Broiler.Documents.Cli", "Broiler.Documents.Cli.csproj");

    [Fact]
    public void The_Cli_Does_Not_Reference_The_Gated_Pdf_Codec()
    {
        string project = File.ReadAllText(CliProjectPath);

        // The project file names the PDF codec in a comment explaining why it is
        // absent, and that comment is worth keeping. What must not exist is a
        // reference, so the assertion is about references.
        string[] referenced = Regex
            .Matches(project, @"<(?:Project|Package)Reference\s+Include=""(?<path>[^""]+)""")
            .Select(match => match.Groups["path"].Value)
            .Where(path => path.Contains("Broiler.Documents.Pdf", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(referenced);
    }

    [Fact]
    public void No_Cli_Source_Composes_The_Pdf_Codec()
    {
        // A mention in a comment or a help string explaining *why* PDF is absent
        // is the point, so the assertion is about the type being used, not about
        // the word appearing.
        var composed = new Regex(@"new\s+PdfDocumentCodec\s*\(", RegexOptions.CultureInvariant);

        string[] offenders = SourceFiles()
            .Where(path => composed.IsMatch(File.ReadAllText(path)) ||
                File.ReadAllText(path).Contains("using Broiler.Documents.Pdf", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(ComponentRoot, path))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_Cli_Adds_No_Third_Party_Runtime_Dependency()
    {
        string project = File.ReadAllText(CliProjectPath);

        Assert.DoesNotContain("<PackageReference", project, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Cli_Stays_Unpacked_So_The_Published_Package_Set_Is_Unchanged()
    {
        // Turning this on is a deliberate release decision, not a side effect of
        // adding a command: a `v*` tag publishes whatever `dotnet pack` produced.
        string project = File.ReadAllText(CliProjectPath);

        Assert.Contains("<IsPackable>false</IsPackable>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_Exit_Code_Names_A_Distinct_Outcome()
    {
        // The codes are a published contract; two of them meaning the same thing
        // would make a harness unable to tell two outcomes apart.
        int[] codes =
        {
            ExitCode.Ok,
            ExitCode.Usage,
            ExitCode.Input,
            ExitCode.Read,
            ExitCode.Write,
            ExitCode.Different,
            ExitCode.Diagnostics,
            ExitCode.Internal,
        };

        Assert.Equal(codes.Length, codes.Distinct().Count());
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory
            .EnumerateFiles(
                Path.Combine(ComponentRoot, "src", "Broiler.Documents.Cli"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj"));

    private static string FindComponentRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")) &&
                File.Exists(Path.Combine(
                    directory.FullName, "src", "Broiler.Documents", "Broiler.Documents.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Broiler.Documents component root not found.");
    }
}
