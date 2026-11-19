using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Broiler.Documents.FormatCodes;
using Broiler.Documents.Model;
using Broiler.Documents.Pagination;
using Broiler.Documents.TestSupport;

namespace Broiler.Documents.Tests;

/// <summary>
/// Architecture guards for ADR 0001/0003: the codec framework references only the
/// document model, stays free of UI/DOM/platform edges, and registers codecs
/// explicitly (no hidden global registration).
/// </summary>
public sealed partial class DocumentsArchitectureTests
{
    private static readonly string[] ExpectedReferences =
    [
        "../Broiler.Documents.Model/Broiler.Documents.Model.csproj",
    ];

    [Fact]
    public void Documents_Project_Targets_Net10_And_References_Only_The_Model()
    {
        XDocument project = XDocument.Load(DocumentsProjectPath());

        Assert.Equal("net10.0", project.Descendants("TargetFramework").Single().Value);
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Equal(ExpectedReferences, RepositoryFiles.ProjectReferences(project));
    }

    [Fact]
    public void Documents_Project_Does_Not_Reference_Ui_Dom_Input_Or_Windows()
    {
        string[] references = RepositoryFiles.ProjectReferences(XDocument.Load(DocumentsProjectPath()));

        Assert.DoesNotContain(references, r => r.Contains("Broiler.UI", StringComparison.Ordinal));
        Assert.DoesNotContain(references, r => r.Contains("Dom", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, r => r.Contains("Input", StringComparison.Ordinal));
        Assert.DoesNotContain(references, r => r.Contains("Windows", StringComparison.Ordinal));
    }

    [Fact]
    public void Catalog_Requires_Explicit_Codec_Registration()
    {
        // No parameterless constructor: codecs must be supplied by the caller.
        Assert.DoesNotContain(
            typeof(DocumentCodecCatalog).GetConstructors(),
            constructor => constructor.GetParameters().Length == 0);
    }

    [Fact]
    public void Framework_Has_No_Module_Initializer()
    {
        MethodInfo[] initializers = [.. typeof(DocumentCodec).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(method => method.GetCustomAttribute<ModuleInitializerAttribute>() is not null)];

        Assert.Empty(initializers);
    }

    [Fact]
    public void Core_Assemblies_Do_Not_Expose_Format_Or_Pdf_Specific_Types()
    {
        Assembly[] coreAssemblies =
        [
            typeof(RichTextDocument).Assembly,
            typeof(DocumentCodec).Assembly,
            typeof(DocumentPaginator).Assembly,
            typeof(FormatCodeProjector).Assembly,
        ];

        foreach (Assembly assembly in coreAssemblies)
        {
            Assert.DoesNotContain(assembly.GetExportedTypes(), type =>
                PdfTypeRegex().IsMatch(type.Name) ||
                (type.Namespace?.StartsWith("Broiler.Documents.Pdf", StringComparison.Ordinal) ?? false));
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
                reference.Name?.StartsWith("Broiler.Documents.Pdf", StringComparison.Ordinal) ?? false);
        }
    }

    [Fact]
    public void Every_Project_In_The_Tree_Is_In_The_Solution()
    {
        string solution = File.ReadAllText(Path.Combine(RepositoryFiles.Root, "Broiler.Documents.slnx"));

        string[] unregistered = [.. Directory
            .EnumerateFiles(Path.Combine(RepositoryFiles.Root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !RepositoryFiles.IsBuildOutput(path))
            .Select(path => Path.GetRelativePath(RepositoryFiles.Root, path).Replace('\\', '/'))
            .Where(path => !solution.Contains("\"" + path + "\"", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];

        Assert.Empty(unregistered);
    }

    [GeneratedRegex(@"^Pdf[A-Z]", RegexOptions.CultureInvariant)]
    private static partial Regex PdfTypeRegex();

    private static string DocumentsProjectPath() =>
        RepositoryFiles.ProjectPath("Broiler.Documents");
}
