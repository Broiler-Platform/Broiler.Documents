using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Broiler.Documents.Html.Tests;

public sealed class HtmlArchitectureTests
{
    private static readonly string[] ExpectedReferences =
    [
        "../../Broiler.DOM/Broiler.Dom.Html/Broiler.Dom.Html.csproj",
        "../../Broiler.DOM/Broiler.Dom/Broiler.Dom.csproj",
        "../Broiler.Documents.Model/Broiler.Documents.Model.csproj",
        "../Broiler.Documents/Broiler.Documents.csproj",
    ];

    [Fact(Timeout = 600000)]
    public void Html_Project_Targets_Net10_And_References_Documents_And_Dom()
    {
        XDocument project = XDocument.Load(HtmlProjectPath());

        Assert.Equal("net10.0", project.Descendants("TargetFramework").Single().Value);
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Equal(ExpectedReferences, ProjectReferences(project));
    }

    [Fact(Timeout = 600000)]
    public void Html_Project_Does_Not_Reference_Ui_Input_Or_Windows()
    {
        string[] references = ProjectReferences(XDocument.Load(HtmlProjectPath()));

        Assert.DoesNotContain(references, reference => reference.Contains("Broiler.UI", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference => reference.Contains("Input", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference => reference.Contains("Windows", StringComparison.Ordinal));
    }

    [Fact(Timeout = 600000)]
    public void Html_Codec_Has_No_Module_Initializer()
    {
        MethodInfo[] initializers = typeof(HtmlDocumentCodec).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(method => method.GetCustomAttribute<ModuleInitializerAttribute>() is not null)
            .ToArray();

        Assert.Empty(initializers);
    }

    private static string HtmlProjectPath() =>
        Path.Combine(FindComponentRoot(), "Broiler.Documents.Html", "Broiler.Documents.Html.csproj");

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
