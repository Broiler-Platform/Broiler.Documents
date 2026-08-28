using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Broiler.Documents.Cli.Infrastructure;

/// <summary>Renders the tool's help.</summary>
public static class HelpText
{
    /// <summary>The command name as a user types it.</summary>
    public const string ToolName = "broilerdoc";

    public static void WriteOverview(TextWriter writer, CommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(registry);

        writer.WriteLine("Broiler.Documents command line: create, edit, convert, render, and compare");
        writer.WriteLine("rich-text documents through the Broiler.Documents codecs.");
        writer.WriteLine();
        writer.WriteLine("usage: " + ToolName + " <command> [arguments] [options]");
        writer.WriteLine();
        writer.WriteLine("commands");

        int width = registry.Commands.Max(entry => entry.Spec.Name.Length);
        foreach (CommandEntry entry in registry.Commands)
            writer.WriteLine("  " + entry.Spec.Name.PadRight(width) + "  " + entry.Spec.Summary);

        writer.WriteLine();
        writer.WriteLine("options common to every command");
        WriteOptions(writer, CommandSpec.Global);

        writer.WriteLine();
        writer.WriteLine("exit codes");
        writer.WriteLine("  0   success, and any verdict reached was positive");
        writer.WriteLine("  1   usage error");
        writer.WriteLine("  2   an input could not be read or an output could not be written");
        writer.WriteLine("  3   a document was rejected by its codec");
        writer.WriteLine("  4   a document could not be written or rendered");
        writer.WriteLine("  5   a comparison found a difference beyond its tolerance");
        writer.WriteLine("  6   diagnostics reached the --fail-on severity");
        writer.WriteLine("  70  an unexpected error; always a defect in this tool");
        writer.WriteLine();
        writer.WriteLine("Run '" + ToolName + " <command> --help' for a command's own options.");
    }

    public static void WriteCommand(TextWriter writer, CommandSpec spec)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(spec);

        writer.WriteLine(spec.Summary);
        writer.WriteLine();
        writer.WriteLine("usage: " + ToolName + " " + spec.Usage);

        if (!string.IsNullOrEmpty(spec.Remarks))
        {
            writer.WriteLine();
            foreach (string line in spec.Remarks.Split('\n'))
                writer.WriteLine(line);
        }

        if (spec.Options.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("options");
            WriteOptions(writer, spec.Options);
        }

        writer.WriteLine();
        writer.WriteLine("common options");
        WriteOptions(writer, CommandSpec.Global);

        if (spec.Examples.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("examples");
            foreach (string example in spec.Examples)
                writer.WriteLine("  " + ToolName + " " + example);
        }
    }

    private static void WriteOptions(TextWriter writer, IReadOnlyList<OptionSpec> options)
    {
        string[] left = options.Select(Spelling).ToArray();
        int width = left.Length == 0 ? 0 : left.Max(text => text.Length);

        for (int i = 0; i < options.Count; i++)
        {
            string description = options[i].Description;
            if (options[i].DefaultValue is string fallback)
                description += " (default " + fallback + ")";
            if (options[i].Repeatable)
                description += " Repeatable.";

            writer.WriteLine("  " + left[i].PadRight(width) + "  " + description);
        }
    }

    private static string Spelling(OptionSpec option) =>
        option.IsFlag ? "--" + option.Name : "--" + option.Name + " <" + option.ValueName + ">";
}
