namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// The parser's contract. Most of these are about failing loudly: an automated
/// caller that mistypes an option must not get a default silently.
/// </summary>
public sealed class CommandLineTests
{
    private static readonly CommandSpec Spec = new(
        "sample",
        "A command for tests.",
        "sample <input> [options]",
        new[]
        {
            OptionSpec.Value("out", "path", "Output path."),
            OptionSpec.Flag("force", "Overwrite."),
            OptionSpec.Many("op", "operation", "An operation."),
        });

    [Fact]
    public void Separate_And_Inline_Values_Parse_The_Same()
    {
        Assert.Equal("a.png", CommandLine.Parse(Spec, new[] { "--out", "a.png" }).Get("out"));
        Assert.Equal("a.png", CommandLine.Parse(Spec, new[] { "--out=a.png" }).Get("out"));
    }

    [Fact]
    public void A_Flag_Does_Not_Swallow_The_Next_Option()
    {
        CommandLine line = CommandLine.Parse(Spec, new[] { "--force", "--out", "a.png" });

        Assert.True(line.Has("force"));
        Assert.Equal("a.png", line.Get("out"));
    }

    [Fact]
    public void A_Repeatable_Option_Keeps_Every_Value_In_Order()
    {
        CommandLine line = CommandLine.Parse(Spec, new[] { "--op", "one", "--op", "two", "--op", "three" });

        Assert.Equal(new[] { "one", "two", "three" }, line.GetAll("op"));
    }

    [Fact]
    public void A_Non_Repeatable_Option_Given_Twice_Is_A_Usage_Error()
    {
        UsageException exception = Assert.Throws<UsageException>(
            () => CommandLine.Parse(Spec, new[] { "--out", "a.png", "--out", "b.png" }));

        Assert.Contains("not repeatable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_Unknown_Option_Is_A_Usage_Error_Rather_Than_Being_Ignored()
    {
        // The whole point: a harness that writes --tolerence must fail, not
        // quietly compare at the default tolerance and report a pass.
        UsageException exception = Assert.Throws<UsageException>(
            () => CommandLine.Parse(Spec, new[] { "--tolerence", "3" }));

        Assert.Contains("--tolerence", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Valued_Option_With_No_Value_Is_A_Usage_Error()
    {
        Assert.Throws<UsageException>(() => CommandLine.Parse(Spec, new[] { "--out" }));
    }

    [Fact]
    public void A_Double_Dash_Ends_Option_Parsing()
    {
        CommandLine line = CommandLine.Parse(Spec, new[] { "--force", "--", "--out" });

        Assert.True(line.Has("force"));
        Assert.Equal(new[] { "--out" }, line.Positionals);
    }

    [Fact]
    public void Extra_Positionals_Are_Reported_With_Their_Values()
    {
        CommandLine line = CommandLine.Parse(Spec, new[] { "one", "two" });

        UsageException exception = Assert.Throws<UsageException>(() => line.RequireNoExtraPositionals(1));
        Assert.Contains("two", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Number_Option_Rejects_Text()
    {
        var spec = new CommandSpec(
            "sample",
            string.Empty,
            string.Empty,
            new[] { OptionSpec.Value("count", "n", "A count.") });

        CommandLine line = CommandLine.Parse(spec, new[] { "--count", "many" });

        Assert.Throws<UsageException>(() => line.GetInt32("count", 0));
    }
}
