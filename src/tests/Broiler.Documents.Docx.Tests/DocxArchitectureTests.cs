using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Broiler.Documents.TestSupport;

namespace Broiler.Documents.Docx.Tests;

public sealed class DocxArchitectureTests
{
    private static readonly string[] ExpectedReferences =
    [
        "../Broiler.Documents.Model/Broiler.Documents.Model.csproj",
        "../Broiler.Documents/Broiler.Documents.csproj",
    ];

    [Fact(Timeout = 600000)]
    public void Docx_Project_Targets_Net10_And_References_Only_Documents_Assemblies()
    {
        XDocument project = XDocument.Load(DocxProjectPath());

        Assert.Equal("net10.0", project.Descendants("TargetFramework").Single().Value);
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Equal(ExpectedReferences, RepositoryFiles.ProjectReferences(project));
    }

    [Fact(Timeout = 600000)]
    public void Docx_Project_Does_Not_Reference_Ui_Dom_Input_Or_Windows()
    {
        string[] references = RepositoryFiles.ProjectReferences(XDocument.Load(DocxProjectPath()));

        Assert.DoesNotContain(references, reference => reference.Contains("Broiler.UI", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference => reference.Contains("Broiler.DOM", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference => reference.Contains("Input", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference => reference.Contains("Windows", StringComparison.Ordinal));
    }

    [Fact(Timeout = 600000)]
    public void Docx_Codec_Has_No_Module_Initializer()
    {
        MethodInfo[] initializers = typeof(DocxDocumentCodec).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(method => method.GetCustomAttribute<ModuleInitializerAttribute>() is not null)
            .ToArray();

        Assert.Empty(initializers);
    }

    private static string DocxProjectPath() =>
        RepositoryFiles.ProjectPath("Broiler.Documents.Docx");
}
