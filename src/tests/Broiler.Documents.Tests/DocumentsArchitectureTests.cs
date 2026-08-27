using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Broiler.Documents.Tests;

/// <summary>
/// Architecture guards for ADR 0001/0003: the codec framework references only the
/// document model, stays free of UI/DOM/platform edges, and registers codecs
/// explicitly (no hidden global registration).
/// </summary>
public sealed class DocumentsArchitectureTests
{
    private static readonly string[] ExpectedReferences =
    [
        "../Broiler.Documents.Model/Broiler.Documents.Model.csproj",
    ];

    [Fact(Timeout = 600000)]
    public void Documents_Project_Targets_Net10_And_References_Only_The_Model()
    {
        XDocument project = XDocument.Load(DocumentsProjectPath());

        Assert.Equal("net10.0", project.Descendants("TargetFramework").Single().Value);
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Equal(ExpectedReferences, ProjectReferences(project));
    }

    [Fact(Timeout = 600000)]
    public void Documents_Project_Does_Not_Reference_Ui_Dom_Input_Or_Windows()
    {
        string[] references = ProjectReferences(XDocument.Load(DocumentsProjectPath()));

        Assert.DoesNotContain(references, r => r.Contains("Broiler.UI", StringComparison.Ordinal));
        Assert.DoesNotContain(references, r => r.Contains("Dom", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, r => r.Contains("Input", StringComparison.Ordinal));
        Assert.DoesNotContain(references, r => r.Contains("Windows", StringComparison.Ordinal));
    }

    [Fact(Timeout = 600000)]
    public void Catalog_Requires_Explicit_Codec_Registration()
    {
        // No parameterless constructor: codecs must be supplied by the caller.
        Assert.DoesNotContain(
            typeof(DocumentCodecCatalog).GetConstructors(),
            constructor => constructor.GetParameters().Length == 0);
    }

    [Fact(Timeout = 600000)]
    public void Framework_Has_No_Module_Initializer()
    {
        MethodInfo[] initializers = typeof(DocumentCodec).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(method => method.GetCustomAttribute<ModuleInitializerAttribute>() is not null)
            .ToArray();

        Assert.Empty(initializers);
    }

    private static string DocumentsProjectPath() =>
        Path.Combine(FindComponentRoot(), "Broiler.Documents", "Broiler.Documents.csproj");

    private static string[] ProjectReferences(XDocument project) =>
        project
            .Descendants("ProjectReference")
            .Select(reference => ((string?)reference.Attribute("Include"))?.Replace('\\', '/'))
            .Where(reference => reference is not null)
            .Cast<string>()
            .OrderBy(reference => reference, StringComparer.Ordinal)
            .ToArray();

    private static string FindComponentRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "Broiler.Documents");
            if (Directory.Exists(candidate) &&
                File.Exists(Path.Combine(directory.FullName, ".gitmodules")) &&
                File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
                return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Broiler.Documents component root not found.");
    }
}
