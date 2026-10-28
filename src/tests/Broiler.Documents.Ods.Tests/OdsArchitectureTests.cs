using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Broiler.Documents.TestSupport;

namespace Broiler.Documents.Ods.Tests;

public sealed class OdsArchitectureTests
{
    private static readonly string[] ExpectedReferences =
    [
        "../Broiler.Documents.Model/Broiler.Documents.Model.csproj",
        "../Broiler.Documents/Broiler.Documents.csproj",
    ];

    [Fact]
    public void Ods_Project_Targets_Net10_And_References_Only_Documents_Core()
    {
        XDocument project = XDocument.Load(OdsProjectPath());

        Assert.Equal("net10.0", project.Descendants("TargetFramework").Single().Value);
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Equal(ExpectedReferences, RepositoryFiles.ProjectReferences(project));
    }

    [Fact]
    public void Ods_Project_Does_Not_Reference_Ui_Dom_Input_Or_Windows()
    {
        string[] references = RepositoryFiles.ProjectReferences(XDocument.Load(OdsProjectPath()));

        Assert.DoesNotContain(references, reference => reference.Contains("Broiler.UI", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference => reference.Contains("Dom", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, reference => reference.Contains("Input", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference => reference.Contains("Windows", StringComparison.Ordinal));
    }

    [Fact]
    public void Ods_Codec_Has_No_Module_Initializer()
    {
        MethodInfo[] initializers = [.. typeof(OdsDocumentCodec).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(method => method.GetCustomAttribute<ModuleInitializerAttribute>() is not null)];

        Assert.Empty(initializers);
    }

    private static string OdsProjectPath() =>
        RepositoryFiles.ProjectPath("Broiler.Documents.Ods");
}
