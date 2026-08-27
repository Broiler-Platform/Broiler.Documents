using System.Reflection;
using System.Xml.Linq;
using Broiler.Graphics;

namespace Broiler.Documents.FormatCodes.Tests;

public sealed class FormatCodeProjectorRobustnessTests
{
    private readonly FormatCodeProjector _projector = new();

    [Fact(Timeout = 600000)]
    public void Pending_Formatting_Is_A_Separate_Noncanonical_Overlay()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("x");
        var options = new FormatCodeProjectionOptions
        {
            PendingStyle = new FormatCodePendingStyle(
                document.Start,
                new InlineStyle { Bold = true, Italic = true }),
        };

        FormatCodeProjection projection = _projector.Project(document, options);

        Assert.Equal("x", projection.Text);
        Assert.Equal(["[Pending Bold ON]", "[Pending Italic ON]"],
            projection.PendingTokens.Select(token => token.DisplayText));
        Assert.All(projection.PendingTokens, token =>
        {
            Assert.Equal(FormatCodeTokenKind.PendingCode, token.Kind);
            Assert.Equal(0, token.ProjectedLength);
        });
    }

    [Fact(Timeout = 600000)]
    public void Empty_Link_Is_Effectively_Default()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("x", new InlineStyle { LinkHref = string.Empty })]);

        Assert.Equal("x", _projector.Project(document).Text);
    }

    [Fact(Timeout = 600000)]
    public void Unknown_And_OutOfDomain_Paragraph_State_Is_Preserved_With_Diagnostics()
    {
        ParagraphStyle style = ParagraphStyle.Default with
        {
            Alignment = (TextAlignment)99,
            ListKind = (ListKind)88,
            IndentLevel = -2,
            LineSpacing = float.NaN,
        };
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("x", InlineStyle.Default, style)]);

        FormatCodeProjection projection = _projector.Project(document);

        Assert.StartsWith("[Align UNKNOWN 99][List UNKNOWN 88][Indent -2][Line Spacing NAN]", projection.Text);
        Assert.Contains(projection.Diagnostics, diagnostic => diagnostic.Code == "FC1001");
        Assert.Contains(projection.Diagnostics, diagnostic => diagnostic.Code == "FC1002");
        Assert.Contains(projection.Diagnostics, diagnostic => diagnostic.Code == "FC1003");
        Assert.Contains(projection.Diagnostics, diagnostic => diagnostic.Code == "FC1004");
    }

    [Fact(Timeout = 600000)]
    public void Unpaired_Surrogate_Is_Escaped_With_A_Diagnostic()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("a\uD800b");

        FormatCodeProjection projection = _projector.Project(document);

        Assert.Equal("a\\u{D800}b", projection.Text);
        Assert.Contains(projection.Diagnostics, diagnostic => diagnostic.Code == "FC1005");
    }

    [Fact(Timeout = 600000)]
    public void Configured_Resource_Limits_Fail_Before_Expansion()
    {
        Assert.Throws<FormatCodeProjectionLimitException>(() => _projector.Project(
            RichTextDocument.FromPlainText("abcd"),
            new FormatCodeProjectionOptions { MaxOutputCharacters = 3 }));

        Assert.Throws<FormatCodeProjectionLimitException>(() => _projector.Project(
            RichTextDocument.FromParagraphs(
                [RichTextParagraph.Create("x", new InlineStyle { Bold = true })]),
            new FormatCodeProjectionOptions { MaxTokens = 1 }));

        Assert.Throws<FormatCodeProjectionLimitException>(() => _projector.Project(
            RichTextDocument.FromParagraphs(
                [RichTextParagraph.Create("x", new InlineStyle { FontFamily = "long" })]),
            new FormatCodeProjectionOptions { MaxQuotedValueCharacters = 3 }));
    }

    [Fact(Timeout = 600000)]
    public void Cancellation_Is_Observed()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => _projector.Project(
            RichTextDocument.FromPlainText(new string('x', 1000)),
            cancellationToken: cancellation.Token));
    }

    [Fact(Timeout = 600000)]
    public void Equivalent_Normalized_Documents_Have_Identical_Output()
    {
        InlineStyle style = new() { Bold = true, Foreground = BColor.Blue };
        RichTextDocument oneRun = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("ab", style)]);
        RichTextDocument mergedRuns = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("a", style).Append(RichTextParagraph.Create("b", style))]);

        Assert.Equal(_projector.Project(oneRun).Text, _projector.Project(mergedRuns).Text);
        Assert.Single(mergedRuns.Paragraphs[0].Runs);
    }

    [Fact(Timeout = 600000)]
    public void Deterministic_Randomized_Styles_Project_Identically_When_Rebuilt()
    {
        var random = new Random(0xB01);
        for (int iteration = 0; iteration < 200; iteration++)
        {
            InlineStyle first = RandomStyle(random);
            InlineStyle second = RandomStyle(random);
            RichTextParagraph paragraphOne = RichTextParagraph.Create("left", first)
                .Append(RichTextParagraph.Create("right", second));
            RichTextParagraph paragraphTwo = RichTextParagraph.Create("left", first)
                .Append(RichTextParagraph.Create("right", second));

            FormatCodeProjection projectionOne = _projector.Project(
                RichTextDocument.FromParagraphs([paragraphOne]));
            FormatCodeProjection projectionTwo = _projector.Project(
                RichTextDocument.FromParagraphs([paragraphTwo]));

            Assert.Equal(projectionOne.Text, projectionTwo.Text);
            Assert.Equal(
                projectionOne.Tokens.Select(token => token.Kind),
                projectionTwo.Tokens.Select(token => token.Kind));
        }
    }

    [Fact(Timeout = 600000)]
    public void Project_References_Only_The_Model_Project_And_No_Platform_Package()
    {
        XDocument project = XDocument.Load(ProjectPath());
        string[] references = project.Descendants("ProjectReference")
            .Select(reference => (string?)reference.Attribute("Include"))
            .Where(reference => reference is not null)
            .Cast<string>()
            .Select(reference => reference.Replace('\\', '/'))
            .ToArray();

        Assert.Equal(["../Broiler.Documents.Model/Broiler.Documents.Model.csproj"], references);
        Assert.Empty(project.Descendants("PackageReference"));

        string[] broilerAssemblies = typeof(FormatCodeProjector).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .Where(name => name.StartsWith("Broiler.", StringComparison.Ordinal))
            .ToArray();
        Assert.DoesNotContain(broilerAssemblies, name =>
            name.Contains("UI", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Dom", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Input", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Windows", StringComparison.OrdinalIgnoreCase));
    }

    private static InlineStyle RandomStyle(Random random) => new()
    {
        Bold = random.Next(2) == 1,
        Italic = random.Next(2) == 1,
        Underline = random.Next(2) == 1,
        Strikethrough = random.Next(2) == 1,
        FontFamily = random.Next(3) == 0 ? null : $"Font {random.Next(4)}",
        FontSize = random.Next(3) == 0 ? null : random.Next(8, 32) + 0.5f,
        Foreground = random.Next(3) == 0 ? BColor.Empty : new BColor(
            (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)),
        Background = random.Next(3) == 0 ? BColor.Empty : new BColor(
            (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)),
        LinkHref = random.Next(3) == 0 ? null : $"https://example.test/{random.Next(8)}",
    };

    private static string ProjectPath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName,
                "Broiler.Documents",
                "Broiler.Documents.FormatCodes",
                "Broiler.Documents.FormatCodes.csproj");
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Formatting Codes project not found.");
    }
}
