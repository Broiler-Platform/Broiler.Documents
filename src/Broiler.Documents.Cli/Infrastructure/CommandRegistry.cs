using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Broiler.Documents.Cli.Infrastructure;

/// <summary>One command: what it declares, and what runs it.</summary>
public sealed class CommandEntry
{
    public CommandEntry(CommandSpec spec, Func<CommandContext, int> handler)
    {
        Spec = spec ?? throw new ArgumentNullException(nameof(spec));
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public CommandSpec Spec { get; }

    public Func<CommandContext, int> Handler { get; }
}

/// <summary>
/// Every command this tool has, in the order help lists them.
/// </summary>
/// <remarks>
/// The order is the order someone meets them in: identify a file, look inside
/// it, make one, change it, convert it, draw it, then compare two of them. Help
/// output that is alphabetical would put <c>compare</c> first and <c>version</c>
/// in the middle, which teaches nothing.
/// </remarks>
public sealed class CommandRegistry
{
    private readonly ReadOnlyCollection<CommandEntry> _commands;

    public CommandRegistry(IEnumerable<CommandEntry> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = new ReadOnlyCollection<CommandEntry>(commands.ToArray());
    }

    public IReadOnlyList<CommandEntry> Commands => _commands;

    public CommandEntry? Find(string name) =>
        _commands.FirstOrDefault(entry =>
            string.Equals(entry.Spec.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The command whose name is closest to <paramref name="name"/>, for a "did you mean".</summary>
    public string? Suggest(string name)
    {
        string lowered = name.ToLowerInvariant();
        return _commands
            .Select(entry => entry.Spec.Name)
            .Where(candidate =>
                candidate.StartsWith(lowered, StringComparison.Ordinal) ||
                lowered.StartsWith(candidate, StringComparison.Ordinal) ||
                Distance(candidate, lowered) <= 2)
            .OrderBy(candidate => Distance(candidate, lowered))
            .FirstOrDefault();
    }

    private static int Distance(string left, string right)
    {
        int[] previous = new int[right.Length + 1];
        int[] current = new int[right.Length + 1];

        for (int j = 0; j <= right.Length; j++)
            previous[j] = j;

        for (int i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= right.Length; j++)
            {
                int cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
