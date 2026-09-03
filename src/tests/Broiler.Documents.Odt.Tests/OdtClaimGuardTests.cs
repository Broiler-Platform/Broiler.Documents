using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Guards the claims the ODT documents make about the ODT code.
/// </summary>
/// <remarks>
/// <para>
/// These are the ODT half of the discipline ADR 0013 extended beyond PDF. The
/// reasoning is the PDF one: the register's rows about what this repository
/// contains are settled by inspection, and an inspection finding that nobody
/// repeats is a claim about the tree rather than a property of it. A data file
/// added beside the codec, a vendored sample document, a diagnostic added with
/// no document describing it, or a package description that says
/// "royalty-free" would each break a recorded row without breaking a build.
/// </para>
/// <para>
/// So the binding is mechanical. These check that the description and the
/// artifact still match, not that either is correct — and unlike the PDF guards,
/// they are enforcing rows that are <em>pending</em>: nothing about ODT is
/// cleared, which makes the negative claims rule the part that actually has to
/// hold today.
/// </para>
/// </remarks>
public sealed class OdtClaimGuardTests
{
    private const string Register = "docs/odt-ip-licensing-register.md";
    private const string Conformance = "docs/odt-conformance.md";

    /// <summary>The documents a reader consults to learn what the ODT codec does.</summary>
    private static readonly string[] DescribingDocuments = [Conformance, Register];

    [Fact(Timeout = 600000)]
    public void Every_Diagnostic_Code_The_Codec_Declares_Is_Described_Somewhere()
    {
        string root = OdtGuardRoots.Component;
        string description = string.Concat(DescribingDocuments.Select(
            path => File.ReadAllText(Path.Combine(root, path))));

        string[] undocumented = DeclaredCodes(root)
            .Where(code => !description.Contains(code, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        // A code is API: a host branches on it, and the documents are where a
        // host learns it exists. Four were undocumented when this guard was
        // written — odt.image.denied, odt.image.omitted, odt.page.geometry and
        // odt.xml — which is the same drift PdfClaimGuardTests found fourteen of.
        Assert.Empty(undocumented);
    }

    [Fact(Timeout = 600000)]
    public void The_Register_Names_Only_Diagnostic_Codes_That_Exist()
    {
        string root = OdtGuardRoots.Component;
        var declared = DeclaredCodes(root).ToHashSet(StringComparer.Ordinal);

        // Backticked code-shaped tokens in the register are claims that a
        // specific diagnostic exists. A row that survived a rename would send a
        // reviewer looking for something that is not there.
        string register = File.ReadAllText(Path.Combine(root, Register));
        string[] missing = Regex.Matches(register, @"`(odt\.[a-z0-9.\-]+)`", RegexOptions.CultureInvariant)
            .Select(match => match.Groups[1].Value)
            .Where(code => !declared.Contains(code))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missing);
    }

    /// <summary>
    /// Wordings ODT-IP-007 forbids: a legal, conformance, or endorsement status
    /// this project has not claimed and, with every row pending, could not.
    /// </summary>
    private static readonly string[] ProhibitedClaims =
    [
        "patent-free", "patent free", "royalty-free", "royalty free",
        "certified", "endorsed", "ODF compliant", "OpenDocument compliant",
        "ODF conformant", "OpenDocument conformant", "fully conforming",
    ];

    [Fact(Timeout = 600000)]
    public void The_Shipped_Package_Description_Makes_No_Claim_The_Register_Declines()
    {
        string root = OdtGuardRoots.Component;

        // The description field is the one string in a project file that reaches
        // a package feed, so it is the one place a claims rule kept only in prose
        // actually costs something. The PDF register learned this the hard way:
        // its package had shipped "patent-free filter set".
        string project = File.ReadAllText(Path.Combine(
            root, "src", "Broiler.Documents.Odt", "Broiler.Documents.Odt.csproj"));

        string description = Regex.Match(project, @"<Description>(.*?)</Description>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant).Groups[1].Value;

        Assert.NotEqual(string.Empty, description);

        string[] offending = ProhibitedClaims
            .Where(claim => description.Contains(claim, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(offending);
    }

    [Fact(Timeout = 600000)]
    public void The_Format_Is_Named_As_The_Register_Proposes()
    {
        string root = OdtGuardRoots.Component;
        string codec = File.ReadAllText(Path.Combine(
            root, "src", "Broiler.Documents.Odt", "OdtDocumentCodec.cs"));

        // "ODT" is the proposed format-list label, and the descriptor is where an
        // application reads it from. A vendor name, a version suffix, or the word
        // "conformant" here would reach every host that lists the format.
        Assert.Matches(@"new DocumentFormatDescriptor\(\s*""ODT""", codec);

        string register = File.ReadAllText(Path.Combine(root, Register));
        Assert.Contains("## Approved labels", register, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void No_Data_File_Sits_Beside_The_Codec()
    {
        string root = OdtGuardRoots.Component;

        // ODT-IP-005 finding one, made a property of the tree rather than a claim
        // about it. The codec reads ODF from the specification's structure and
        // carries no table of its own; a committed data file would be the shape
        // of the thing that finding rules out, and would need its own source row.
        string[] dataFiles = Directory
            .EnumerateFiles(Path.Combine(root, "src", "Broiler.Documents.Odt"), "*", SearchOption.AllDirectories)
            .Where(path => !OdtGuardRoots.IsBuildOutput(path))
            .Where(path => Path.GetExtension(path) is not (".cs" or ".csproj"))
            .Select(path => Path.GetRelativePath(root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(dataFiles);
    }

    [Fact(Timeout = 600000)]
    public void The_Codec_Takes_No_Package_Reference()
    {
        string root = OdtGuardRoots.Component;

        // ODT-IP-005 finding two: there is no third-party ODF toolkit here to
        // account for, because there is no package reference at all. The row's
        // provenance position rests on that, so it is asserted rather than
        // remembered. OdtArchitectureTests covers which projects it references;
        // this covers that it references nothing from a feed.
        XDocument project = XDocument.Load(Path.Combine(
            root, "src", "Broiler.Documents.Odt", "Broiler.Documents.Odt.csproj"));

        Assert.Empty(project.Descendants("PackageReference"));
    }

    [Fact(Timeout = 600000)]
    public void No_OpenDocument_File_Is_Committed_Anywhere()
    {
        string root = OdtGuardRoots.Component;

        // ODT-IP-005 finding three and ODT-IP-009's default rejection. Possession
        // is not permission to redistribute, and a committed package would be
        // redistribution. OdtTestPackage.cs is why the suite does not need one.
        string[] extensions = [".odt", ".ott", ".fodt", ".ods", ".odp", ".odg"];

        string[] documents = Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !OdtGuardRoots.IsBuildOutput(path))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(documents);
    }

    [Fact(Timeout = 600000)]
    public void The_Conformance_Document_Defers_To_The_Register_On_Rights()
    {
        string root = OdtGuardRoots.Component;
        string conformance = File.ReadAllText(Path.Combine(root, Conformance));

        // The conformance document used to say the rights controls "are the PDF
        // ones today" and that extending them was roadmap work. They are not, and
        // it is not. It must point at ODT's own register so the rights position is
        // read in one place rather than restated in two that can disagree.
        Assert.Contains("odt-ip-licensing-register.md", conformance, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every diagnostic code the ODT codec declares. Unlike the PDF codec there
    /// is no <c>OdtDiagnosticCodes</c> constant class, so the codes are the
    /// literals the source passes to the diagnostic constructors.
    /// </summary>
    private static IEnumerable<string> DeclaredCodes(string root) =>
        Directory
            .EnumerateFiles(Path.Combine(root, "src", "Broiler.Documents.Odt"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !OdtGuardRoots.IsBuildOutput(path))
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), @"""(odt\.[a-z0-9.\-]+)""",
                    RegexOptions.CultureInvariant)
                .Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal);
}
