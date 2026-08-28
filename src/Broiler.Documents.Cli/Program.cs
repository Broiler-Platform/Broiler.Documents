using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Broiler.Documents.Cli.Commands;
using Broiler.Documents.Cli.Documents;
using Broiler.Documents.Cli.Infrastructure;

namespace Broiler.Documents.Cli;

/// <summary>
/// The <c>broilerdoc</c> entry point.
/// </summary>
/// <remarks>
/// <para>
/// Two rules shape everything here, and both come from the tool being meant for
/// an automated caller as much as for a person.
/// </para>
/// <para>
/// First, the exit code is the contract. Every failure path lands on exactly one
/// documented code, and the split between "the comparison found a difference"
/// (<see cref="ExitCode.Different"/>) and every code above it is deliberate: a
/// harness that collapses them reports "the export changed" when what actually
/// happened is "the file was missing".
/// </para>
/// <para>
/// Second, stdout carries the payload and stderr carries the problems, always.
/// A pipeline that reads only stdout gets clean output; one that watches only
/// stderr sees every failure. <c>--json</c> puts the whole result on stdout as
/// one object, including the exit code, so a caller never has to parse the
/// human-readable form.
/// </para>
/// </remarks>
public static class Program
{
    public static int Main(string[] arguments)
    {
        Console.OutputEncoding = Encoding.UTF8;
        return Run(arguments ?? Array.Empty<string>(), Console.Out, Console.Error);
    }

    /// <summary>
    /// The whole tool, with its streams supplied. Tests drive this rather than
    /// spawning a process, so a failing case is debuggable in place.
    /// </summary>
    public static int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        CommandRegistry registry = BuildRegistry();

        if (arguments.Count == 0)
        {
            HelpText.WriteOverview(output, registry);
            return ExitCode.Usage;
        }

        string name = arguments[0];

        if (name is "-h" or "--help" or "help" or "-?" or "/?")
        {
            if (arguments.Count > 1 && registry.Find(arguments[1]) is CommandEntry requested)
            {
                HelpText.WriteCommand(output, requested.Spec);
                return ExitCode.Ok;
            }

            HelpText.WriteOverview(output, registry);
            return ExitCode.Ok;
        }

        if (name is "--version" or "-v")
            name = "version";

        CommandEntry? entry = registry.Find(name);
        if (entry is null)
        {
            error.WriteLine("error: unknown command '" + name + "'.");
            if (registry.Suggest(name) is string suggestion)
                error.WriteLine("Did you mean '" + suggestion + "'?");
            error.WriteLine("Run '" + HelpText.ToolName + " --help' for the command list.");
            return ExitCode.Usage;
        }

        CommandContext? context = null;

        try
        {
            CommandLine line = CommandLine.Parse(entry.Spec, arguments.Skip(1).ToArray());

            if (line.Help)
            {
                HelpText.WriteCommand(output, entry.Spec);
                return ExitCode.Ok;
            }

            context = new CommandContext(line, output, error);
            int exitCode = entry.Handler(context);
            WriteJsonIfAsked(context, entry.Spec.Name, exitCode, output);
            return exitCode;
        }
        catch (UsageException exception)
        {
            error.WriteLine("error: " + exception.Message);
            error.WriteLine(
                "Run '" + HelpText.ToolName + " " + (exception.CommandName ?? entry.Spec.Name) +
                " --help' for this command's options.");
            return Finish(context, entry.Spec.Name, ExitCode.Usage, output, exception.Message);
        }
        catch (DocumentIoException exception)
        {
            error.WriteLine("error: " + exception.Message);
            return Finish(context, entry.Spec.Name, exception.ExitCode, output, exception.Message);
        }
        catch (DocumentException exception)
        {
            // A codec refused the input outright rather than reporting it as a
            // diagnostic: a limit was exceeded, or the bytes were unusable.
            error.WriteLine("error: " + exception.Message);
            return Finish(context, entry.Spec.Name, ExitCode.Read, output, exception.Message);
        }
        catch (IOException exception)
        {
            error.WriteLine("error: " + exception.Message);
            return Finish(context, entry.Spec.Name, ExitCode.Input, output, exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            error.WriteLine("error: " + exception.Message);
            return Finish(context, entry.Spec.Name, ExitCode.Input, output, exception.Message);
        }
        catch (Exception exception)
        {
            // Anything reaching here is a defect in this tool, not in the input.
            // The stack trace goes to stderr because a harness that hits one
            // needs to be able to paste it into a bug report.
            error.WriteLine("internal error: " + exception.Message);
            error.WriteLine(exception.ToString());
            return Finish(context, entry.Spec.Name, ExitCode.Internal, output, exception.Message);
        }
    }

    private static CommandRegistry BuildRegistry() => new(new[]
    {
        InspectCommands.Formats(),
        InspectCommands.Probe(),
        InspectCommands.Info(),
        DocumentCommands.Dump(),
        DocumentCommands.New(),
        DocumentCommands.Edit(),
        DocumentCommands.Convert(),
        RenderCommand.Create(),
        CompareCommand.Create(),
        RoundtripCommand.Create(),
        InspectCommands.Version(),
    });

    private static int Finish(
        CommandContext? context,
        string commandName,
        int exitCode,
        TextWriter output,
        string message)
    {
        if (context is null)
            return exitCode;

        context.Result["error"] = message;
        WriteJsonIfAsked(context, commandName, exitCode, output);
        return exitCode;
    }

    private static void WriteJsonIfAsked(
        CommandContext context,
        string commandName,
        int exitCode,
        TextWriter output)
    {
        if (!context.Json)
            return;

        var document = new JsonObject
        {
            ["command"] = commandName,
            ["exitCode"] = exitCode,
            ["ok"] = exitCode == ExitCode.Ok,
        };

        foreach (KeyValuePair<string, JsonNode?> property in context.Result)
            document[property.Key] = property.Value?.DeepClone();

        output.WriteLine(document.ToJsonString(JsonOutput.Indented));
    }
}
