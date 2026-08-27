using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Broiler.Documents.Markdown.Tests;

public sealed class MarkdownArchitectureTests
{
    private static readonly string[] ExpectedReferences =
    [
        "../Broiler.Documents.Model/Broiler.Documents.Model.csproj",
        "../Broiler.Documents/Broiler.Documents.csproj",
    ];

    [Fact(Timeout = 600000)]
    public void Markdown_Project_Targets_Net10_And_References_Only_Documents_Core()
    {
        XDocument project = XDocument.Load(MarkdownProjectPath());

        Assert.Equal("net10.0", project.Descendants("TargetFramework").Single().Value);
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Equal(ExpectedReferences, ProjectReferences(project));
    }

    [Fact(Timeout = 600000)]
    public void Markdown_Project_Does_Not_Reference_Ui_Dom_Input_Or_Windows()
    {
        string[] references = ProjectReferences(XDocument.Load(MarkdownProjectPath()));

        Assert.DoesNotContain(references, reference => reference.Contains("Broiler.UI", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference => reference.Contains("Dom", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, reference => reference.Contains("Input", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference => reference.Contains("Windows", StringComparison.Ordinal));
    }

    [Fact(Timeout = 600000)]
    public void Markdown_Codec_Has_No_Module_Initializer()
    {
        MethodInfo[] initializers = typeof(MarkdownDocumentCodec).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(method => method.GetCustomAttribute<ModuleInitializerAttribute>() is not null)
            .ToArray();

        Assert.Empty(initializers);
    }

    private static string MarkdownProjectPath() =>
        Path.Combine(FindComponentRoot(), "src", "Broiler.Documents.Markdown", "Broiler.Documents.Markdown.csproj");

    private static string[] ProjectReferences(XDocument project) =>
        project
            .Descendants("ProjectReference")
            .Select(reference => ((string?)reference.Attribute("Include"))?.Replace('\\', '/'))
            .Where(reference => reference is not null)
            .Cast<string>()
            .OrderBy(reference => reference, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// The Broiler.Documents repository root: the directory that owns
    /// <c>Directory.Build.props</c> and holds the component's projects under
    /// <c>src</c>. Found by walking up from the test binary, so it resolves the
    /// same way standalone and when this component is checked out inside the
    /// aggregate repository.
    /// </summary>
    private static string FindComponentRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")) &&
                File.Exists(Path.Combine(
                    directory.FullName, "src", "Broiler.Documents", "Broiler.Documents.csproj")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Broiler.Documents component root not found.");
    }
}
