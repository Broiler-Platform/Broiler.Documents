using System.Text.Json;
using System.Text.RegularExpressions;

namespace Broiler.Documents.Tests;

/// <summary>
/// Guards the PDF Phase 0 reset. These tests intentionally protect architecture
/// and governance only; they do not imply that PDF parsing or writing exists.
/// </summary>
public sealed class PdfPhaseZeroGuardTests
{
    private static readonly string[] RequiredDocuments =
    [
        "Broiler.Documents/docs/pdf-support-roadmap.md",
        "Broiler.Documents/docs/pdf-feature-matrix.md",
        "Broiler.Documents/docs/pdf-ip-licensing-register.md",
        "Broiler.Documents/docs/pdf-approved-sources.md",
        "Broiler.Documents/docs/pdf-corpus-manifest.schema.json",
        "Broiler.Documents/docs/pdf-corpus-manifest.json",
        "Broiler.Documents/docs/pdf-phase0-status.md",
        "Broiler.Documents/docs/pdf-extension-points.md",
        "Broiler.Documents/docs/pdf-construct-inventory.md",
        "Broiler.Documents/docs/adr/0007-pdf-component-scope-and-delivery.md",
        "Broiler.Documents/docs/adr/0008-pdf-codec-requests-results-and-commit.md",
        "Broiler.Documents/docs/adr/0009-pdf-security-resources-and-privacy.md",
        "Broiler.Documents/docs/adr/0010-pdf-pagination-units-fonts-and-platforms.md",
        "Broiler.Documents/docs/adr/0011-pdf-standards-ip-provenance-and-claims.md",
        "Broiler.Documents/docs/adr/0012-pdf-base-implementation-and-composed-extensions.md",
    ];

    private static readonly string[] SharedSourceRoots =
    [
        "Broiler.Documents/Broiler.Documents",
        "Broiler.Documents/Broiler.Documents.Model",
        "Broiler.Graphics/Broiler.Graphics",
        "Broiler.Media/Broiler.Media.Image",
    ];

    [Fact(Timeout = 600000)]
    public void Phase_Zero_Decisions_And_Registers_Are_Versioned()
    {
        string root = FindRepositoryRoot();
        Assert.All(RequiredDocuments, path => Assert.True(File.Exists(Path.Combine(root, path)), path));
    }

    [Fact(Timeout = 600000)]
    public void Corpus_Starts_Empty_Instead_Of_Importing_Legacy_Fixtures()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "Broiler.Documents/docs/pdf-corpus-manifest.json")));

        Assert.Equal(1, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Empty(manifest.RootElement.GetProperty("samples").EnumerateArray());
    }

    [Fact(Timeout = 600000)]
    public void Cli_Has_No_Legacy_External_Pdf_Process_Surface()
    {
        string root = FindRepositoryRoot();
        string program = File.ReadAllText(Path.Combine(root, "src/Broiler.Cli/Program.cs"));
        string[] retiredTokens =
        [
            "--convert-pdf",
            "--preserve-layout",
            "BROILER_PDF_APP",
            "PdfConverterProcessRunner",
            "Broiler.Pdf",
        ];

        Assert.All(retiredTokens, token => Assert.DoesNotContain(token, program, StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(Path.Combine(root, "src/Broiler.Cli.Tests/PdfToWordConverterTests.cs")));
    }

    [Fact(Timeout = 600000)]
    public void Shared_Components_Do_Not_Expose_Pdf_Specific_Types_Or_Namespaces()
    {
        string root = FindRepositoryRoot();
        var violations = new List<string>();
        var pdfType = new Regex(
            @"\b(?:class|record|struct|interface|enum)\s+Pdf[A-Z]",
            RegexOptions.CultureInvariant);

        foreach (string relativeRoot in SharedSourceRoots)
        {
            string sourceRoot = Path.Combine(root, relativeRoot);
            foreach (string file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                         .Where(path => !IsBuildOutput(path)))
            {
                string source = File.ReadAllText(file);
                if (source.Contains("namespace Broiler.Documents.Pdf", StringComparison.Ordinal) ||
                    pdfType.IsMatch(source))
                    violations.Add(Path.GetRelativePath(root, file));
            }

            foreach (string project in Directory.EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories))
            {
                if (File.ReadAllText(project).Contains("Broiler.Documents.Pdf", StringComparison.OrdinalIgnoreCase))
                    violations.Add(Path.GetRelativePath(root, project));
            }
        }

        Assert.Empty(violations);
    }

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")) &&
                Directory.Exists(Path.Combine(directory.FullName, "Broiler.Documents")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Broiler repository root not found.");
    }
}
