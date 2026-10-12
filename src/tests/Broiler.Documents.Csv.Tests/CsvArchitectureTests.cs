using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Broiler.Documents.TestSupport;

namespace Broiler.Documents.Csv.Tests;

public sealed class CsvArchitectureTests
{
    private static readonly string[] ExpectedReferences =
    [
        "../Broiler.Documents.Model/Broiler.Documents.Model.csproj",
        "../Broiler.Documents/Broiler.Documents.csproj",
    ];

    [Fact]
    public void Csv_Project_Targets_Net10_And_References_Only_Documents_Core()
    {
        XDocument project = XDocument.Load(CsvProjectPath());

        Assert.Equal("net10.0", project.Descendants("TargetFramework").Single().Value);
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Equal(ExpectedReferences, RepositoryFiles.ProjectReferences(project));
    }

    [Fact]
    public void Csv_Project_Does_Not_Reference_Ui_Dom_Input_Or_Windows()
    {
        string[] references = RepositoryFiles.ProjectReferences(XDocument.Load(CsvProjectPath()));

        Assert.DoesNotContain(references, reference => reference.Contains("Broiler.UI", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference => reference.Contains("Dom", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, reference => reference.Contains("Input", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference => reference.Contains("Windows", StringComparison.Ordinal));
    }

    [Fact]
    public void Csv_Codec_Has_No_Module_Initializer()
    {
        MethodInfo[] initializers = [.. typeof(CsvDocumentCodec).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(method => method.GetCustomAttribute<ModuleInitializerAttribute>() is not null)];

        Assert.Empty(initializers);
    }

    [Fact]
    public void Csv_Codec_Registers_No_Encoding_Provider()
    {
        // The Windows-1252 fallback is a table in Broiler.Documents, not
        // CodePagesEncodingProvider: registering a provider is process-wide
        // state, which ADR 0003 rules out for a codec.
        string source = string.Concat(Directory
            .EnumerateFiles(Path.GetDirectoryName(CsvProjectPath())!, "*.cs", SearchOption.AllDirectories)
            .Where(path => !RepositoryFiles.IsBuildOutput(path))
            .Select(File.ReadAllText));

        Assert.DoesNotContain("RegisterProvider", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CodePagesEncodingProvider", source, StringComparison.Ordinal);
    }

    private static string CsvProjectPath() =>
        RepositoryFiles.ProjectPath("Broiler.Documents.Csv");
}
