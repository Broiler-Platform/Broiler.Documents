using System.Text.RegularExpressions;

namespace Broiler.Documents.Tests;

/// <summary>
/// Guards the claims the PDF documents make about the PDF code.
/// </summary>
/// <remarks>
/// <para>
/// These exist because of what IP-001 actually grants. Adobe's public patent
/// licence covers a <em>Compliant Implementation</em> of ISO 32000-1, and the
/// register's answer to "which implementation is that?" is the feature matrix and
/// the construct inventory. Those are prose, the codec is code, and prose drifts.
/// A diagnostic added without a matching row, or a row naming a code that was
/// renamed away, does not break a build — it quietly makes the documents describe
/// something other than what ships, which is the one thing the licence condition
/// asks not to happen.
/// </para>
/// <para>
/// So the binding is mechanical here rather than remembered. These are governance
/// guards in the same spirit as <see cref="PdfPhaseZeroGuardTests"/>: they check
/// that the description and the artifact still match, not that either is correct.
/// </para>
/// </remarks>
public sealed class PdfClaimGuardTests
{
    /// <summary>The documents a reviewer reads to learn what the codec does.</summary>
    private static readonly string[] DescribingDocuments =
    [
        "docs/pdf-feature-matrix.md",
        "docs/pdf-construct-inventory.md",
        "docs/pdf-extension-points.md",
        "docs/pdf-support-roadmap.md",
        "docs/pdf-ip-licensing-register.md",
    ];

    [Fact(Timeout = 600000)]
    public void Every_Diagnostic_Code_The_Codec_Declares_Is_Described_Somewhere()
    {
        string root = PdfGuardRoots.Component;
        string description = string.Concat(DescribingDocuments.Select(
            path => File.ReadAllText(Path.Combine(root, path))));

        string[] undocumented = DeclaredCodes(root)
            .Where(code => !description.Contains(code, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        // A code is API: a host branches on it, and the documents are where a host
        // learns it exists. One that appears in neither is a capability statement
        // nobody outside this repository can act on.
        Assert.Empty(undocumented);
    }

    [Fact(Timeout = 600000)]
    public void No_Feature_Matrix_Entry_Claims_Support_While_A_Register_Row_Is_Pending()
    {
        string root = PdfGuardRoots.Component;
        string matrix = File.ReadAllText(Path.Combine(root, "docs/pdf-feature-matrix.md"));
        string register = File.ReadAllText(Path.Combine(root, "docs/pdf-ip-licensing-register.md"));

        bool anythingPending =
            register.Contains("**Pending", StringComparison.Ordinal) ||
            register.Contains("**Blocked", StringComparison.Ordinal);

        // The matrix's own headline rule, made mechanical. Status words appear in
        // table cells, so a cell holding exactly "Supported" is the claim; the
        // word inside a sentence explaining the rule is not.
        bool claimsSupport = Regex.IsMatch(matrix, @"\|\s*Supported\s*\|", RegexOptions.CultureInvariant);

        Assert.False(
            anythingPending && claimsSupport,
            "The feature matrix marks an entry Supported while the IP/licensing register still has a pending or blocked row.");
    }

    [Fact(Timeout = 600000)]
    public void The_Register_Names_Only_Diagnostic_Codes_That_Exist()
    {
        string root = PdfGuardRoots.Component;
        var declared = DeclaredCodes(root).ToHashSet(StringComparer.Ordinal);

        // Backticked code-shaped tokens in the register are claims that a specific
        // diagnostic exists. A row that survives a rename would send a reviewer
        // looking for something that is not there.
        string register = File.ReadAllText(Path.Combine(root, "docs/pdf-ip-licensing-register.md"));
        string[] missing = Regex.Matches(register, @"`(pdf\.[a-z0-9.\-]+|document\.[a-z0-9.\-]+)`", RegexOptions.CultureInvariant)
            .Select(match => match.Groups[1].Value)
            .Where(code => code.Contains('.', StringComparison.Ordinal) && !declared.Contains(code))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missing);
    }

    /// <summary>
    /// Wordings that assert a legal or endorsement status this project has
    /// declined to claim on every row of its own register.
    /// </summary>
    private static readonly string[] ProhibitedClaims =
    [
        "patent-free", "patent free", "royalty-free", "certified", "endorsed", "Acrobat",
    ];

    [Fact(Timeout = 600000)]
    public void No_Shipped_Package_Description_Makes_A_Claim_The_Register_Declines()
    {
        string root = PdfGuardRoots.Component;

        // The description field is the one string in a project file that reaches
        // a package feed, so it is the one place a claims rule kept only in prose
        // actually costs something. It said "patent-free filter set" until IP-018
        // was applied, contradicting every codec row in the register.
        string[] offending = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !PdfGuardRoots.IsBuildOutput(path))
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), @"<Description>(.*?)</Description>",
                    RegexOptions.Singleline | RegexOptions.CultureInvariant)
                .Select(match => (Path: Path.GetRelativePath(root, path), Text: match.Groups[1].Value)))
            .Where(entry => ProhibitedClaims.Any(claim =>
                entry.Text.Contains(claim, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => entry.Path)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offending);
    }

    [Fact(Timeout = 600000)]
    public void The_Format_Is_Named_As_IP_018_Approved()
    {
        string root = PdfGuardRoots.Component;
        string codec = File.ReadAllText(Path.Combine(
            root, "src", "Broiler.Documents.Pdf", "PdfDocumentCodec.cs"));

        // "PDF" is the approved format-list label, and the descriptor is where an
        // application reads it from. A vendor name or a version suffix here would
        // reach every host that lists the format.
        Assert.Contains("new DocumentFormatDescriptor(\"PDF\"", codec, StringComparison.Ordinal);

        string register = File.ReadAllText(Path.Combine(root, "docs/pdf-ip-licensing-register.md"));
        Assert.Contains("## Approved labels", register, StringComparison.Ordinal);
    }

    /// <summary>The PDF source trees whose character data is authored, not transcribed.</summary>
    private static readonly string[] CodecSourceRoots =
    [
        "src/Broiler.Documents.Pdf",
        "src/Broiler.Documents.Pdf.Images",
        "src/Broiler.Documents.Pdf.Fonts",
    ];

    [Fact(Timeout = 600000)]
    public void No_Data_File_Sits_Beside_The_Codec()
    {
        string root = PdfGuardRoots.Component;

        // IP-021's authored-not-copied position, made a property of the tree
        // rather than a claim about it. Every encoding table, glyph name, and
        // metric in this codec is built in code from the character or proportion
        // it denotes; a committed glyph list, encoding table, or metric file
        // would be the shape of the thing that position rules out, and it would
        // need its own source decision before it belonged here.
        string[] dataFiles = CodecSourceRoots
            .Select(relative => Path.Combine(root, relative))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            .Where(path => !PdfGuardRoots.IsBuildOutput(path))
            .Where(path => Path.GetExtension(path) is not (".cs" or ".csproj"))
            .Select(path => Path.GetRelativePath(root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(dataFiles);
    }

    /// <summary>Every diagnostic code constant the PDF codec declares.</summary>
    private static IEnumerable<string> DeclaredCodes(string root)
    {
        string source = File.ReadAllText(Path.Combine(
            root, "src", "Broiler.Documents.Pdf", "PdfDiagnosticCodes.cs"));

        return Regex.Matches(source, @"=\s*""([^""]+)""", RegexOptions.CultureInvariant)
            .Select(match => match.Groups[1].Value);
    }
}
