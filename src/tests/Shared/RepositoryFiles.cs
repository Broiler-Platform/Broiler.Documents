using System.Xml.Linq;

namespace Broiler.Documents.TestSupport;

/// <summary>Repository discovery and project inspection shared by architecture tests.</summary>
internal static class RepositoryFiles
{
    internal static string Root { get; } = FindRoot();

    internal static string ProjectPath(string name) =>
        Path.Combine(Root, "src", name, name + ".csproj");

    internal static string[] ProjectReferences(XDocument project) => References(project, "ProjectReference");

    internal static string[] PackageReferences(XDocument project) => References(project, "PackageReference");

    internal static bool IsBuildOutput(string path) =>
        path.Replace('\\', '/').Split('/').Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase));

    private static string[] References(XDocument project, string elementName) =>
        [.. project.Descendants(elementName)
            .Select(reference => ((string?)reference.Attribute("Include")
                ?? throw new InvalidDataException($"A {elementName} is missing Include.")).Replace('\\', '/'))
            .OrderBy(reference => reference, StringComparer.Ordinal)];

    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")) &&
                File.Exists(Path.Combine(directory.FullName, "src", "Broiler.Documents", "Broiler.Documents.csproj")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Broiler.Documents component root not found.");
    }
}
