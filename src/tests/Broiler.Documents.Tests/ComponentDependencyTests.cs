using System.Xml.Linq;

namespace Broiler.Documents.Tests;

/// <summary>Keep standalone builds on the declared package graph, independent of sibling checkouts.</summary>
public sealed class ComponentDependencyTests
{
    [Fact]
    public void Project_References_Stay_Inside_This_Component()
    {
        string root = PdfGuardRoots.Component;
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            XDocument project = XDocument.Load(path);
            foreach (XElement reference in project.Descendants("ProjectReference"))
            {
                string include = ((string)reference.Attribute("Include")!).Replace('\\', '/');
                string target = Path.GetFullPath(include, Path.GetDirectoryName(path)!);
                string relative = Path.GetRelativePath(root, target);
                Assert.False(Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal), target);
                Assert.True(File.Exists(target), target);
            }
        }
    }

    [Fact]
    public void Runtime_Packages_Are_Broiler_Owned_And_Centrally_Versioned()
    {
        string root = PdfGuardRoots.Component;
        XDocument central = XDocument.Load(Path.Combine(root, "Directory.Packages.props"));
        var versions = central.Descendants("PackageVersion")
            .ToDictionary(item => (string)item.Attribute("Include")!, item => (string)item.Attribute("Version")!);
        Assert.Equal("true", central.Descendants("ManagePackageVersionsCentrally").Single().Value);
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            bool isTest = Path.GetRelativePath(Path.Combine(root, "src"), path).Replace('\\', '/').StartsWith("tests/", StringComparison.Ordinal);
            foreach (XElement reference in XDocument.Load(path).Descendants("PackageReference"))
            {
                string id = (string)reference.Attribute("Include")!;
                Assert.Null(reference.Attribute("Version"));
                Assert.Null(reference.Attribute("VersionOverride"));
                Assert.True(versions.TryGetValue(id, out string? version), id);
                Assert.False(string.IsNullOrWhiteSpace(version));
                if (!isTest)
                    Assert.StartsWith("Broiler.", id, StringComparison.Ordinal);
            }
        }
    }
}
