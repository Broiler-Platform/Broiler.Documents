namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// The repository root the ODT guards work against, and the build-output rule
/// they share.
/// </summary>
/// <remarks>
/// The ODT guards only ever read this component: its codec source, its documents,
/// and its own tracked files. There is no aggregate-root case here as there is
/// for the PDF head guards, because nothing about ODT's claims lives in an
/// application repository.
/// </remarks>
internal static class OdtGuardRoots
{
    /// <summary>
    /// The Broiler.Documents repository root: the directory that owns
    /// <c>Directory.Build.props</c> and holds the component's projects under
    /// <c>src</c>. Found by walking up from the test binary, so it resolves the
    /// same way standalone and when this component is checked out inside the
    /// aggregate repository.
    /// </summary>
    internal static string Component { get; } = FindComponent();

    /// <summary>Whether a path sits inside a <c>bin</c> or <c>obj</c> directory.</summary>
    internal static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");

    private static string FindComponent()
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
