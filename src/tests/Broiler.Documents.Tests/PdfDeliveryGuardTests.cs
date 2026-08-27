using System.Text.RegularExpressions;

namespace Broiler.Documents.Tests;

/// <summary>
/// Guards the PDF delivery boundary described in the PDF support roadmap §4.1
/// and the registration rule in §10.1.
/// </summary>
/// <remarks>
/// <para>
/// The codec is not a published capability: the package is not packed, and it
/// reaches an application only where a composition root names it. It is now
/// registered — for opening only — by the Windows and Linux Writer heads, which
/// is the read-preview integration §10.1 describes. Everywhere else the old rule
/// stands unchanged, and these tests fail the build if it slips, so "we shipped
/// PDF by accident" is still not a thing that can happen quietly.
/// </para>
/// <para>
/// The rule that matters most here is the one about transitivity. A codec must
/// arrive in a head because that head asked for it, never because it is a
/// transitive reference of something shared: putting it in
/// <c>Broiler.Writer.Core</c> would hand it to the Android and WebAssembly
/// Writers too, whose package-size, memory, trimming and AOT gates it has not
/// passed.
/// </para>
/// </remarks>
public sealed class PdfDeliveryGuardTests
{
    /// <summary>
    /// The only files in <c>src</c> that may name the PDF codec: the two desktop
    /// composition roots that register it, their project files, and the Writer
    /// tests that cover the registration.
    /// </summary>
    private static readonly string[] RegistrationSites =
    [
        "src/Broiler.Writer.Windows/Program.cs",
        "src/Broiler.Writer.Windows/Broiler.Writer.Windows.csproj",
        "src/Broiler.Writer.Linux/Program.cs",
        "src/Broiler.Writer.Linux/Broiler.Writer.Linux.csproj",
        "src/Broiler.Writer.FormatCodes.Tests/WriterPdfFormatTests.cs",
        "src/Broiler.Writer.FormatCodes.Tests/Broiler.Writer.FormatCodes.Tests.csproj",
    ];

    /// <summary>
    /// Projects that must never name the PDF codec, called out by name because
    /// what each one would enable is different. The shared Writer core would
    /// enable it in every head at once; the Android and WebAssembly heads have
    /// their own outstanding gates.
    /// </summary>
    private static readonly string[] ProjectsThatMustNotCarryPdf =
    [
        "src/Broiler.Writer",
        "src/Broiler.Writer.Android",
        "src/Broiler.Writer.WebAssembly",
    ];

    [Fact(Timeout = 600000)]
    public void The_Pdf_Package_Is_Not_Packable_Before_Its_Release_Gates_Pass()
    {
        string project = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Broiler.Documents/Broiler.Documents.Pdf/Broiler.Documents.Pdf.csproj"));

        Assert.Contains("<IsPackable>false</IsPackable>", project);
    }

    [Fact(Timeout = 600000)]
    public void The_Pdf_Codec_References_No_Third_Party_Runtime_Dependency()
    {
        string root = FindRepositoryRoot();
        string project = File.ReadAllText(Path.Combine(
            root,
            "Broiler.Documents/Broiler.Documents.Pdf/Broiler.Documents.Pdf.csproj"));

        // The base build is deliberately dependency-free: everything it decodes,
        // maps, or measures is implemented in this repository or in the runtime.
        Assert.DoesNotContain("<PackageReference", project);

        var allowed = new[] { "Broiler.Documents.csproj", "Broiler.Documents.Model.csproj" };
        foreach (Match match in Regex.Matches(project, @"<ProjectReference\s+Include=""([^""]+)"""))
        {
            string referenced = Path.GetFileName(match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar));
            Assert.Contains(referenced, allowed);
        }
    }

    [Fact(Timeout = 600000)]
    public void Only_The_Enabled_Heads_Name_The_Pdf_Codec()
    {
        string root = FindRepositoryRoot();

        string[] violations = NamesPdf(root, "*.cs")
            .Concat(NamesPdf(root, "*.csproj"))
            .Where(path => !RegistrationSites.Contains(path, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact(Timeout = 600000)]
    public void The_Shared_Writer_Core_And_The_Mobile_Heads_Cannot_Acquire_Pdf()
    {
        string root = FindRepositoryRoot();
        string[] carriers = NamesPdf(root, "*.cs")
            .Concat(NamesPdf(root, "*.csproj"))
            .Where(path => ProjectsThatMustNotCarryPdf.Any(
                project => path.StartsWith(project + "/", StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Stated separately from the allow-list test because the consequence is
        // different: a reference here is not one head enabling a codec, it is
        // every head that references the shared core acquiring it silently.
        Assert.Empty(carriers);
    }

    [Fact(Timeout = 600000)]
    public void No_Head_Registers_The_Pdf_Codec_For_Saving()
    {
        string root = FindRepositoryRoot();

        // The writer exists and passes its own unit tests, but PDF export has its
        // own release gate. Every registration must therefore enable opening only,
        // which is what keeps the capability out of the Save dialog, the save
        // dispatch, and the extension a user can type into the Save box.
        foreach (string site in RegistrationSites.Where(path => path.EndsWith("Program.cs", StringComparison.Ordinal)))
        {
            string source = File.ReadAllText(Path.Combine(root, site.Replace('/', Path.DirectorySeparatorChar)));
            foreach (Match match in Regex.Matches(source, @"new\s+WriterDocumentFormat\((?<arguments>[^;]*?)\)\s*\)"))
            {
                string arguments = match.Groups["arguments"].Value;
                if (!arguments.Contains("PdfDocumentCodec", StringComparison.Ordinal))
                    continue;

                Assert.Contains("WriterFormatCapabilities.Open", arguments, StringComparison.Ordinal);
                Assert.DoesNotContain("WriterFormatCapabilities.Save", arguments, StringComparison.Ordinal);
                Assert.DoesNotContain("WriterFormatCapabilities.OpenAndSave", arguments, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>Repo-relative paths under <c>src</c> whose text names the PDF codec.</summary>
    private static IEnumerable<string> NamesPdf(string root, string pattern) =>
        Directory.EnumerateFiles(Path.Combine(root, "src"), pattern, SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path =>
            {
                string source = File.ReadAllText(path);
                return source.Contains("PdfDocumentCodec", StringComparison.Ordinal) ||
                    source.Contains("Broiler.Documents.Pdf", StringComparison.OrdinalIgnoreCase);
            })
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'));

    [Fact(Timeout = 600000)]
    public void No_Pdf_Fixture_Is_Committed_Outside_The_Rights_Aware_Corpus()
    {
        string root = FindRepositoryRoot();

        // Every PDF the tests use is generated in code. A committed .pdf would need
        // an entry in the corpus manifest with its provenance and rights first.
        string[] committed = Directory
            .EnumerateFiles(Path.Combine(root, "Broiler.Documents"), "*.pdf", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        Assert.Empty(committed);
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
