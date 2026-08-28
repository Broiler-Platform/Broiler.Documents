using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Broiler.Documents.Cli.Infrastructure;

/// <summary>
/// One option a command accepts. An option with no <see cref="ValueName"/> is a
/// flag: it consumes no following token, so <c>--json --out x.png</c> parses the
/// way a reader expects rather than swallowing <c>--out</c>.
/// </summary>
public sealed class OptionSpec
{
    public OptionSpec(
        string name,
        string? valueName,
        string description,
        bool repeatable = false,
        string? defaultValue = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ValueName = valueName;
        Description = description ?? string.Empty;
        Repeatable = repeatable;
        DefaultValue = defaultValue;
    }

    public string Name { get; }

    /// <summary>The placeholder shown in help, or null when this is a flag.</summary>
    public string? ValueName { get; }

    public string Description { get; }

    /// <summary>True when the option may be given more than once and every value is kept.</summary>
    public bool Repeatable { get; }

    public string? DefaultValue { get; }

    public bool IsFlag => ValueName is null;

    public static OptionSpec Flag(string name, string description) =>
        new(name, null, description);

    public static OptionSpec Value(string name, string valueName, string description, string? defaultValue = null) =>
        new(name, valueName, description, repeatable: false, defaultValue);

    public static OptionSpec Many(string name, string valueName, string description) =>
        new(name, valueName, description, repeatable: true);
}

/// <summary>
/// The declared shape of one command: what it is for, how it is spelled, and
/// every option it accepts. The parser validates against this rather than
/// ignoring what it does not recognize, so a mistyped option in a test harness
/// fails loudly instead of silently taking a default.
/// </summary>
public sealed class CommandSpec
{
    public CommandSpec(
        string name,
        string summary,
        string usage,
        IEnumerable<OptionSpec> options,
        IEnumerable<string>? examples = null,
        string? remarks = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Summary = summary ?? string.Empty;
        Usage = usage ?? string.Empty;
        Options = new ReadOnlyCollection<OptionSpec>(options?.ToArray() ?? Array.Empty<OptionSpec>());
        Examples = new ReadOnlyCollection<string>(examples?.ToArray() ?? Array.Empty<string>());
        Remarks = remarks;
    }

    public string Name { get; }

    public string Summary { get; }

    public string Usage { get; }

    public IReadOnlyList<OptionSpec> Options { get; }

    public IReadOnlyList<string> Examples { get; }

    public string? Remarks { get; }

    /// <summary>
    /// Options every command accepts. They are appended to each command's own
    /// set rather than parsed separately, so <c>--json</c> is legal wherever a
    /// command can produce output and illegal nowhere.
    /// </summary>
    public static IReadOnlyList<OptionSpec> Global { get; } = new ReadOnlyCollection<OptionSpec>(new[]
    {
        OptionSpec.Flag("json", "Emit the result as a single JSON object on stdout."),
        OptionSpec.Flag("quiet", "Suppress human-readable progress; errors still reach stderr."),
        OptionSpec.Flag("verbose", "Include per-diagnostic and per-item detail."),
        OptionSpec.Flag("help", "Show this command's help and exit 0."),
    });

    public IEnumerable<OptionSpec> AllOptions => Options.Concat(Global);
}
