using System.Xml.Linq;

namespace Broiler.Documents.Model.Tests;

/// <summary>
/// Architecture guards for ADR 0001/0002: the promoted model is platform-neutral
/// and references only Broiler.Graphics — no UI, DOM, input, or platform edge.
/// </summary>
public sealed class ModelArchitectureTests
{
    private static readonly string[] ExpectedReferences =
    [
        "../../Broiler.Graphics/Broiler.Graphics/Broiler.Graphics.csproj",
    ];

    [Fact(Timeout = 600000)]
    public void Model_Project_Targets_Net10_And_References_Only_Graphics()
    {
        XDocument project = XDocument.Load(ModelProjectPath());

        Assert.Equal("net10.0", project.Descendants("TargetFramework").Single().Value);
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Equal(ExpectedReferences, ProjectReferences(project));
    }

    [Fact(Timeout = 600000)]
    public void Model_Project_Does_Not_Reference_Ui_Dom_Input_Or_Windows()
    {
        string[] references = ProjectReferences(XDocument.Load(ModelProjectPath()));

        Assert.DoesNotContain(references, r => r.Contains("Broiler.UI", StringComparison.Ordinal));
        Assert.DoesNotContain(references, r => r.Contains("Dom", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, r => r.Contains("Input", StringComparison.Ordinal));
        Assert.DoesNotContain(references, r => r.Contains("Windows", StringComparison.Ordinal));
    }

    [Fact(Timeout = 600000)]
    public void Model_Assembly_Only_References_Graphics_At_Runtime()
    {
        string[] referenced = typeof(RichTextDocument).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .Where(name => name.StartsWith("Broiler.", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(["Broiler.Graphics"], referenced);
    }

    private static string ModelProjectPath() =>
        Path.Combine(FindComponentRoot(), "Broiler.Documents.Model", "Broiler.Documents.Model.csproj");

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
