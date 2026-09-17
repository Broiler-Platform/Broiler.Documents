using System.Xml.Linq;
using Broiler.Documents.TestSupport;

namespace Broiler.Documents.Model.Tests;

/// <summary>
/// Architecture guards for ADR 0001/0002: the promoted model is platform-neutral
/// and references only Broiler.Graphics — no UI, DOM, input, or platform edge.
/// </summary>
public sealed class ModelArchitectureTests
{
    [Fact]
    public void Model_Project_Targets_Net10_And_References_Only_Graphics()
    {
        XDocument project = XDocument.Load(ModelProjectPath());

        Assert.Equal("net10.0", project.Descendants("TargetFramework").Single().Value);
        Assert.Equal("Broiler.Graphics", (string?)Assert.Single(project.Descendants("PackageReference")).Attribute("Include"));
        Assert.Empty(RepositoryFiles.ProjectReferences(project));
    }

    [Fact]
    public void Model_Project_Does_Not_Reference_Ui_Dom_Input_Or_Windows()
    {
        string[] references = RepositoryFiles.ProjectReferences(XDocument.Load(ModelProjectPath()));

        Assert.DoesNotContain(references, r => r.Contains("Broiler.UI", StringComparison.Ordinal));
        Assert.DoesNotContain(references, r => r.Contains("Dom", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, r => r.Contains("Input", StringComparison.Ordinal));
        Assert.DoesNotContain(references, r => r.Contains("Windows", StringComparison.Ordinal));
    }

    [Fact]
    public void Model_Assembly_Only_References_Graphics_At_Runtime()
    {
        string[] referenced = [.. typeof(RichTextDocument).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .Where(name => name.StartsWith("Broiler.", StringComparison.Ordinal))];

        Assert.Equal(["Broiler.Graphics"], referenced);
    }

    private static string ModelProjectPath() =>
        RepositoryFiles.ProjectPath("Broiler.Documents.Model");
}
