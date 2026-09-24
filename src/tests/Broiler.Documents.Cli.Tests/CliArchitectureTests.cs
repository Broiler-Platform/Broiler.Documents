using System.Text.RegularExpressions;
using System.Xml.Linq;
using Broiler.Documents.Cli.Composition;
using Broiler.Documents.Pdf;
using Broiler.Documents.Pdf.Filters;
using Broiler.Documents.Pdf.Images;
using Broiler.Documents.TestSupport;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// Constraints on the CLI head itself, checked against its project file and its
/// source rather than against a reviewer's memory.
/// </summary>
/// <remarks>
/// The PDF guards here are the component-local counterpart of the aggregate's
/// <c>Only_The_Enabled_Heads_Name_The_Pdf_Codec</c>. This head lives in this
/// repository, so unlike the aggregate's heads it can be checked from here, and
/// it must be: a command line is exactly the surface an automated system would
/// come to depend on, so a PDF capability composed here by accident would be
/// hard to withdraw later. This head reads PDF and writes none - the
/// read-preview state of <c>docs/pdf-support-roadmap.md</c> §4.1 - and the
/// guards hold it to exactly that.
/// </remarks>
public sealed partial class CliArchitectureTests
{
    private static readonly string ComponentRoot = RepositoryFiles.Root;

    private static string CliProjectPath =>
        Path.Combine(ComponentRoot, "src", "Broiler.Documents.Cli", "Broiler.Documents.Cli.csproj");

    [Fact]
    public void The_Cli_References_The_Pdf_Codec_And_Only_The_Provider_Package_It_Composes_From()
    {
        string project = File.ReadAllText(CliProjectPath);

        // The font and image providers compose decoders each with a register row
        // of its own. Reaching for one is a decision, not a side effect of
        // wanting PDF read at all: the image package is here for its ICC
        // profile reader (IP-024), and the font package is not here.
        string[] referenced = [.. MyRegex().Matches(project)
            .Select(match => match.Groups["path"].Value)
            .Where(path => path.Contains("Broiler.Documents.Pdf", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetFileName(path.Replace('\\', '/')))];

        Assert.Equal(["Broiler.Documents.Pdf.csproj", "Broiler.Documents.Pdf.Images.csproj"], referenced);
    }

    [Fact]
    public void The_Composed_Pdf_Codec_Converts_Icc_Colour_And_Decodes_No_Image_Filter()
    {
        var reader = Assert.IsType<ReadOnlyCodec>(CodecComposition.CreateCatalog().FindByName("PDF"));
        PdfCodecServices services = Assert.IsType<PdfDocumentCodec>(reader.Inner).Services;

        Assert.IsType<IccColorProfileReader>(services.ColorProfileReader);

        // Linking the image package is not composing its filters. Each of them is
        // a decoder with a decision of its own, and none of those was taken here.
        Assert.False(services.SupportsFilter(PdfFilterNames.Dct));
        Assert.False(services.SupportsFilter(PdfFilterNames.Jpx));
        Assert.False(services.SupportsFilter(PdfFilterNames.Jbig2));
        Assert.False(services.SupportsFilter(PdfFilterNames.CcittFax));
        Assert.Null(services.FontProgramReader);
    }

    [Fact]
    public void Only_The_Composition_Root_Names_The_Pdf_Codec_And_Only_To_Read()
    {
        // Every `new PdfDocumentCodec(` is the argument of a ReadOnlyCodec, and
        // the composition root is the one file that says either.
        var composed = new Regex(@"new\s+PdfDocumentCodec\s*\(", RegexOptions.CultureInvariant);
        var readOnly = new Regex(@"new\s+ReadOnlyCodec\s*\(\s*new\s+PdfDocumentCodec\s*\(", RegexOptions.CultureInvariant);

        string[] naming = [.. SourceFiles()
            .Where(path => composed.IsMatch(File.ReadAllText(path)) ||
                File.ReadAllText(path).Contains("using Broiler.Documents.Pdf", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(ComponentRoot, path).Replace('\\', '/'))];

        Assert.Equal(["src/Broiler.Documents.Cli/Composition/CodecComposition.cs"], naming);

        string root = File.ReadAllText(Path.Combine(ComponentRoot, naming[0]));
        Assert.Equal(composed.Matches(root).Count, readOnly.Matches(root).Count);
    }

    [Fact]
    public void The_Composed_Pdf_Codec_Reads_And_Does_Not_Write()
    {
        DocumentCodec pdf = Assert.IsType<ReadOnlyCodec>(CodecComposition.CreateCatalog().FindByName("PDF"));

        Assert.True(pdf.CanRead);
        Assert.False(pdf.CanWrite);
        Assert.Throws<NotSupportedException>(() => pdf.Write(RichTextDocument.FromPlainText("text"), Stream.Null));
    }

    [Fact]
    public void The_Cli_Adds_No_Third_Party_Runtime_Dependency()
    {
        XDocument project = XDocument.Load(CliProjectPath);
        Assert.Equal(["Broiler.Graphics", "Broiler.Media.Image.Managed"],
            RepositoryFiles.PackageReferences(project));
    }

    [Fact]
    public void The_Cli_Stays_Unpacked_So_The_Published_Package_Set_Is_Unchanged()
    {
        // Turning this on is a deliberate release decision, not a side effect of
        // adding a command: a `v*` tag publishes whatever `dotnet pack` produced.
        string project = File.ReadAllText(CliProjectPath);

        Assert.Contains("<IsPackable>false</IsPackable>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_Exit_Code_Names_A_Distinct_Outcome()
    {
        // The codes are a published contract; two of them meaning the same thing
        // would make a harness unable to tell two outcomes apart.
        int[] codes =
        [
            ExitCode.Ok,
            ExitCode.Usage,
            ExitCode.Input,
            ExitCode.Read,
            ExitCode.Write,
            ExitCode.Different,
            ExitCode.Diagnostics,
            ExitCode.Internal,
        ];

        Assert.Equal(codes.Length, codes.Distinct().Count());
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory
            .EnumerateFiles(
                Path.Combine(ComponentRoot, "src", "Broiler.Documents.Cli"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !RepositoryFiles.IsBuildOutput(path));
    [GeneratedRegex(@"<(?:Project|Package)Reference\s+Include=""(?<path>[^""]+)""")]
    private static partial Regex MyRegex();
}
