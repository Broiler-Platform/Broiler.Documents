using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Broiler.Documents.TestSupport;

namespace Broiler.Documents.Html.Tests;

public sealed class HtmlArchitectureTests
{
    private static readonly string[] ExpectedReferences =
    [
        "../Broiler.Documents.Model/Broiler.Documents.Model.csproj",
        "../Broiler.Documents/Broiler.Documents.csproj",
    ];

    [Fact]
    public void Html_Project_Targets_Net10_And_References_Documents_And_Dom()
    {
        XDocument project = XDocument.Load(HtmlProjectPath());

        Assert.Equal("net10.0", project.Descendants("TargetFramework").Single().Value);
        Assert.Equal(["Broiler.Dom", "Broiler.Dom.Html"], RepositoryFiles.PackageReferences(project));
        Assert.Equal(ExpectedReferences, RepositoryFiles.ProjectReferences(project));
    }

    [Fact]
    public void Html_Project_Does_Not_Reference_Ui_Input_Or_Windows()
    {
        string[] references = RepositoryFiles.ProjectReferences(XDocument.Load(HtmlProjectPath()));

        Assert.DoesNotContain(references, reference => reference.Contains("Broiler.UI", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference => reference.Contains("Input", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference => reference.Contains("Windows", StringComparison.Ordinal));
    }

    [Fact]
    public void Html_Codec_Has_No_Module_Initializer()
    {
        MethodInfo[] initializers = [.. typeof(HtmlDocumentCodec).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(method => method.GetCustomAttribute<ModuleInitializerAttribute>() is not null)];

        Assert.Empty(initializers);
    }

    private static string HtmlProjectPath() =>
        RepositoryFiles.ProjectPath("Broiler.Documents.Html");
}
