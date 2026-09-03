using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Broiler.Documents.Tests;

/// <summary>
/// Guards the claims the RTF, DOCX, HTML and Markdown rights registers make about
/// their codecs.
/// </summary>
/// <remarks>
/// <para>
/// One suite rather than four. ADR 0013 gives each format its own register and
/// its own claim gate, and it separates the <em>registers</em> so a pending row
/// in one cannot silence another format's claims. It does not require four copies
/// of the same five assertions: these are parameterised over the formats, each
/// reads only its own register and its own codec, and a failure names the format
/// it belongs to. ODT's equivalent lives in its own test project because that is
/// where it was written; folding it in here would be tidier and is not worth
/// touching a passing guard for.
/// </para>
/// <para>
/// What these check is that each register's <em>inspection</em> rows stay true.
/// The rows about other parties' rights cannot be tested by anything, which is
/// exactly why they are the ones that need a person to decide them.
/// </para>
/// <para>
/// <see cref="PdfGuardRoots"/> is named for the guards it was written for and is
/// not PDF-specific; ADR 0013 made this concern component-wide after that name
/// was chosen.
/// </para>
/// </remarks>
public sealed class FormatClaimGuardTests
{
    /// <summary>Each format: its codec project, its register, and the documents it reads.</summary>
    public static TheoryData<string, string, string> Formats => new()
    {
        { "Rtf", "docs/rtf-ip-licensing-register.md", ".rtf" },
        { "Docx", "docs/docx-ip-licensing-register.md", ".docx;.dotx" },
        { "Html", "docs/html-ip-licensing-register.md", ".html;.htm" },
        { "Markdown", "docs/markdown-ip-licensing-register.md", string.Empty },
    };

    /// <summary>
    /// Wordings every register in this component declines, whatever the format.
    /// </summary>
    /// <remarks>
    /// Deliberately not including vendor names. DOCX-IP-006 forbids naming
    /// Microsoft or Word in a <em>label</em>, and the DOCX package description
    /// legitimately says "WordprocessingML" — the technical name of the markup.
    /// A guard that could not tell those apart would fail on a true statement.
    /// </remarks>
    private static readonly string[] ProhibitedClaims =
    [
        "patent-free", "patent free", "royalty-free", "royalty free",
        "certified", "endorsed", "fully conforming", "compliant", "conformant",
    ];

    [Theory(Timeout = 600000)]
    [MemberData(nameof(Formats))]
    public void No_Data_File_Sits_Beside_A_Codec(string format, string register, string extensions)
    {
        _ = register;
        _ = extensions;

        // Every register's provenance row states this, and a committed table or
        // fixture would be the shape of the thing it rules out.
        string root = PdfGuardRoots.Component;
        string[] dataFiles = Directory
            .EnumerateFiles(Path.Combine(root, "src", "Broiler.Documents." + format), "*", SearchOption.AllDirectories)
            .Where(path => !PdfGuardRoots.IsBuildOutput(path))
            .Where(path => Path.GetExtension(path) is not (".cs" or ".csproj"))
            .Select(path => Path.GetRelativePath(root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(dataFiles);
    }

    [Theory(Timeout = 600000)]
    [MemberData(nameof(Formats))]
    public void No_Codec_Takes_A_Package_Reference(string format, string register, string extensions)
    {
        _ = register;
        _ = extensions;

        // "There is no third-party toolkit here to account for" rests on this, and
        // it is the cheapest of all these findings to verify.
        XDocument project = XDocument.Load(Path.Combine(
            PdfGuardRoots.Component,
            "src",
            "Broiler.Documents." + format,
            "Broiler.Documents." + format + ".csproj"));

        Assert.Empty(project.Descendants("PackageReference"));
    }

    [Theory(Timeout = 600000)]
    [MemberData(nameof(Formats))]
    public void The_Shipped_Package_Description_Makes_No_Claim_A_Register_Declines(
        string format, string register, string extensions)
    {
        _ = register;
        _ = extensions;

        // The description field is the one string in a project file that reaches a
        // package feed. The PDF package shipped "patent-free filter set" until
        // IP-018 was applied, which is why this is checked rather than trusted.
        string project = File.ReadAllText(Path.Combine(
            PdfGuardRoots.Component,
            "src",
            "Broiler.Documents." + format,
            "Broiler.Documents." + format + ".csproj"));

        string description = Regex.Match(
            project,
            @"<Description>(.*?)</Description>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant).Groups[1].Value;

        Assert.NotEqual(string.Empty, description);

        string[] offending = ProhibitedClaims
            .Where(claim => description.Contains(claim, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(offending);
    }

    [Theory(Timeout = 600000)]
    [MemberData(nameof(Formats))]
    public void Every_Format_Has_A_Register_That_Bounds_Its_Wording(
        string format, string register, string extensions)
    {
        _ = format;
        _ = extensions;

        // A register that goes missing takes the claims boundary with it, and the
        // label set is the part an application actually reads.
        string text = File.ReadAllText(Path.Combine(PdfGuardRoots.Component, register));

        Assert.Contains("## Approved labels", text, StringComparison.Ordinal);
        Assert.Contains("NO LAWYER HAS REVIEWED ANY OF THIS", text, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void No_Document_Of_A_Supported_Format_Is_Committed()
    {
        // Possession is not permission to redistribute, and every register says so
        // per artifact. Markdown is excluded on purpose: this repository's own
        // documentation is Markdown, so the extension cannot distinguish a vendored
        // fixture from a file that is meant to be here. MD-IP-004 carries that one
        // by inspection instead, with the CommonMark test suite specifically in
        // view as the thing most likely to be copied in.
        string root = PdfGuardRoots.Component;
        string[] extensions = [".rtf", ".docx", ".dotx", ".html", ".htm", ".odt", ".ott"];

        string[] documents = Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !PdfGuardRoots.IsBuildOutput(path))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(documents);
    }
}
