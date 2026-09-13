using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Broiler.Documents.TestSupport;

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
        Assert.Equal(ExpectedReferences, RepositoryFiles.ProjectReferences(project));
    }

    [Fact(Timeout = 600000)]
    public void Documents_Project_Does_Not_Reference_Ui_Dom_Input_Or_Windows()
    {
        string[] references = RepositoryFiles.ProjectReferences(XDocument.Load(DocumentsProjectPath()));

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
        RepositoryFiles.ProjectPath("Broiler.Documents");
}
