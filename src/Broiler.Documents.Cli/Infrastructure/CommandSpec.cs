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
public sealed class OptionSpec(string name, string? valueName, string description, bool repeatable = false, string? defaultValue = null)
{
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

    /// <summary>The placeholder shown in help, or null when this is a flag.</summary>
    public string? ValueName { get; } = valueName;

    public string Description { get; } = description ?? string.Empty;

    /// <summary>True when the option may be given more than once and every value is kept.</summary>
    public bool Repeatable { get; } = repeatable;

    public string? DefaultValue { get; } = defaultValue;

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
public sealed class CommandSpec(string name, string summary, string usage, IEnumerable<OptionSpec> options, 
    IEnumerable<string>? examples = null, string? remarks = null)
{
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

    public string Summary { get; } = summary ?? string.Empty;

    public string Usage { get; } = usage ?? string.Empty;

    public IReadOnlyList<OptionSpec> Options { get; } = new ReadOnlyCollection<OptionSpec>(options?.ToArray() ?? []);

    public IReadOnlyList<string> Examples { get; } = new ReadOnlyCollection<string>(examples?.ToArray() ?? []);

    public string? Remarks { get; } = remarks;

    /// <summary>
    /// Options every command accepts. They are appended to each command's own
    /// set rather than parsed separately, so <c>--json</c> is legal wherever a
    /// command can produce output and illegal nowhere.
    /// </summary>
    public static IReadOnlyList<OptionSpec> Global { get; } = new ReadOnlyCollection<OptionSpec>(
    [
        OptionSpec.Flag("json", "Emit the result as a single JSON object on stdout."),
        OptionSpec.Flag("quiet", "Suppress human-readable progress; errors still reach stderr."),
        OptionSpec.Flag("verbose", "Include per-diagnostic and per-item detail."),
        OptionSpec.Flag("help", "Show this command's help and exit 0."),
    ]);

    public IEnumerable<OptionSpec> AllOptions => Options.Concat(Global);
}
