namespace Broiler.Documents.Tests;

/// <summary>
/// The two directory roots the PDF guards work against, and the rule for when a
/// guard may be skipped.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Component"/> is this repository. It always exists, so every guard
/// over this component's own source, docs, and registers runs unconditionally.
/// </para>
/// <para>
/// <see cref="Aggregate"/> is the repository that holds the application heads -
/// <c>src/Broiler.Writer.*</c> and <c>src/Broiler.Cli</c>. Broiler.Documents is
/// consumed by those heads but does not contain them, so when this repository is
/// built standalone there is nothing for a head guard to read. Those guards use
/// <see cref="RequireAggregate"/> and report as skipped rather than passing on an
/// empty check; checked out inside the aggregate they run in full.
/// </para>
/// </remarks>
internal static class PdfGuardRoots
{
    /// <summary>
    /// The Broiler.Documents repository root: owns <c>Directory.Build.props</c>
    /// and holds this component's projects under <c>src</c>.
    /// </summary>
    internal static string Component { get; } = FindComponent();

    /// <summary>
    /// The aggregate repository root, or <see langword="null"/> when the
    /// application heads are not part of this checkout.
    /// </summary>
    internal static string? Aggregate { get; } = FindAggregate();

    /// <summary>
    /// The aggregate root, skipping the calling test when the heads are absent.
    /// </summary>
    internal static string RequireAggregate()
    {
        Skip.If(Aggregate is null,
            "The application heads (src/Broiler.Writer.*, src/Broiler.Cli) are not in this " +
            "checkout. This guard runs in the aggregate repository, where they live.");

        return Aggregate!;
    }

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

    private static string? FindAggregate()
    {
        // Keep walking past the component root: in the aggregate layout this
        // component sits one or more levels below it.
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Broiler.Writer.Windows")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src", "Broiler.Cli")))
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }
}
