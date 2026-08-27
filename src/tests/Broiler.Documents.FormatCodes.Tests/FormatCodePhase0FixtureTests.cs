using System.Text;
using System.Text.Json;

namespace Broiler.Documents.FormatCodes.Tests;

public sealed class FormatCodePhase0FixtureTests
{
    private static readonly InlineStyle[] DenseStyles =
    [
        new InlineStyle { Bold = true },
        new InlineStyle { Italic = true },
        new InlineStyle { Underline = true },
        new InlineStyle { Strikethrough = true },
        new InlineStyle { FontSize = 15.5f },
        new InlineStyle { FontFamily = "Broiler Sans" },
        new InlineStyle { LinkHref = "https://example.invalid/format-code" },
    ];

    public static IEnumerable<object[]> FixtureData()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures.json");
        List<FixtureSpec> fixtures = JsonSerializer.Deserialize<List<FixtureSpec>>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Fixture manifest is empty.");
        return fixtures.Select(fixture => new object[] { fixture });
    }

    [Theory]
    [MemberData(nameof(FixtureData))]
    public void Phase0_Fixture_Projects_Deterministically_At_Full_Scale(FixtureSpec fixture)
    {
        RichTextDocument document = BuildFixture(fixture);
        var projector = new FormatCodeProjector();

        FormatCodeProjection first = projector.Project(document);
        FormatCodeProjection second = projector.Project(document);

        Assert.Equal(fixture.TargetChars, document.PlainText.Length);
        Assert.Equal(fixture.Paragraphs, document.ParagraphCount);
        Assert.Equal(fixture.ExpectedRuns, document.Paragraphs.Sum(paragraph => paragraph.Runs.Count));
        Assert.Equal(first.Text, second.Text);
        Assert.Equal(first.Text, string.Concat(first.Tokens.Select(token => token.DisplayText)));
        Assert.Equal(ExpectBackground(fixture.Id),
            FormatCodeProjectionPolicy.RecommendBackgroundProjection(document));
    }

    private static bool ExpectBackground(string id) => id is
        "plain-1m" or "high-run-density-100k" or "many-empty-paragraphs";

    private static RichTextDocument BuildFixture(FixtureSpec fixture) => fixture.Generator switch
    {
        "plain" => RichTextDocument.FromPlainText(BuildPatternText(fixture.TargetChars)),
        "high-run-density" => BuildHighRunDensity(fixture),
        "empty-paragraphs" => RichTextDocument.FromParagraphs(
            Enumerable.Repeat(RichTextParagraph.Empty, fixture.Paragraphs)),
        "unicode" => RichTextDocument.FromPlainText(BuildUnicodeText(fixture.TargetChars)),
        _ => throw new InvalidOperationException($"Unknown generator '{fixture.Generator}'."),
    };

    private static RichTextDocument BuildHighRunDensity(FixtureSpec fixture)
    {
        int contentCharacters = fixture.TargetChars - (fixture.Paragraphs - 1);
        int baseLength = contentCharacters / fixture.Paragraphs;
        int remainder = contentCharacters % fixture.Paragraphs;
        var paragraphs = new RichTextParagraph[fixture.Paragraphs];
        for (int i = 0; i < paragraphs.Length; i++)
        {
            paragraphs[i] = RichTextParagraph.Create(
                BuildPatternText(baseLength + (i < remainder ? 1 : 0), i),
                DenseStyles[i % DenseStyles.Length]);
        }

        return RichTextDocument.FromParagraphs(paragraphs);
    }

    private static string BuildPatternText(int length, int phase = 0)
    {
        const string pattern = "The quick brown fox 0123456789. ";
        return string.Create(length, phase, static (span, start) =>
        {
            for (int i = 0; i < span.Length; i++)
                span[i] = pattern[(i + start) % pattern.Length];
        });
    }

    private static string BuildUnicodeText(int length)
    {
        const string pattern = "A\u0308 café Ελληνικά עברית العربية 中文 😀 [x] \\ \t \u0001 ";
        var builder = new StringBuilder(length);
        while (builder.Length + pattern.Length <= length)
            builder.Append(pattern);
        builder.Append('界', length - builder.Length);
        return builder.ToString();
    }

    public sealed record FixtureSpec(
        string Id,
        string Generator,
        int TargetChars,
        int Paragraphs,
        int ExpectedRuns,
        string Purpose);
}
