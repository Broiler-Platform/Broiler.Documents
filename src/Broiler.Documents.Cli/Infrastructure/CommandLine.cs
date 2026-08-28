using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Broiler.Documents.Cli.Infrastructure;

/// <summary>Raised when the command line itself is wrong. Always exit code <see cref="ExitCode.Usage"/>.</summary>
public sealed class UsageException : Exception
{
    public UsageException(string message, string? commandName = null)
        : base(message)
    {
        CommandName = commandName;
    }

    /// <summary>The command whose help to show alongside the message, when one was identified.</summary>
    public string? CommandName { get; }
}

/// <summary>
/// The parsed command line for one command: its positional arguments and the
/// options it was given, validated against a <see cref="CommandSpec"/>.
/// </summary>
/// <remarks>
/// <para>
/// Two spellings are accepted for every valued option, <c>--name value</c> and
/// <c>--name=value</c>, and the second is the one to reach for when a value can
/// itself start with a dash. A bare <c>--</c> ends option parsing, so a file
/// literally named <c>--out</c> is still addressable.
/// </para>
/// <para>
/// Unknown options are an error rather than a shrug. In an automated harness a
/// silently ignored <c>--tolerence</c> does not fail; it quietly compares at the
/// default tolerance and reports a pass nobody earned.
/// </para>
/// </remarks>
public sealed class CommandLine
{
    private readonly Dictionary<string, List<string>> _options;
    private readonly List<string> _positionals;

    private CommandLine(
        CommandSpec spec,
        Dictionary<string, List<string>> options,
        List<string> positionals)
    {
        Spec = spec;
        _options = options;
        _positionals = positionals;
    }

    public CommandSpec Spec { get; }

    public IReadOnlyList<string> Positionals => _positionals;

    public bool Json => Has("json");

    public bool Quiet => Has("quiet");

    public bool Verbose => Has("verbose");

    public bool Help => Has("help");

    public static CommandLine Parse(CommandSpec spec, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(arguments);

        Dictionary<string, OptionSpec> known = spec.AllOptions.ToDictionary(
            option => option.Name,
            StringComparer.OrdinalIgnoreCase);

        var options = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var positionals = new List<string>();
        bool literalsOnly = false;

        for (int i = 0; i < arguments.Count; i++)
        {
            string argument = arguments[i];

            if (literalsOnly || !argument.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(argument);
                continue;
            }

            if (argument.Length == 2)
            {
                literalsOnly = true;
                continue;
            }

            string name = argument[2..];
            string? inlineValue = null;
            int separator = name.IndexOf('=');
            if (separator >= 0)
            {
                inlineValue = name[(separator + 1)..];
                name = name[..separator];
            }

            if (!known.TryGetValue(name, out OptionSpec? option))
                throw new UsageException($"Unknown option '--{name}'.", spec.Name);

            if (option.IsFlag)
            {
                if (inlineValue is not null && !IsTruthy(inlineValue))
                    throw new UsageException($"'--{option.Name}' is a flag and takes no value.", spec.Name);
                Add(options, option, "true", spec.Name);
                continue;
            }

            if (inlineValue is not null)
            {
                Add(options, option, inlineValue, spec.Name);
                continue;
            }

            if (i + 1 >= arguments.Count)
                throw new UsageException($"'--{option.Name}' needs a value ({option.ValueName}).", spec.Name);

            Add(options, option, arguments[++i], spec.Name);
        }

        return new CommandLine(spec, options, positionals);
    }

    public bool Has(string name) => _options.ContainsKey(name);

    /// <summary>The option's value, or <paramref name="fallback"/> when it was not given.</summary>
    public string? Get(string name, string? fallback = null) =>
        _options.TryGetValue(name, out List<string>? values) ? values[^1] : fallback;

    /// <summary>The option's value, or the usage error that says it was required.</summary>
    public string Require(string name)
    {
        string? value = Get(name);
        if (string.IsNullOrEmpty(value))
            throw new UsageException($"'--{name}' is required.", Spec.Name);
        return value;
    }

    /// <summary>Every value given for a repeatable option, in the order given.</summary>
    public IReadOnlyList<string> GetAll(string name) =>
        _options.TryGetValue(name, out List<string>? values) ? values : Array.Empty<string>();

    public int GetInt32(string name, int fallback)
    {
        string? value = Get(name);
        if (string.IsNullOrEmpty(value))
            return fallback;
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            throw new UsageException($"'--{name}' expects a whole number, not '{value}'.", Spec.Name);
        return parsed;
    }

    public double GetDouble(string name, double fallback)
    {
        string? value = Get(name);
        if (string.IsNullOrEmpty(value))
            return fallback;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ||
            double.IsNaN(parsed) || double.IsInfinity(parsed))
            throw new UsageException($"'--{name}' expects a finite number, not '{value}'.", Spec.Name);
        return parsed;
    }

    /// <summary>The one positional this command takes, or the usage error naming it.</summary>
    public string RequirePositional(int index, string name)
    {
        if (index >= _positionals.Count)
            throw new UsageException($"Missing <{name}>.", Spec.Name);
        return _positionals[index];
    }

    /// <summary>Fails when more positionals arrived than the command knows what to do with.</summary>
    public void RequireNoExtraPositionals(int expected)
    {
        if (_positionals.Count > expected)
        {
            string extra = string.Join(", ", _positionals.Skip(expected).Select(value => $"'{value}'"));
            throw new UsageException($"Unexpected argument(s): {extra}.", Spec.Name);
        }
    }

    private static void Add(
        Dictionary<string, List<string>> options,
        OptionSpec option,
        string value,
        string commandName)
    {
        if (!options.TryGetValue(option.Name, out List<string>? values))
        {
            values = new List<string>(1);
            options[option.Name] = values;
        }
        else if (!option.Repeatable && !option.IsFlag)
        {
            throw new UsageException(
                $"'--{option.Name}' was given more than once and is not repeatable.", commandName);
        }

        values.Add(value);
    }

    private static bool IsTruthy(string value) =>
        value is "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
