namespace Broiler.Documents;

/// <summary>
/// Whether an operation produced a usable result, independently of whether it
/// produced diagnostics.
/// </summary>
/// <remarks>
/// <para>
/// Severity and status answer different questions, and conflating them is how a
/// host ends up either refusing perfectly good documents or silently accepting
/// gutted ones. A read can carry a dozen warnings and still be a complete
/// rendering of the format's supported subset (<see cref="Success"/>); another
/// can carry a single warning that means a page was dropped
/// (<see cref="Partial"/>). Hosts branch on this, never on a diagnostic count or
/// on <see cref="DocumentReadResult.HasErrors"/>.
/// </para>
/// <para>
/// CLI exit codes, host prompts, batch continuation, and telemetry all derive
/// from this value (ADR 0008).
/// </para>
/// </remarks>
public enum DocumentResultStatus
{
    /// <summary>
    /// The format's declared supported subset was processed and the result is
    /// usable under the documented contract.
    /// </summary>
    Success,

    /// <summary>
    /// A usable result exists, but named content or features were skipped or
    /// remain uncertain. A host must obtain explicit caller approval before
    /// replacing an open document with it or publishing it as output.
    /// </summary>
    Partial,

    /// <summary>
    /// No usable result exists, and no host may accept one. The accompanying
    /// document or output is a placeholder, not content.
    /// </summary>
    Rejected,
}

/// <summary>How far a write got before it stopped.</summary>
/// <remarks>
/// The commit point is the atomic file replace, the publication of a complete
/// output buffer, or the first byte written to an unstaged caller-owned stream —
/// whichever the destination uses. <see cref="DocumentResultStatus.Success"/>
/// requires <see cref="Committed"/>.
/// </remarks>
public enum DocumentDestinationState
{
    /// <summary>Nothing was written; the destination is untouched.</summary>
    NotStarted,

    /// <summary>The complete output reached the destination.</summary>
    Committed,

    /// <summary>
    /// Bytes reached a caller-owned stream and then the write stopped. The prefix
    /// is not a valid document and discarding it is the caller's responsibility.
    /// </summary>
    PartialDestination,
}
