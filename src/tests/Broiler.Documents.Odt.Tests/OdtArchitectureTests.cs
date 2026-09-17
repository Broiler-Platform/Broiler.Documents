using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Broiler.Documents.TestSupport;

namespace Broiler.Documents.Odt.Tests;

public sealed class OdtArchitectureTests
{
    private static readonly string[] ExpectedReferences =
    [
        "../Broiler.Documents.Model/Broiler.Documents.Model.csproj",
        "../Broiler.Documents/Broiler.Documents.csproj",
    ];

    [Fact]
    public void Odt_Project_Targets_Net10_And_References_Only_Documents_Assemblies()
    {
        XDocument project = XDocument.Load(OdtProjectPath());

        Assert.Equal("net10.0", project.Descendants("TargetFramework").Single().Value);
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Equal(ExpectedReferences, RepositoryFiles.ProjectReferences(project));
    }

    [Fact]
    public void Odt_Project_Does_Not_Reference_Ui_Dom_Input_Or_Windows()
    {
        string[] references = RepositoryFiles.ProjectReferences(XDocument.Load(OdtProjectPath()));

        Assert.DoesNotContain(references, reference => reference.Contains("Broiler.UI", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference => reference.Contains("Broiler.DOM", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference => reference.Contains("Input", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference => reference.Contains("Windows", StringComparison.Ordinal));
    }

    [Fact]
    public void Odt_Codec_Has_No_Module_Initializer()
    {
        MethodInfo[] initializers = [.. typeof(OdtDocumentCodec).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(method => method.GetCustomAttribute<ModuleInitializerAttribute>() is not null)];

        Assert.Empty(initializers);
    }

    private static string OdtProjectPath() =>
        RepositoryFiles.ProjectPath("Broiler.Documents.Odt");
}
