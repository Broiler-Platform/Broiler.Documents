namespace Broiler.Documents.Cli.Infrastructure;

/// <summary>
/// The process exit codes this tool promises. They are part of the contract an
/// automated caller scripts against, so each one names a distinct outcome and
/// none of them is reused for a second meaning.
/// </summary>
/// <remarks>
/// The split that matters most is <see cref="Different"/> versus everything
/// above it. A comparison that finds a difference is a <em>successful</em> run
/// that reached a negative verdict; a run that could not read its input never
/// reached a verdict at all. A harness that collapses the two reports "the
/// export changed" when what actually happened is "the file was missing".
/// </remarks>
public static class ExitCode
{
    /// <summary>The command did what it was asked, and any verdict it reached was positive.</summary>
    public const int Ok = 0;

    /// <summary>The command line itself was wrong: unknown command, unknown option, missing argument.</summary>
    public const int Usage = 1;

    /// <summary>An input could not be opened, or an output could not be created.</summary>
    public const int Input = 2;

    /// <summary>A document was read but rejected: no codec matched, or the codec produced no usable result.</summary>
    public const int Read = 3;

    /// <summary>A document could not be written or rendered.</summary>
    public const int Write = 4;

    /// <summary>A comparison completed and the two sides differ beyond the tolerance given.</summary>
    public const int Different = 5;

    /// <summary>The run produced diagnostics at or above the severity <c>--fail-on</c> named.</summary>
    public const int Diagnostics = 6;

    /// <summary>An unexpected exception escaped. Always a defect in this tool.</summary>
    public const int Internal = 70;
}
