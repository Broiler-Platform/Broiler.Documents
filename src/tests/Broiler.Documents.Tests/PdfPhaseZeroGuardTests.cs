using System.Text.Json;
using System.Text.RegularExpressions;

namespace Broiler.Documents.Tests;

/// <summary>
/// Guards the PDF Phase 0 reset. These tests intentionally protect architecture
/// and governance only; they do not imply that PDF parsing or writing exists.
/// </summary>
/// <remarks>
/// Two roots are in play. Everything this component owns - its docs, its
/// registers, its own source - is checked against <see cref="PdfGuardRoots.Component"/>
/// and always runs. The one guard that reaches into an application head is
/// checked against <see cref="PdfGuardRoots.Aggregate"/> and skips when this
/// repository is built standalone, because the head simply is not there to
/// inspect; see <see cref="PdfGuardRoots"/>.
/// </remarks>
public sealed class PdfPhaseZeroGuardTests
{
    /// <summary>Component-relative paths that must exist and stay versioned.</summary>
    private static readonly string[] RequiredDocuments =
    [
        "docs/pdf-support-roadmap.md",
        "docs/pdf-feature-matrix.md",
        "docs/pdf-ip-licensing-register.md",
        "docs/pdf-approved-sources.md",
        "docs/pdf-corpus-manifest.schema.json",
        "docs/pdf-corpus-manifest.json",
        "docs/pdf-phase0-status.md",
        "docs/pdf-extension-points.md",
        "docs/pdf-construct-inventory.md",
        "docs/adr/0007-pdf-component-scope-and-delivery.md",
        "docs/adr/0008-pdf-codec-requests-results-and-commit.md",
        "docs/adr/0009-pdf-security-resources-and-privacy.md",
        "docs/adr/0010-pdf-pagination-units-fonts-and-platforms.md",
        "docs/adr/0011-pdf-standards-ip-provenance-and-claims.md",
        "docs/adr/0012-pdf-base-implementation-and-composed-extensions.md",
    ];

    /// <summary>
    /// Source trees that every Broiler head links, so a PDF-specific type
    /// appearing in one of them would reach heads that never asked for it. The
    /// last two arrive through the submodules at the repository root.
    /// </summary>
    private static readonly string[] SharedSourceRoots =
    [
        "src/Broiler.Documents",
        "src/Broiler.Documents.Model",
        "Broiler.Graphics/src/Broiler.Graphics",
        "Broiler.Graphics/Broiler.Media/src/Broiler.Media.Image",
    ];

    [Fact(Timeout = 600000)]
    public void Phase_Zero_Decisions_And_Registers_Are_Versioned()
    {
        string root = PdfGuardRoots.Component;
        Assert.All(RequiredDocuments, path => Assert.True(File.Exists(Path.Combine(root, path)), path));
    }

    [Fact(Timeout = 600000)]
    public void Corpus_Starts_Empty_Instead_Of_Importing_Legacy_Fixtures()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(PdfGuardRoots.Component, "docs/pdf-corpus-manifest.json")));

        Assert.Equal(1, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Empty(manifest.RootElement.GetProperty("samples").EnumerateArray());
    }

    [SkippableFact(Timeout = 600000)]
    public void Cli_Has_No_Legacy_External_Pdf_Process_Surface()
    {
        string root = PdfGuardRoots.RequireAggregate();

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
        string root = PdfGuardRoots.Component;
        var violations = new List<string>();
        var pdfType = new Regex(
            @"\b(?:class|record|struct|interface|enum)\s+Pdf[A-Z]",
            RegexOptions.CultureInvariant);

        foreach (string relativeRoot in SharedSourceRoots)
        {
            string sourceRoot = Path.Combine(root, relativeRoot);

            // A missing root means the submodules were not checked out. Fail
            // rather than skip: silently covering two roots instead of four
            // would report a pass this test did not earn.
            Assert.True(Directory.Exists(sourceRoot),
                $"Shared source root '{relativeRoot}' is missing. Run: git submodule update --init");

            foreach (string file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                         .Where(path => !PdfGuardRoots.IsBuildOutput(path)))
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
}
